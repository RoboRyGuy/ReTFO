using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.ModdedInstanceData;
using ReTFO.Archipelago.Patches;
using ReTFO.Archipelago.Utilities;
using SNetwork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Members;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;
using AP = Archipelago.MultiClient.Net;

namespace ReTFO.Archipelago.Features;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// Tracks Archipelago state
/// </summary>
[EnableFeatureByDefault, DisallowInGameToggle]
public partial class StateTracker : ArchipelagoFeature
{
    public override string Name => "State Tracker";
    public override string Description => "Core feature used for all Archipelago operations during play";
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
    protected AP.ArchipelagoSession? ApSession { get; set; } = null;

    // Things provided by the server at initial sync
    protected List<string> ExpeditionNames { get; set; } = new();
    protected HashSet<string> RandomizationCategories { get; set; } = new();
    public long RootSeed { get; protected set; } = 0;

    // Things needing to be consistently synced
    protected HashSet<long> FoundRegions { get; init; } = new();
    protected HashSet<string> FoundUnusedRegions { get; init; } = new();
    protected HashSet<long> FoundLocations { get; init; } = new();
    protected Dictionary<Item, int> ItemCounts { get; init; } = new();
    protected Dictionary<Item, Queue<Item>> QueuedItemReplacements = new();
    public List<Tuple<Item, string>> ItemsInTerminalSystem { get; init; } = new();

    /// <summary>
    /// Get the current state tracker
    /// </summary>
    /// <returns>The current state tracker</returns>
    public static StateTracker Get() => Plugin.Get().StateTracker;

    /// <summary>
    /// Helper type which allows functions to accept either a location or an ID as an input
    /// </summary>
    public struct LocationOrId
    {
        public Location Location { get; init; }
        public static implicit operator LocationOrId(long id)
            => new LocationOrId() { Location = StateTracker.Get().MidManager.GetProcessedGameData().LookupLocation(id) };
        public static implicit operator LocationOrId(Location location)
            => new LocationOrId() { Location = location };
        public static implicit operator Location(LocationOrId self)
            => self.Location;
    }

    /// <summary>
    /// Helper type which allows functions to accept either an item or an ID as an input
    /// </summary>
    public struct ItemOrId
    {
        public Item Item { get; init; }
        public static implicit operator ItemOrId(long id)
            => new ItemOrId() { Item = StateTracker.Get().MidManager.GetProcessedGameData().LookupItem(id) };
        public static implicit operator ItemOrId(Item location)
            => new ItemOrId() { Item = location };
        public static implicit operator Item(ItemOrId self)
            => self.Item;
    }

    /// <summary>
    /// Possible states the StateTracker can be in, generally with regards to network connectivity
    /// </summary>
    public enum eState
    {
        /// <summary>
        /// StateTracker is not connected in any way; default state
        /// </summary>
        Disconnected,

        /// <summary>
        /// StateTracker is connected to the server, and awaiting some sync packet
        /// </summary>
        Connected,

        /// <summary>
        /// StateTracker is synced with the server and maintaining that sync; the game is ready to play
        /// </summary>
        Synced,

        /// <summary>
        /// StateTracker is faking a connection for debug, and can be treated as synced
        /// </summary>
        FakeConnect,

        /// <summary>
        /// StateTracker is faking a connection as a client so it can connect to a host player
        /// </summary>
        ClientConnect,

        /// <summary>
        /// StateTracker is faking a connection as client so it can connect to a hose player, and is fully synced
        /// </summary>
        ClientSynced,
    }

    public struct State
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public State() { }

        /// <summary>
        /// Set the current state dirctly from an enum value
        /// </summary>
        /// <param name="flag">The enum value to assign</param>
        public static implicit operator State(eState flag)
            => new State() { StateFlag = flag };

        /// <summary>
        /// Current state
        /// </summary>
        public eState StateFlag { get; set; } = eState.Disconnected;

        /// <summary>
        /// True if currently disconnected
        /// </summary>
        public bool IsDisconnected => StateFlag == eState.Disconnected;

