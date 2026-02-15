
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ReTFO.Archipelago.ModdedInstanceData2;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ReTFO.Archipelago;

[BepInPlugin(GUID, Name, Version)]
[BepInProcess("GTFO.exe")]
public class Plugin : BasePlugin
{
    public const string Name = "Archipelago";       // Plugin name
    public const string Author = "RoboRyGuy";       // Plugin author
    public const string GUID = $"{Author}.{Name}";  // Plugin GUID, unique identifier used by BepInEx
    public const string Version = "1.0.0";          // Plugin version, can be used by System.Version

    // Reference to plugin instance that is loaded by BepInEx
    private static Plugin? _plugin = null;

    // Instance of harmony used for patching
    protected Harmony harmony = new(GUID);

    // Get the plugin instance, throws an exception if it fails
    public static Plugin Get() => TryGet() ?? throw new NullReferenceException($"Tried to retrieve {Name}, but it was not loaded!");

    // Tries to get the plugin instance, returns null if it fails
    public static Plugin? TryGet() => _plugin ??= IL2CPPChainloader.Instance.Plugins.FirstOrDefault(p => p.Key == GUID).Value.Instance as Plugin;

    // Tries to get the plugin instance, returns true if it succeeds
    public static bool TryGet([NotNullWhen(true)] out Plugin? plugin)
    {
        plugin = TryGet();
        return plugin != null;
    }

    public override void Load()
    {
        _plugin = this;
        harmony.PatchAll(GetType());
        harmony.PatchAll(typeof(SingleRundownPatch));
        harmony.PatchAll(typeof(PostShowIntel));
        Log.LogInfo($"{GUID} is loaded!");
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        return true;
    }

    // --------------------------------------------------------------------------------------------

    // Manager for Modded instance data, manages generating it and such
    // Subclasses ProcessExpedition, in case you want to react to that event
    public Manager Manager 
    {
        get { return manager ??= new(); }
        protected set { manager = value; } 
    }
    private Manager? manager = null;

    // Event handler for the ProcessLayer event (for generating modded instance data)
    public ProcessLayer ProcessLayer
    {
        get { return processLayer ??= new ProcessLayer().RegisteredTo(Manager.ProcessExpedition); }
        protected set { processLayer = value; }
    }
    private ProcessLayer? processLayer = null;

    // Event handler for the ProcessZone event (for generating modded instance data)
    public ProcessZone ProcessZone
    {
        get { return processZone ??= new ProcessZone().RegisteredTo(ProcessLayer); }
        protected set { processZone = value; }
    }
    private ProcessZone? processZone = null;

    // Event handler for the ProcessTerminal event (for generating modded instance data)
    public ProcessTerminal ProcessTerminal
    {
        get { return processTerminal ??= new ProcessTerminal().RegisteredTo(ProcessZone); }
        protected set { processTerminal = value; }
    }
    private ProcessTerminal? processTerminal = null;

    // Hanlder for the ProcessObjective callbacks (for generating modded instance data)
    public ProcessObjective ProcessObjective
    {
        get { return processObjective ??= new ProcessObjective().RegisteredTo(ProcessLayer); }
        protected set { processObjective = value; }
    }
    private ProcessObjective? processObjective = null;

    public ModdedInstanceData2.ModdedInstanceData GetModdedInstanceData()
    {
        // Force init various events, in case they aren't initted
        object obj;
        obj = Manager;
        obj = ProcessLayer;
        obj = ProcessZone;
        obj = ProcessTerminal;
        obj = ProcessObjective;

        // Create and return data
        return Manager.CreateData();
    }
}
