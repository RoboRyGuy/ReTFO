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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using TheArchive;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago;

using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Collections.Generic;

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

        var types = Assembly.GetExecutingAssembly().GetTypes();
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
            InjectRecursive(type.GetNestedTypes(AccessTools.all));
        }
    }

    private void PatchRecursive(Type[] types)
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

    /// <summary>
    /// Debug patch I added to try and figure out a checkpoint bug.
    /// Since I've added it, though, the bug has not occured. So, I guess it fixes the bug?
    /// </summary>
    [HarmonyPatch(typeof(SNet_Replication), nameof(SNet_Replication.RecallBytes))]
    [HarmonyPrefix]
    public static void PreRecallBytes(SNet_Replication __instance, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes, uint size)
    {

    }

    public static void Helper(string message)
    {
        FeaturesAPI.FeatureLogger.Notice(message);
        FeaturesAPI.FeatureLogger.Notice($" -> Has Lobby? {SNet.Lobby != null}");
        FeaturesAPI.FeatureLogger.Notice($" -> Lobby ID: {SNet.Lobby?.Name ?? "null"}");
        FeaturesAPI.FeatureLogger.Notice($" -> Has Hub? {SNet.SessionHub.IsInHub()}");
    }

}

[HarmonyPatch]
public static class LogPatch
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.CreateLobby));
        yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.JoinLobby), [typeof(ulong), typeof(bool)] );
        yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.JoinLobby), [typeof(SNet_LobbyIdentifier), typeof(bool)] );
        yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.JoinLobby), [typeof(SNet_JoinableLobby), typeof(bool)] );
        yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.LeaveLobby));
        yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.OnJoinedLobby));
        yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.SetWaitingForLobby));
        yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.ExecutePendingJoinRequest));
        //yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.PlayerJoinedLobby));
        //yield return AccessTools.Method(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.PlayerLeftLobby));

        yield return AccessTools.Method(typeof(SNet_SessionHub), nameof(SNet_SessionHub.AddPlayerToSession));
        yield return AccessTools.Method(typeof(SNet_SessionHub), nameof(SNet_SessionHub.CreateHub));
        yield return AccessTools.Method(typeof(SNet_SessionHub), nameof(SNet_SessionHub.LeaveHub));
        yield return AccessTools.Method(typeof(SNet_SessionHub), nameof(SNet_SessionHub.OnForceJoinLobby));
        yield return AccessTools.Method(typeof(SNet_SessionHub), nameof(SNet_SessionHub.OnJoinedLobby));
        yield return AccessTools.Method(typeof(SNet_SessionHub), nameof(SNet_SessionHub.OnLeftLobby));
        yield return AccessTools.Method(typeof(SNet_SessionHub), nameof(SNet_SessionHub.RelocateLobby));
        yield return AccessTools.Method(typeof(SNet_SessionHub), nameof(SNet_SessionHub.RemovePlayerFromSession));
        yield return AccessTools.Method(typeof(SNet_SessionHub), nameof(SNet_SessionHub.SendWantsToJoinHub));

        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.JoinLobby));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.LeaveLobby));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.OnGameFriendLobbyJoinRequested));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.OnLobbyCreated));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.OnLobbyEntered));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.OnLobbyStatusChange));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.OnP2PSessionRequest));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.AcceptP2PSessionWithUser));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.StartLobby));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.CreateLocalPlayer));
        yield return AccessTools.Method(typeof(SNet_Core_STEAM), nameof(SNet_Core_STEAM.CreateLobbyObject));

        yield return AccessTools.Method(typeof(SNet_Lobby_STEAM), nameof(SNet_Lobby_STEAM.OnLocalPlayerJoinedLobby));
        yield return AccessTools.Method(typeof(SNet_Lobby_STEAM), nameof(SNet_Lobby_STEAM.PlayerJoined), [typeof(SNet_Player), typeof(Steamworks.CSteamID)]);
        yield return AccessTools.Method(typeof(SNet_Lobby_STEAM), nameof(SNet_Lobby_STEAM.PlayerJoined), [typeof(Steamworks.CSteamID), ]);
        yield return AccessTools.Method(typeof(SNet_Lobby_STEAM), nameof(SNet_Lobby_STEAM.PlayerLeft));
        yield return AccessTools.Method(typeof(SNet_Lobby_STEAM), nameof(SNet_Lobby_STEAM.PlayerLeaveReason));
    }

    private static int depth = 0;

    public static string FormatName(MethodBase m)
    {
        var paras = m.GetParameters();
        if (paras.Any())
            return $"{m.DeclaringType!.Name}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType} {p.Name}"))})";
        else 
            return $"{m.DeclaringType!.Name}.{m.Name}()";
    }

    public static void Prefix(MethodBase __originalMethod)
    {
        Plugin.Get().Log.LogInfo($"{new string(' ', 2 * (depth++))}-> {FormatName(__originalMethod)}");
    }

    public static void Postfix(MethodBase __originalMethod)
    {
        Plugin.Get().Log.LogInfo($"{new string(' ', 2 * (--depth))}<- {FormatName(__originalMethod)}");
    }

}
