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
    protected HashSet<RegionID> m_regionWhitelist { get; set; } = new();
    protected HashSet<RegionID> m_regionBlacklist { get; set; } = new();
    protected HashSet<LocationID> m_locationWhitelist { get; set; } = new();
    protected HashSet<LocationID> m_locationBlacklist { get; set; } = new();
    protected HashSet<ItemID> m_itemWhitelist { get; set; } = new();
    protected HashSet<ItemID> m_itemBlacklist { get; set; } = new();
    protected List<(LocationID, ItemID)> m_filledEmptyLocations { get; set; } = new();
    protected SortedList<ItemID, int> m_goalItemCounts { get; set; } = new();
    public int SkippableGoalCount { get; protected set; } = 0;

    public IReadOnlySet<RegionID> RegionWhitelist => m_regionWhitelist;
    public IReadOnlySet<RegionID> RegionBlacklist => m_regionBlacklist;
    public IReadOnlySet<LocationID> LocationWhitelist => m_locationWhitelist;
    public IReadOnlySet<LocationID> LocationBlacklist => m_locationBlacklist;
    public IReadOnlySet<ItemID> ItemWhitelist => m_itemWhitelist;
    public IReadOnlySet<ItemID> ItemBlacklist => m_itemBlacklist;
    public IReadOnlyList<(LocationID, ItemID)> FilledEmptyLocations => m_filledEmptyLocations;
    public IReadOnlyDictionary<ItemID, int> GoalItemCounts => m_goalItemCounts;

    // Things consistently updated
    protected HashSet<RegionID> m_foundRegion { get; init; } = new();
    protected HashSet<string> m_foundUnusedRegions { get; init; } = new();
    protected HashSet<LocationID> m_foundLocations { get; init; } = new();
    protected HashSet<LocationID> m_trashedLocations { get; init; } = new();
    protected Dictionary<ItemID, int> m_collectedItemCounts { get; init; } = new(); /// Actual list of items held. See <see cref="CollectedItemCounts"/> for the public interface
    protected Dictionary<ItemID, int> m_sessionItemCounts { get; init; } = new(); // Items received since reconnecting
    protected Dictionary<ItemID, Queue<ItemID>> m_queuedItemReplacements = new();
    public List<(ItemID, string)> ItemsInTerminalSystem { get; init; } = new(); // Owned by claim command handler; only here for SNet convenience
    protected SortedList<LocationID, PlayerAgent> m_locationCheckContinuity = new();

    public IReadOnlySet<RegionID> FoundRegion => m_foundRegion;
    public IReadOnlySet<string> FoundUnusedRegions => m_foundUnusedRegions;
    public IReadOnlySet<LocationID> FoundLocations => m_foundLocations;
    public IReadOnlySet<LocationID> TrashedLocations => m_trashedLocations;
    public IReadOnlyDictionary<ItemID, int> CollectedItemCounts => m_collectedItemCounts;
    public IReadOnlyDictionary<ItemID, int> SessionItemCounts => m_sessionItemCounts;
    public IReadOnlyDictionary<ItemID, Queue<ItemID>> QueuedItemReplacements => m_queuedItemReplacements;

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
            stateTracker.m_trashedLocations.Clear();
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
        m_regionWhitelist = [ data.Region_AllExpeditions ];
        m_regionBlacklist = [];
        m_locationWhitelist = [ data.Location_All ];
        m_locationBlacklist = [ ];
        m_itemWhitelist = [ data.Item_All ];
        //ItemBlacklist = [ data.Item_Scans, data.Item_Warps, data.Item_ExpeditionUnlocks, data.Item_LobbySlotUnlocks ];
        m_itemBlacklist = [ data.Item_Scans, data.Item_FloatingExpeditionUnlocks, data.Item_LobbySlotUnlocks ];
        //FilledEmptyLocations = new(); // Created below
        m_goalItemCounts = new();
        SkippableGoalCount = 0;

        // Reset empty locations to simplify this next part
        foreach (var pair in data.Locations.GetAllValues())
            if (pair.Value?.RandData.IsEmpty ?? false) pair.Value.SetItem(new());

        // Manually calculate the filled empty locations using a simplified algorithm
        bool regionTest(KeyValuePair<RegionID, Region> r)
            => data.Regions.IsChild(r.Key, m_regionWhitelist) && !data.Regions.IsChild(r.Key, m_regionBlacklist);
        HashSet<RegionID> reachableRegions = data.Regions.GetAllValues().Where(regionTest).Select(r => r.Key).ToHashSet();

        bool itemTest((RegionID, ItemID) item)
            => reachableRegions.Contains(item.Item1)
            && data.Items.IsChild(item.Item2, m_itemWhitelist) && !data.Items.IsChild(item.Item2, m_itemBlacklist);
        Queue<ItemID> availableItems = new(data.GetAllFloatingItems().Where(itemTest).Select(pair => pair.Item2));

        reachableRegions.RemoveWhere(r => !data.Regions.LookUpValue(r).Reachable);
        bool locationTest(KeyValuePair<LocationID, Location> l)
            => l.Value.RandData.IsEmpty && l.Value.OwningRegionIDs.All(reachableRegions.Contains)
            && data.Locations.IsChild(l.Key, m_locationWhitelist) && !data.Locations.IsChild(l.Key, m_locationBlacklist);
        List<LocationID> emptyLocations = data.Locations.GetAllValuesNonNull().Where(locationTest).Select(l => l.Key).ToList();

        m_filledEmptyLocations = DistributeItems(emptyLocations, availableItems);

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
                m_goalItemCounts[pair.Value.ItemID] = m_goalItemCounts.GetValueOrDefault(pair.Value.ItemID, 0) + 1;
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
            m_regionWhitelist = state.RegionWhitelist.Select(i => new RegionID() { ID = i }).ToHashSet();
            m_regionBlacklist = state.RegionBlacklist.Select(i => new RegionID() { ID = i }).ToHashSet();
            m_locationWhitelist = state.LocationWhitelist.Select(i => new LocationID() { ID = i }).ToHashSet();
            m_locationBlacklist = state.LocationBlacklist.Select(i => new LocationID() { ID = i }).ToHashSet();
            m_itemWhitelist = state.ItemWhitelist.Select(i => new ItemID() { ID = i }).ToHashSet();
            m_itemBlacklist = state.ItemBlacklist.Select(i => new ItemID() { ID = i }).ToHashSet();
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
                m_foundLocations.Add(new LocationID() { ID = checked((uint)id) });
            RundownHandler.UpdateAllCounts();
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
                loginSuccessful.SlotData.GetValueOrDefault("root_seed", null)
                ?? throw new NullReferenceException("Failed to retrieve root_seed from slot data")
            );

            m_regionWhitelist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("region_whitelist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new RegionID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve region_whitelist from slot data")
            );

            m_regionBlacklist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("region_blacklist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new RegionID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve region_blacklist from slot data")
            );

            m_locationWhitelist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("location_whitelist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new LocationID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve location_whitelist from slot data")
            );

            m_locationBlacklist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("location_blacklist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new LocationID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve location_blacklist from slot data")
            );

            m_itemWhitelist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("item_whitelist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new ItemID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve item_whitelist from slot data")
            );

            m_itemBlacklist = new(
                (loginSuccessful.SlotData.GetValueOrDefault("item_blacklist", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<long>>()?.Select(l => new ItemID() { ID = checked((uint)l) })
                    ?? throw new NullReferenceException("Failed to retrieve item_blacklist from slot data")
            );

            m_filledEmptyLocations = (loginSuccessful.SlotData.GetValueOrDefault("filled_empty_locations", null) as Newtonsoft.Json.Linq.JArray)?
                    .ToObject<List<List<long>>>()?.Select(l => (new LocationID() { ID = checked((uint)l[0]) }, new ItemID() { ID = checked((uint)l[1]) }))?
                    .ToList()
                    ?? throw new NullReferenceException("Failed to retrieve filled_empty_locations from slot data");

            if (CurrentState != eState.HostReconnecting)
            {
                IEnumerable<(ItemID, long)> rawGoalItems = 
                    (loginSuccessful.SlotData.GetValueOrDefault("goal_item_results", null) as Newtonsoft.Json.Linq.JArray)?
                        .ToObject<List<List<long>>>()?.Select(l => (new ItemID() { ID = checked((uint)l[0]) }, l[1]))
                        ?? throw new NullReferenceException("Failed to retrieve goal_items from slot data");

                m_goalItemCounts = new();
                foreach (var pair in rawGoalItems)
                    m_goalItemCounts[pair.Item1] = checked((int)pair.Item2);
            }

            SkippableGoalCount = (int)(long)(
                loginSuccessful.SlotData.GetValueOrDefault("skippable_goal_count", null)
                ?? throw new NullReferenceException("Failed to retrieve skippable_goal_count from slot data")
            );

            if (m_goalItemCounts.Sum(pair => pair.Value) <= SkippableGoalCount)
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
            FeatureLogger.Error($"Available slot data:\n  {string.Join("\n  ", loginSuccessful.SlotData.Keys)}");
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
            m_sessionItemCounts.Clear();
            CurrentState = eState.HostConnected;
            ApSession.SetClientState(AP.Enums.ArchipelagoClientState.ClientPlaying);
            LogForLobby("<#0F0>Sucessfully reconnected!</color>", false);
        }
        m_foundLocations.UnionWith(ApSession.Locations.AllLocationsChecked.Select(v => new LocationID() { ID = checked((uint)v) }));
    }

    /// <summary>
    /// Set up the multiworld using the current slot data
    /// </summary>
    protected void SetupMultiworld()
    {
        Game.Data data = GameData;

        // Add the always and never tags to ensure they're in the sets
        m_regionWhitelist.Add(data.Region_Always);
        m_regionBlacklist.Add(data.Region_Never);
        m_locationWhitelist.Add(data.Location_Always);
        m_locationBlacklist.Add(data.Location_Never);
        m_itemWhitelist.Add(data.Item_Always);
        m_itemBlacklist.Add(data.Item_Never);

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
        RundownHandler.OverwriteRundowns(this);

        // Ensure everything is reset
        m_foundRegion.Clear();
        m_foundUnusedRegions.Clear();
        m_foundLocations.Clear();
        m_trashedLocations.Clear();
        m_collectedItemCounts.Clear();
        m_sessionItemCounts.Clear();
        ItemsInTerminalSystem.Clear();
        m_queuedItemReplacements.Clear();

        // Identify reachable regions and locations
        foreach (var id in data.Regions.GetAllIDs())
            data.SetRegionRandomized(id, false);

        HashSet<RegionID> reachableRegions = new HashSet<RegionID>();
        foreach (var id in m_regionWhitelist)
        {
            if (!data.Regions.IsChild(id, m_regionBlacklist))
                reachableRegions.Add(id);
        }
        foreach (var id in data.Regions.GetAllIDs())
        {
            var def = data.Regions.LookUpDefinition(id);
            if (def.OtherParents == null)
            {   // This version is more optimized, but only works if there is exactly one parent
                if (reachableRegions.Contains(def.Parent) && !m_regionBlacklist.Contains(id))
                    reachableRegions.Add(id);
            }
            else
            {   // This check is much more thorough and can probably be optimized
                if (def.AllParents.Any(reachableRegions.Contains) && !data.Regions.IsChild(id, m_regionBlacklist))
                    reachableRegions.Add(id);
            }
        }
        foreach (var id in reachableRegions) data.SetRegionRandomized(id, true);

        // List of reachable locations
        List<KeyValuePair<LocationID, Location>> reachableLocations = data.Locations.GetAllValuesNonNull()
            .Where(l => l.Value.OwningRegionIDs.All(id => reachableRegions.Contains(id))).ToList();

        // Set whitelist/blacklist for items
        foreach (var pair in data.Items.GetAllValuesNonNull())
        {   
            pair.Value.UpdateRandomization(
                isWhitelisted: data.Items.IsChild(pair.Key, m_itemWhitelist),
                isBlacklisted: data.Items.IsChild(pair.Key, m_itemBlacklist)
            );
        }

        // Clean up empty locations and set whitelist/blacklist
        foreach (var entry in data.Locations.GetAllValuesNonNull())
        {   
            // Clear empty locations of their items
            if (entry.Value.RandData.IsEmpty) entry.Value.SetItem(new());
            else if (entry.Value.ItemID.IsNull) FeatureLogger.Error($"Found non-empty location with null item: [{entry.Key}] {data.Locations.LookUpName(entry.Key)}");

            entry.Value.UpdateRandomization(
                isReachable: false,
                isWhitelisted: data.Locations.IsChild(entry.Key, m_locationWhitelist),
                isBlacklisted: data.Locations.IsChild(entry.Key, m_locationBlacklist),
                isRandomized: false,
                isRandomlike: false
            );
        }

        // Set filled floating locations with their item IDs
        foreach (var pair in m_filledEmptyLocations)
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
                else m_collectedItemCounts[pair.Value.ItemID] = m_collectedItemCounts.GetValueOrDefault(pair.Value.ItemID, 0) + 1;
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
                bool isReachable = data.Regions.IsChild(pair.Item1, m_regionWhitelist) && !data.Regions.IsChild(pair.Item1, m_regionBlacklist);
                if (item.RandData.CanBeRandomized || !isReachable) item.OnItemLost(this, pair.Item2);
                else m_collectedItemCounts[pair.Item2] = m_collectedItemCounts.GetValueOrDefault(pair.Item2, 0) + 1;
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
            int count = m_collectedItemCounts.GetValueOrDefault(pair.Item2, 0);
            if (count > 0)
                m_collectedItemCounts[pair.Item2] = count - 1;
            else
                CollectItem(pair.Item2);
        }

        // Clearing is not necessary but we'll do it anyway to be safe
        m_collectedItemCounts.Clear();
        m_sessionItemCounts.Clear();

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
        bool test = data.Regions.IsChild(owningRegion, m_regionWhitelist) && !data.Regions.IsChild(owningRegion, m_regionBlacklist);
        if (!test) return false;

        Item? instance = data.Items.LookUpValue(item);
        if (instance == null)
            return data.Items.IsChild(item, m_itemWhitelist) && !data.Items.IsChild(item, m_itemBlacklist);
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
            if (!m_foundUnusedRegions.Add(name))
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
    /// <param name="skipInteraction">If true, skip sending the interaction over SNet. Typically reserved for internal use</param>
    public void NotifyFoundRegion(RegionID regionId, PlayerAgent? player, bool skipInteraction = false)
    {
        Game.Data data = GameData; 
        Region region = data.Regions.LookUpValue(regionId);

        if (!m_foundRegion.Add(regionId))
            return;

        FeatureLogger.Debug($"Discovered region [{regionId.ID}] {data.Regions.LookUpName(regionId)}");

        // Check for auto-discover locations
        bool isFoundLocation(LocationID locID)
        {
            if (m_foundLocations.Contains(locID)) return false;
            var loc = data.Locations.LookUpValueChecked(locID);
            if (!loc.RandData.IsAutoDiscovered) return false;
            if (loc.OwningRegionIDs.Any(r => !m_foundRegion.Contains(r))) return false;
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

        if (!m_foundLocations.Add(id)) return location;

        FeatureLogger.Debug($"Discovered Location: [{id.ID}] {gameData.Locations.LookUpName(id)}");
        if (!skipInteraction)
            SendInteraction(pArchipelagoInteraction.eType.CheckLocation, value: id.ID);

        if (CurrentState == eState.FakeConnect && location.RandData.IsTreatedAsRandom) 
        {
            // IsTreatedAsRandom guarantees ItemID is valid
            CollectItem(location.ItemID, id, player);
            LogForLobby($"Collected item {gameData.Items.LookUpName(location.ItemID)} from FakeConnect", false);
            RundownHandler.UpdateAllCounts();
        }
        else if (player != null)
        {
            m_locationCheckContinuity.Add(id, player);
        }

        if (ApSession != null)
            ApSession.Locations.CompleteLocationChecksAsync(id.ID).ContinueWith(OnLocationChecksCompleted);

        RundownHandler.UpdateAllCounts();
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

            if (!m_foundLocations.Add(id) || force)
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
                m_locationCheckContinuity.Add(id, player);
            }

            if (ApSession != null)
                networkIds.Add(id.ID);
        }

        if (ApSession != null && networkIds.Count > 0)
            ApSession.Locations.CompleteLocationChecksAsync(networkIds.ToArray()).ContinueWith(OnLocationChecksCompleted);
        RundownHandler.UpdateAllCounts();
    }

    /// <summary>
    /// MAke on location as "seen"; this will identify them as seen on Archipelago
    ///  and can beused to leave hints for players so that, shoudl they not collect this item
    ///  now, they can know where to collect it later
    /// </summary>
    /// <param name="id">The location to scout</param>
    /// <param name="hintType">The hint creation policy for scouting this location</param>
    public void ScoutLocation(LocationID id, AP.Enums.HintCreationPolicy hintType = AP.Enums.HintCreationPolicy.CreateAndAnnounceOnce, bool skipInteraction = false)
    {
        if (!skipInteraction)
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
    public void ScoutLocations(IEnumerable<LocationID> ids, AP.Enums.HintCreationPolicy hintType = AP.Enums.HintCreationPolicy.CreateAndAnnounceOnce, bool skipInteraction = false)
    {
        if (!skipInteraction)
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
            if (m_trashedLocations.Add(id) && !skipInteraction)
                SendInteraction(pArchipelagoInteraction.eType.MarkTrash, value: id.ID);
        }
        RundownHandler.UpdateAllCounts();
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
        => m_foundLocations.Contains(id) || (includeTrash && m_trashedLocations.Contains(id));

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

        if (m_queuedItemReplacements.TryGetValue(id, out var replacements))
        {
            if (replacements.Count < 1) 
                throw new NotSupportedException();
            else if (replacements.Count == 1)
                m_queuedItemReplacements.Remove(id);
            id = replacements.Dequeue();
        }

        int newCount = m_collectedItemCounts.GetValueOrDefault(id, 0) + 1;
        m_collectedItemCounts[id] = newCount;
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
        if ((ApSession != null) && m_goalItemCounts.ContainsKey(id))
        {
            m_goalItemCounts[id] -= 1;
            if (m_goalItemCounts.Sum(pair => pair.Value) <= SkippableGoalCount)
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
        if (m_queuedItemReplacements.TryGetValue(targetItem, out var replacements))
            replacements.Enqueue(sourceItem);
        else
        {
            replacements = new();
            replacements.Enqueue(sourceItem);
            m_queuedItemReplacements[targetItem] = replacements;
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
        int currentCount = m_collectedItemCounts.GetValueOrDefault(id, 0);
        if (currentCount <= 0)
        {
            FeatureLogger.Error($"Cannot uncollect item; it is not currently collected! {id} {GameData.Items.LookUpName(id)}");
            return;
        }
        m_collectedItemCounts[id] = currentCount - 1;
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
            var test = RegionWhitelist.Where(r => !r.IsNull).Select(GameData.Regions.LookUpName).ToList();
            var test2 = RegionBlacklist.Where(r => !r.IsNull).Select(GameData.Regions.LookUpName).ToList();
            //Expedition.Data data = Expedition.Data.GetFromCurrentExpedition();
            //RegionID end = data.GetLayer(LayerType.Secondary).AllZones.First().Region_OnDoorOpenedEvents;
            //
            //var paths = data.GetAllPaths().Where(p => p.Value.EndingRegion.Equals(end)).ToList();
            //var sourceRegions = paths.Select(p => data.Regions.LookUpName(p.Value.StartingRegion));
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

        if (!ApSession.Items.Any()) return;
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

                int newCount = m_sessionItemCounts.GetValueOrDefault(id, 0) + 1;
                m_sessionItemCounts[id] = newCount;

                // If the session count is now greater, we also add to our actual count
                if (newCount > m_collectedItemCounts.GetValueOrDefault(id, 0))
                {
                    // Identifying source, if possible
                    LocationID sourceLocation = new();
                    PlayerAgent? sourceAgent = null;

                    if (itemInfo.Player.Slot == ApSession.Players.ActivePlayer.Slot)
                    {
                        sourceLocation = new() { ID = checked((uint)itemInfo.LocationId) };
                        if (m_locationCheckContinuity.TryGetValue(sourceLocation, out sourceAgent))
                            m_locationCheckContinuity.Remove(sourceLocation);
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
        RundownHandler.UpdateAllCounts();
    }

    /// <summary>
    /// Convenience helper to write a log message for the player.
    /// These messages are placed in the local player's chat.
    /// </summary>
    /// <param name="message">The message to log. Can be formatted using XML.</param>
    /// <param name="localOnly">If true, do not sync this message. Otherwise, sends to all players</param>
    public static void LogForLobby(string message, bool localOnly)
    {
        FeatureLogger.Notice(message);

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
                foreach (var id in self.m_collectedItemCounts.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)))
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

}
