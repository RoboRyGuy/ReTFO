using CellMenu;
using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Player;
using ReTFO.Archipelago.Features.FloatingItems;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.ModdedInstanceData;
using ReTFO.Archipelago.Patches;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Members;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Core.FeaturesAPI.Components;
using TheArchive.Interfaces;
using UnityEngine;
using AP = Archipelago.MultiClient.Net;

namespace ReTFO.Archipelago.Features;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// Tracks Archipelago state
/// </summary>
[EnableFeatureByDefault, AutomatedFeature]
public partial class StateTracker : ArchipelagoFeature
{
    public override string Name => "AP Server Settings";
    public override string Description => "Controls AP server and sync settings.";
    public override FeatureGroup Group => FeatureGroups.Archipelago;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private Plugin? m_plugin = null;
    public Plugin Plugin
    {
        get => m_plugin ??= Plugin.Get();
        protected set => m_plugin = value;
    }

    public MidManager MidManager => Plugin.MidManager;
    public AP.ArchipelagoSession? ApSession { get; protected set; } = null;
    public static Version APVersion = new(1, 0, 0);
    List<string> GameTags = [ "DeathLink", "EnergyLink" ];
    public Guid SessionGuid { get; protected set; } = new();
    protected Task<AP.Packets.RoomInfoPacket>? ConnectTask { get; set; } = null;
    protected Task<AP.LoginResult>? LoginTask { get; set; } = null;
    public event Action<StateTracker>? OnStateChange;

    // Things set up at initial sync
    public long RootSeed { get; protected set; } = 0;
    protected HashSet<Expedition.Data> Expeditions { get; set; } = new();
    protected HashSet<RandomizationTag> WhitelistTags { get; set; } = new();
    protected HashSet<RandomizationTag> BlacklistTags { get; set; } = new();
    public bool RequiresSecondaries { get; protected set; } = new();
    public bool RequiresOverloads { get; protected set; } = new();

    // Things consistently updated
    protected HashSet<RegionID> FoundRegions { get; init; } = new();
    protected HashSet<string> FoundUnusedRegions { get; init; } = new();
    protected HashSet<LocationID> FoundLocations { get; init; } = new();

    /// Actual list of items held. See <see cref="CollectedItemCounts"/> for the public interface
    protected Dictionary<ItemID, int> ActualItemCounts { get; init; } = new(); 
    protected Dictionary<ItemID, int> SessionItemCounts { get; init; } = new(); // Items received since reconnecting
    protected List<ItemID> NeededWinItems { get; init; } = new(); // Items needed to win, recalculated on reconnect

    protected Dictionary<ItemID, Queue<ItemID>> QueuedItemReplacements = new();
    public List<Tuple<ItemID, string>> ItemsInTerminalSystem { get; init; } = new();

    /// <summary>
    /// Get the current state tracker
    /// </summary>
    /// <returns>The current state tracker</returns>
    public static StateTracker Get() => Plugin.Get().StateTracker!;

    public override void OnEnable()
    {
        base.OnEnable();
        Plugin.StateTracker = this;
    }

    /// <summary>
    /// Possible states the StateTracker can be in, generally with regards to network connectivity
    /// </summary>
    public enum eState
    {
        /// <summary>
        /// StateTracker is clean with no connection info
        /// </summary>
        CleanState,

        /// <summary>
        /// StateTracker is connecting to AP for a new connection
        /// </summary>
        HostConnecting,

        /// <summary>
        /// StateTracker is connected to AP
        /// </summary>
        HostConnected,

        /// <summary>
        /// StateTracker was connected to AP, and is trying to reconnect
        /// </summary>
        HostReconnecting,

        /// <summary>
        /// StateTracker is faking a connection for debug, and can be treated as synced
        /// </summary>
        FakeConnect,

        /// <summary>
        /// StateTracker is both connected to AP and is a client in a lobby
        /// </summary>
        ConnectedClient,

        /// <summary>
        /// StateTracker is not connected to AP and we are a client in a lobby.
        /// This means StateTracker is using the host as a proxy to access AP.
        /// </summary>
        ProxyClient,
    }

    /// <summary>
    /// Current state of this tracker
    /// </summary>
    public eState CurrentState { get; set; } = new();

    /// <summary>
    /// Class used to store network settings
    /// </summary>
    public class NetworkSettingsType : PrivateFeatureSettingsPatch.IOptionallyPrivate
    {
        public bool IsCurrentlyPrivate => HideConnectionDetails;

        [FSDisplayName("Use Debug Mode")]
        [FSDescription("Skip connecting to Archipelago and start in debug mode.")]
        public bool UseDebugMode { get; set; } = false;

        [FSDisplayName("Hide Connection Details")]
        [FSDescription("For streamers. Hides connection details while not editing them. Also prevents them from being logged.")]
        public bool HideConnectionDetails { get; set; } = true;

        [FSDisplayName("Server Address")]
        [FSDescription("Address of the server to connect to.\nSupports IPv4 and IPv6 addresses, as well as domain names.")]
        [PrivateFeatureSettingsPatch.FSOptionallyPrivate]
        public string ServerAddress { get; set; } = "localhost";

        [FSDisplayName("Server Port")]
        [FSDescription("Port of the server\nValid port numbers are in the 16-bit range (0-65535)\nArchipelago defaults to using port 38281")]
        [PrivateFeatureSettingsPatch.FSOptionallyPrivate]
        public ushort Port { get; set; } = 38281;

        [FSDisplayName("Slot Name")]
        [FSDescription("Slot in the server to try to connect to.")]
        [PrivateFeatureSettingsPatch.FSOptionallyPrivate]
        public string Username { get; set; } = "admin";

        [FSDisplayName("Use Password")]
        [FSDescription("If true, will attempt to authenticate to Archipelago using the below password\nIf false, will try to skip password authentication")]
        public bool HasPassword { get; set; } = false;

        [FSDisplayName("Password")]
        [FSDescription("The password to use when connecting to Archipelago")]
        [PrivateFeatureSettingsPatch.FSOptionallyPrivate]
        public string Password { get; set; } = "Password";

        [FSDisplayName("Export MID Data")]
        [FSDescription(
            "Export the MID data file to your Downloads folder. The MID data file is a \".ini\" file which can be used to create APWorlds for modded rundowns." 
            + "\nTo use the MID file, place it into the Players folder (the same folder you normally place YAMLs) and restart the launcher."
            + " This will add a new GTFO world which can then be used to play your modded game. This sub-world can be reused by any players"
            + " running the same mods as you, and should be left in the Players folder at least until your game is generated."
        )]
        public FButton ExportMidDataButton { get; set; } = new FButton("Export", callback: () => StateTracker.Get().MidManager.ExportMidData(null));

        [FSDisplayName("Export Tags to CSV")]
        [FSDescription(
            "Export all tag data as a CSV file to your Downloads folder." 
            + "\nThe CSV export is for an Excel-style viewing of tags, and is easier to parse programmatically."
        )]
        public FButton ExportTagsToCSVButton { get; set; } = new FButton("Export", callback: () => StateTracker.Get().MidManager.ExportTagsToCSV(null));

        [FSDisplayName("Export Tags to JSON")]
        [FSDescription(
            "Export all tag data as a JSON file to your Downloads folder." 
            + "\nThe JSON export is for hierarchal viewing of the tags, and can make viewing tags easier if"
            + " you have a good JSON viewer (such as VS Code). Several websites also have good JSON viewers"
            + " available for free online without needing to download or install anything."
        )]
        public FButton ExportTagsToJSONButton { get; set; } = new FButton("Export", callback: () => StateTracker.Get().MidManager.ExportTagsToJSON(null));
    }

    /// <summary>
    /// Instance of settings. Note that this is controlled by TheArchive.
    /// </summary>
    [FeatureConfig]
    public static NetworkSettingsType Config { get; set; } = null!;

