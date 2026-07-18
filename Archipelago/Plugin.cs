using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.ModdedInstanceData;
using ReTFO.Archipelago.Utilities;
using SNetwork;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using TheArchive;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago;

using ReTFO.Archipelago.ModdedInstanceData.Processors;

// Marks a class as needing to be injected to Il2Cpp. Optionally accepts a list of interfaces the type implements
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
internal class InjectToIl2Cpp : Attribute
{
    public InjectToIl2Cpp() { InterfaceTypes = Array.Empty<Type>(); }
    public InjectToIl2Cpp(Type type) { InterfaceTypes = new Type[1] { type }; }
    public InjectToIl2Cpp(Type[] types) { InterfaceTypes = types; }
    public Type[] InterfaceTypes;
}

[BepInPlugin(GUID, Name, Version)]
[BepInProcess("GTFO.exe")]
[BepInDependency(MTFO.MTFO.GUID)]
[BepInDependency(ArchiveMod.GUID)]
public class Plugin : BasePlugin
{
    public const string Name = "BetaArchipelago";   // Plugin name
    public const string Author = "RoboRyGuy";       // Plugin author
    public const string GUID = $"{Author}.{Name}";  // Plugin GUID, unique identifier used by BepInEx
    public const string Version = "0.0.4";          // Plugin version, can be used by System.Version

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

        var types = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.DeclaringType == null);
        InjectRecursive(types);
        PatchRecursive(types);
        AddProcessors(MidManager);

      Log.LogInfo($"{GUID} is loaded!");
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        return true;
    }

    private void InjectRecursive(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            var attribute = type.GetCustomAttribute<InjectToIl2Cpp>();
            if (attribute == null) continue;
            if (type.IsAssignableTo(typeof(Il2CppObjectBase)))
            {
                RegisterTypeOptions options = new()
                {
                    Interfaces = attribute.InterfaceTypes
                };
                ClassInjector.RegisterTypeInIl2Cpp(type, options);
            }
            InjectRecursive(type.GetNestedTypes(AccessTools.all));
        }
    }

    private void PatchRecursive(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            if (type.GetCustomAttribute<HarmonyPatch>() == null) continue;
            harmony.PatchAll(type);
            PatchRecursive(type.GetNestedTypes(AccessTools.all));
        }
    }

    public static void AddProcessors(MidManager midManager)
    {
        var expeditionProcessor = new Expedition.Processor().SubscribedTo(midManager.GetProcessor<Game.Data>());
        midManager.RegisterProcessor(expeditionProcessor);

        var layerProcessor = new Layer.Processor().SubscribedTo(expeditionProcessor);
        midManager.RegisterProcessor(layerProcessor);

        var zoneProcessor = new Zone.Processor().SubscribedTo(layerProcessor);
        midManager.RegisterProcessor(zoneProcessor);

        var terminalProcessor = new Terminal.Processor().SubscribedTo(zoneProcessor);
        midManager.RegisterProcessor(terminalProcessor);

        var objectiveProcessor = new Objective.Processor().SubscribedTo(layerProcessor);
        midManager.RegisterProcessor(objectiveProcessor);

        var eventProcessor = new Event.Processor();
        midManager.RegisterProcessor(eventProcessor);
    }

    // --------------------------------------------------------------------------------------------

    private ArchipelagoArchiveModule? m_archiveModule = null;
    public ArchipelagoArchiveModule ArchiveModule
    {
        get => m_archiveModule ??= ArchiveMod.Modules.OfType<ArchipelagoArchiveModule>().First();
        internal set => m_archiveModule = value;
    }

    public IArchiveLogger Logger => ArchiveModule.Logger;

    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Event invoked when StateTracker initalizes replication.
    /// You can use this event to set up custom packets using StateTracker's replicator
    /// Also useful for late patches.
    /// </summary>
    public event Action<SNet_Replicator>? LateSetup;

    /// <summary>
    /// Invoke late setup. Called in <see cref="StateTracker.SetupReplication"/>
    /// </summary>
    /// <param name="replicator"></param>
    internal void InvokeLateSetup(SNet_Replicator replicator)
        => LateSetup?.Invoke(replicator);

    /// <summary>
    /// Tracks Archipelago state and syncs both with AP server and lobby members
    /// </summary>
    public StateTracker? StateTracker { get; internal set; } = null;

    /// <summary>
    /// Manager for Modded instance data, manages generating it and such
    /// </summary>
    private MidManager m_midManager = new();
    public MidManager MidManager 
    { 
        get => m_midManager;
        protected set => m_midManager = value;
    }
}
