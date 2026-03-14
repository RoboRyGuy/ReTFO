using GameData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.ModdedInstanceData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
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
[EnableFeatureByDefault, AutomatedFeature]
public class StateTracker : ArchipelagoFeature
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
    public int RootSeed { get; protected set; } = 0;

    // Things needing to be consistently synced
    protected HashSet<long> FoundRegions { get; init; } = new();
    protected HashSet<string> FoundUnusedRegions { get; init; } = new();
    protected HashSet<long> FoundLocations { get; init; } = new();
    protected Dictionary<Item, int> ItemCounts { get; init; } = new();
    protected Dictionary<Item, Queue<Item>> QueuedItemReplacements = new();
    public List<Tuple<Item, string>> ItemsInTerminalSystem { get; init; } = new();

    public enum State
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
    }

    /// <summary>
    /// Current network state of the StateTracker
    /// </summary>
    public State CurrentState { get; protected set; } = State.Disconnected;

    /// <summary>
    /// Enter the fake connect state, which is for debug
    /// </summary>
    public void FakeConnect()
    {
        if (CurrentState != State.Disconnected)
        {
            if (CurrentState != State.FakeConnect)
                FeatureLogger.Error("Cannot enter FakeConnect state; currently connectd");
            else
                FeatureLogger.Warning("Ignoring FakeConnect; already fake connected!");
            return;
        }

        CurrentState = State.FakeConnect;
        RootSeed = 0;
        ExpeditionNames = new()
        {
            "R1B1", "R1A1", "R1B2", "R1C1", "R1C2", "R1D1",
            "R3A1", "R3A2", "R3A3", "R3B1", "R3B2", "R3C1", "R3D1",
            "R4A1", "R4A2", "R4A3", "R4B1", "R4B2", "R4B3", "R4C1", "R4C2", "R4C3", "R4D1", "R4D2", "R4E1",
            "R5A1", "R5A2", "R5A3", "R5B1",
            "R6A1", "R6AX", "R6B1", "R6B2", "R6BX", "R6C1", "R6C2", "R6C2", "R6C3", "R6CX", "R6D2", "R6D2", "R6D3", "R6D4",
            "R7A1", "R7B1", "R7B2", "R7B3", "R7C1", "R7C2", "R7C3", "R7D1", "R7D2", "R7E1",
            "R8A1", "R8A2", "R8B1", "R8B2", "R8B3", "R8B4", "R8C1", "R8C2", "R8D1", "R8D2", "R8E1", "R8E2",
        };
        IEnumerable<string> helper()
        {
            yield return "All";
        }
        RandomizationCategories = 
            helper()
            .Concat(ExpeditionNames.Select(MidManager.LookupExpedition).Select(UnlockExpeditionHandler.GetExpeditionUnlockedItem!).Select(i => i.RandomizationCategories[0]))
            .ToHashSet();

        PostConnectCommon();
    }

    /// <summary>
    /// Try to start a connection to Archipelago with the current network settings
    /// </summary>
    public void Connect()
    {
        if (CurrentState != State.Disconnected)
        {
            if (CurrentState != State.FakeConnect)
                FeatureLogger.Error("Cannot connect; currently in FakeConnect state!");
            else
                FeatureLogger.Warning("Ignoring Connect request; already connected!");
            return;
        }

        var settings = APServerSettings.Config;
        if (settings.UseDebugMode)
            FakeConnect();
        else
        {
            ApSession = AP.ArchipelagoSessionFactory.CreateSession(settings.ServerAddress, settings.GetPort());
            ApSession.ConnectAsync().ContinueWith(PostConnect);
        }
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
            CurrentState = State.Disconnected;
            ApSession = null;
            return;
        }
        else
            CurrentState = State.Connected;

        // Blocking call
        var settings = APServerSettings.Config;
        AP.LoginResult loginResult = ApSession!.TryConnectAndLogin(
            "gtfo", 
            settings.Username, 
            AP.Enums.ItemsHandlingFlags.IncludeStartingInventory,
            version: Version.Parse(Plugin.Version), // By definition of Version for BepInEx, this must succeed
            tags: null,
            uuid: null,
            password: settings.HasPassword ? settings.Password : null,
            requestSlotData: true
        );

        if (!loginResult.Successful || loginResult is not AP.LoginSuccessful loginSuccessful)
        {
            FeatureLogger.Error("Login to Archipelago failed!");
            CurrentState = State.Disconnected;
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

        CurrentState = State.Synced;
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
        int sectorCount = ExpeditionNames.Select(MidManager.LookupExpedition)
            .Select(e => 1 + (e!.HasSecondary ? 1 : 0) + (e!.HasOverload ? 1 : 0))
            .Sum();

        FeatureLogger.Notice("Beginning graph traversal for new rundowns");
        if (!MidManager.DoGraphTraversal(gameData, true, true, RandomizationCategories, sectorCount, true))
            FeatureLogger.Error("Graph traversal failed! This will likely cause future problems. View log for details");
        else
            FeatureLogger.Success("Graph traversal succeeded!");

        var reachableRegions = gameData.RegionList.Select((r, i) => Tuple.Create(r, i)).Where(pair => pair.Item1.Reachable);
        var reachableRegionIDs = reachableRegions.Select(pair => pair.Item2).ToHashSet();

        var reachableLocations = gameData.LocationList.Where(l => l.OwningRegionIds.All(id => reachableRegionIDs.Contains(id)));
        var emptyLocations = reachableLocations.Where(l => IsLocationRandomized(l, true)).Where(l => l.ItemID == 0).ToList();

        var floatingItems = new Stack<Item>(MidManager.GetProcessedGameData().FloatingItemIds.Select(MidManager.GetProcessedGameData().LookupItem).Where(IsItemRandomized));
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
                if (emptyLocations[(i + RootSeed) % emptyLocations.Count].ItemID != 0L)
                    FeatureLogger.Error("Overwriting item during floating items placement!"); // In case there's an error in my math
                emptyLocations[(i + RootSeed) % emptyLocations.Count].ItemID = floatingItems.Pop().ID;
                count -= 1f;
            }
        }

        if (floatingItems.Count > 0)
            FeatureLogger.Error("Failed to place all floating items! Bug in algorithm?");

        var randomizedLocations = reachableLocations.Where(l => IsLocationRandomized(l));
        if (CurrentState == State.Synced || CurrentState == State.Connected)
            ApSession!.Locations.ScoutLocationsAsync(randomizedLocations.Select(l => l.ID).ToArray()).ContinueWith(OnLocationsScouted);

        TryOverwriteRundowns();
    }

    // Returns true if the location/item pair (by location ID) was randomized
    public bool IsLocationRandomized(long locationId)
        => IsLocationRandomized(MidManager.GetProcessedGameData().LookupLocation(locationId));

    // Returns true if the location/item pair was randomized
    public bool IsLocationRandomized(Location loc, bool allowNoItem = false)
    {
        void log(string msg) => FeatureLogger.Debug($"Location {loc.ID} {loc.Name} is not randomized: {msg}");

        if (loc.Type == eRandomizationType.None)
        {
            log("Randomization is not supported");
            return false;
        }
        else if (!allowNoItem && loc.ItemID == 0)
        {
            log($"No associated item");
            return false;
        }
        else if (!allowNoItem && !IsItemRandomized(MidManager.GetProcessedGameData().LookupItem(loc.ItemID)))
        {
            log("Contained item is not randomized");
            return false;
        }
        else
        {
            FeatureLogger.Debug($"Location {loc.ID} {loc.Name} is randomized");
            return true;
        }
    }

    // Returns true if the item is randomized
    public bool IsItemRandomized(Item? item)
        => (item?.Type ?? eRandomizationType.None) != eRandomizationType.None
        && item!.RandomizationCategories.Any(RandomizationCategories.Contains);

    /// <summary>
    /// Notify the state tracker that a region has been found by region name
    /// </summary>
    /// <param name="name">Name of the region</param>
    public void NotifyFoundRegion(string name)
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
        foreach (var loc in gameData.LookupRegion(region).ConnectedLocationIds.Select(gameData.LookupLocation))
        {
            if (!loc.AutoDiscover) continue;
            if (FoundLocations.Contains(loc.ID)) continue;
            if (loc.OwningRegionIds.Any(r => !FoundRegions.Contains(r))) continue;
            NotifyFoundLocation(loc.ID);
        }
    }

    /// <summary>
    /// Notify the state tracker that a location has been found / "checked"
    /// </summary>
    /// <param name="id">ID of the location</param>
    /// <param name="force">Intended for debug; set to true to force the location to be rediscovered, obtaining the item a second time</param>
    /// <returns>True if the location has been randomized, false otherwise</returns>
    /// <remarks>
    /// If this function returns true, the caller should generally block the vanilla item from being obtained.
    /// If it returns false, the caller should generally allow the vanilla item to be obtained.
    /// </remarks>
    public bool NotifyFoundLocation(long id, bool force = false)
    {
        bool isRandomized = IsLocationRandomized(id);
        if (FoundLocations.Add(id) || force)
        {
            Plugin plugin = Plugin.Get();
            Game.Data gameData = MidManager.GetProcessedGameData();
            Location location = gameData.LookupLocation(id);
            FeatureLogger.Debug($"Discovered Location: {location.Name}");

            if (isRandomized)
            {
                if (CurrentState == State.FakeConnect)
                    CollectItem(location.ItemID); // Bypass network connection
                else
                    ApSession?.Locations.CompleteLocationChecksAsync(id);
            }
        }
        return isRandomized;
    }

    /// <summary>
    /// Notify the state tracker that many locations have been found / "checked"
    /// </summary>
    /// <param name="ids">IDs of the locations</param>
    /// <param name="force">Intended for debug; set to true to force the locations to be rediscovered, obtaining the items a second time</param>
    /// <returns>True if any of the provided locations have been randomized, false otherwise</returns>
    /// <remarks>
    /// If this function returns true, the caller should generally block the vanilla item from being obtained.
    /// If it returns false, the caller should generally allow the vanilla item to be obtained.
    /// This function combines all discovered IDs into a single network call, but is otherwise the same as <see cref="NotifyFoundLocation(long, bool)"/>
    /// </remarks>
    public bool NotifyFoundLocations(IEnumerable<long> ids, bool force = false)
    {
        var idPairs = ids.Select(id => Tuple.Create(id, IsLocationRandomized(id)));
        bool anyRandomized = idPairs.Any(pair => pair.Item2);
        var actualPairs = idPairs.Where(pair => FoundLocations.Add(pair.Item1) || force).ToArray();
        if (!actualPairs.Any()) return anyRandomized;

        Plugin plugin = Plugin.Get();
        Game.Data gameData = MidManager.GetProcessedGameData();
        foreach (var pair in actualPairs)
        {
            Location location = gameData.LookupLocation(pair.Item1);
            FeatureLogger.Debug($"Discovered Location: {location.Name}");
            if (CurrentState == State.FakeConnect && pair.Item2)
                CollectItem(location.ItemID);
        }

        if (CurrentState != State.FakeConnect)
            ApSession?.Locations.CompleteLocationChecksAsync(actualPairs.Select(pair => pair.Item1).ToArray());

        return anyRandomized;
    }

    /// <summary>
    /// If synced, overwrite the existing rundown list with new ones for the expedition
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown for several misc edge cases which should never occur</exception>
    public void TryOverwriteRundowns()
    {
        if (CurrentState < State.Synced) return;

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
            newExpedition.Accessibility = CurrentState == State.FakeConnect ? eExpeditionAccessibility.AlwaysAllow : eExpeditionAccessibility.AlwayBlock;
            newExpedition.UnlockedByExpedition = new() { Tier = eRundownTier.TierA, Exp = 0 };
            newExpeditions.Enqueue(newExpedition);
        }

        // This section mostly deals with distributing expeditions into separate rundowns such that it looks pretty and unique
        int numRundowns;
        if (newExpeditions.Count < 12)
            numRundowns = 1;
        else
            numRundowns = newExpeditions.Count / 10;

        System.Random random = new(RootSeed);
        List<RundownDataBlock> newRundowns = Enumerable.Range(1, numRundowns).Select(i => RundownDataBlock.GetBlock($"Archipelago {i}")).ToList();
        GameSetupDataBlock setup = GameSetupDataBlock.GetAllBlocks()[0];
        setup.RundownIdsToLoad = new(numRundowns);

        for (int i = 0; i < numRundowns; i++)
        {
            var rundown = newRundowns[i];
            if (newRundowns[i] == null)
            {
                newRundowns[i] = new() { name = $"Archipelago {i + 1}" };
                RundownDataBlock.AddBlock(newRundowns[i]);
                rundown = newRundowns[i];

                setup.RundownIdsToLoad.Add(rundown.persistentID);

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
        Globals.Global.RundownIdToLoad = 0;
        Globals.Global.ActiveRundownIds = setup.RundownIdsToLoad.ToArray().Cast<Il2CppStructArray<uint>>();

        var selections = MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections;
        var positions = MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelectionPositions;
        for (int i = 1; i < 6; i++)
        {
            (selections[i + 1], selections[i]) = (selections[i], selections[i + 1]);
            (positions[i + 1], positions[i]) = (positions[i], positions[i + 1]);
        }

        for (int i = 0; i < numRundowns; i++)
        {
            MainMenuGuiLayer.Current.PageRundownNew.UpdateRundownSelectionButton(
                MainMenuGuiLayer.Current.PageRundownNew.m_rundownSelections[i],
                newRundowns[i].persistentID
            );
        }
    }

    /// <summary>
    /// Standard update method used to check for received items
    /// </summary>
    public override void Update()
    {
        if (CurrentState != State.Synced)
            return;

        if (ApSession == null)
            FeatureLogger.Error("ApSession is null during item update!");
        else while (ApSession!.Items.Any())
        {
            var itemInfo = ApSession.Items.DequeueItem();

            // If the location in question is from our own game and it doesn't support randomization,
            //  then it's an unrandomized item which was already collected the vanilla way
            if (itemInfo.LocationGame == ApSession.ConnectionInfo.Game) // TODO: I don't think this is correct
            {
                Location location = MidManager.GetProcessedGameData().LookupLocation(itemInfo.LocationId);
                if (!IsLocationRandomized(location))
                    continue;
            }

            CollectItem(itemInfo.ItemId);
        }
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
    public void CollectItem(string itemName)
        => CollectItem(MidManager.GetProcessedGameData().LookupItem(itemName));

    // Immediately collect an item, bypassing location checks and so forth
    public void CollectItem(long itemId)
        => CollectItem(MidManager.GetProcessedGameData().LookupItem(itemId));

    /// <summary>
    /// Immediately collect an item
    /// Only checks against queued items; all other checks are skipped
    /// </summary>
    /// <param name="item">The item to collect</param>
    public void CollectItem(Item item)
    {
        if (QueuedItemReplacements.TryGetValue(item, out var replacements))
        {
            if (replacements.Count == 1)
                QueuedItemReplacements.Remove(item);
            item = replacements.Dequeue();
        }

        ItemCounts[item] = ItemCounts.GetValueOrDefault(item, 0) + 1;
        item.OnItemObtained(this);
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
    public void UncollectItem(Item item)
    {
        int currentCount = ItemCounts.GetValueOrDefault(item, 0);
        if (currentCount <= 0)
        {
            FeatureLogger.Error($"Cannot uncollect item; it is not currently collected! Item: {item.Name}");
            return;
        }
        ItemCounts[item] = currentCount - 1;
        item.OnItemLost(this);
    }

    /// <summary>
    /// Add an item to the terminal system.
    /// The terminal system is cleared each expedition; re-add the item each time if needed.
    /// </summary>
    /// <param name="item">The item to add</param>
    public void AddItemToTerminal(Item item)
    {
        System.Random random = new();
        char r() // Picks a random character
        {
            int choice = (int)(36d * random.NextDouble());
            return (char)(choice >= 10 ? 'A' + (choice - 10) : '0' + choice);
        }

        string newCode;
        do
        {
            newCode = $"{r()}{r()}{r()}-{r()}{r()}{r()}-{r()}{r()}{r()}";
        } while (ItemsInTerminalSystem.Any(pair => pair.Item2 == newCode));

        ItemsInTerminalSystem.Add(Tuple.Create(item, newCode));
    }

    [ArchivePatch(typeof(WardenObjectiveManager), nameof(WardenObjectiveManager.OnLocalPlayerStartExpedition))]
    public static class WardenObjectiveManager__OnLocalPlayerStartExpedition__Patch
    {
        public static void Postfix()
        {
            StateTracker self = ArchipelagoFeatureHelper.GetFeature<StateTracker>();
            Expedition.Data? expeditionData = Expedition.Data.FromCurrentExpedition();
            if (expeditionData == null)
                FeatureLogger.Error("Failed to identify expedition on drop; skipping relevant events");

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