    /// <summary>
    /// Get a list of expedition data from a set of input names.
    /// Logs an error if a name does not have expedition data, then skips it.
    /// </summary>
    public HashSet<Expedition.Data> ExpeditionsFromNames(ICollection<string> names)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        HashSet<Expedition.Data> results = new(names.Count);
        foreach (string name in names.Distinct())
        {
            if (gameData.TryLookupExpedition(name, out Expedition.Data? data))
                results.Add(data);
            else
                FeatureLogger.Error($"Failed to find expedition with name {name}; dropping!");
        }
        return results;
    }

    /// <summary>
    /// Get a list of randomization tags from string names.
    /// Logs an error if a name does not have a tag value, then skips it
    /// </summary>
    public HashSet<RandomizationTag> TagsFromNames(ICollection<string> names)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        HashSet<RandomizationTag> results = new(names.Count);
        foreach (string name in names.Distinct())
        {
            if (gameData.TryLookupTag(name, out RandomizationTag data))
                results.Add(data);
            else
                FeatureLogger.Error($"Failed to find tag with name {name}; dropping!");
        }
        return results;
    }

    /// <summary>
    /// Enter the fake connect state, which is for debug
    /// </summary>
    public void FakeConnect()
    {
        if (CurrentState != eState.CleanState)
        {
            FeatureLogger.Error($"Cannot start fake connection; already in state: {Enum.GetName(CurrentState)}");
            return;
        }

        // Replace this with a menu config at some point
        CurrentState = eState.FakeConnect;
        RootSeed = 0;
        Expeditions = ExpeditionsFromNames([ 
            "R1B1", "R6A1", "R3A3", "R6B1", "R8C2", "R4D2", "R8E1",
            //"R1B1", "R1A1", "R1B2", "R1C1", "R1C2", "R1D1",
            //"R3A1", "R3A2", "R3A3", "R3B1", "R3B2", "R3C1", "R3D1",
            //"R4A1", "R4A2", "R4A3", "R4B1", "R4B2", "R4B3", "R4C1", "R4C2", "R4C3", "R4D1", "R4D2", "R4E1",
            //"R5A1", "R5A2", "R5A3", "R5B1",
            //"R6A1", "R6AX", "R6B1", "R6B2", "R6BX", "R6C1", "R6C2", "R6C3", "R6CX", "R6D1", "R6D2", "R6D3", "R6D4",
            //"R7A1", "R7B1", "R7B2", "R7B3", "R7C1", "R7C2", "R7C3", "R7D1", "R7D2", "R7E1",
            //"R8A1", "R8A2", "R8B1", "R8B2", "R8B3", "R8B4", "R8C1", "R8C2", "R8D1", "R8D2", "R8E1", "R8E2",
        ]);
        WhitelistTags = [ MidManager.GetProcessedGameData().Tag_All ];
        BlacklistTags = [ MidManager.GetProcessedGameData().Tag_ExpeditionUnlocks, MidManager.GetProcessedGameData().Tag_LobbySlotUnlocks ];
        RequiresSecondaries = true;
        RequiresOverloads = true;

        SetupMultiworld();

        FeatureLogger.Notice("Due to fake connect, removing all expedition locks.");
        UnlockExpeditionHandler.UnlockAll();
    }

    /// <summary>
    /// Enter the client connect state, where this user receives Archipelago data from the lobby host.
    /// This is invoked by <see cref="ReceiveInitState(pArchipelagoInitState)"/>
    /// </summary>
    public void ClientConnect(pArchipelagoInitState state)
    {
        if (!SNetwork.SNet.Lobbies.WaitingForLobbyResponse)
        {
            FeatureLogger.Debug("Ignoring init packet; not currently waiting to enter a lobby");
            return;
        }

        if (CurrentState == eState.ConnectedClient || CurrentState == eState.ProxyClient)
        {
            FeatureLogger.Debug("Ignoring init packet; already set up as a client");
            return;
        }

        if (state.GameName != MidManager.GetProcessedGameData().Name)
        {
            FeatureLogger.Error("Cannot connect as client; the host is playing with different mods than us!");
            return;
        }

        FeatureLogger.Notice("Received init packet. Preparing to join as client...");
        if (CurrentState == eState.CleanState)
        {   // We need to set up the multiworld
            FeatureLogger.Notice("Joining as proxy client...");
            RootSeed = state.RootSeed;
            Expeditions = ExpeditionsFromNames(state.ExpeditionNames);
            WhitelistTags = state.WhitelistTags.Select(i => new RandomizationTag() { AsId = i }).ToHashSet();
            BlacklistTags = state.BlacklistTags.Select(i => new RandomizationTag() { AsId = i }).ToHashSet();
            CurrentState = eState.ProxyClient;
            SetupMultiworld();
        }
        else
        {
            FeatureLogger.Notice("Joining as a connected client...");
            if (RootSeed != state.RootSeed)
            {
                FeatureLogger.Error("Refused to join as client; root seeds do not match! This indicates the host is connected to a different slot or server.");
                return;
            }
            CurrentState = eState.ConnectedClient;
        }

        // We are officially set up to join the lobby as a client - just gotta fake a "join allowed" response
        SNetwork.SNet.SessionHub.OnMasterSessionAnswer(cachedMasterAnswer);
        FeatureLogger.Success("Successfully joined!");
    }

    /// <summary>
    /// Try to start a connection to Archipelago with the current network settings.
    /// Called by the Connect to Archipelago button in the starting menu.
    /// </summary>
    public void Connect()
    {
        if (Config.UseDebugMode)
        {
            FakeConnect();
            return;
        }

        if (CurrentState != eState.CleanState)
        {
            FeatureLogger.Error($"Cannot start new connection; already in state: {Enum.GetName(CurrentState)}");
            return;
        }

        if (ConnectTask != null)
        {
            FeatureLogger.Warning("Cannot start new connection; currently waiting to connect to Archipelago");
            return;
        }

        CurrentState = eState.HostConnecting;
        SessionGuid = Guid.NewGuid();
        ApSession = AP.ArchipelagoSessionFactory.CreateSession(Config.ServerAddress, Config.Port);
        ApSession.Locations.CheckedLocationsUpdated += (locs) => 
        {
            foreach (long id in locs) 
                FoundLocations.Add(new LocationID() { AsId = id });
            UpdateLocationCounts();
        };
        ConnectTask = ApSession.ConnectAsync();
    }

    /// <summary>
    /// Handle a failed connection due to various reasons (failed to connect, send login, or authenticate)
    /// </summary>
    /// <param name="debugName">A string describing the action being attempted which failed</param>
    protected void HandleFailedConnection(string debugName, Exception? exception)
    {
        FeatureLogger.Error($"Failed to {debugName} to Archipelago Host!");
        if (exception != null)
        {
            FeatureLogger.Error("Encountered the following exception:");
            FeatureLogger.Exception(exception);
        }

        if (CurrentState == eState.HostConnecting)
        {
            ApSession = null;
            CurrentState = eState.CleanState;
        }
        else if (CurrentState == eState.HostReconnecting)
        {
            ConnectTask = ReconnectDelayed();
        }
        else
        {
            FeatureLogger.Warning($" -> Failed to {debugName} while not in connecting state?");
        }
    }

    /// <summary>
    /// Attempt to reconnect after a specified delay
    /// </summary>
    /// <param name="delay">The delay, in milliseconds</param>
    protected async Task<AP.Packets.RoomInfoPacket> ReconnectDelayed(int delay=2500)
    {
        FeatureLogger.Notice($"Attempting to reconnect to {Config.ServerAddress}...");
        await Task.Delay(delay).ConfigureAwait(false);
        return await ApSession!.ConnectAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Handle the connection task result and, if possible, attempt to connect to Archipelago
    /// </summary>
    public void HandleConnectionResult(Task<AP.Packets.RoomInfoPacket> connectResult)
    {
        ConnectTask = null;
        
        if (!connectResult.IsCompletedSuccessfully)
        {
            // Playing it safe - not sure if the exception will contain address information
            string uri = ApSession?.Socket.Uri.ToString() ?? "null!";
            HandleFailedConnection("connect", Config.HideConnectionDetails ? new Exception("Exception hidden because HideConnectionDetails is enabled!") : connectResult.Exception);
            if (Config.HideConnectionDetails)
                FeatureLogger.Error("Connection debug hidden due to HideConnectionDetails");
            else
            {
                FeatureLogger.Error($"Address: {Config.ServerAddress}:{Config.Port}");
                FeatureLogger.Error($"Calculated URI: {uri}");
            }
            return;
        }

        string? name = MidManager.GetProcessedGameData().Name;
        LoginTask = ApSession!.LoginAsync(
            name == null ? "GTFO" : $"GTFO ({name})",
            Config.Username,
            AP.Enums.ItemsHandlingFlags.AllItems,
            tags: GameTags.ToArray(),
            uuid: SessionGuid.ToString(),
            password: Config.HasPassword ? Config.Password : null,
            requestSlotData: true
        );
    }

    /// <summary>
    /// Callback to handle logging in
    /// </summary>
    /// <param name="connectResult">The connect task this is a callback for</param>
    protected void HandleLoginResult(Task<AP.LoginResult> loginTask)
    {
        LoginTask = null;

        if (!loginTask.IsCompletedSuccessfully)
        {
            HandleFailedConnection("login", Config.HideConnectionDetails ? new Exception("Exception hidden because HideConnectionDetails is enabled!") : loginTask.Exception);
            return;
        }

        AP.LoginResult loginResult = loginTask.Result;
        if (ApSession == null) throw new NullReferenceException(nameof(ApSession));

        if (!loginResult.Successful || loginResult is not AP.LoginSuccessful loginSuccessful)
        {
            HandleFailedConnection("authenticate", null);
            if (Config.HideConnectionDetails)
                FeatureLogger.Error("Connection debug is hidden because HideConnectionDetails is enabled.");
            else
            {
                if (loginResult is AP.LoginFailure failure)
                {
                    foreach (string message in failure.Errors)
                        FeatureLogger.Error($" -> Error response: {message}");
                }

                FeatureLogger.Error("Tried to connect with following information:");
                FeatureLogger.Error($"Username: {Config.Username}");
                FeatureLogger.Error(Config.HasPassword ? $"Password: {Config.Password}" : "No password");
                string? name = MidManager.GetProcessedGameData().Name;
                FeatureLogger.Error($"Game: {(name == null ? "GTFO" : $"GTFO ({name})")}");
            }
            return;
        }
        ApSession.SetClientState(AP.Enums.ArchipelagoClientState.ClientPlaying);

        try
        {
            RootSeed = (long)loginSuccessful.SlotData["RootSeed"];
            Expeditions = ExpeditionsFromNames(
                (loginSuccessful.SlotData["ExpeditionNames"] as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<string>>() 
                    ?? throw new NullReferenceException("Failed to retrieve expedition names from slot data")
            );
            WhitelistTags = new(
                (loginSuccessful.SlotData["WhitelistTags"] as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?
                    .Select(l => new RandomizationTag() { AsId = l }) 
                    ?? throw new NullReferenceException("Failed to retrieve whitelist tags from slot data")
            );
            BlacklistTags = new(
                (loginSuccessful.SlotData["BlacklistTags"] as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?
                    .Select(l => new RandomizationTag() { AsId = l }) 
                    ?? throw new NullReferenceException("Failed to retrieve blacklist tags from slot data")
            );

            object test = loginSuccessful.SlotData["RequiresSecondaries"];
            if (test is bool aTest)
                RequiresSecondaries = aTest;
            else
                RequiresSecondaries = 0 != (long)test;

            test = loginSuccessful.SlotData["RequiresOverloads"];
            if (test is bool bTest)
                RequiresOverloads = bTest;
            else
                RequiresOverloads = 0 != (long)test;

        }
        catch (Exception e)
        {
            FeatureLogger.Error("Encountered issues while retrieving slot data!");
            FeatureLogger.Exception(e);

            ApSession.Socket.DisconnectAsync();
            ApSession = null;
            return;
        }

        if (CurrentState == eState.HostConnecting)
        {
            CurrentState = eState.HostConnected;
            SetupMultiworld();
            ApSession.SetClientState(AP.Enums.ArchipelagoClientState.ClientReady);
        }
        else if (CurrentState == eState.HostReconnecting)
        {
            CurrentState = eState.HostConnected;
            ApSession.SetClientState(AP.Enums.ArchipelagoClientState.ClientPlaying);
        }
        FoundLocations.UnionWith(ApSession.Locations.AllLocationsChecked.Select(v => new LocationID() { AsId = v }));
    }

    /// <summary>
    /// Set up the multiworld using the current slot data
    /// </summary>
    protected void SetupMultiworld()
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        WhitelistTags.Add(gameData.Tag_Always); // Just in case
        BlacklistTags.Add(gameData.Tag_Never);  // Just in case

        FeatureLogger.Notice("Beginning graph traversal for new rundowns");
        HashSet<RandomizationTag> unlockTags = Expeditions.Select(e => e.Tag_UnlockItems_ByExpedition.SelfResolve()).ToHashSet();
        HashSet<RandomizationTag> goalTags = Expeditions.Select(e => e.Tag_GoalItems_ByExpedition.SelfResolve()).ToHashSet();

        if (!MidManager.DoGraphTraversal(gameData, true, unlockTags, goalTags, false))
        {
            FeatureLogger.Error("Graph traversal failed! Cancelling connection. View log for details.");
            ApSession?.Socket.DisconnectAsync();
            ApSession = null;
            CurrentState = eState.CleanState;
        }
        else
            FeatureLogger.Success("Graph traversal succeeded!");

        // Early overwrite so that callbacks can depend on the rundowns being there
        TryOverwriteRundowns();

        // Ensure everything is reset
        FoundRegions.Clear();
        FoundUnusedRegions.Clear();
        FoundLocations.Clear();
        ActualItemCounts.Clear();
        SessionItemCounts.Clear();
        ItemsInTerminalSystem.Clear();
        QueuedItemReplacements.Clear();


        // Identify reachable regions and locations
        HashSet<RegionID> reachableRegionIDs = gameData
            .GetAllRegions()
            .Where(r => r.Value.Reachable)
            .Select(r => r.Key)
            .ToHashSet();
        List<KeyValuePair<LocationID, Location>> reachableLocations = gameData
            .GetAllLocations()
            .Where(l => l.Value.OwningRegionIds.All(id => reachableRegionIDs.Contains(id)))
            .ToList();

        // Set up items
        var reachableItems = reachableLocations
            .Select(l => l.Value.ItemID)
            .Where(i => !i.IsNull)
            .Concat(gameData.GetAllFloatingItemIds())
            .Distinct();
        foreach (var id in reachableItems)
        {
            Item item = gameData.LookupItem(id);
            item.RandMode = TestRandomization(item);
        }

        // Set up locations
        foreach (var pair in reachableLocations)
        {
            pair.Value.RandMode = TestRandomization(pair.Value);
            if (pair.Value.RandData.IsEmpty && pair.Value.RandMode.IsRandomized)
                pair.Value.RandMode = new(RandTest.eType.UnusedEmptyLocation);
            else if (pair.Value.RandMode.IsRandomized)
            {
                if (pair.Value.ItemID.IsNull)
                    FeatureLogger.Error($"Encountered non-empty location with null item ID during randomization: {gameData.LookupTagDef(pair.Value.NameTag).Name}");
                else
                    pair.Value.RandMode = gameData.LookupItem(pair.Value.ItemID).RandMode;
            }
        }

        // Find and place floating items randomly in empty locations
        List<Location> emptyLocations;
        HashSet<ItemID> seenItems = new();
        Queue<ItemID> queuedItems = new();
        IEnumerable<ItemID> floatingItems;

        // Round 1 - Place floating progression items into priority spots
        emptyLocations = reachableLocations
            .Select(pair => pair.Value)
            .Where(l => l.RandMode.Type == RandTest.eType.UnusedEmptyLocation)
            .Where(l => l.RandData.IsPriority)
            .ToList();
        floatingItems = gameData
            .GetAllFloatingItemIds()
            .Select(id => new KeyedItem(id, gameData.LookupItem(id)))
            .Where(i => i.Item.RandMode.IsRandomized)
            .Where(i => i.RandData.IsProgression)
            .Select(i => i.ID);
        foreach (var id in floatingItems) queuedItems.Enqueue(id);
        foreach (var id in queuedItems) seenItems.Add(id);
        DistributeItems(emptyLocations, queuedItems);

        // Round 2 - Place the remaining progression items (if any) into all locations
        emptyLocations.Clear();
        emptyLocations.AddRange(reachableLocations
            .Select(pair => pair.Value)
            .Where(l => l.RandMode.Type == RandTest.eType.UnusedEmptyLocation)
            .Where(l => l.RandData.IsPriority || l.RandData.IsDefault)
        );
        DistributeItems(emptyLocations, queuedItems);

        // Round 3 - Place all items into all spots
        emptyLocations.Clear();
        emptyLocations.AddRange(reachableLocations
            .Select(pair => pair.Value)
            .Where(l => l.RandMode.Type == RandTest.eType.UnusedEmptyLocation)
            //.Where(l => l.RandData.IsAnyPriority)
        );
        floatingItems = gameData
            .GetAllFloatingItemIds()
            .Where(id => !seenItems.Contains(id))
            .Where(i => gameData.LookupItem(i).RandMode.IsRandomized);
        foreach (var id in floatingItems) queuedItems.Enqueue(id);
        foreach (var id in queuedItems) seenItems.Add(id);
        DistributeItems(emptyLocations, queuedItems);

        // Round 4 - Any remaining items are given
        if (queuedItems.Count > 0)
        {
            FeatureLogger.Warning($"Insufficient empty locations for selected floating items. Granting {queuedItems.Count} free items!");
            foreach (var item in queuedItems)
            {
                // Simulate losing the item if relevant, since our reachable location check below won't
                Item value = gameData.LookupItem(item);
                if (value.RandData.CollectedByDefault)
                    value.OnItemLost(this);

                // Pick up the item if not connected to AP - otherwise, AP will give it in the starting inventory
                if (CurrentState == eState.FakeConnect)
                    CollectItem(item);
            }
        }

        // OnItemLost callbacks for all randomized items
        foreach (var pair in reachableLocations)
        {
            if (!pair.Value.RandMode.IsRandomized) continue;
            if (pair.Value.ItemID.IsNull)
            {
                FeatureLogger.Error($"Encountered randomized empty location during OnItemLost callbacks: {gameData.LookupTagDef(pair.Value.NameTag).Name}");
                continue;
            }

            Item item = gameData.LookupItem(pair.Value.ItemID);
            if (!item.RandData.CollectedByDefault) continue;
            item.OnItemLost(this);
        }

        // Granting lose on start items if they were not randomized
        foreach (var id in gameData.GetAllFloatingItemIds())
        {
            Item item = gameData.LookupItem(id);
            if (item.RandMode.IsRandomized) continue;
            ActualItemCounts[id] = ActualItemCounts.GetValueOrDefault(id, 0) + 1;
        }

        // Scout locations - We could use RescoutLocations, but this skips discovering reachable locations
        if (ApSession != null)
        {
            // Request all reachable locations be scouted
            var randomizedLocations = reachableLocations.Where(l => l.Value.RandMode.IsRandomized);
            ApSession.Locations.ScoutLocationsAsync(
                AP.Enums.HintCreationPolicy.None,
                randomizedLocations.Select(l => l.Key.AsId).ToArray()
            ).ContinueWith(OnLocationsScouted);
        }

        // Some other setup
        RecalcWinItems();
        NotifyFoundRegion(gameData.MenuRegionName, PlayerManager.GetLocalPlayerAgent());
        OnStateChange?.Invoke(this);
    }

    /// <summary>
    /// Immediately recalculate the win items required by the current settings.
    /// This is relatively expensive, so avoid calling it if possible. It's currently
    ///  only called on connect and reconnect, when the item counts are reset.
    /// </summary>
    public void RecalcWinItems()
    {
        NeededWinItems.Clear();
        if (CurrentState == eState.ProxyClient) return; // Calculating this might be bad as a client

        Game.Data data = MidManager.GetProcessedGameData();

        // Get the set of tags matching our win items
        HashSet<RandomizationTag> whitelistTags = new() { data.Tag_GoalItems };
        HashSet<RandomizationTag> blacklistTags = new();

        foreach (var pair in data.GetAllExpeditions())
        {
            if (!Expeditions.Contains(pair.Value, new Expedition.Data.Comparer()))
                blacklistTags.Add(pair.Value.Tag_GoalItems_ByExpedition);
        }

        foreach (var exp in Expeditions)
        {
            if (!RequiresSecondaries && exp.HasSecondary)
                blacklistTags.Add(ObjectiveHandlers.SharedObjectiveHandler.GetSectorClearedItem(exp.GetLayer(LayerType.Secondary)).NameTag);

            if (!RequiresOverloads && exp.HasOverload)
                blacklistTags.Add(ObjectiveHandlers.SharedObjectiveHandler.GetSectorClearedItem(exp.GetLayer(LayerType.Overload)).NameTag);
        }

        // Check which items match that criteria
        var allItemIds = data.GetAllLocations().Select(l => l.Value.ItemID).Concat(data.GetAllFloatingItemIds());
        foreach (ItemID id in allItemIds)
        {
            if (id.IsNull) continue;
            Item item = data.LookupItem(id);
            if (item.RequiredExpedition != null && !Expeditions.Contains(item.RequiredExpedition)) continue;
            if (data.AnyTagMatches(blacklistTags, item)) continue;
            if (!data.AnyTagMatches(whitelistTags, item)) continue;
            NeededWinItems.Add(id);
        }

        // Remove any items we've already collected and check for a win
        foreach (var id in SessionItemCounts.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)))
            NeededWinItems.Remove(id);
        if (NeededWinItems.Count == 0)
        {
            ApSession?.SetClientState(AP.Enums.ArchipelagoClientState.ClientGoal);
            FeatureLogger.Success("Congratulations, you have won the game!");
        }
    }

    /// <summary>
    /// Fully reset the state. Kick the user to the menu, reset the menu,
    ///  disconnect state, and all that jazz
    /// </summary>
    public void Reset()
    {
        Globals.Global.ActiveRundownIds = null;
        ApSession?.Socket.DisconnectAsync();
        ApSession = null;
        MainMenuGuiLayer.Current.PageRundownNew.ResetElements();
        if (SNetwork.SNet.Lobbies.IsInLobby)
            SNetwork.SNet.Lobbies.LeaveLobby();
        CurrentState = eState.CleanState;
        MainMenuGuiLayer.Current.PageRundownNew.m_selectionIsRevealed = true;
    }

    /// <summary>
    /// Test the randomization properties of a location
    /// </summary>
    /// <param name="loc">The location to test</param>
    /// <returns>A RandTest which tells how this location is or is not randomized</returns>
    public RandTest TestRandomization(Location loc)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        RandTest.eType result;

        if (gameData.AnyTagMatches(BlacklistTags, loc))
            result = RandTest.eType.LocationBlacklisted;
        else if (!gameData.AnyTagMatches(WhitelistTags, loc))
            result = RandTest.eType.LocationNotWhitelisted;
        else
            result = RandTest.eType.Randomized;

        return new RandTest(result);
    }

    /// <summary>
    /// Test the randomization properties of an item
    /// </summary>
    /// <param name="item">The item to test</param>
    /// <returns>A RandTest which tells how this item is or is not randomized</returns>
    public RandTest TestRandomization(Item item)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        RandTest.eType result;

        if ((item.RequiredExpedition != null) && !Expeditions.Contains(item.RequiredExpedition, new Expedition.Data.Comparer()))
            result = RandTest.eType.ItemExpeditionNotRandomized;
        else if (gameData.AnyTagMatches(BlacklistTags, item))
            result = RandTest.eType.ItemBlacklisted;
        else if (!gameData.AnyTagMatches(WhitelistTags, item))
            result = RandTest.eType.ItemNotWhitelisted;
        else
            result = RandTest.eType.Randomized;

        if (item.RandData.IsRandomLike)
            result |= RandTest.eType.Randomlike;
        return new RandTest(result);
    }

    /// <summary>
    /// Notify the state tracker that a region has been found by region name
    /// </summary>
    /// <param name="name">Name of the region</param>
    /// <param name="player">Player who found the region, or null if too inconvenient to identify</param>
    public void NotifyFoundRegion(string name, PlayerAgent? player)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        if (!gameData.TryLookupRegion(name, out KeyedRegion region))
        {   // We'll often find unregistered regions
            if (!FoundUnusedRegions.Add(name))
                FeatureLogger.Debug($"Ignoring region because it is not registered: {name}");
            return;
        }
        NotifyFoundRegion(region.ID, player);
    }

    /// <summary>
    /// Notify the state tracker that a region has been found by region ID
    /// </summary>
    /// <param name="name">ID of the region which was found</param>
    /// <param name="player">Player who found the region, or null if too inconvenient to identify</param>
    public void NotifyFoundRegion(RegionID regionId, PlayerAgent? player)
    {
        Game.Data data = MidManager.GetProcessedGameData();
        ReadOnlyRegion region = data.LookupRegion(regionId);

        if (!FoundRegions.Add(regionId))
            return;

        FeatureLogger.Debug($"Discovered region [{regionId.AsId}] {region.Name}");
        SendInteraction(pArchipelagoInteraction.eType.CheckRegion, value: regionId.AsId);

        // Check for auto-discover locations
        bool isFoundLocation(LocationID locID)
        {
            if (FoundLocations.Contains(locID)) return false;
            var loc = data.LookupLocation(locID);
            if (!loc.RandData.IsAutoDiscovered) return false;
            if (loc.OwningRegionIds.Any(r => !FoundRegions.Contains(r))) return false;
            return true;
        }

        var locs = region.ConnectedLocationIds.Where(isFoundLocation).ToArray();
        if (locs.Length > 0) NotifyFoundLocations(locs, player);
    }

    /// <summary>
    /// Notify the state tracker that a single location has been found / "checked"
    /// </summary>
    /// <param name="location">The location which has been checked</param>
    /// <param name="player">The player who found the location, or null if too inconvenient to identify</param>
    /// <param name="force">Intended for debug. If true, forces the locations to be rediscovered, obtaining another copy of their item</param>
    /// <returns>The location object (for convenience)</returns>
    public Location NotifyFoundLocation(LocationID id, PlayerAgent? player, bool force = false)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        Location location = gameData.LookupLocation(id);

        if (!FoundLocations.Add(id)) return location;

        FeatureLogger.Debug($"Discovered Location: [{id.AsId}] {gameData.LookupTagDef(location.NameTag).Name}");
        SendInteraction(pArchipelagoInteraction.eType.CheckLocation, value: id.AsId);

        if (location.RandMode.IsTreatedAsRandom && CurrentState == eState.FakeConnect)
            CollectItem(location.ItemID, id, player);

        if (ApSession != null) // We must notify, but we'll reject the item on inbound
            ApSession.Locations.CompleteLocationChecksAsync(id.AsId).ContinueWith(OnLocationChecksCompleted);
        
        UpdateLocationCounts();
        return location;
    }

    /// <summary>
    /// Notify the state tracker that many locations have been found / "checked"
    /// </summary>
    /// <param name="ids">IDs of the locations</param>
    /// <param name="player">The player who found the locations, or null if too inconvenient to identify</param>
    /// <param name="force">Intended for debug; set to true to force the locations to be rediscovered, obtaining another copy of their items</param>
    public void NotifyFoundLocations(IEnumerable<LocationID> ids, PlayerAgent? player, bool force = false)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        List<long> networkIds = new();
        foreach (var id in ids)
        {
            Location loc = gameData.LookupLocation(id);

            if (!FoundLocations.Add(id) || force)
                continue;

            FeatureLogger.Debug($"Discovered Location: [{id.AsId}] {gameData.LookupTagDef(loc.NameTag).Name}");
            SendInteraction(pArchipelagoInteraction.eType.CheckLocation, value: id.AsId);

            if (loc.RandMode.IsTreatedAsRandom && CurrentState == eState.FakeConnect)
                CollectItem(loc.ItemID, id, player);

            if (ApSession != null)
                networkIds.Add(id.AsId);
        }

        if (ApSession != null && networkIds.Count > 0)
            ApSession.Locations.CompleteLocationChecksAsync(networkIds.ToArray()).ContinueWith(OnLocationChecksCompleted);
        UpdateLocationCounts();
    }

    /// <summary>
    /// Distributes the provided items evenly into the provided location list.
    /// </summary>
    /// <param name="locations">The locations to distribute to</param>
    /// <param name="items">The items to distribute</param>
    /// <remarks>
    /// Distributes only as many items as there is space to distribute.
    /// The locations in the list will be modified, and the stack of items will be modified.
    /// </remarks>
    protected void DistributeItems(IReadOnlyList<Location> locations, Queue<ItemID> items)
    {
        if (items.Count == 0) return;

        // Using double here because that's what python uses by default
        double count = 0.2; // .2 instead of 0 because of precision issues
        double step = (items.Count >= locations.Count) ? 1.0 : (items.Count / (double)locations.Count);
        long locCount = locations.Count; // Micro optimization
        for (long i = 0; i < locCount; i++)
        {
            count += step;
            if (count >= 1.0)
            {
                int index = (int)((i + Math.Abs(RootSeed)) % locCount);
                if (!locations[index].ItemID.IsNull)
                    FeatureLogger.Error("Overwriting item during floating items placement!"); // In case there's an error in my math
                locations[index].ItemID = items.Dequeue();
                locations[index].RandMode = new(RandTest.eType.Randomized);
                count -= 1.0;

                Game.Data data = MidManager.GetProcessedGameData();
                data.TryLookupLocation(locations[index].NameTag, out var loc);
                //FeatureLogger.Warning($"Distributed item {locations[index].ItemID} into location {loc.ID}");
            }
        }
    }

    /// <summary>
    /// If synced, overwrite the existing rundown list with new ones for the expedition.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown for several misc edge cases which should never occur</exception>
    /// <remarks>
    /// TODO: Move this to its own handler; add functionality to enable / disable
    /// </remarks>
    protected void TryOverwriteRundowns()
    {
        if (CurrentState == eState.CleanState) return;

        // Find all expeditions and create copies to be loaded in
        Game.Data gameData = MidManager.GetProcessedGameData();
        Queue<ExpeditionInTierData> newExpeditions = new();

        foreach (var expData in Expeditions)
        {
            ExpeditionInTierData newExpedition = expData.Expedition.MemberwiseClone().Cast<ExpeditionInTierData>();
            newExpedition.Descriptive.Prefix = expData.ExpeditionName;
            newExpedition.Descriptive.SkipExpNumberInName = true;
            newExpedition.ExcludeFromMatchmaking = true;
            newExpedition.ExcludeFromProgression = false;
            newExpedition.Descriptive.ProgressionVisualStyle = eProgressionVisualStyle.Normal;
            newExpedition.Accessibility = eExpeditionAccessibility.AlwaysAllow; // Will be overwritten by expedition unlock item
            newExpedition.HideOnLocked = false; // R8 right-side expeditions
            newExpedition.UnlockedByExpedition = new() { Tier = eRundownTier.TierA, Exp = 0 };
            newExpeditions.Enqueue(newExpedition);
        }

        // This section mostly deals with distributing expeditions into separate rundowns such that it looks pretty and unique
        int numRundowns;
        if (newExpeditions.Count < 12)
            numRundowns = 1;
        else
            numRundowns = (int)Math.Ceiling(newExpeditions.Count * .1);
        numRundowns = Math.Min(numRundowns, MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections.Count);

        System.Random random = new(RootSeed.GetHashCode());
        List<RundownDataBlock> newRundowns = Enumerable.Range(1, numRundowns).Select(i => RundownDataBlock.GetBlock($"Archipelago {i}")).ToList();

        for (int i = 0; i < numRundowns; i++)
        {
            var rundown = newRundowns[i];
            if (newRundowns[i] == null)
            {
                newRundowns[i] = new() { name = $"Archipelago {i + 1}" };
                RundownDataBlock.AddBlock(newRundowns[i]);
                rundown = newRundowns[i];

                rundown.NeverShowRundownTree = false;
                rundown.UseTierUnlockRequirements = false;
                rundown.VanityItemLayerDropDataBlock = 0;

                rundown.ReqToReachTierA = new()
                {
                    AllClearedSectors = 0,
                    MainSectors = 0,
                    SecondarySectors = 0,
                    ThirdSectors = 0
                };

                rundown.ReqToReachTierB = rundown.ReqToReachTierA;
                rundown.ReqToReachTierC = rundown.ReqToReachTierA;
                rundown.ReqToReachTierD = rundown.ReqToReachTierA;
                rundown.ReqToReachTierE = rundown.ReqToReachTierA;

                static Localization.LocalizedText MakeText(string name, string text)
                {
                    LanguageData data = new()
                    {
                        ShouldTranslate = false,
                        Translation = text
                    };
                    TextDataBlock block = new()
                    {
                        SkipLocalization = true,
                        MachineTranslation = true,
                        English = text,
                        Description = "Text auto-generated by Archipelago",
                        CharacterMetaData = 1,
                        French = data,
                        Italian = data,
                        German = data,
                        Spanish = data,
                        Russian = data,
                        Portuguese_Brazil = data,
                        Polish = data,
                        Japanese = data,
                        Korean = data,
                        Chinese_Traditional = data,
                        Chinese_Simplified = data,
                        ExportVersion = 6, // I'm not really sure what these are for
                        ImportVersion = 6, // I'm not really sure what these are for
                        name = name,
                        internalEnabled = true,
                    };
                    TextDataBlock.AddBlock(block);
                    return new()
                    {
                        Id = block.persistentID,
                        OldId = 0,
                        UntranslatedText = text,
                    };
                }

                rundown.StorytellingData = new()
                {
                    Title = MakeText($"{rundown.name} - Title", $"{rundown.name}\nMULTIVERSE"),
                    ExternalExpTitle = MakeText($"{rundown.name} - ExpTitle", rundown.name),
                    SurfaceDescription = MakeText($"{rundown.name} - Surface Description", ""
                      + "\nWORK WITH FELLOW PRISONERS TO RECOVER SECURE ASSETS AND COMPLETE THE RUNDOWN"
                      + "\n\n-----------------------------------"
                      + "\n\nCOORDINATE ACROSS THE MULTIWORLD TO MINIMIZE CASUALTIES AND ACCELERATE PRIORITIES"
                      + "\n\n-----------------------------------"
                      + "\n\nCOLLECT WARDEN ARTIFACTS TO SUPPLEMENT SUCCESS MARGINS AT THE COST OF EMOTIONAL DURESS"
                    ),
                    SurfaceIconPosition = new(0, 0),
                    Visuals = new() { ColorBackground = Color.magenta },
                    TextLog = MakeText($"{rundown.name} - Log", "THIS IS ARCHIPELAGO"),
                    TextLogPos = new(0, 0),
                };
            }

            (rundown.TierA ??= new()).Clear();
            (rundown.TierB ??= new()).Clear();
            (rundown.TierC ??= new()).Clear();
            (rundown.TierD ??= new()).Clear();
            (rundown.TierE ??= new()).Clear();
        }

        // We're looking at all expedition slots and weighting them. Higher weight = more likely to be disqualified later
        const float rw = 1.3f; // Weighting of the rundown index
        const float tw = 2f; // Weighting of the tier
        const float iw = 3f; // Weighting of the expedition index

        const int numTiers = 5;
        const int numIndicies = 5;

        // The order matters here. It's set up to go through all A1 slots, then all A2 slots, etc..
        float middle = numRundowns * .5f + .5f;
        List<float> weights = Enumerable.Repeat(1f, 1)
            .SelectMany(w => Enumerable.Range(1, numTiers).Select(wt => w * MathF.Pow(tw, MathF.Abs(wt - 2.5f) * .4f)))
            .SelectMany(w => Enumerable.Range(1, numRundowns).Select(wr => w * MathF.Pow(rw, MathF.Abs(wr - middle) / middle)))
            .SelectMany(w => Enumerable.Range(0, numIndicies).Select(wi => w * MathF.Pow(iw, MathF.Abs(wi - 2f) * .5f)))
            .Select(w => w / (rw * tw * iw)) // Normalize 0 to 1
            .ToList();
        float totalWeight = weights.Sum();

        // We now randomly eliminate as many expedition slots as needed
        const float throwoutChance = .3f; // Chance to make slot empty instead of just disabled
        for (int j = newExpeditions.Count; j < weights.Count; j++)
        {
            float sample = totalWeight * random.NextSingle();
            float sum = 0f;
            for (int k = 0; k < weights.Count; k++)
            {
                sum += MathF.Max(weights[k], 0f);
                if (sum > sample)
                {
                    totalWeight -= weights[k];
                    if (weights[k] > throwoutChance)
                        weights[k] = -2f;
                    else
                        weights[k] = -1f;
                    break;
                }
            }
        }

        // Populate the slots with expeditions!
        for (int tier = 0; tier < numTiers; tier++)
        {
            for (int rundownIndex = 0; rundownIndex < numRundowns; rundownIndex++)
            {
                for (int index = 0; index < numIndicies; index++)
                {
                    int i = tier * numRundowns * numIndicies
                        + rundownIndex * numIndicies + index;

                    ExpeditionInTierData expedition;
                    if (weights[i] > 0f)
                        expedition = newExpeditions.Dequeue();
                    else
                        continue; // Throwout

                    switch (tier)
                    {
                        case 0:
                            newRundowns[rundownIndex].TierA.Add(expedition);
                            break;
                        case 1:
                            newRundowns[rundownIndex].TierB.Add(expedition);
                            break;
                        case 2:
                            newRundowns[rundownIndex].TierC.Add(expedition);
                            break;
                        case 3:
                            newRundowns[rundownIndex].TierD.Add(expedition);
                            break;
                        case 4:
                            newRundowns[rundownIndex].TierE.Add(expedition);
                            break;
                        default:
                            throw new NotSupportedException("Attempted to place rundown in a tier outside A-E");
                    }
                }
            }
        }

        if (newExpeditions.Count > 0)
            throw new NotSupportedException("Failed to populate the correct number of expeditions");

        // Clean up the visuals
        static IEnumerable<Tuple<int, TierVisualData>> GetVisualData(RundownDataBlock rundown)
        {
            yield return Tuple.Create(rundown.TierA.Count, rundown.StorytellingData.Visuals.TierAVisuals);
            yield return Tuple.Create(rundown.TierB.Count, rundown.StorytellingData.Visuals.TierBVisuals);
            yield return Tuple.Create(rundown.TierC.Count, rundown.StorytellingData.Visuals.TierCVisuals);
            yield return Tuple.Create(rundown.TierD.Count, rundown.StorytellingData.Visuals.TierDVisuals);
            yield return Tuple.Create(rundown.TierE.Count, rundown.StorytellingData.Visuals.TierEVisuals);
        }
        foreach (var pair in newRundowns.SelectMany(GetVisualData))
        {
            pair.Item2.Scale = pair.Item1 switch
            {
                0 => .4f + .4f * random.NextSingle(),
                1 => .1f,
                2 => .35f + .2f * random.NextSingle(),
                3 => .5f + .22f * random.NextSingle(),
                4 => .675f + .1f * random.NextSingle(),
                5 => .72f + .18f * random.NextSingle(),
                _ => throw new NotSupportedException("More than 5 expeditions in a single tier!")
            };
            // Note: ScaleYModifier appears to have no effect
            pair.Item2.Color = Color.HSVToRGB(MathF.Pow(random.NextSingle(), .125f), 1f, 1f);
        }

        foreach (var icon in MainMenuGuiLayer.Current.PageRundownNew.m_expIconsAll)
        {
            icon.SetStatusTextVisible(true);
        }

        // Set up the menu so everything is correctly displayed
        Il2CppSystem.Collections.Generic.List<uint> ids = new(newRundowns.Count);
        foreach (var rundown in newRundowns) ids.Add(rundown.persistentID);
        Globals.Global.RundownIdToLoad = ids[0];
        Globals.Global.ActiveRundownIds = ids.ToArray().Cast<Il2CppStructArray<uint>>();
        GameSetupDataBlock.GetAllBlocks()[0].RundownIdsToLoad = ids;

        // Moving rundown 7 from position 2 to position 7 (so rundowns load in order)
        if (MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections.Count == 8)
        {
            var selections = MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections;
            var positions = MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelectionPositions;
            for (int i = 1; i < 6; i++)
            {
                (selections[i + 1], selections[i]) = (selections[i], selections[i + 1]);
                (positions[i + 1], positions[i]) = (positions[i], positions[i + 1]);
            }
        }

        // Placing new rundowns into the selections
        for (int i = 0; i < numRundowns; i++)
        {
            MainMenuGuiLayer.Current.PageRundownNew.UpdateRundownSelectionButton(
                MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections[i],
                newRundowns[i].persistentID
            );
        }

        // Because we modify this in ModifyRundownMenuPatch, we need to revert it now.
        // If we don't, we get a null refernece exception as GTFO tries to clean up a non-showing rundown screen
        MainMenuGuiLayer.Current.PageRundownNew.m_selectionIsRevealed = false;

        // Shows the rundowns menu.
        if (numRundowns == 1)
        {
            // Update the spawned rundown based on the new data in Globals.Global
            MainMenuGuiLayer.Current.PageRundownNew.ResetRundownItems();
            MainMenuGuiLayer.Current.PageRundownNew.m_currentRundownData = newRundowns[0];
            MainMenuGuiLayer.Current.PageRundownNew.PlaceRundown(newRundowns[0]);
            MainMenuGuiLayer.Current.PageRundownNew.UpdateHeaderText();

            // The function normally assigned to the Connect button for single-rundown menus
            MainMenuGuiLayer.Current.PageRundownNew._Setup_b__102_0(0);
        }
        else
        {
            // I believe this is the lambda assigned to the SelectRundown button. Either way, it works :)
            MainMenuGuiLayer.Current.PageRundownNew._Setup_b__102_3(0);
        }
    }

    /// <summary>
    /// Ensures location checks are completed successfully
    /// </summary>
    /// <param name="task">The location check task</param>
    protected void OnLocationChecksCompleted(Task task)
    {
        if (task.IsCompletedSuccessfully)
            return;

        FeatureLogger.Error("Failed to complete location checks! Retry not currently implemented...");
    }

    /// <summary>
    /// Immediately attempt to scout all randomized locations
    /// </summary>
    public void RescoutLocations()
    {
        Game.Data gameData = MidManager.GetProcessedGameData();

        HashSet<RegionID> reachableRegions = gameData
            .GetAllRegions()
            .Where(r => r.Value.Reachable)
            .Select(r => r.Key)
            .ToHashSet();

        IEnumerable<KeyValuePair<LocationID, Location>> locations = gameData
            .GetAllLocations()
            .Where(l => l.Value.OwningRegionIds.All(reachableRegions.Contains));
        var arr = locations.Select(l => l.Key.AsId).ToArray();

        FeatureLogger.Debug($"Sending location scout request for {arr.Length} locations");
        ApSession!.Locations.ScoutLocationsAsync(AP.Enums.HintCreationPolicy.None, arr)
            .ContinueWith(OnLocationsScouted);
    }

    /// <summary>
    /// Internal callback for when locations are scouted
    /// </summary>
    /// <param name="task">The task the callback is for</param>
    protected void OnLocationsScouted(Task<Dictionary<long, AP.Models.ScoutedItemInfo>> task)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        if (!task.IsCompletedSuccessfully)
        {
            FeatureLogger.Error("Failed to scout locations!");
            RescoutLocations();
        }
        else
        {
            FeatureLogger.Debug($"Successfully scouted {task.Result.Count} locations");
            foreach (var pair in task.Result)
            {
                Location location = gameData.LookupLocation(new LocationID() { AsId = pair.Value.LocationId });
                location.ScoutedItem = pair.Value;
            }
        }
    }

    /// <summary>
    /// Access the the list of collected items
    /// </summary>
    public IReadOnlyDictionary<ItemID, int> CollectedItemCounts => ActualItemCounts;

    /// <summary>
    /// Check if a specific location has been collected
    /// </summary>
    public bool HasLocation(RandomizationTag name)
    {
        if (MidManager.GetProcessedGameData().TryLookupLocation(name, out var loc))
            return HasLocation(loc.ID);
        return false;
    }

    /// <summary>
    /// Check if a specific location has been collected
    /// </summary>
    public bool HasLocation(LocationID id) => FoundLocations.Contains(id);

    /// <summary>
    /// Immediately collect an item
    /// Only checks against queued items; all other checks are skipped
    /// </summary>
    /// <param name="id">The item to collect</param>
    /// <param name="sourceLocationId">ID fo the location this item was found in, if found locally (non-randomized)</param>
    /// <param name="player">The player who found the item, if applicable and convenient to identify</param>
    /// <param name="skipInteraction">Used internally; prevents the interaction from being sent to all other players</param>
    public void CollectItem(ItemID id, LocationID sourceLocationId = default, PlayerAgent? player = null, bool skipInteraction = false)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();

        if (QueuedItemReplacements.TryGetValue(id, out var replacements))
        {
            if (replacements.Count < 1) 
                throw new NotSupportedException();
            if (replacements.Count == 1)
                QueuedItemReplacements.Remove(id);
            id = replacements.Dequeue();
        }

        Item item = gameData.LookupItem(id);
        FeatureLogger.Debug($"Collecting item [{id.AsId}] {gameData.LookupTagDef(item.NameTag).Name}");
        int newCount = ActualItemCounts.GetValueOrDefault(id, 0) + 1;
        ActualItemCounts[id] = newCount;
        item.OnItemObtained(this, sourceLocationId, player);

        if (!skipInteraction)
            SendInteraction(pArchipelagoInteraction.eType.CollectItem, value: id.AsId, count: (ushort)newCount);
    }

    /// <summary>
    /// Immediately collect targetItem and uncollect sourceItem. 
    /// Next time targetItem would be obtained, sourceItem is obtained instead.
    /// </summary>
    /// <param name="sourceItem">The item to be uncollected and queued for collection</param>
    /// <param name="targetItem">The item to immediately collect</param>
    /// <remarks>
    /// This is intended for "progressive" items. If you receive an item that depends on another item,
    /// you can immediately swap in for the item it depends on in order to give the player 
    /// </remarks>
    public void ReplaceItem(ItemID sourceItem, ItemID targetItem)
    {
        if (QueuedItemReplacements.TryGetValue(targetItem, out var replacements))
            replacements.Enqueue(sourceItem);
        else
        {
            replacements = new();
            replacements.Enqueue(sourceItem);
            QueuedItemReplacements[targetItem] = replacements;
        }

        UncollectItem(sourceItem);
        CollectItem(targetItem, skipInteraction: true);
    }

    /// <summary>
    /// Lose or "uncollect" an item. Not all items support being uncollected
    /// </summary>
    /// <param name="id">The item to uncollect</param>
    public void UncollectItem(ItemID id)
    {
        Item item = MidManager.GetProcessedGameData().LookupItem(id);
        int currentCount = ActualItemCounts.GetValueOrDefault(id, 0);
        if (currentCount <= 0)
        {
            FeatureLogger.Error($"Cannot uncollect item; it is not currently collected! Item: {MidManager.GetProcessedGameData().LookupTagDef(item.NameTag).Name}");
            return;
        }
        ActualItemCounts[id] = currentCount - 1;
        item.OnItemLost(this);
    }

    public void AddItemToTerminal(Item item)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        if (gameData.TryLookupItem(item.NameTag, out KeyedItem pair))
            AddItemToTerminal(pair.ID);
        else
            FeatureLogger.Error($"Failed to add item to terminal: {gameData.LookupTagDef(item.NameTag).Name}");
    }

    /// <summary>
    /// Add an item to the terminal system.
    /// The terminal system is cleared each expedition; re-add the item each time if needed.
    /// </summary>
    /// <param name="item">The item to add</param>
    public void AddItemToTerminal(ItemID item)
    {
        // Lazy seed generation
        System.Random random = new(Tuple.Create(RootSeed, item).GetHashCode());
        char r() // Picks a random character
        {
            int choice = (int)(36d * random.NextDouble());
            return (char)(choice >= 10 ? 'A' + (choice - 10) : '0' + choice);
        }

        // Generate a code which doesn't match any other in the terminal system
        string newCode;
        do
        {   
            newCode = $"{r()}{r()}{r()}-{r()}{r()}{r()}-{r()}{r()}{r()}";
        } while (ItemsInTerminalSystem.Any(pair => pair.Item2 == newCode));

        ItemsInTerminalSystem.Add(Tuple.Create(item, newCode));
    }

    /// <summary>
    /// Standard update method used to check for received items
    /// </summary>
    public override void Update()
    {
        //if (Input.GetKeyDown(KeyCode.J))
        //{
        //    Game.Data data = MidManager.GetProcessedGameData();
        //    KeyedItem item = LockGearHandler.GetGearItem(data, PlayerOfflineGearDataBlock.GetBlock(4));
        //    CollectItem(item.ID);
        //}
        //else if (Input.GetKey(KeyCode.P))
        //{
        //    Game.Data data = MidManager.GetProcessedGameData();
        //    foreach (var item in CollectedItemCounts)
        //    {
        //        if (item.Value == 0) continue;
        //        string name = data.LookupTagDef(data.LookupItem(item.Key).NameTag).Name;
        //        if (name.Contains("Gear"))
        //            UncollectItem(item.Key);
        //    }
        //}

        if (ConnectTask?.IsCompleted ?? false)
            HandleConnectionResult(ConnectTask);
        if (LoginTask?.IsCompleted ?? false)
            HandleLoginResult(LoginTask);

        if (ApSession == null)
            return;

        if (!ApSession.Socket.Connected && ConnectTask == null)
        {   // Begin the reconnect process
            FeatureLogger.Error("Disconnected from Archipelago! Attempting reconnect...");
            CurrentState = eState.HostReconnecting;
            ConnectTask = ApSession.ConnectAsync();
        }

        while (ApSession.Items.Any())
        {
            try
            {
                AP.Models.ItemInfo itemInfo = ApSession.Items.DequeueItem();

                // Add to our session count.
                ItemID id = new ItemID() { AsId = itemInfo.ItemId };
                int newCount = SessionItemCounts.GetValueOrDefault(id, 0) + 1;
                SessionItemCounts[id] = newCount;

                // Check if this is a needed item, and, if it is, update our win conditions
                if (NeededWinItems.Remove(id) && NeededWinItems.Count == 0)
                {
                    ApSession.SetClientState(AP.Enums.ArchipelagoClientState.ClientGoal);
                    FeatureLogger.Success("Congratulations, you have won the game!");
                }

                // If the session count is now greater, we also add to our actual count
                if (newCount > ActualItemCounts.GetValueOrDefault(id, 0))
                {
                    // If this item is from our game and is not randomized, we do not process receiving it
                    if (itemInfo.Player.Slot == ApSession.Players.ActivePlayer.Slot)
                    {
                        LocationID locId = new() { AsId = itemInfo.LocationId };
                        Location loc = MidManager.GetProcessedGameData().LookupLocation(locId);
                        if (!loc.RandMode.IsRandomized)
                            continue;
                    }

                    // Actually collecting the item
                    CollectItem(id);
                }
            }
            catch (Exception ex)
            {
                // Using a blue notice to help add breaks between exceptions, since they blend together so well
                FeatureLogger.Notice("Encountered unexpected exception while receiving items from Archipelago:");
                FeatureLogger.Exception(ex);
            }
        }
    }

    /// <summary>
    /// Convenience helper to write a log message for the player.
    /// These messages are placed in the local player's chat.
    /// </summary>
    public static void LogForPlayer(string message)
    {
        var logs = Enumerable.Empty<PUI_GameEventLog>()
            .Append(MainMenuGuiLayer.Current.PageLoadout?.m_gameEventLog)
            .Append(MainMenuGuiLayer.Current.PageMap?.m_gameEventLog)
            .Append(GuiManager.PlayerLayer?.m_gameEventLog)
        ;
        
        foreach (var log in logs)
        {
            if (log == null) continue;
            log.AddLogItem(message);

            // This part is important. It somehow prevents crashes :)
            log.UpdateHeightOffset();
        }
    }

    /// <summary>
    /// Prevent all chats from clearing themselves when the level ends.
    /// This results in chat messages persisting between expeditions.
    /// </summary>
    [ArchivePatch(typeof(PUI_GameEventLog), nameof(PUI_GameEventLog.OnLevelCleanup))]
    public static class PUI_GameEventLog__OnLevelCleanup__Patch
    {
        public static bool Prefix() => false;
    }

    /// <summary>
    /// React to players starting a new expedition - specifically, invoking the relevant handler on items
    /// </summary>
    [ArchivePatch(typeof(WardenObjectiveManager), nameof(WardenObjectiveManager.OnLocalPlayerStartExpedition))]
    public static class WardenObjectiveManager__OnLocalPlayerStartExpedition__Patch
    {
        public static void Postfix()
        {
            StateTracker self = ArchipelagoFeatureHelper.GetFeature<StateTracker>();
            self.ApSession?.SetClientState(AP.Enums.ArchipelagoClientState.ClientPlaying);

            Expedition.Data? expeditionData = Expedition.Data.TryFromCurrentExpedition();
            if (expeditionData == null)
                FeatureLogger.Error("Failed to identify expedition on drop; skipping relevant events");
            else
                FeatureLogger.Notice($"Started expedition: {expeditionData.ExpeditionName}");

            self.ItemsInTerminalSystem.Clear();
            Game.Data gameData = self.MidManager.GetProcessedGameData();
            if (expeditionData is not null)
            {
                foreach (var id in self.ActualItemCounts.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)))
                {
                    Item item = gameData.LookupItem(id);
                    item.OnStartExpeditionWithItem(self, expeditionData);
                }
            }
            else
            {
                FeatureLogger.Error(" -> Items not added to terminal system!");
            }
        }
    }

    /// <summary>
    /// Replace artifact heat with the unfound location count
    /// </summary>
    [ArchivePatch(typeof(CM_ExpeditionIcon_New), nameof(CM_ExpeditionIcon_New.SetArtifactHeat))]
    public static class CM_ExpeditionIcon_New__SetArtifactHeat__Patch
    {
        public static bool Prefix(CM_ExpeditionIcon_New __instance)
        {
            StateTracker stateTracker = StateTracker.Get();
            Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();
            if (!gameData.TryLookupExpedition(__instance.DataBlock.Descriptive.Prefix, out var data))
            {
                FeatureLogger.Error($"Failed to lookup expedition for location count: {__instance.DataBlock.Descriptive.Prefix}");
                return true;
            }

            HashSet<RegionID> traversedRegions = new();
            HashSet<LocationID> locations = new();
            void searchRecusive(RegionID region)
            {
                if (!traversedRegions.Add(region)) return;
                var r = gameData.LookupRegion(region);
                foreach (var loc in r.ConnectedLocationIds) locations.Add(loc);
                foreach (var path in r.ConnectedPaths)
                    searchRecusive(gameData.LookupPath(path).EndingRegion);
            }
            searchRecusive(data.StartingRegion);

            List<KeyedLocation> randomizedLocations = locations
                .Select(id => new KeyedLocation(id, gameData.LookupLocation(id)))
                .Where(l => l.Location.RandMode.IsTreatedAsRandom)
                .Where(l => !(l.RandData.IsExcluded || l.RandData.IsTrap))
                .ToList();
            int foundCount = randomizedLocations.Count(l => stateTracker.HasLocation(l.ID));

            __instance.m_artifactHeatText.SetText($"Found Items: {foundCount} / {randomizedLocations.Count}");

            Color color;
            if (randomizedLocations.Count == 0)
                color = Color.grey;
            else if (foundCount == randomizedLocations.Count)
                color = new Color(0f, 1f, 0f);
            else
                color = Color.white;
            __instance.m_artifactHeatText.SetFaceColor(color);

            return false;
        }
    }

    /// <summary>
    /// Little helper which safely updates location counts on all expedition icons
    /// </summary>
    public static void UpdateLocationCounts()
    {
        IEnumerable<CM_MenuBar?> items = [
            //MainMenuGuiLayer.Current.PageBelowSpec?.m_menuBar,
            //MainMenuGuiLayer.Current.PageBugHunters?.m_menuBar,
            //MainMenuGuiLayer.Current.PageCredits?.m_menuBar,
            //MainMenuGuiLayer.Current.PageCustomExpeditionSuccess?.m_menuBar,
            //MainMenuGuiLayer.Current.PageEmpty?.m_menuBar,
            //MainMenuGuiLayer.Current.PageEULA?.m_menuBar,
            //MainMenuGuiLayer.Current.PageExpeditionFail?.m_menuBar,
            //MainMenuGuiLayer.Current.PageExpeditionSuccess?.m_menuBar,
            MainMenuGuiLayer.Current.PageGearDetails?.m_menuBar,
            //MainMenuGuiLayer.Current.PageIntro?.m_menuBar,
            //MainMenuGuiLayer.Current.PageIntro?.m_menuBar,
            MainMenuGuiLayer.Current.PageLoadout?.m_menuBar,
            //MainMenuGuiLayer.Current.PageLogos?.m_menuBar,
            //MainMenuGuiLayer.Current.PageMap?.m_menuBar,
            //MainMenuGuiLayer.Current.PageMatchmaking?.m_menuBar,
            //MainMenuGuiLayer.Current.PageObjectives?.m_menuBar,
            MainMenuGuiLayer.Current.PagePlayerDetails?.m_menuBar,
            MainMenuGuiLayer.Current.PageRundown?.m_menuBar,
            MainMenuGuiLayer.Current.PageRundownNew?.m_menuBar,
            MainMenuGuiLayer.Current.PageSettings?.m_menuBar,
            //MainMenuGuiLayer.Current.PageStart?.m_menuBar,
        ];

        foreach (var item in items)
        {
            if (item?.m_expIcon?.DataBlock != null)
                item.m_expIcon.SetArtifactHeat(1f);
        }

        // This is a comparably expensive operation
        if (!GameStateManager.IsInExpedition && MainMenuGuiLayer.Current.PageRundownNew != null)
        {
            StateTracker stateTracker = StateTracker.Get();
            foreach (var icon in MainMenuGuiLayer.Current.PageRundownNew.m_expIconsAll)
            {
                Expedition.Data? data = Expedition.Data.TryFromExpedition(icon.DataBlock);
                if (data == null) continue;

                int mainCount = stateTracker.CollectedItemCounts.GetValueOrDefault(ObjectiveHandlers.SharedObjectiveHandler.GetSectorClearedItem(data.MainLayer).ID, 0);
                int secondaryCount = !data.HasSecondary ? 0
                    : stateTracker.CollectedItemCounts.GetValueOrDefault(ObjectiveHandlers.SharedObjectiveHandler.GetSectorClearedItem(data.GetLayer(LayerType.Secondary)).ID, 0);
                int overloadCount = !data.HasOverload ? 0
                    : stateTracker.CollectedItemCounts.GetValueOrDefault(ObjectiveHandlers.SharedObjectiveHandler.GetSectorClearedItem(data.GetLayer(LayerType.Overload)).ID, 0);

                eExpeditionIconStatus status;
                if (icon.DataBlock.Accessibility == eExpeditionAccessibility.AlwaysAllow)
                {
                    if (mainCount > 0) status = eExpeditionIconStatus.PlayedAndFinished;
                    else status = eExpeditionIconStatus.NotPlayed;
                }
                else status = eExpeditionIconStatus.TierLocked;

                icon.SetStatus(
                    status,
                    mainCount == 0 ? "-" : mainCount.ToString(),
                    secondaryCount == 0 ? "-" : secondaryCount.ToString(),
                    overloadCount == 0 ? "-" : overloadCount.ToString(),
                    "-",
                    1f
                );
            }
        }
    }

    /// <summary>
    /// Ensure that the artifact heat is updated after an expedition ends, even if no one grabbed any artifacts
    /// </summary>
    [ArchivePatch(typeof(RundownManager), nameof(RundownManager.OnExpeditionEnded))]
    public static class RundownManager__OnExpeditionEnded__Patch
    {
        public static void Postfix()
            => UpdateLocationCounts();
    }

}
