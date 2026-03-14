using BepInEx;
using BepInEx.Unity.IL2CPP;
using Clonesoft.Json;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.ModdedInstanceData;
using ReTFO.Archipelago.Utilities;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using TheArchive;
using TheArchive.Core;
using TheArchive.Core.Attributes;
using TheArchive.Core.Localization;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago;

// Marks a class as needing to be injected to Il2Cpp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
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
[BepInDependency(TheArchive.ArchiveMod.GUID)]
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

        var types = Assembly.GetExecutingAssembly().GetTypes();
        InjectRecursive(types);
        foreach (Type type in types) if (type.GetCustomAttribute<HarmonyPatch>() != null) harmony.PatchAll(type);

        Log.LogInfo($"{GUID} is loaded!");
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        return true;
    }

    private void InjectRecursive(Type[] types)
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
            InjectRecursive(type.GetNestedTypes());
        }
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

    // Tracks Archipelago state and syncs with server
    private StateTracker? m_stateTracker = null;
    public StateTracker StateTracker 
    { 
        get => m_stateTracker ??= ArchipelagoFeatureHelper.GetFeature<StateTracker>(); 
        protected set => m_stateTracker = value; 
    }

    // Manager for Modded instance data, manages generating it and such
    private MidManager m_midManager = new();
    public MidManager MidManager 
    { 
        get => m_midManager;
        protected set => m_midManager = value; 
    }

}
