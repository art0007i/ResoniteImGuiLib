using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using FrooxEngine;

namespace ResoniteImGuiLib;

public static class ImGuiLib
{
    public static ImGuiInstance GetOrCreateInstance(ImGuiReady onReady)
    {
        return GetOrCreateInstance("global", onReady);
    }
    public static ImGuiInstance GetOrCreateInstance(string name = "global", ImGuiReady? onReady = null)
    {
        return ImGuiInstance.GetOrCreate(name, onReady);
    }
}

[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BasePlugin
{
#nullable disable
    internal static new ManualLogSource Log;
    internal static new ConfigFile Config;

    internal static ConfigEntry<color> DefaultBackgroundColor;
#nullable enable

    public static readonly CancellationTokenSource CancellationToken = new CancellationTokenSource();

    public override void Load()
    {
        Log = base.Log;
        Config = base.Config;

        DefaultBackgroundColor = Config.Bind("General", "DefaultBackgroundColor", new color(0.45f, 0.55f, 0.60f, 1.00f), "Default background color that new ImGui windows will have.");

        BepisResoniteWrapper.ResoniteHooks.OnEngineReady += () =>
        {
            Engine.Current.OnShutdown += () => CancellationToken.Cancel();
            Engine.Current.OnShutdownRequest += _ => CancellationToken.Cancel();
        };
    }
}