        /// <summary>
        /// True if currently connected to the AP server
        /// </summary>
        public bool IsConnected => StateFlag == eState.Connected || StateFlag == eState.Synced;

        /// <summary>
        /// True if current faking a server connection
        /// </summary>
        public bool IsFakeConnected => StateFlag == eState.FakeConnect;

        /// <summary>
        /// True if connect as a client to another player connect to the host Archipelago
        /// </summary>
        public bool IsClientConnected => StateFlag == eState.ClientConnect || StateFlag == eState.ClientSynced;
    }

    public State CurrentState { get; set; } = new();

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
        [FSDescription("Hide connection details while not editing them. For streamers.")]
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
        [FSDescription("Slot in the server to try to connect to\nIn simpler terms, your username")]
        [PrivateFeatureSettingsPatch.FSOptionallyPrivate]
        public string Username { get; set; } = "admin";

        [FSDisplayName("Use Password")]
        [FSDescription("If true, will attempt to authenticate to Archipelago using the below password\nIf false, will try to skip password authentication")]
        public bool HasPassword { get; set; } = false;

        [FSDisplayName("Password")]
        [FSDescription("The password to use when connecting to Archipelago")]
        [PrivateFeatureSettingsPatch.FSOptionallyPrivate]
        public string Password { get; set; } = "Password";

    }

    /// <summary>
    /// Instance of settings. Note that this is controlled by TheArchive.
    /// </summary>
    [FeatureConfig]
    public static NetworkSettingsType Config { get; set; } = null!;

    /// <summary>
    /// Enter the fake connect state, which is for debug
    /// </summary>
    public void FakeConnect()
    {
        if (CurrentState.IsConnected)
        {
            if (!CurrentState.IsFakeConnected)
                FeatureLogger.Error("Cannot enter FakeConnect state; currently connectd");
            else
                FeatureLogger.Warning("Ignoring FakeConnect; already fake connected!");
            return;
        }

        CurrentState = eState.FakeConnect;
        RootSeed = 0;
        ExpeditionNames = new()
        {
            "R1B1", "R1A1", "R1B2", "R1C1", "R1C2", "R1D1",
            "R3A1", "R3A2", "R3A3", "R3B1", "R3B2", "R3C1", "R3D1",
            "R4A1", "R4A2", "R4A3", "R4B1", "R4B2", "R4B3", "R4C1", "R4C2", "R4C3", "R4D1", "R4D2", "R4E1",
            "R5A1", "R5A2", "R5A3", "R5B1",
            "R6A1", "R6AX", "R6B1", "R6B2", "R6BX", "R6C1", "R6C2", "R6C3", "R6CX", "RD61", "R6D2", "R6D3", "R6D4",
            "R7A1", "R7B1", "R7B2", "R7B3", "R7C1", "R7C2", "R7C3", "R7D1", "R7D2", "R7E1",
            "R8A1", "R8A2", "R8B1", "R8B2", "R8B3", "R8B4", "R8C1", "R8C2", "R8D1", "R8D2", "R8E1", "R8E2",
        };
        IEnumerable<string> helper()
        {
            yield break;
            yield return "All";
        }
        RandomizationCategories =
            helper()
            .Concat(ExpeditionNames)
            .ToHashSet();

        PostConnectCommon();
    }

    /// <summary>
    /// Enter the client connect state, where this user receives Archipelago data from the lobby host
    /// </summary>
    public void ClientConnect()
    {
        // TODO
        throw new NotImplementedException();
    }

    /// <summary>
    /// Try to start a connection to Archipelago with the current network settings
    /// </summary>
    public void Connect()
    {
        if (Config.UseDebugMode)
        {
            FakeConnect();
            return;
        }

        if (!CurrentState.IsDisconnected)
        {
            if (CurrentState.IsFakeConnected)
                FeatureLogger.Error("Cannot connect; currently in FakeConnect state!");
            else
                FeatureLogger.Warning("Ignoring Connect request; already connected!");
            return;
        }

        ApSession = AP.ArchipelagoSessionFactory.CreateSession(Config.ServerAddress, Config.Port);
        ApSession.ConnectAsync().ContinueWith(PostConnect);
    }

    /// <summary>
    /// Callback to handle connecting
    /// </summary>
    /// <param name="connectResult">The connect task this is a callback for</param>
    protected void PostConnect(Task<AP.Packets.RoomInfoPacket> connectResult)
    {
        if (!connectResult.IsCompletedSuccessfully)
        {
            FeatureLogger.Error("Failed to connect to Archipelago Host!");
            CurrentState = eState.Disconnected;
            ApSession = null;
            return;
        }
        else
            CurrentState = eState.Connected;

        // Blocking call
        AP.LoginResult loginResult = ApSession!.TryConnectAndLogin(
            "gtfo",
            Config.Username,
            AP.Enums.ItemsHandlingFlags.IncludeStartingInventory,
            version: Version.Parse(Plugin.Version), // By definition of Version for BepInEx, this must succeed
            tags: null,
            uuid: null,
            password: Config.HasPassword ? Config.Password : null,
            requestSlotData: true
        );

        if (!loginResult.Successful || loginResult is not AP.LoginSuccessful loginSuccessful)
        {
            FeatureLogger.Error("Login to Archipelago failed!");
            CurrentState = eState.Disconnected;
            ApSession.Socket.DisconnectAsync();
            ApSession = null;
            return;
        }

        if (loginSuccessful.SlotData.GetValueOrDefault("ExpeditionNames") is not int rootSeed)
        {
            FeatureLogger.Error("No root seed associated with slot data!");
            return;
        }

        if (loginSuccessful.SlotData.GetValueOrDefault("ExpeditionNames") is not List<string> expeditionNames)
        {
            FeatureLogger.Error("No expedition names associated with slot data!");
            return;
        }

        if (loginSuccessful.SlotData.GetValueOrDefault("RandomizationCategories") is not List<string> randomizationCategories)
        {
            FeatureLogger.Error("No randomization categories associated with slot data!");
            return;
        }

        CurrentState = eState.Synced;
        RootSeed = rootSeed;
        ExpeditionNames = expeditionNames;
        RandomizationCategories = randomizationCategories.Distinct().ToHashSet();

        PostConnectCommon();
    }

    /// <summary>
    /// Shared code from both PostConnect and FakeConnect
    /// </summary>
    protected void PostConnectCommon()
    {
        Game.Data gameData = MidManager.GetProcessedGameData();

        FeatureLogger.Notice("Beginning graph traversal for new rundowns");
        if (!MidManager.DoGraphTraversal(gameData, true, true, ExpeditionNames, true))
        {
            FeatureLogger.Error("Graph traversal failed! Cancelling connection. View log for details");
            ApSession?.Socket.DisconnectAsync();
            ApSession = null;
            CurrentState = eState.Disconnected;
        }
        else
            FeatureLogger.Success("Graph traversal succeeded!");

        // Early overwrite so that callbacks can depend on them being there
        TryOverwriteRundowns();

        // Ensure everything is reset
        FoundRegions.Clear();
        FoundUnusedRegions.Clear();
        FoundLocations.Clear();
        ItemCounts.Clear();
        ItemsInTerminalSystem.Clear();
        QueuedItemReplacements.Clear();

        // Identify reachable regions and locations
        var reachableRegions = gameData.RegionList.Select((r, i) => Tuple.Create(r, i)).Where(pair => pair.Item1.Reachable);
        var reachableRegionIDs = reachableRegions.Select(pair => pair.Item2).ToHashSet();

        var reachableLocations = gameData.LocationList.Where(l => l.OwningRegionIds.All(id => reachableRegionIDs.Contains(id)));
        var emptyLocations = reachableLocations
            .Where(l => TestRandomization(l).Type == RandTest.eType.RandomizedEmptyLocation)
            .ToList();

        // Find and place floating items randomly in empty locations
        var floatingItems = new Stack<Item>(
            MidManager.GetProcessedGameData().FloatingItemIds
            .Select(MidManager.GetProcessedGameData().LookupItem)
            .Where(i => TestRandomization(i).Type == RandTest.eType.RandomizedFloatingItem));
        foreach (var item in floatingItems)
            item.OnItemLost(this);
        if (floatingItems.Count > emptyLocations.Count)
        {
            FeatureLogger.Error($"More floating items than empty locations! Immediately collecting {floatingItems.Count - emptyLocations.Count} extra items");
            while (floatingItems.Count > emptyLocations.Count)
                CollectItem(floatingItems.Pop());
        }

        float count = 0.2f; // .2 instead of 0 because of floating point issues
        float step = floatingItems.Count / (float)emptyLocations.Count;
        for (int i = 0; i < emptyLocations.Count; i++)
        {
            count += step;
            if (count >= 1f)
            {
                if (emptyLocations[(i + Math.Abs(unchecked((int)RootSeed))) % emptyLocations.Count].ItemID != 0L)
                    FeatureLogger.Error("Overwriting item during floating items placement!"); // In case there's an error in my math
                emptyLocations[(i + Math.Abs(unchecked((int)RootSeed))) % emptyLocations.Count].ItemID = floatingItems.Pop().ID;
                count -= 1f;
            }
        }

        if (floatingItems.Count > 0)
            FeatureLogger.Error("Failed to place all floating items! Bug in algorithm?");

        // Attempt to scout all relevant locations
        var randomizedLocations = reachableLocations.Where(l => TestRandomization(l).IsRandomized);
        if (CurrentState.IsConnected)
            ApSession!.Locations.ScoutLocationsAsync(randomizedLocations.Select(l => l.ID).ToArray()).ContinueWith(OnLocationsScouted);

        // If we're in fake connect, we need to at least allow access to the first expedition
        if (CurrentState.IsFakeConnected)
        {
            var firstCat = $"{ExpeditionNames[0]} Start Items";
            FeatureLogger.Notice($"Because of fake connect, granting items from category {firstCat}");
            var itemIds = emptyLocations.Select(l => l.ItemID).Where(i => i != 0);
            foreach (var item in itemIds.Select(gameData.LookupItem).Where(i => i.RandData.Categories.Contains(firstCat)))
                CollectItem(item);
        }
    }

    /// <summary>
    /// Wraps around a randomization test result, and informs on how an entity is or is not randomized
    /// </summary>
    public struct RandTest
    {
        public RandTest()
        {
            Type = eType.LocationDoesNotSupportRandomization;
            IsRandomlike = false;
            IntersectingRandCategories = Enumerable.Empty<string>();
        }

        public enum eType
        {
            /// <summary>
            /// This is randomized. If a location, the contained item is also randomized
            /// </summary>
            Randomized,

            /// <summary>
            /// This is a location which contains no item, but is a candidate for randomization
            /// </summary>
            RandomizedEmptyLocation,

            /// <summary>
            /// This is an item with no source location which is a candidate for randomization
            /// </summary>
            RandomizedFloatingItem,

            /// <summary>
            /// This location does not support randomization
            /// </summary>
            LocationDoesNotSupportRandomization,

            /// <summary>
            /// This location is not allowed to be randomized due to the applied randomization categories
            /// </summary>
            LocationBlockedByCategory,

            /// <summary>
            /// This item (or the item contained by this location) does not support randomization
            /// </summary>
            ItemDoesNotSupporRandomization,

            /// <summary>
            /// This item (or the item contained by this location) does not match any of the randomization categories
            /// </summary>
            ItemNotAllowedByCategory,
        }

        /// <summary>
        /// The result type of randomization
        /// </summary>
        public eType Type { get; init; }

        /// <summary>
        /// If true, this item should be treated as random even when not randomized
        /// </summary>
        public bool IsRandomlike { get; init; }

        /// <summary>
        /// If Type == LocationBlockedByCategory, these are the blocking categories.
        /// If Randomized or RandomizedFloatItem, these are the categories which allowed randomization.
        /// </summary>
        public IEnumerable<string> IntersectingRandCategories { get; init; }

        /// <summary>
        /// True if the randomization type is "Randomized"
        /// </summary>
        public bool IsRandomized => Type == eType.Randomized;

        /// <summary>
        /// True if the IsRandomized or IsRandomlike
        /// </summary>
        public bool IsTreatedAsRandom => IsRandomized || IsRandomlike;
    }

    /// <summary>
    /// Test the randomization properties of a location
    /// </summary>
    /// <param name="loc">The location to test</param>
    /// <returns>A RandTest which tells how this location is or is not randomized</returns>
    public RandTest TestRandomization(LocationOrId loc)
    {
        if (loc.Location.RandData.IsNoneRandomization)
            return new RandTest() { Type = RandTest.eType.LocationDoesNotSupportRandomization };

        if (loc.Location.RandData.Categories.TryIntersect(RandomizationCategories, out var intersection))
        {
            return new RandTest()
            {
                Type = RandTest.eType.LocationBlockedByCategory,
                IntersectingRandCategories = intersection,
                IsRandomlike = loc.Location.ItemID != 0 && MidManager.GetProcessedGameData().LookupItem(loc.Location.ItemID).RandData.IsRandomLike,
            };
        }

        if (loc.Location.ItemID == 0)
            return new RandTest() { Type = RandTest.eType.RandomizedEmptyLocation };

        return TestRandomization(MidManager.GetProcessedGameData().LookupItem(loc.Location.ItemID), true);
    }

    /// <summary>
    /// Test the randomization properties of an item
    /// </summary>
    /// <param name="item">The item to test</param>
    /// <param name="hasSourceLocation">True if the item has a source location. Generally, call with hasSourceLocation=false</param>
    /// <returns>A RandTest which tells how this item is or is not randomized</returns>
    public RandTest TestRandomization(ItemOrId item, bool hasSourceLocation = false)
    {
        if (item.Item.RandData.IsNoneRandomization)
            return new RandTest() { Type = RandTest.eType.ItemDoesNotSupporRandomization };

        if (!item.Item.RandData.Categories.TryIntersect(RandomizationCategories, out var intersection))
            return new RandTest() { Type = RandTest.eType.ItemNotAllowedByCategory, IsRandomlike = item.Item.RandData.IsRandomLike };

        return new RandTest()
        {
            Type = hasSourceLocation ? RandTest.eType.Randomized : RandTest.eType.RandomizedFloatingItem,
            IsRandomlike = item.Item.RandData.IsRandomLike,
            IntersectingRandCategories = intersection
        };
    }

    /// <summary>
    /// Notify the state tracker that a region has been found by region name
    /// </summary>
    /// <param name="name">Name of the region</param>
    /// <param name="player">Player who found the region, or null if too inconvenient to identify</param>
    public void NotifyFoundRegion(string name, PlayerAgent? player)
    {
        Game.Data gameData = MidManager.GetProcessedGameData();
        if (!gameData.RegionLookup.TryGetValue(name, out int region))
        {   // We'll often find unregistered regions
            if (!FoundUnusedRegions.Add(name))
                FeatureLogger.Debug($"Ignoring region because it is not registered: {name}");
            return;
        }

        if (!FoundRegions.Add(region))
            return;
        FeatureLogger.Debug($"Discovered region {region}: {name}");

        // Check for auto-discover locations
        bool isFoundLocation(long locID)
        {
            var loc = gameData.LookupLocation(locID);
            if (!loc.RandData.AutoDiscover) return false;
            if (FoundLocations.Contains(loc.ID)) return false;
            if (loc.OwningRegionIds.Any(r => !FoundRegions.Contains(r))) return false;
            return true;
        }
        NotifyFoundLocations(
            gameData.LookupRegion(region).ConnectedLocationIds.Where(isFoundLocation).ToArray(),
            player
        );
    }

    /// <summary>
    /// Notify the state tracker that a single location has been found / "checked"
    /// </summary>
    /// <param name="location">The location which has been checked</param>
    /// <param name="player">The player who found the location, or null if too inconvenient to identify</param>
    /// <param name="onReloadAction">
    /// An optional callback which is called when loading a checkpoint.
    /// This is typically to allow the location to be despawned, etc to help enforce continuity.
    /// </param>
    /// <param name="force">Intended for debug. If true, forces the locations to be rediscovered, obtaining another copy of their item</param>
    /// <returns>The randomization result of the item. Randomized and randomlike items should be blocked from being picked up</returns>
    public RandTest NotifyFoundLocation(LocationOrId location, PlayerAgent? player, Action? onReloadAction = null, bool force = false)
    {
        if (onReloadAction != null)
            FeatureLogger.Warning("onReloadAction is missing!");

        RandTest randomization = TestRandomization(location.Location);

        if (!FoundLocations.Add(location.Location.ID))
            return randomization;
        FeatureLogger.Debug($"Discovered Location: {location.Location.Name}");

        if (randomization.IsTreatedAsRandom)
        {
            if (CurrentState.IsFakeConnected)
                CollectItem(location.Location.ItemID, location.Location.ID, player);
            else if (CurrentState.IsConnected)
                ApSession!.Locations.CompleteLocationChecksAsync(location.Location.ID).ContinueWith(OnLocationChecksCompleted);
        }
        else if (randomization.IsRandomlike && location.Location.ItemID != 0)
            CollectItem(location.Location.ItemID, location.Location.ID);

        return randomization;
    }

    /// <summary>
    /// Notify the state tracker that many locations have been found / "checked"
    /// </summary>
    /// <param name="ids">IDs of the locations</param>
    /// <param name="player">The player who found the locations, or null if too inconvenient to identify</param>
    /// <param name="onReloadActions">
    /// Optional callbacks which are called when loading a checkpoint.
    /// These typically are to allow the location to be despawned, etc to help enforce continuity.
    /// </param>
    /// <param name="force">Intended for debug; set to true to force the locations to be rediscovered, obtaining another copy of their items</param>
    public void NotifyFoundLocations(IEnumerable<long> ids, PlayerAgent? player, IEnumerable<Action>? onReloadActions = null, bool force = false)
    {
        if (onReloadActions != null)
            FeatureLogger.Warning("onReloadAction is missing!");

        Game.Data gameData = MidManager.GetProcessedGameData();
        List<long> networkIds = new();
        foreach (var id in ids)
        {
            Location loc = gameData.LookupLocation(id);
            RandTest randomization = TestRandomization(loc);

            if (!FoundLocations.Add(id))
                continue;
            FeatureLogger.Debug($"Discovered Location: {loc.Name}");

            if (randomization.IsTreatedAsRandom)
            {
                if (CurrentState.IsFakeConnected)
                    CollectItem(loc.ItemID, id, player);
                else if (CurrentState.IsConnected)
                    networkIds.Add(id);
            }
            else if (randomization.IsRandomlike && loc.ItemID != 0)
                CollectItem(loc.ItemID, id);
        }

        if (CurrentState.IsConnected && networkIds.Count > 0)
            ApSession!.Locations.CompleteLocationChecksAsync(networkIds.ToArray()).ContinueWith(OnLocationChecksCompleted);
    }

    /// <summary>
    /// If synced, overwrite the existing rundown list with new ones for the expedition
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown for several misc edge cases which should never occur</exception>
    public void TryOverwriteRundowns()
    {
        if (CurrentState.IsDisconnected) return;

        // Find all expeditions and create copies to be loaded in
        var expeditions = ExpeditionNames.Select(n => Plugin.MidManager.LookupExpedition(n)).ToList();
        Queue<ExpeditionInTierData> newExpeditions = new();

        for (int i = 0; i < ExpeditionNames.Count; i++)
        {
            if (expeditions[i] == null)
            {
                FeatureLogger.Error($"Expedition not found while placing rundowns: {ExpeditionNames[i]}");
                continue;
            }

            ExpeditionInTierData newExpedition = expeditions[i]!.Expedition.MemberwiseClone().Cast<ExpeditionInTierData>();
            newExpedition.Descriptive.Prefix = ExpeditionNames[i];
            newExpedition.Descriptive.SkipExpNumberInName = true;
            newExpedition.ExcludeFromMatchmaking = true;
            newExpedition.ExcludeFromProgression = true;
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
            numRundowns = newExpeditions.Count / 10;
        numRundowns = Math.Min(numRundowns, MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections.Count);

        System.Random random = new(RootSeed);
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
                        Description = "",
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
                        ExportVersion = 1,
                        ImportVersion = 1,
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
                    Title = MakeText($"{rundown.name} - Title", rundown.name),
                    ExternalExpTitle = MakeText($"{rundown.name} - ExpTitle", rundown.name),
                    SurfaceDescription = MakeText($"{rundown.name} - Surface Description", ""
                      + "\nWORK WITH <#FF0>FELLOW <i>PRISON</i>ERS</color> TO RECOVER <#0F0>SECURE AS<i>SETS</i></color> AND <#F0F>COMPLETE THE </color><#F00>RUN<i>DOWN</i></color>"
                      + "\n\n-----------------------------------"
                      + "\n\n<#FF0>COORDINATE</color> ACROSS THE <#00F><i>MULTI</i>WORLD</color> TO <#F00>MINIMIZE <i>CASUAL</i>TIES</color> AND <#0F0>ACCELE<i>RATE PRIOR</i>ITIES</color>"
                      + "\n\n-----------------------------------"
                      + "\n\nCOLLECT <#F00>WARDEN <i>ART</i>IFACTS</color> TO <#FF0>SUPPLEMENT SUCCESS MAR<i>GINS</i></color> AT THE COST OF <#F0F><i>EMOTION</i>AL DUR<i>ESS<i></color>"
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
                    //else if (weights[i] > -1.5f)
                    //    expedition = dudExpedition;
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

        if (newExpeditions.Count > 0)
            throw new NotSupportedException("Failed to populate the correct number of expeditions");

        // Set up the menu so everything is correctly displayed
        Il2CppSystem.Collections.Generic.List<uint> ids = new(newRundowns.Count);
        foreach (var rundown in newRundowns) ids.Add(rundown.persistentID);
        Globals.Global.RundownIdToLoad = 0;
        Globals.Global.ActiveRundownIds = ids.ToArray().Cast<Il2CppStructArray<uint>>();
        GameSetupDataBlock.GetAllBlocks()[0].RundownIdsToLoad = ids;

        // Shuffling the rundowns so they're actually in order (and not 7 in position 2 for some reason)
        var selections = MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections;
        var positions = MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelectionPositions;
        for (int i = 1; i < 6; i++)
        {
            (selections[i + 1], selections[i]) = (selections[i], selections[i + 1]);
            (positions[i + 1], positions[i]) = (positions[i], positions[i + 1]);
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
        // I believe this is the lambda assigned to the SelectRundown button. Either way, it works :)
        MainMenuGuiLayer.Current.PageRundownNew._Setup_b__102_3(0);
    }

    /// <summary>
    /// Standard update method used to check for received items
    /// </summary>
    public override void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))
        {
            var repManagers = SNet.Replication.m_replicationManagers;
        }

        if (!CurrentState.IsConnected)
            return;

        if (ApSession == null)
            FeatureLogger.Error("ApSession is null during item update!");
        else while (ApSession!.Items.Any())
        {
            AP.Models.ItemInfo itemInfo = ApSession.Items.DequeueItem();

            // If the location in question is from our own game and it wasn't randomized, then
            //  the item was already "collected"
            if (itemInfo.LocationGame == ApSession.ConnectionInfo.Game) // TODO: I don't think this is correct
            {
                Location location = MidManager.GetProcessedGameData().LookupLocation(itemInfo.LocationId);
                if (!TestRandomization(location).IsRandomized)
                    continue;
            }

            CollectItem(itemInfo.ItemId);
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
    /// Internal callback for when locations are scouted
    /// </summary>
    /// <param name="task">The tasked the callback is for</param>
    protected void OnLocationsScouted(Task<Dictionary<long, AP.Models.ScoutedItemInfo>> task)
    {
        if (!task.IsCompletedSuccessfully)
        {
            FeatureLogger.Error("Failed to scout locations! Retrying...");
            var ids = ExpeditionNames.Select(MidManager.LookupExpedition)
                .SelectMany(ex => ex?.RegionList ?? Enumerable.Empty<Region>())
                .SelectMany(r => r.ConnectedLocationIds)
                .Distinct();
            ApSession!.Locations.ScoutLocationsAsync(ids.ToArray()).ContinueWith(OnLocationsScouted);
            return;
        }

        foreach (var pair in task.Result)
        {
            Location location = MidManager.GetProcessedGameData().LookupLocation(pair.Key);
            location.ScoutedItem = pair.Value;
            FeatureLogger.Debug($"Location has been scouted: {location.Name}");
        }
    }

    // Access the list of items
    public IReadOnlyDictionary<Item, int> CollectedItemCounts
        => ItemCounts;

    // Check if a location has been collected
    public bool HasLocation(string name)
        => HasLocation(MidManager.GetProcessedGameData().LookupLocation(name).ID);

    // Check if a location has been collected
    public bool HasLocation(long id)
        => FoundLocations.Contains(id);

    // Immediately collect an item, bypassing lcoation checks and so forth
    public void CollectItem(string itemName, long sourceLocationId = 0, PlayerAgent? player = null)
        => CollectItem(MidManager.GetProcessedGameData().LookupItem(itemName), sourceLocationId, player);

    /// <summary>
    /// Immediately collect an item
    /// Only checks against queued items; all other checks are skipped
    /// </summary>
    /// <param name="item">The item to collect</param>
    /// <param name="sourceLocationId">The location to collect the item from, or 0 if there is no such location</param>
    public void CollectItem(ItemOrId item, long sourceLocationId = 0, PlayerAgent? player = null)
    {
        if (QueuedItemReplacements.TryGetValue(item, out var replacements))
        {
            if (replacements.Count == 1)
                QueuedItemReplacements.Remove(item);
            item = replacements.Dequeue();
        }

        ItemCounts[item] = ItemCounts.GetValueOrDefault(item, 0) + 1;
        item.Item.OnItemObtained(this, sourceLocationId, player);
        FeatureLogger.Debug($"Collecting item: {item.Item.Name}");
        SendInteraction(pArchipelagoInteraction.eType.CollectItem, item.Item.ID);
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
    public void ReplaceItem(Item sourceItem, Item targetItem)
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
        CollectItem(targetItem);
    }

    /// <summary>
    /// Lose or "uncollect" an item. Not all items support being uncollected
    /// </summary>
    /// <param name="item">The item to uncollect</param>
    public void UncollectItem(ItemOrId item)
    {
        int currentCount = ItemCounts.GetValueOrDefault(item, 0);
        if (currentCount <= 0)
        {
            FeatureLogger.Error($"Cannot uncollect item; it is not currently collected! Item: {item.Item.Name}");
            return;
        }
        ItemCounts[item] = currentCount - 1;
        item.Item.OnItemLost(this);
    }

    /// <summary>
    /// Add an item to the terminal system.
    /// The terminal system is cleared each expedition; re-add the item each time if needed.
    /// </summary>
    /// <param name="item">The item to add</param>
    public void AddItemToTerminal(ItemOrId item)
    {
        // Lazy seed generation
        long seed = RootSeed ^ item.Item.ID;
        seed = seed ^ (seed >> 32);
        System.Random random = new(unchecked((int)seed));
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

        ItemsInTerminalSystem.Add(Tuple.Create(item.Item, newCode));
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
            Expedition.Data? expeditionData = Expedition.Data.FromCurrentExpedition();
            if (expeditionData == null)
                FeatureLogger.Error("Failed to identify expedition on drop; skipping relevant events");
            else
                FeatureLogger.Notice($"Started expedition: {expeditionData.ExpeditionName}");

            self.ItemsInTerminalSystem.Clear();
            if (expeditionData is not null)
            {
                foreach (var item in self.ItemCounts.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)))
                    item.OnStartExpeditionWithItem(self, expeditionData);
            }
            else
            {
                FeatureLogger.Error(" -> Items not added to terminal system!");
            }
        }
    }

}
