using BepInEx.Configuration;
using Elements.Core;
using ResoniteImGuiLib.SDL3;
using SDL3;
using System.Collections.Concurrent;

namespace ResoniteImGuiLib;

public class ImGuiInstance
{
    private ImGuiInstance(string name)
    {
        _name = name;
        _entry = Plugin.Config.Bind("Windows", "Rect_" + Name, new int4(200, 200, 400, 300));
    }

    public string Name => _name;
    /// <summary>
    /// Called whenever ImGui is being rendered. You can use ImGui functions inside here.
    /// </summary>
    public event Action? Layout;
    /// <summary>
    /// Called whenever the window is processing an event. Returning false means that the event will not be processed further.
    /// </summary>
    public event Func<SDL.Event, bool>? SdlEvent;

    private string _name;
    private ConfigEntry<int4> _entry;

    private static bool IsSDL3Running => _sdl3Thread is { IsAlive: true };
    private static Thread? _sdl3Thread;
    private static Dictionary<string, ImGuiInstance> GuiInstances = new();
    internal static ConcurrentQueue<ImGuiInstance> newInstances = new();
    internal static ConcurrentQueue<(string, Action<SDL3Window>)> windowRequests = new();

    /// <summary>
    /// Tries to open the window. If it's already open, nothing happens. This function may be expensive to call rapidly.
    /// </summary>
    public void OpenWindow()
    {
        newInstances.Enqueue(this);
    }

    /// <summary>
    /// Gets a reference to the SDL3Window inside of a callback.
    /// The callback will be called inside the SDL3 Thread.
    /// </summary>
    /// <param name="callback"></param>
    public void GetImGui(Action<SDL3Window> callback)
    {
        windowRequests.Enqueue((Name, callback));
    }

    /// <summary>
    /// Creates a new ImGuiInstance with the "global" name or returns it if it already exists.
    /// </summary>
    /// <param name="onReady">Callback for when the ImGui instance is ready. You can put initialization logic here.</param>
    /// <returns></returns>
    public static ImGuiInstance GetOrCreate(ImGuiReady onReady) => GetOrCreate("global", onReady);


    /// <summary>
    /// Creates a new ImGuiInstance or returns one if it already exists.
    /// </summary>
    /// <param name="onReady">Callback for when the ImGui instance is ready. You can put initialization logic here.</param>
    /// <returns></returns>
    public static ImGuiInstance GetOrCreate(string name = "global", ImGuiReady? onReady = null)
    {
        if (GuiInstances.TryGetValue(name, out var instance))
        {
            if (onReady != null)
                instance.GetImGui((gui) => onReady(gui, false));
            return instance;
        }

        instance = new ImGuiInstance(name);
        GuiInstances.Add(name, instance);
        if (onReady != null)
            instance.GetImGui((gui) => onReady(gui, true));

        newInstances.Enqueue(instance);
        TryStartSDL3Thread();

        return instance;
    }

    private static void TryStartSDL3Thread()
    {
        if (IsSDL3Running)
            return;

        _sdl3Thread = new Thread(RunSDL3)
        {
            Name = $"Resonite ImGui SDL3",
            Priority = ThreadPriority.Highest,
            IsBackground = false
        };

        _sdl3Thread.Start();
    }
    static Dictionary<string, SDL3Window> windows = new();
    static Dictionary<uint, SDL3Window> windowsById = new();
    private static void RunSDL3()
    {
        try
        {
            while (true)
            {
                while (newInstances.TryDequeue(out var request))
                {
                    if (windows.ContainsKey(request.Name)) continue;

                    SDL3Window app = new SDL3Window("ImGuiContext: " + request.Name, request._entry.Value.x, request._entry.Value.y, request._entry.Value.z, request._entry.Value.w);
                    app.WindowRectModified += (rect) => request._entry.Value = rect;
                    // TODO: maybe listen to config changes to resize window
                    // request._entry.SettingChanged += (_, _) => { };
                    app.RenderCallback = request.Layout;
                    app.SdlEventReceived = request.SdlEvent;
                    if (!windows.TryAdd(request.Name, app))
                    {
                        Plugin.Log.LogError($"Failed to add window with name {request.Name}!");
                    }
                    if (!windowsById.TryAdd(app.SdlWindowId, app))
                    {
                        Plugin.Log.LogError($"Failed to add window with id {app.SdlWindowId}, name {request.Name}!");
                    }
                }
                for (int i = 0; i < windowRequests.Count; i++)
                {
                    if (windowRequests.TryDequeue(out var callback))
                    {
                        if (windows.TryGetValue(callback.Item1, out var window))
                        {
                            callback.Item2?.Invoke(window);
                        }
                        else
                        {
                            windowRequests.Enqueue(callback);
                        }
                    }
                }
                while (SDL.PollEvent(out SDL.Event ev))
                {
                    if (windowsById.TryGetValue(ev.Window.WindowID, out var window))
                    {
                        window.EventQueue.Enqueue(ev);
                    }
                }

                var removed = new HashSet<(string, uint)>();
                foreach (var (key, window) in windows)
                {
                    if(window.Disposed)
                    {
                        removed.Add((key, window.SdlWindowId));
                        continue;
                    }
                    window.SetContext();

                    if (Plugin.CancellationToken.IsCancellationRequested)
                    {
                        window.ForceDispose();
                        continue;
                    }

                    if (window.ShouldClose)
                    {
                        removed.Add((key, window.SdlWindowId));
                        window.Dispose();
                        continue;
                    }

                    window.RunOneFrame();
                }
                foreach (var (k, k2) in removed)
                {
                    var b = windows.Remove(k);
                    var b2 = windowsById.Remove(k2);
                    Plugin.Log.LogDebug($"Removed window {k}, success codes: {b}|{b2}");
                }
                if (Plugin.CancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Prevent busy looping when nothing is happening.
                if (windows.Count == 0)
                {
                    Thread.Sleep(1000);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"SDL3 Thread crashed: {e}");
        }

        _sdl3Thread?.Interrupt();
        _sdl3Thread = null;
    }
}

public delegate void ImGuiReady(SDL3Window imGui, bool isNewInstance);