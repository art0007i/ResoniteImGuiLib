using CodeGenerationConfig;
using Elements.Core;
using ImGuiNET;
using SDL3;
using System.Diagnostics;

namespace ResoniteImGuiLib.SDL3;

public class SDL3Window : IDisposable
{
    public readonly IntPtr Window;
    public readonly IntPtr Device;
    public readonly ImGuiSDL3 Platform;
    public readonly ImGuiSDL3Renderer Renderer;
    internal readonly Queue<SDL.Event> EventQueue = new();
    public event Action<int4>? WindowRectModified;

    public color? ClearColor = null;

    public Action? RenderCallback { get; set; }

    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private TimeSpan _time = TimeSpan.Zero;

    private SDL.Rect _screenClipRect;
    private IntPtr _imGuiContext;

    public SDL3Window(string name, int posX, int posY, int width, int height)
    {
        if (!SDL.Init(SDL.InitFlags.Video))
            throw new Exception($"SDL_Init failed: {SDL.GetError()}");

        // Create window & renderer
        const SDL.WindowFlags windowFlags = SDL.WindowFlags.Resizable;
        if (!SDL.CreateWindowAndRenderer(name, width, height, windowFlags, out Window, out Device))
            throw new Exception($"SDL_CreateWindowAndRenderer failed: {SDL.GetError()}");

        SDL.SetWindowTitle(Window, name);
        SDL.SetWindowSize(Window, width, height);
        SDL.SetWindowPosition(Window, posX, posY);

        // Enable VSync
        SDL.SetRenderVSync(Device, 1);

        // Setup screen clip rect
        SetupScreenClipRect();

        // Create ImGui context
        _imGuiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imGuiContext);

        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;
        io.Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

        // Init platform and renderer
        Platform = new ImGuiSDL3(Window, Device);
        Renderer = new ImGuiSDL3Renderer(Device);

        //ImGuiManager.TriggerImGuiRecreated();
    }

    private bool _disposed;

    ~SDL3Window()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Window != IntPtr.Zero)
            {
                SDL.GetWindowPosition(Window, out int x, out int y);
                SDL.GetWindowSize(Window, out int w, out int h);
                WindowRectModified?.Invoke(new(x, y, w, h));
            }
        }
        catch
        {
            // Ignore if SDL window is already invalid
        }

        if (disposing)
        {
            Renderer?.Dispose();
            Platform?.Dispose();

            RenderCallback = null;
        }

        if (_imGuiContext != IntPtr.Zero)
        {
            ImGui.SetCurrentContext(_imGuiContext);
            ImGui.DestroyContext();
            _imGuiContext = IntPtr.Zero;
        }

        if (Device != IntPtr.Zero)
        {
            SDL.DestroyRenderer(Device);
        }

        if (Window != IntPtr.Zero)
        {
            SDL.DestroyWindow(Window);
        }
    }

    public void RunOneFrame()
    {
        if (_disposed) return;

        ImGui.SetCurrentContext(_imGuiContext);

        ImGui.GetIO().DeltaTime = (float) (_timer.Elapsed - _time).TotalSeconds;
        _time = _timer.Elapsed;

        PollEvents();

        Update();

        var clear = ClearColor ?? Plugin.DefaultBackgroundColor.Value;
        SDL.SetRenderDrawColor(Device, (byte) (clear.R * 255), (byte) (clear.G * 255), (byte) (clear.B * 255), (byte) (clear.A * 255));

        Render();

        if (_shouldClose || Plugin.CancellationToken.IsCancellationRequested)
        {
            Dispose();
        }
        ImGui.SetCurrentContext(IntPtr.Zero);
    }

    private bool _shouldClose;

    private void PollEvents()
    {
        if (ImGui.GetIO().WantTextInput && !SDL.TextInputActive(Window))
            SDL.StartTextInput(Window);
        else if (!ImGui.GetIO().WantTextInput && SDL.TextInputActive(Window))
            SDL.StopTextInput(Window);

        while (EventQueue.TryDequeue(out var ev))
        {
            Platform.ProcessEvent(ev);

            switch ((SDL.EventType) ev.Type)
            {
                // TODO: reimplement these events
                case SDL.EventType.WindowFocusGained:
                    //ImGuiManager.TriggerImGuiFocusChanged(true);
                    break;
                case SDL.EventType.WindowFocusLost:
                    //ImGuiManager.TriggerImGuiFocusChanged(false);
                    break;
                case SDL.EventType.Terminating:
                case SDL.EventType.WindowCloseRequested:
                case SDL.EventType.Quit:
                    _shouldClose = true;
                    break;
                case SDL.EventType.WindowResized:
                    SetupScreenClipRect();
                    SDL.GetWindowPosition(Window, out var x, out var y);
                    WindowRectModified?.Invoke(new(x, y, ev.Window.Data1, ev.Window.Data2));
                    break;
                case SDL.EventType.WindowMoved:
                    SDL.GetWindowSize(Window, out var h, out var w);
                    WindowRectModified?.Invoke(new(ev.Window.Data1, ev.Window.Data2, w, h));
                    break;
            }
        }
    }

    private void Update()
    {
        Platform.NewFrame();
        Renderer.NewFrame();
        ImGui.NewFrame();
        RenderCallback?.Invoke();
        ImGui.EndFrame();
    }

    private void Render()
    {
        SDL.RenderClear(Device);

        // Reset the clip rect to the screen size
        SDL.SetRenderClipRect(Device, _screenClipRect);

        // Render ImGui
        ImGui.Render();
        Renderer.RenderDrawData(ImGui.GetDrawData());

        SDL.RenderPresent(Device);
    }

    private void SetupScreenClipRect()
    {
        SDL.GetWindowSize(Window, out int w, out int h);
        _screenClipRect = new SDL.Rect
        {
            X = 0,
            Y = 0,
            W = w,
            H = h
        };
    }
}