using CellMenu;
using GameData;
using Player;
using ReTFO.Archipelago.Features.FloatingItems;
using ReTFO.Archipelago.Features.ObjectiveHandlers;
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
    public override string Name => "Archipelago Settings";
    public override string Description => "Controls AP many import Archipelago settings.";
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
    public Game.Data GameData => MidManager.GetProcessedGameData();
    public AP.ArchipelagoSession? ApSession { get; protected set; } = null;
    public static Version APVersion = new(1, 0, 0);
    List<string> GameTags = [ "DeathLink", "EnergyLink" ];
    public Guid SessionGuid { get; protected set; } = new();
    protected Task<AP.Packets.RoomInfoPacket>? ConnectTask { get; set; } = null;
    protected Task<AP.LoginResult>? LoginTask { get; set; } = null;
    public event Action<StateTracker>? OnStateChange;

    // Things set up at initial sync
    public long RootSeed { get; protected set; } = 0;
    protected HashSet<RegionID> RegionWhitelist { get; set; } = new();
    protected HashSet<RegionID> RegionBlacklist { get; set; } = new();
    protected HashSet<LocationID> LocationWhitelist { get; set; } = new();
    protected HashSet<LocationID> LocationBlacklist { get; set; } = new();
    protected HashSet<ItemID> ItemWhitelist { get; set; } = new();
    protected HashSet<ItemID> ItemBlacklist { get; set; } = new();
    protected List<(LocationID, ItemID)> FilledEmptyLocations { get; set; } = new();
    protected SortedList<ItemID, int> GoalItemCounts { get; set; } = new();
    public int SkippableGoalCount { get; protected set; } = 0;

    // Things consistently updated
    protected HashSet<RegionID> FoundRegions { get; init; } = new();
    protected HashSet<string> FoundUnusedRegions { get; init; } = new();
    protected HashSet<LocationID> FoundLocations { get; init; } = new();
    protected HashSet<LocationID> TrashedLocations { get; init; } = new();
    protected Dictionary<ItemID, int> ActualItemCounts { get; init; } = new(); /// Actual list of items held. See <see cref="CollectedItemCounts"/> for the public interface
    protected Dictionary<ItemID, int> SessionItemCounts { get; init; } = new(); // Items received since reconnecting
    protected Dictionary<ItemID, Queue<ItemID>> QueuedItemReplacements = new();
    public List<(ItemID, string)> ItemsInTerminalSystem { get; init; } = new();
    protected SortedList<LocationID, PlayerAgent> LocationCheckContinuity = new();

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
    public class Settings : PrivateFeatureSettingsPatch.IOptionallyPrivate
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

        [FSDisplayName("Empty Trash")]
        [FSDescription("Unmark any trash items, revealing their checks in the expedition location counts.")]
        public FButton EmptyTrashButton { get; set; } = new FButton("Empty Trash", callback: () =>
        {
            StateTracker stateTracker = StateTracker.Get();
            stateTracker.TrashedLocations.Clear();
            stateTracker.SendInteraction(pArchipelagoInteraction.eType.EmptyTrash);
        });

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

        [FSDisplayName("Reactivate State Tracker")]
        [FSDescription("Certain errors will cause State Tracker to stop updating. This is currently out of my control. Press this button to turn it back on.")]
        public FButton RestartButton { get; set; } = new FButton("Reactivate", callback: () =>
            {
                FeatureManager.DisableAutomatedFeature(typeof(StateTracker));
                FeatureManager.EnableAutomatedFeature(typeof(StateTracker));
            }
        );
    }

    /// <summary>
    /// Instance of settings. Note that this is controlled by TheArchive.
    /// </summary>
    [FeatureConfig]
    public static Settings Config { get; set; } = null!;

    /// <summary>
    /// Enter the fake connect state, which is for debug
    /// </summary>
    public void FakeConnect()
    {
        if (CurrentState != eState.CleanState)
        {
            LogErrorForLobby($"Cannot start fake connection; already in state: {Enum.GetName(CurrentState)}");
            return;
        }

        // Replace this with a menu config at some point
        Game.Data data = GameData;
        CurrentState = eState.FakeConnect;
        RootSeed = 0;
        RegionWhitelist = [ data.Region_Menu ];
        RegionBlacklist = [];
        LocationWhitelist = [ data.Location_All ];
        LocationBlacklist = [ ];
        ItemWhitelist = [ data.Item_All ];
        //ItemBlacklist = [ data.Item_Scans, data.Item_Warps, data.Item_ExpeditionUnlocks, data.Item_LobbySlotUnlocks ];
        ItemBlacklist = [ data.Item_Scans, data.Item_ExpeditionUnlocks, data.Item_LobbySlotUnlocks ];
        //FilledEmptyLocations = new(); // Created below
        GoalItemCounts = new();
        SkippableGoalCount = 0;

        // Reset empty locations to simplify this next part
        foreach (var pair in data.Locations.GetAllValues())
            if (pair.Value?.RandData.IsEmpty ?? false) pair.Value.SetItem(new());

        // Manually calculate the filled empty locations using a simplified algorithm
        bool regionTest(KeyValuePair<RegionID, Region> r)
            => data.Regions.IsChild(r.Key, RegionWhitelist) && !data.Regions.IsChild(r.Key, RegionBlacklist);
        HashSet<RegionID> reachableRegions = data.Regions.GetAllValues().Where(regionTest).Select(r => r.Key).ToHashSet();

        bool itemTest((RegionID, ItemID) item)
            => reachableRegions.Contains(item.Item1)
            && data.Items.IsChild(item.Item2, ItemWhitelist) && !data.Items.IsChild(item.Item2, ItemBlacklist);
        Queue<ItemID> availableItems = new(data.GetAllFloatingItems().Where(itemTest).Select(pair => pair.Item2));

        reachableRegions.RemoveWhere(r => !data.Regions.LookUpValue(r).Reachable);
        bool locationTest(KeyValuePair<LocationID, Location> l)
            => l.Value.RandData.IsEmpty && l.Value.OwningRegionIDs.All(reachableRegions.Contains)
            && data.Locations.IsChild(l.Key, LocationWhitelist) && !data.Locations.IsChild(l.Key, LocationBlacklist);
        List<LocationID> emptyLocations = data.Locations.GetAllValuesNonNull().Where(locationTest).Select(l => l.Key).ToList();

        FilledEmptyLocations = DistributeItems(emptyLocations, availableItems);

        // Deploy the multiworld!
        SetupMultiworld();

        // Round 4 - Any remaining items are given
        if (availableItems.Count > 0)
        {
            LogErrorForLobby($"Insufficient empty locations for selected floating items. Granting {availableItems.Count} free items!");
            foreach (var item in availableItems)
                CollectItem(item);
        }

        // Calculate goal items
        foreach (var pair in data.Locations.GetAllValuesNonNull())
        {
            if (pair.Value.ItemID.IsNull) continue;
            if (data.Items.IsChild(pair.Value.ItemID, data.Item_SectorClears))
                GoalItemCounts[pair.Value.ItemID] = GoalItemCounts.GetValueOrDefault(pair.Value.ItemID, 0) + 1;
        }

        FeatureLogger.Notice("Due to fake connect, removing all expedition locks.");
        UnlockExpeditionHandler.UnlockAll();
    }

    /// <summary>
    /// Enter the client connect state, where this user receives Archipelago data from the lobby host.
    /// This is invoked by <see cref="ReceiveInitState(pArchipelagoInitState)"/>
    /// </summary>
    public void ClientConnect(pArchipelagoInitState state)
    {
        if (m_cachedMasterAnswer.answer != SNetwork.pMasterSessionAnswerType.AllowedToJoinHub)
        {
            FeatureLogger.Notice($"Ignoring init packet; master answer was {Enum.GetName(m_cachedMasterAnswer.answer)}");
            return;
        }

        if (CurrentState == eState.ConnectedClient || CurrentState == eState.ProxyClient)
        {
            FeatureLogger.Notice("Ignoring init packet; already set up as a client");
            return;
        }

        if (state.GameName != GameData.Name)
        {
            FeatureLogger.Error("Cannot connect as client; the host is playing with different mods than us!");
            return;
        }

        FeatureLogger.Notice("Received init packet. Preparing to join as client...");
        if (CurrentState == eState.CleanState)
        {   // We need to set up the multiworld
            FeatureLogger.Notice("Joining as proxy client...");
            RootSeed = state.RootSeed;
            RegionWhitelist = state.RegionWhitelist.Select(i => new RegionID() { ID = i }).ToHashSet();
            RegionBlacklist = state.RegionBlacklist.Select(i => new RegionID() { ID = i }).ToHashSet();
            LocationWhitelist = state.LocationWhitelist.Select(i => new LocationID() { ID = i }).ToHashSet();
            LocationBlacklist = state.LocationBlacklist.Select(i => new LocationID() { ID = i }).ToHashSet();
            ItemWhitelist = state.ItemWhitelist.Select(i => new ItemID() { ID = i }).ToHashSet();
            ItemBlacklist = state.ItemBlacklist.Select(i => new ItemID() { ID = i }).ToHashSet();
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
        SNetwork.SNet.SessionHub.OnMasterSessionAnswer(m_cachedMasterAnswer);
        FeatureLogger.Success("Successfully joined!");
    }

    /// <summary>
    /// Try to start a connection to Archipelago using the current config settings.
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
            LogErrorForLobby($"Cannot start new connection; already in state: {Enum.GetName(CurrentState)}");
            return;
        }

        if (ConnectTask != null)
        {
            LogWarningForLobby("Cannot start new connection; currently waiting to connect to Archipelago");
            return;
        }

        CurrentState = eState.HostConnecting;
        SessionGuid = Guid.NewGuid();
        ApSession = AP.ArchipelagoSessionFactory.CreateSession(Config.ServerAddress, Config.Port);
        ApSession.Locations.CheckedLocationsUpdated += (locs) =>
        {
            foreach (long id in locs)
                FoundLocations.Add(new LocationID() { ID = checked((uint)id) });
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
        LogErrorForLobby($"Failed to {debugName} to Archipelago Host!");
        if (exception != null)
        {
            LogForLobby("View the log for details.", true);
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
            LogErrorForLobby($" -> Failed to {debugName} while not in connecting state?");
        }
    }

    /// <summary>
    /// Attempt to reconnect after a specified delay
    /// </summary>
    /// <param name="delay">The delay, in milliseconds</param>
    protected async Task<AP.Packets.RoomInfoPacket> ReconnectDelayed(int delay=2500)
    {
        FeatureLogger.Notice($"Attempting to reconnect to Archipelago Host...");
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
            {
                LogErrorForLobby("Connection debug hidden due to HideConnectionDetails");
            }
            else
            {
                LogErrorForLobby($"Address: {Config.ServerAddress}:{Config.Port}");
                LogErrorForLobby($"Calculated URI: {uri}");
            }
            return;
        }

        string? name = GameData.Name;
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
                string? name = GameData.Name;
                FeatureLogger.Error($"Game: {(name == null ? "GTFO" : $"GTFO ({name})")}");
            }
            return;
        }
        ApSession.SetClientState(AP.Enums.ArchipelagoClientState.ClientPlaying);

        try
        {
            RootSeed = (long)(
                loginSuccessful.SlotData.GetValueOrDefault("RootSeed", null)
                ?? throw new NullReferenceException("Failed to retrieve RootSeed from slot data")
            );

            RegionWhitelist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("RegionWhitelist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new RegionID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve RegionWhitelist from slot data")
            );

            RegionBlacklist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("RegionBlacklist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new RegionID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve RegionBlacklist from slot data")
            );

            LocationWhitelist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("LocationWhitelist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new LocationID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve LocationWhitelist from slot data")
            );

            LocationBlacklist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("LocationBlacklist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new LocationID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve LocationBlacklist from slot data")
            );

            ItemWhitelist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("ItemWhitelist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new ItemID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve ItemWhitelist from slot data")
            );

            ItemBlacklist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("ItemBlacklist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new ItemID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve ItemBlacklist from slot data")
            );

            FilledEmptyLocations = (loginSuccessful.SlotData.GetValueOrDefault("FilledEmptyLocations", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<List<long>>>()?.Select(l => (new LocationID() { ID = checked((uint)l[0]) }, new ItemID() { ID = checked((uint)l[1]) }))?
                    .ToList()
                    ?? throw new NullReferenceException("Failed to retrieve FilledEmptyLocations from slot data");

            IEnumerable<ItemID> rawGoalItems = (loginSuccessful.SlotData.GetValueOrDefault("GoalItems", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new ItemID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve GoalItems from slot data");

            GoalItemCounts = new();
            foreach (var id in rawGoalItems) 
                GoalItemCounts[id] = GoalItemCounts.GetValueOrDefault(id, 0) + 1;

            SkippableGoalCount = (int)(long)(
                loginSuccessful.SlotData.GetValueOrDefault("SkippableGoalCount", null)
                ?? throw new NullReferenceException("Failed to retrieve SkippableGoalCount from slot data")
            );

            if (GoalItemCounts.Sum(pair => pair.Value) <= SkippableGoalCount)
            {
                ApSession.SetClientState(AP.Enums.ArchipelagoClientState.ClientGoal);
                FeatureLogger.Success("Congratulations, you have won the game!");
            }
        }
        catch (Exception e)
        {
            LogForLobby("Encountered error while retrieving slot data; view log for exception details.", true);
            FeatureLogger.Error("Encountered error while retrieving slot data:");
            FeatureLogger.Exception(e);
            Reset();
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
            SessionItemCounts.Clear();
            CurrentState = eState.HostConnected;
            ApSession.SetClientState(AP.Enums.ArchipelagoClientState.ClientPlaying);
            LogForLobby("<#0F0>Sucessfully reconnected!</color>", false);
        }
        FoundLocations.UnionWith(ApSession.Locations.AllLocationsChecked.Select(v => new LocationID() { ID = checked((uint)v) }));
    }

    /// <summary>
    /// Set up the multiworld using the current slot data
    /// </summary>
    protected void SetupMultiworld()
    {
        Game.Data data = GameData;

        // Add the always and never tags to ensure they're in the sets
        RegionWhitelist.Add(data.Region_Always);
        RegionBlacklist.Add(data.Region_Never);
        LocationWhitelist.Add(data.Location_Always);
        LocationBlacklist.Add(data.Location_Never);
        ItemWhitelist.Add(data.Item_Always);
        ItemBlacklist.Add(data.Item_Never);

        //FeatureLogger.Notice("Beginning graph traversal for multiworld");
        //if (!MidManager.DoGraphTraversal(data, true, false))
        //{
        //    string message = "Graph traversal failed! Cancelling connection. View log for details.";
        //    FeatureLogger.Error(message);
        //    LogForLobby(message, true);
        //    ApSession?.Socket.DisconnectAsync();
        //    ApSession = null;
        //    CurrentState = eState.CleanState;
        //}
        //else
        //    FeatureLogger.Success("Graph traversal succeeded!");

        // Attempt early overwrite so that callbacks can depend on the rundowns being there
        TryOverwriteRundowns();

        // Ensure everything is reset
        FoundRegions.Clear();
        FoundUnusedRegions.Clear();
        FoundLocations.Clear();
        TrashedLocations.Clear();
        ActualItemCounts.Clear();
        SessionItemCounts.Clear();
        ItemsInTerminalSystem.Clear();
        QueuedItemReplacements.Clear();

        // Reset rand data
        foreach (var entry in data.Items.GetAllValuesNonNull())
            entry.Value.UpdateRandomization(false, false);

        // Identify reachable regions and locations
        HashSet<RegionID> reachableRegions = new HashSet<RegionID>();
        foreach (var id in RegionWhitelist)
        {
            if (!data.Regions.IsChild(id, RegionBlacklist)) reachableRegions.Add(id);
        }
        foreach (var id in data.Regions.GetAllIDs())
        {
            if (data.Regions.LookUpDefinition(id).AllParents.Any(reachableRegions.Contains) && !RegionBlacklist.Contains(id))
                reachableRegions.Add(id);
        }
        foreach (var id in reachableRegions)
            if (!data.Regions.LookUpValue(id).Reachable) reachableRegions.Remove(id);


        HashSet<RegionID> reachableRegionIDs = data.Regions.GetAllValues()
            .Where(p => p.Value.Reachable && data.Regions.IsChild(p.Key, RegionWhitelist) && !data.Regions.IsChild(p.Key, RegionBlacklist))
            .Select(p => p.Key).ToHashSet();

        // List of reachable locations
        List<KeyValuePair<LocationID, Location>> reachableLocations = data.Locations.GetAllValuesNonNull()
            .Where(l => l.Value.OwningRegionIDs.All(id => reachableRegionIDs.Contains(id))).ToList();

        // Set whitelist/blacklist for items
        foreach (var pair in data.Items.GetAllValuesNonNull())
        {   
            pair.Value.UpdateRandomization(
                isWhitelisted: data.Items.IsChild(pair.Key, ItemWhitelist),
                isBlacklisted: data.Items.IsChild(pair.Key, ItemBlacklist)
            );
        }

        // Clean up empty locations and set whitelist/blacklist
        foreach (var entry in data.Locations.GetAllValuesNonNull())
        {   // Clear empty locations of their
            if (entry.Value.RandData.IsEmpty) entry.Value.SetItem(new());
            else if (entry.Value.ItemID.IsNull) FeatureLogger.Error($"Found non-empty location with null item: [{entry.Key}] {data.Locations.LookUpName(entry.Key)}");

            entry.Value.UpdateRandomization(
                isReachable: false,
                isWhitelisted: data.Locations.IsChild(entry.Key, LocationWhitelist),
                isBlacklisted: data.Locations.IsChild(entry.Key, LocationBlacklist),
                isRandomized: false,
                isRandomlike: false
            );
        }

        // Set filled floating locations with their item IDs
        foreach (var pair in FilledEmptyLocations)
        {   
            Location? location = data.Locations.LookUpValue(pair.Item1);
            if (location == null)
                FeatureLogger.Error("Received filled location ID points to a null location!");
            else if (!location.RandData.IsEmpty)
                FeatureLogger.Error("Received filled location ID points to a non-empty location!");
            else
                location.SetItem(pair.Item2);
        }

        // Finishing up the processing
        foreach (var pair in reachableLocations)
        {
            // Update reachability
            pair.Value.UpdateReachable(true);
            if (pair.Value.ItemID.IsNull) continue;

            // Set randomization
            Item item = data.Items.LookUpValueChecked(pair.Value.ItemID);
            bool isRandom = true
                && (pair.Value.RandData.IsWhitelisted || item.RandData.IsWhitelisted)
                && !(pair.Value.RandData.IsBlacklisted || item.RandData.IsBlacklisted);
            pair.Value.UpdateRandomized(isRandom, !isRandom && item.RandData.IsRandomLike);

            // Handling CollectedByDefault item instances
            // Note we skip floating items (which are placed in empty locations) since we can guarantee
            //  that all desired floating items were placed. So, instead, we skip it now and process the
            //  full floating list
            if (item.RandData.IsCollectedByDefault && !pair.Value.RandData.IsEmpty)
            {
                if (pair.Value.RandData.IsTreatedAsRandom) item.OnItemLost(this, pair.Value.ItemID);
                else ActualItemCounts[pair.Value.ItemID] = ActualItemCounts.GetValueOrDefault(pair.Value.ItemID, 0) + 1;
            }
        }

        // Process all collected by default floating items
        foreach (var pair in data.GetAllFloatingItems())
        {
            if (pair.Item2.IsNull)
            {
                FeatureLogger.Error($"Floating item ID is null, but should not be! {pair.Item1}");
                continue;
            }

            Item item = data.Items.LookUpValueChecked(pair.Item2);
            if (item.RandData.IsCollectedByDefault)
            {
                bool isReachable = data.Regions.IsChild(pair.Item1, RegionWhitelist) && !data.Regions.IsChild(pair.Item1, RegionBlacklist);
                if (item.RandData.CanBeRandomized || !isReachable) item.OnItemLost(this, pair.Item2);
                else ActualItemCounts[pair.Item2] = ActualItemCounts.GetValueOrDefault(pair.Item2, 0) + 1;
            }
        }

        // Scout locations - We could use RescoutLocations, but this skips rediscovering reachable locations
        if (ApSession != null)
        {
            // Request all reachable locations be scouted
            var randomizedLocations = reachableLocations.Where(l => l.Value.RandData.IsRandomized).ToList();
            long[] ids = new long[randomizedLocations.Count];
            for (int i = 0; i < randomizedLocations.Count; i++) ids[i] = randomizedLocations[i].Key.ID;
            ApSession.Locations.ScoutLocationsAsync(AP.Enums.HintCreationPolicy.None, ids).ContinueWith(OnLocationsScouted);
        }

        // Some other setup
        NotifyFoundRegion(data.Region_Menu, PlayerManager.GetLocalPlayerAgent());
        OnStateChange?.Invoke(this);
    }

    /// <summary>
    /// Fully reset the state. Kick the user to the menu, reset the menu,
    ///  disconnect state, and all that jazz
    /// </summary>
    public void Reset()
    {
        ApSession?.Socket.DisconnectAsync();
        ApSession = null;
        ConnectTask = null;
        LoginTask = null;

        //CM_MenuBar.__c.__9._Setup_b__42_3(); // The "Exit Expedtion" button's callback
        SNetwork.SNet.Lobbies.LeaveLobby(); // This only works if not in an expedition

        uint savedId = Globals.Global.ActiveRundownIds[0];
        Globals.Global.ActiveRundownIds = new(1);
        Globals.Global.ActiveRundownIds[0] = savedId;

        MainMenuGuiLayer.Current.PageRundownNew.m_isRevealing = false;
        MainMenuGuiLayer.Current.PageRundownNew.m_rundownIsRevealed = false;
        MainMenuGuiLayer.Current.PageRundownNew.m_selectionIsRevealed = false;
        MainMenuGuiLayer.Current.PageRundownNew.m_dataIsSetup = false;
        MainMenuGuiLayer.Current.PageRundownNew.m_rundownIntroIsDone = false;
        MainMenuGuiLayer.Current.PageRundownNew.m_cortexIntroIsDone = false;

        MainMenuGuiLayer.Current.PageRundownNew.m_selectRundownButton.OnBtnPressCallback.Invoke(0);
        MainMenuGuiLayer.Current.PageRundownNew.PostSetup();
        MainMenuGuiLayer.Current.ChangePage(eCM_MenuPage.CMP_RUNDOWN_NEW);
        MainMenuGuiLayer.Current.PageRundownNew.m_selectionIsRevealed = true;

        // Reseting item collection state
        Game.Data data = GameData;
        foreach (var id in CollectedItemCounts.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)))
        {
            Item? item = data.Items.LookUpValue(id);
            if (!(item?.RandData.IsCollectedByDefault ?? true))
                item.OnItemLost(this, id);
        }
        foreach (var pair in data.GetAllFloatingItems())
        {
            Item item = data.Items.LookUpValueChecked(pair.Item2);
            if (!item.RandData.IsCollectedByDefault) continue;
            int count = ActualItemCounts.GetValueOrDefault(pair.Item2, 0);
            if (count > 0)
                ActualItemCounts[pair.Item2] = count - 1;
            else
                CollectItem(pair.Item2);
        }

        // Clearing is not necessary but we'll do it anyway to be safe
        ActualItemCounts.Clear();
        SessionItemCounts.Clear();

        CurrentState = eState.CleanState;
        OnStateChange?.Invoke(this);
    }

    /// <summary>
    /// Returns true if the provided item is randomized.
    /// This is most commonly used for floating items, since non-floating items can simply
    ///  check if their owning location is randomized.
    /// </summary>
    /// <param name="owningRegion">The region the item is logically contained in</param>
    /// <param name="item">The item which is being tested</param>
    /// <returns></returns>
    public bool IsItemRandomized(RegionID owningRegion, ItemID item)
    {
        Game.Data data = GameData;
        bool test = data.Regions.IsChild(owningRegion, RegionWhitelist) && !data.Regions.IsChild(owningRegion, RegionBlacklist);
        if (!test) return false;

        Item? instance = data.Items.LookUpValue(item);
        if (instance == null)
            return data.Items.IsChild(item, ItemWhitelist) && !data.Items.IsChild(item, ItemBlacklist);
        else
            return instance.RandData.CanBeRandomized;
    }

    /// <summary>
    /// Notify the state tracker that a region has been found by region name
    /// </summary>
    /// <param name="name">Name of the region</param>
    /// <param name="player">Player who found the region, or null if too inconvenient to identify</param>
    /// <param name="skipInteraction">If true, skip sending the interactino over SNet</param>
    public void NotifyFoundRegion(string name, PlayerAgent? player, bool skipInteraction = false)
    {
        Game.Data gameData = GameData;
        if (!gameData.Regions.TryLookUpID(name, out RegionID region))
        {   // We'll often find unregistered regions
            if (!FoundUnusedRegions.Add(name))
                FeatureLogger.Debug($"Ignoring region because it is not registered: {name}");
            return;
        }
        NotifyFoundRegion(region, player, skipInteraction);
    }

    /// <summary>
    /// Notify the state tracker that a region has been found by region ID
    /// </summary>
    /// <param name="regionId">ID of the region which was found</param>
    /// <param name="player">Player who found the region, or null if too inconvenient to identify</param>
    /// <param name="skipInteraction">If true, skip sending the interactino over SNet</param>
    public void NotifyFoundRegion(RegionID regionId, PlayerAgent? player, bool skipInteraction = false)
    {
        Game.Data data = GameData; 
        Region region = data.Regions.LookUpValue(regionId);

        if (!FoundRegions.Add(regionId))
            return;

        FeatureLogger.Debug($"Discovered region [{regionId.ID}] {data.Regions.LookUpName(regionId)}");

        // Check for auto-discover locations
        bool isFoundLocation(LocationID locID)
        {
            if (FoundLocations.Contains(locID)) return false;
            var loc = data.Locations.LookUpValueChecked(locID);
            if (!loc.RandData.IsAutoDiscovered) return false;
            if (loc.OwningRegionIDs.Any(r => !FoundRegions.Contains(r))) return false;
            return true;
        }

        var locs = region.ConnectedLocations.Where(isFoundLocation).ToArray();
        if (locs.Length > 0) NotifyFoundLocations(locs, player);
    }

    /// <summary>
    /// Notify the state tracker that a single location has been found / "checked"
    /// </summary>
    /// <param name="id">The location which has been checked</param>
    /// <param name="player">The player who found the location, or null if too inconvenient to identify</param>
    /// <param name="force">Intended for debug. If true, forces the locations to be rediscovered, obtaining another copy of their item</param>
    /// <param name="skipInteraction">If true, skip sending the interactino over SNet</param>
    /// <returns>The location object (for convenience)</returns>
    public Location NotifyFoundLocation(LocationID id, PlayerAgent? player, bool force = false, bool skipInteraction = false)
    {
        Game.Data gameData = GameData;
        Location location = gameData.Locations.LookUpValueChecked(id);

        if (!FoundLocations.Add(id)) return location;

        FeatureLogger.Debug($"Discovered Location: [{id.ID}] {gameData.Locations.LookUpName(id)}");
        if (!skipInteraction)
            SendInteraction(pArchipelagoInteraction.eType.CheckLocation, value: id.ID);

        if (CurrentState == eState.FakeConnect && location.RandData.IsTreatedAsRandom) 
        {
            // IsTreatedAsRandom guarantees ItemID is valid
            CollectItem(location.ItemID, id, player);
            LogForLobby($"Collected item {gameData.Items.LookUpName(location.ItemID)} from FakeConnect", false);
        }
        else if (player != null && location.RandData.IsRandomlike)
        {
            LocationCheckContinuity.Add(id, player);
        }

        if (ApSession != null)
            ApSession.Locations.CompleteLocationChecksAsync(id.ID).ContinueWith(OnLocationChecksCompleted);
        
        UpdateLocationCounts();
        return location;
    }

    /// <summary>
    /// Notify the state tracker that many locations have been found / "checked"
    /// </summary>
    /// <param name="ids">IDs of the locations</param>
    /// <param name="player">The player who found the locations, or null if too inconvenient to identify</param>
    /// <param name="force">Intended for debug; set to true to force the locations to be rediscovered, obtaining another copy of their items</param>
    /// <param name="skipInteraction">If true, skip sending the interactino over SNet</param>
    public void NotifyFoundLocations(IEnumerable<LocationID> ids, PlayerAgent? player, bool force = false, bool skipInteraction = false)
    {
        Game.Data gameData = GameData;
        List<long> networkIds = new();
        foreach (var id in ids)
        {
            Location loc = gameData.Locations.LookUpValueChecked(id);

            if (!FoundLocations.Add(id) || force)
                continue;

            FeatureLogger.Debug($"Discovered Location: [{id.ID}] {gameData.Locations.LookUpName(id)}");
            if (!skipInteraction)
                SendInteraction(pArchipelagoInteraction.eType.CheckLocation, value: id.ID);

            if (loc.RandData.IsTreatedAsRandom && CurrentState == eState.FakeConnect)
            {
                // IsTreatedAsRandom guarantees ItemID is valid
                CollectItem(loc.ItemID, id, player);
                LogForLobby($"Collected item {gameData.Items.LookUpName(loc.ItemID)} from FakeConnect", false);
            }
            else if (player != null && loc.RandData.IsRandomlike)
            {
                LocationCheckContinuity.Add(id, player);
            }

            if (ApSession != null)
                networkIds.Add(id.ID);
        }

        if (ApSession != null && networkIds.Count > 0)
            ApSession.Locations.CompleteLocationChecksAsync(networkIds.ToArray()).ContinueWith(OnLocationChecksCompleted);
        UpdateLocationCounts();
    }

    /// <summary>
    /// MAke on location as "seen"; this will identify them as seen on Archipelago
    ///  and can beused to leave hints for players so that, shoudl they not collect this item
    ///  now, they can know where to collect it later
    /// </summary>
    /// <param name="id">The location to scout</param>
    /// <param name="hintType">The hint creation policy for scouting this location</param>
    public void ScoutLocation(LocationID id, AP.Enums.HintCreationPolicy hintType = AP.Enums.HintCreationPolicy.CreateAndAnnounceOnce)
    {
        SendInteraction(pArchipelagoInteraction.eType.ScoutLocation, value: id.ID);
        ApSession?.Locations.ScoutLocationsAsync(hintType, id.ID);
    }

    /// <summary>
    /// Mark one or more locations as "seen"; this will identify them as seen on Archipelago
    ///  and can be used to leave hints for players so that, should they not collect this item 
    ///  now, they can know where to collect it later
    /// </summary>
    /// <param name="ids">IDs of the locations to scout</param>
    /// <param name="hintType">The hint creation policy for scouting these locations</param>
    public void ScoutLocations(IEnumerable<LocationID> ids, AP.Enums.HintCreationPolicy hintType = AP.Enums.HintCreationPolicy.CreateAndAnnounceOnce)
    {
        foreach (var id in ids)
            SendInteraction(pArchipelagoInteraction.eType.ScoutLocation, value: id.ID);
        ApSession?.Locations.ScoutLocationsAsync(hintType, ids.Select(id => (long)id.ID).ToArray());
    }

    /// <summary>
    /// Marks one or more locations as "trash"; StateTracker will state that the items were
    ///  found for the player but will not actually collect the location or notify AP that
    ///  the location was checked.
    /// </summary>
    /// <param name="ids">The location(s) to mark as trash</param>
    /// <param name="player">The player who marked them as trash, if applicable</param>
    /// <param name="skipInteraction">If true, skip sending the interaction over SNet</param>
    public void MarkAsTrash(IEnumerable<LocationID> ids, PlayerAgent? player, bool skipInteraction = false)
    {
        foreach (var id in ids)
        {
            if (TrashedLocations.Add(id) && !skipInteraction)
                SendInteraction(pArchipelagoInteraction.eType.MarkTrash, value: id.ID);
        }
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
    protected List<(LocationID, ItemID)> DistributeItems(IReadOnlyList<LocationID> locations, Queue<ItemID> items)
    {
        if (items.Count == 0) return [];

        // Using double here to better simulate the server (it uses python, which uses double precision floating points)
        Game.Data data = GameData;
        double count = 0.2; // .2 instead of 0 because of precision issues
        double step = (items.Count >= locations.Count) ? 1.0 : (items.Count / (double)locations.Count);
        long locCount = locations.Count; // Micro optimization
        List<(LocationID, ItemID)> results = new();
        for (long i = 0; i < locCount; i++)
        {
            count += step;
            if (count >= 1.0)
            {
                int index = (int)((i + Math.Abs(RootSeed)) % locCount);
                LocationID locId = locations[index];
                ItemID itemId = items.Dequeue();
                data.Locations.LookUpValueChecked(locId).SetItem(itemId);
                results.Add((locId, itemId));
                count -= 1.0;
            }
        }
        return results;
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
        Game.Data gameData = GameData;

        // Set of parent tags so we can identify which expeditions are required to reach/see all enabled regions
        HashSet<RegionID> parents = gameData.Regions.GetAllParents(RegionWhitelist.Where(id => !gameData.Regions.IsChild(id, RegionBlacklist)));

        Queue<ExpeditionInTierData> newExpeditions = new();
        foreach (Expedition.Data expData in gameData.GetAllExpeditions())
        {
            if (!(gameData.Regions.IsChild(expData.Region_Expedition, RegionWhitelist) && !gameData.Regions.IsChild(expData.Region_Expedition, RegionBlacklist)) && !parents.Contains(expData.Region_Expedition))
                continue;

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
        static IEnumerable<(int, TierVisualData)> GetVisualData(RundownDataBlock rundown)
        {
            yield return (rundown.TierA.Count, rundown.StorytellingData.Visuals.TierAVisuals);
            yield return (rundown.TierB.Count, rundown.StorytellingData.Visuals.TierBVisuals);
            yield return (rundown.TierC.Count, rundown.StorytellingData.Visuals.TierCVisuals);
            yield return (rundown.TierD.Count, rundown.StorytellingData.Visuals.TierDVisuals);
            yield return (rundown.TierE.Count, rundown.StorytellingData.Visuals.TierEVisuals);
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
        Globals.Global.ActiveRundownIds = ids.ToArray().Cast<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<uint>>();
        GameSetupDataBlock.GetAllBlocks()[0].RundownIdsToLoad = ids;

        // Moving rundown 7 from position 2 to position 7 (so rundowns load in order)
        if (MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections.Count == 8)
        {
            // This is easiest done with a full sort, in case it gets changed mid-game (ie by us, then the user presses reset and overwrite again, ...)
            string[] sortKeys = MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections.Select(s => s.Name).ToArray();
            var sortValues = Enumerable.Range(0, 8).Select(i => (MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections[i], MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelectionPositions[i])).ToArray();
            Array.Sort(sortKeys, sortValues);

            for (int i = 0; i < sortKeys.Length; i++)
            {
                MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections[i] = sortValues[i].Item1;
                MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelectionPositions[i] = sortValues[i].Item2;
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
        Game.Data gameData = GameData;

        long[] arr = gameData.Locations.GetAllValuesNonNull()
            .Where(pair => pair.Value.RandData.IsReachable)
            .Select(l => (long)l.Key.ID).ToArray();

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
        Game.Data gameData = GameData;
        if (!task.IsCompletedSuccessfully)
        {
            FeatureLogger.Error("Failed to scout locations!");
            RescoutLocations();
        }
        else
        {
            FeatureLogger.Debug($"Successfully scouted {task.Result.Count} locations");

            // Apply found location info
            foreach (var pair in task.Result)
            {
                Location location = gameData.Locations.LookUpValueChecked(new LocationID() { ID = checked((uint)pair.Value.LocationId) });
                location.ScoutedItemName = pair.Value.ItemDisplayName;
                location.ScoutedPlayerName = pair.Value.Player.Name;
                location.ScoutedGameName = pair.Value.ItemGame;
            }

            // Send a scouting update for only the scouted items
            m_stateReplicator!.SendScouting(FormatScoutingUpdate(task.Result.Select(pair => new LocationID() { ID = (uint)pair.Value.LocationId })));
        }
    }

    /// <summary>
    /// Access the the list of collected items
    /// </summary>
    public IReadOnlyDictionary<ItemID, int> CollectedItemCounts => ActualItemCounts;

    /// <summary>
    /// Check if a specific location has been collected
    /// </summary>
    /// <param name="name">The name of the location</param>
    /// <param name="includeTrash">If true, also returns true if the item is marked as trash</param>
    public bool HasLocation(string name, bool includeTrash = true)
    {
        if (GameData.Locations.TryLookUpID(name, out LocationID id))
            return HasLocation(id, includeTrash);
        return false;
    }

    /// <summary>
    /// Check if a specific location has been collected
    /// </summary>
    /// <param name="id">The id of the location</param>
    /// <param name="includeTrash">If true, also returns true if the item is marked as trash</param>
    public bool HasLocation(LocationID id, bool includeTrash = true) 
        => FoundLocations.Contains(id) || (includeTrash && TrashedLocations.Contains(id));

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
        Game.Data gameData = GameData;

        if (QueuedItemReplacements.TryGetValue(id, out var replacements))
        {
            if (replacements.Count < 1) 
                throw new NotSupportedException();
            else if (replacements.Count == 1)
                QueuedItemReplacements.Remove(id);
            id = replacements.Dequeue();
        }

        int newCount = ActualItemCounts.GetValueOrDefault(id, 0) + 1;
        ActualItemCounts[id] = newCount;
        Item? item = gameData.Items.LookUpValue(id);
        if (item == null)
        {
            FeatureLogger.Error($"Collecting null item: {id} {gameData.Items.LookUpName(id)}");
        }
        else
        {
            FeatureLogger.Debug($"Collecting item [{id.ID}] {gameData.Items.LookUpName(id)}");
            item.OnItemObtained(this, sourceLocationId, player, id);
        }

        // Check if we've satisifed our win condition
        if (SNetwork.SNet.IsMaster && GoalItemCounts.ContainsKey(id))
        {
            GoalItemCounts[id] -= 1;
            if (GoalItemCounts.Sum(pair => pair.Value) <= SkippableGoalCount)
            {
                ApSession?.SetClientState(AP.Enums.ArchipelagoClientState.ClientGoal);
                FeatureLogger.Success("Congratulations, you have won the game!");
            }
        }

        if (!skipInteraction)
            SendInteraction(pArchipelagoInteraction.eType.CollectItem, value: id.ID, count: (ushort)newCount);
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
        int currentCount = ActualItemCounts.GetValueOrDefault(id, 0);
        if (currentCount <= 0)
        {
            FeatureLogger.Error($"Cannot uncollect item; it is not currently collected! {id} {GameData.Items.LookUpName(id)}");
            return;
        }
        ActualItemCounts[id] = currentCount - 1;
        GameData.Items.LookUpValue(id)?.OnItemLost(this, id);
    }

    /// <summary>
    /// Add an item to the terminal system, allowing players to "withdraw" the item using the relevant callback.
    /// The terminal system is cleared each expedition; re-add the item each time if needed.
    /// </summary>
    /// <param name="item">The item to add</param>
    public void AddItemToTerminal(ItemID item)
    {
        // Lazy system-independent seed generation
        System.Random random = new((RootSeed, item).GetHashCode());
        char r() // Picks a random character
        {
            int choice = random.Next(35);
            return (char)(choice >= 10 ? 'A' + (choice - 10) : '0' + choice);
        }

        // Generate a code which doesn't match any other in the terminal system
        string newCode;
        do
        {   
            newCode = $"{r()}{r()}{r()}-{r()}{r()}{r()}-{r()}{r()}{r()}";
        } while (ItemsInTerminalSystem.Any(pair => pair.Item2 == newCode));

        ItemsInTerminalSystem.Add((item, newCode));
    }

    /// <summary>
    /// Standard update method used to check for received items
    /// </summary>
    public override void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))
        {
        }

        if (ConnectTask?.IsCompleted ?? false)
            HandleConnectionResult(ConnectTask);
        if (LoginTask?.IsCompleted ?? false)
            HandleLoginResult(LoginTask);

        if (ApSession == null)
            return;

        if (!ApSession.Socket.Connected && ConnectTask == null)
        {   // Begin the reconnect process
            LogErrorForLobby("Disconnected from Archipelago! Attempting reconnect...");
            CurrentState = eState.HostReconnecting;
            ConnectTask = ApSession.ConnectAsync();
        }

        while (ApSession.Items.Any())
        {
            try
            {
                AP.Models.ItemInfo itemInfo = ApSession.Items.DequeueItem();

                // Add to our session count.
                ItemID id = new ItemID() { ID = checked((uint)itemInfo.ItemId) };
                if (id.IsNull)
                {
                    FeatureLogger.Error("Received null item id from Archipelago server!");
                    continue;
                }

                int newCount = SessionItemCounts.GetValueOrDefault(id, 0) + 1;
                SessionItemCounts[id] = newCount;

                // If the session count is now greater, we also add to our actual count
                if (newCount > ActualItemCounts.GetValueOrDefault(id, 0))
                {
                    // Identifying source, if possible
                    LocationID sourceLocation = new();
                    PlayerAgent? sourceAgent = null;

                    if (itemInfo.Player.Slot == ApSession.Players.ActivePlayer.Slot)
                    {
                        sourceLocation = new() { ID = checked((uint)itemInfo.LocationId) };
                        if (LocationCheckContinuity.TryGetValue(sourceLocation, out sourceAgent))
                            LocationCheckContinuity.Remove(sourceLocation);
                    }

                    // Actually collecting the item
                    CollectItem(id, sourceLocation, sourceAgent);
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
    /// <param name="message">The message to log. Can be formatted using XML.</param>
    /// <param name="localOnly">If true, do not sync this message. Otherwise, sends to all players</param>
    public static void LogForLobby(string message, bool localOnly)
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

        if (!localOnly)
            StateTracker.Get().m_stateReplicator?.SendLog(message);
    }

    /// <summary>
    /// Shortcut to log a message as both a warning and for the lobby
    /// </summary>
    protected static void LogWarningForLobby(string message)
    {
        FeatureLogger.Warning(message);
        LogForLobby($"<#FF0>{message}</color>", true);
    }

    /// <summary>
    /// Shortcut to log a message as both an error and for the lobby
    /// </summary>
    protected static void LogErrorForLobby(string message)
    {
        FeatureLogger.Error(message);
        LogForLobby($"<#F00>{message}</color>", true);
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

            if (!Expedition.Data.TryGetFromCurrentExpedition(out Expedition.Data? expeditionData))
                FeatureLogger.Error("Failed to identify expedition on drop; skipping relevant events");
            else
                FeatureLogger.Notice($"Started expedition: {expeditionData.ExpeditionName}");

            self.ItemsInTerminalSystem.Clear();
            Game.Data gameData = self.GameData;
            if (expeditionData is not null)
            {
                foreach (var id in self.ActualItemCounts.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)))
                {
                    Item? item = gameData.Items.LookUpValue(id);
                    item?.OnStartExpeditionWithItem(self, expeditionData, id);
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
            Game.Data gameData = stateTracker.GameData;
            if (!gameData.TryGetExpeditionData(__instance.DataBlock, out var data))
            {
                FeatureLogger.Error($"Failed to look up expedition for location count: {__instance.DataBlock.Descriptive.Prefix}");
                return true;
            }

            HashSet<RegionID> expeditionRegions = [data.Region_Expedition];
            foreach (var entry in data.Regions.GetAllEntries())
                if (entry.Value.Definition.AllParents.Any(expeditionRegions.Contains)) expeditionRegions.Add(entry.Key);
            var locations = expeditionRegions.SelectMany(r => data.Regions.LookUpValue(r).ConnectedLocations);

            int totalCount = 0, foundCount = 0;
            foreach (LocationID id in locations)
            {
                Location? loc = data.Locations.LookUpValue(id);

                if (loc == null) continue;
                if (!loc.RandData.IsTreatedAsRandom) continue;
                if (loc.RandData.IsExcluded || loc.RandData.IsTrap) continue;

                ++totalCount;
                if (stateTracker.HasLocation(id)) ++foundCount;
            }

            __instance.m_artifactHeatText.SetText($"Found Items: {foundCount} / {totalCount}");

            Color color;
            if (totalCount== 0)
                color = Color.grey;
            else if (foundCount == totalCount)
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
                if (!Expedition.Data.TryGetFromExpedition(icon.DataBlock, out Expedition.Data? data)) continue;

                int mainCount = stateTracker.CollectedItemCounts.GetValueOrDefault(data.MainLayer.Item_SectorClear_Instance, 0);
                int secondaryCount = !data.HasSecondary ? 0
                    : stateTracker.CollectedItemCounts.GetValueOrDefault(data.GetLayer(LayerType.Secondary).Item_SectorClear_Instance, 0);
                int overloadCount = !data.HasOverload ? 0
                    : stateTracker.CollectedItemCounts.GetValueOrDefault(data.GetLayer(LayerType.Overload).Item_SectorClear_Instance, 0);

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
