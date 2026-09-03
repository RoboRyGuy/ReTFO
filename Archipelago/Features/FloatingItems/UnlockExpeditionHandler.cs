using GameData;
using Player;
using ReTFO.Archipelago.Features.ObjectiveHandlers;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Members;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Core.FeaturesAPI.Components;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.FloatingItems;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class UnlockExpeditionHandler_Tags
{
    extension (Game.Data data)
    {
        /// <summary>
        /// Parent region of regions containing floating expedition unlock items
        /// </summary>
        public RegionID Region_FloatingExpeditionPaths
            => RegionID.From(data, "Floating Expedition Paths", data => new("Region with paths connecting expeditions to the menu region via the ITEM requirements.", new()));

        /// <summary>
        /// Parent region of regions containing progressive expedition unlock paths
        /// </summary>
        public RegionID Region_ProgressiveExpeditionPaths
            => RegionID.From(data, "Progressive Expedition Paths", data => new("Region with paths connecting expeditions to the menu region via the PROGRESSIVE requirements.", new()));

        /// <summary>
        /// Parent region of regions containing progressive expedition unlocks
        /// </summary>
        public RegionID Region_ProgressiveExpeditionLocations
            => RegionID.From(data, "Progressive Expedition Locations", data => new("Region with standard progressive locations/item rewards for clearing expeditions.", new()));

        /// <summary>
        /// Parent location of progressive expedition unlock locations
        /// </summary>
        public LocationID Location_ProgressiveExpeditionUnlocks
            => LocationID.From(data, "Progressive Expedition Unlock Locations", data => new("Locations containing progressive expedition unlock items", data.Location_Never));

        /// <summary>
        /// Parent tag of all items which unlock expeditions
        /// </summary>
        public ItemID Item_FloatingExpeditionUnlocks
            => ItemID.From(data, "Floating Expedition Unlock Items", data => new("Floating items which trigger specifc expeditions to be unlocked", data.Item_All));

        /// <summary>
        /// A stacking expedition unlock item
        /// </summary>
        public ItemID Item_ProgressiveExpeditionUnlock
            => ItemID.From(
                data, 
                "Progressive Expedition Unlock", 
                data => new("Item which, by gaining more, unlocks more and more expeditions", data.Item_All),
                new UnlockExpeditionHandler.ProgressiveExpeditionUnlockItem()
            );

        /// <summary>
        /// Special event item used to allow main-only clears
        /// </summary>
        public ItemID Item_AllowMainOnlyProgression
            => ItemID.From(
                data,
                "Allow Main Only Progression",
                data => new("Item which allows players to progress to next expedition with only a main clear", data.Item_Never),
                new Item(new ItemData() { IsProgression = true })
            );
    }

    extension (Expedition.Data data)
    {
        /// <summary>
        /// Region containing the progressive expedition unlock reward for a particular expedition
        /// </summary>
        public RegionID Region_ProgressiveExpeditionUnlock_ByExpedition
            => RegionID.From(data, $"{data.ExpeditionName} Unlock Expedition Region", data => new("Region containing the progressive expedition unlock reward for a particular expedition", data.Region_Expedition, data.Region_ProgressiveExpeditionLocations));

        /// <summary>
        /// Location containing the progressive expedition unlock for a particular expedition
        /// </summary>
        public LocationID Location_ProgressiveExpeditionUnlock_Instance
            => LocationID.From(data, $"{data.ExpeditionName} Progressive Expedition Unlock Location", data => new("Location containing the progressive expedition unlock item for a particular expedition", data.Location_ProgressiveExpeditionUnlocks));

        /// <summary>
        /// Item which unlocks a particular expedition
        /// </summary>
        public ItemID Item_FloatingExpedtionUnlock_Instance
            => ItemID.From(
                data, 
                $"{data.ExpeditionName} Expedition Unlock Item", 
                data => new("Item which unlocks a particular expedition", data.Item_FloatingExpeditionUnlocks),
                new UnlockExpeditionHandler.FloatingExpeditionUnlockItem(data.Region_Expedition)
            );
    }
}

// Handles the UnlockExpedition item, and the path between menu and the expedition's first zone
[EnableFeatureByDefault, AutomatedFeature]
public class UnlockExpeditionHandler : ArchipelagoFeature
{
    public override string Name => "Unlock Expedition Handler";
    public override string Description 
        => "Locks expeditions and adds floating expedition unlock items which unlock them";
    public override FeatureGroup Group => FeatureGroups.FloatingHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private static void ClearProgressiveExpeditions(StateTracker st)
        => s_expeditionProgressivelyOrderered = null;

    public override void OnEnable()
    {
        base.OnEnable();
        StateTracker.Get().OnStateChange += ClearProgressiveExpeditions;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        StateTracker.Get().OnStateChange -= ClearProgressiveExpeditions;
    }

    [FeatureConfig]
    public static int Config { get; set; }

    public class Settings
    {
        [FSDisplayName("Unlock All Expeditions")]
        [FSDescription("Immediately unlocks all expeditions")]
        public FButton UnlockAllButton { get; set; } = new FButton("Unlock All", callback: UnlockAll);

        [FSDisplayName("Re-Lock All Expeditions")]
        [FSDescription("Undoes the Unlock All button, applying locks to any expeditions which were previously locked")]
        public FButton ResetAllButton { get; set; } = new FButton("Reset Locks", callback: ResetLocks);
    }

    /// <summary>
    /// Helper values which describes different supported progression styles for options handling
    /// </summary>
    private enum ProgressionStyles
    {
        BIT_EnableFloatingItems,
        BIT_EnableFloatingPaths,
        BIT_StartWithFloatingItems,
        BIT_EnableProgressiveItems,
        BIT_EnableProgressivePaths,
        BIT_AllowMainOnlyProgression,

        StartWithFloating = 1 << BIT_StartWithFloatingItems,
        FloatingItems     = 1 << BIT_EnableFloatingItems,
        FloatingPaths     = 1 << BIT_EnableFloatingPaths,
        ProgressiveItems  = 1 << BIT_EnableProgressiveItems,
        ProgressivePaths  = 1 << BIT_EnableProgressivePaths,
        AllowMainOnly     = 1 << BIT_AllowMainOnlyProgression,

        FREE         = FloatingPaths | FloatingItems | StartWithFloating,
        ITEM         = FloatingPaths | FloatingItems,
        PROGRESSIVE  = ProgressivePaths | ProgressiveItems | AllowMainOnly,
        FULLPROG     = ProgressivePaths | ProgressiveItems,
        DUAL         = PROGRESSIVE | ITEM,
        FULLDUAL     = FULLPROG | ITEM,
        SEMIPROG     = PROGRESSIVE | FloatingItems,
        SEMIFULLPROG = FULLPROG | FloatingItems,
    }

    /// <summary>
    /// Options group used to configure expeditions
    /// </summary>
    public const string EXPEDITION_OPTION_CATEGORY = "Expeditions";

    /// <summary>
    /// Cached options which are updated per-expedition processed
    /// </summary>
    private (OptionMultiChoice?, OptionMultiChoice?) m_choices = (null, null);

    /// <summary>
    /// Creates supported expedition options and returns the ones we typically modify
    /// </summary>
    private (OptionMultiChoice, OptionMultiChoice) GetOrCreateOptions(Game.Data data)
    {
        if (m_choices.Item1 != null) return m_choices!;

        OptionID enabledExpeditions = data.AddOption(m_choices.Item1 = new OptionMultiChoice(
            displayName: "Enabled Expeditions",
            description:
                "Select one or more expeditions to enable. These will be the expeditions you must clear."
                + " Optionally, choose \"All Expeditions\" to enable everything (including normally hidden expeditions, at your own risk)",
            category: EXPEDITION_OPTION_CATEGORY,
            categorySort: Array.Empty<uint>(),
            defaultValue: 0x0E, // Second, third, and fourth entries in list (skipping All Expeditions)
            condition: new(),
            choiceNames: new() { "All Expeditions" },
            choiceValues: new() { data.Region_AllExpeditions.ID }
        ));

        data.AddOption(new OptionAddToSet(
            target: Option.eSetTarget.RegionWhitelist,
            tag: enabledExpeditions,
            condition: new()
        ));

        OptionID progressionStyleChoice = data.AddOption(new OptionChoice(
            displayName: "Progression Style",
            description:
                "Controls how expeditions are unlocked."
                + "\n FREE         - All expeditions start unlocked."
                + "\n ITEM         - You must obtain an item to unlock an expedition."
                + "\n PROGRESSIVE  - Each main sector cleared unlocks a new expedition."
                + "\n FULLPROG     - Each full clear unlocks a new expedition."
                + "\n                This means PE, secondary and overload are required."
                + "\n DUAL         - Enables both ITEM and PROGRESSIVE styles."
                + "\n FULLDUAL     - Enables both ITEM and FULLPROG styles."
                + "\n SEMIPROG     - Both ITEM and PROGRESSIVE. Archipelago will ignore"
                + "\n                ITEM paths during randomization."
                + "\n SEMIFULLPROG - Both ITEM and FULLPROG. Archipelago will ignore"
                + "\n                ITEM paths during randomization.",
            category: EXPEDITION_OPTION_CATEGORY,
            categorySort: Array.Empty<uint>(),
            defaultValue: (long)ProgressionStyles.ITEM,
            condition: new(),
            choiceNames: new() { "FREE", "ITEM", "PROGRESSIVE", "FULLPROG", "DUAL", "FULLDUAL", "SEMIPROG", "SEMIFULLPROG" },
            choiceValues: new() {
                (long)ProgressionStyles.FREE,
                (long)ProgressionStyles.ITEM,
                (long)ProgressionStyles.PROGRESSIVE,
                (long)ProgressionStyles.FULLPROG,
                (long)ProgressionStyles.DUAL,
                (long)ProgressionStyles.FULLDUAL,
                (long)ProgressionStyles.SEMIPROG,
                (long)ProgressionStyles.SEMIFULLPROG,
            }
        ));

        OptionID isStartWithFloating = data.AddOption(new OptionGetBitOperation(progressionStyleChoice, (long)ProgressionStyles.BIT_StartWithFloatingItems));
        OptionID isFloatingItems = data.AddOption(new OptionGetBitOperation(progressionStyleChoice, (long)ProgressionStyles.BIT_EnableFloatingItems));
        OptionID isFloatingPaths = data.AddOption(new OptionGetBitOperation(progressionStyleChoice, (long)ProgressionStyles.BIT_EnableFloatingPaths));
        OptionID isProgressiveItems = data.AddOption(new OptionGetBitOperation(progressionStyleChoice, (long)ProgressionStyles.BIT_EnableProgressiveItems));
        OptionID isProgressivePaths = data.AddOption(new OptionGetBitOperation(progressionStyleChoice, (long)ProgressionStyles.BIT_EnableProgressivePaths));
        OptionID isAllowMainOnly = data.AddOption(new OptionGetBitOperation(progressionStyleChoice, (long)ProgressionStyles.BIT_AllowMainOnlyProgression));

        ItemID floatingTag = data.Item_FloatingExpeditionUnlocks;
        uint[] floatingSort = Option.MakeSortKey(data, floatingTag);

        data.AddOption(new OptionAddAll(
            condition: isStartWithFloating,
            target: Option.eDictTarget.StartVouchers,
            tag: floatingTag
        ));

        data.AddOption(new OptionAddToSet(
            condition: isFloatingItems,
            target: Option.eSetTarget.ItemWhitelist,
            floatingTag
        ));

        data.AddOption(new OptionAddToSet(
            condition: data.AddOption(new OptionOrOperation(
                isFloatingPaths, 
                data.AddOption(new OptionAndOperation(isFloatingItems, data.Option_IsFakeGeneration)))
            ),
            target: Option.eSetTarget.RegionWhitelist,
            tag: data.Region_FloatingExpeditionPaths
        ));

        data.AddOption(new OptionAddToSet(
            condition: data.AddOption(new OptionNotOperation(isProgressiveItems)),
            target: Option.eSetTarget.RegionBlacklist,
            tag: data.Region_ProgressiveExpeditionLocations
        ));

        data.AddOption(new OptionAddToSet(
            condition: isProgressivePaths,
            target: Option.eSetTarget.RegionWhitelist,
            tag: data.Region_ProgressiveExpeditionPaths
        ));

        data.AddOption(new OptionAddCount(
            condition: isAllowMainOnly,
            target: Option.eDictTarget.StartInventory,
            tag: data.Item_AllowMainOnlyProgression,
            count: 1L
        ));

        OptionID floatStartExpeditions = data.AddOption(m_choices.Item2 = new OptionMultiChoice(
            displayName: "Starting Expeditions (ITEM)",
            description:
                "Only applicable in ITEM mode. Choose one or more expeditions which start unlocked."
                + " You must choose expeditions which are enabled in the \"Enabled Expeditions\" option, "
                + " otherwise this option has no effect.",
            category: EXPEDITION_OPTION_CATEGORY,
            categorySort: floatingSort,
            defaultValue: 0x01, // First entry in list
            condition: isFloatingPaths,
            choiceNames: new(),
            choiceValues: new()
        ));

        data.AddOption(new OptionAddAll(
            condition: isFloatingPaths,
            target: Option.eDictTarget.StartVouchers,
            tag: floatStartExpeditions
        ));

        OptionID floatStartExpeditionsRand = data.AddOption(new OptionRange(
            displayName: "Random Starting Expeditions (ITEM)",
            description:
                "Only applicable in ITEM mode. The desired number of expeditions will be randomly"
                + " selected and will start unlocked. Intended for when the expeditions themselves are"
                + " randomly selected.",
            category: EXPEDITION_OPTION_CATEGORY,
            categorySort: floatingSort,
            defaultValue: 0,
            condition: isFloatingPaths,
            min: 0,
            max: 99
        ));

        data.AddOption(new OptionAddCount(
            condition: isFloatingPaths,
            target: Option.eDictTarget.StartVouchers,
            tag: floatingTag,
            count: floatStartExpeditionsRand
        ));

        ItemID progressiveTag = data.Item_ProgressiveExpeditionUnlock;
        uint[] progressiveSort = Option.MakeSortKey(data, progressiveTag);

        OptionID progressiveUnlockCount = data.AddOption(new OptionRange(
            displayName: "Number of Unlocked Expeditions (PROGRESSIVE)",
            description: 
                "Only applicable in any variant of PROGRESSIVE mode. The number of expeditions which will start unlocked."
                + " This generally guarantees you have access to at least N expeditions at any given time.",
            category: EXPEDITION_OPTION_CATEGORY,
            categorySort: progressiveSort,
            defaultValue: 1,
            condition: isProgressivePaths,
            min: 0,
            max: 99
        ));

        data.AddOption(new OptionAddCount(
            condition: isProgressivePaths,
            target: Option.eDictTarget.StartInventory,
            tag: progressiveTag,
            count: progressiveUnlockCount
        ));

        return m_choices!;
    }

    /// <summary>
    /// Get the sort key used to order expeditions in "progressive" order
    /// </summary>
    public static string GetProgressiveSortKey(Expedition.Data data)
        => $"{Enum.GetName(data.ExpeditionTier) ?? "unknown"}-{data.ExpeditionIndex}-{data.ExpeditionName}";

    /// <summary>
    /// Gets all enabled expedition in the order they're unlocked in a progressive-style playthrough
    /// </summary>
    public static IEnumerable<RegionID> GetExpeditionsProgressiveOrdering(StateTracker stateTracker)
    {
        if (s_expeditionProgressivelyOrderered != null) 
            return s_expeditionProgressivelyOrderered;

        Game.Data data = stateTracker.GameData;
        List<Expedition.Data> enabledExpeditions = new();
        foreach (var entry in data.Regions.GetAllEntries())
        {
            if (!entry.Value.Value.Randomized) continue;
            if (Expedition.Data.TryFromRegion(data, entry.Key, out var expedition))
                enabledExpeditions.Add(expedition);
        }

        string[] keys = new string[enabledExpeditions.Count];
        s_expeditionProgressivelyOrderered = new RegionID[enabledExpeditions.Count];

        int count = 0;
        foreach (var expedition in enabledExpeditions)
        {
            keys[count] = GetProgressiveSortKey(expedition);
            s_expeditionProgressivelyOrderered[count] = expedition.Region_Expedition;
            ++count;
        }

        Array.Sort(keys, s_expeditionProgressivelyOrderered);
        return s_expeditionProgressivelyOrderered;
    }

    /// <summary>
    /// Cache of the result of <see cref="GetExpeditionsProgressiveOrdering(StateTracker)"/>
    /// </summary>
    private static RegionID[]? s_expeditionProgressivelyOrderered = null;

    /// <summary>
    /// Enumerates all expeditions from a rundown datablock 
    /// </summary>
    private static IEnumerable<ExpeditionInTierData> UnpackRundown(RundownDataBlock rundown)
    {
        foreach (var expedition in rundown.TierA) yield return expedition;
        foreach (var expedition in rundown.TierB) yield return expedition;
        foreach (var expedition in rundown.TierC) yield return expedition;
        foreach (var expedition in rundown.TierD) yield return expedition;
        foreach (var expedition in rundown.TierE) yield return expedition;
    }

    /// <summary>
    /// Gets all expeditions declared in all rundown datablocks
    /// </summary>
    /// <returns></returns>
    private static IEnumerable<ExpeditionInTierData> GetExpeditions()
        => RundownDataBlock.GetAllBlocks().SelectMany(UnpackRundown) ?? Enumerable.Empty<ExpeditionInTierData>();

    /// <summary>
    /// A floating itemw which unlocks specific expeditions
    /// </summary>
    public class FloatingExpeditionUnlockItem : Item
    {
        public FloatingExpeditionUnlockItem(RegionID expedition)
            : base(new ItemData() { IsProgression = true })
        {
            ExpeditionRegion = expedition;
        }

        public RegionID ExpeditionRegion { get; private init; }

        private IEnumerable<ExpeditionInTierData> FindExpeditions(Game.Data data)
        {
            List<ExpeditionInTierData> expeditions = new();
            
            foreach (var expedition in GetExpeditions())
            {
                if (data.TryGetExpeditionData(expedition, out Expedition.Data? eData))
                    if (eData.Region_Expedition.Equals(ExpeditionRegion)) expeditions.Add(expedition);
            }

            if (expeditions.Count == 0)
                FeatureLogger.Error($"Failed to find expedition during lock/unlock event: {data.Regions.LookUpName(ExpeditionRegion)}");

            return expeditions;
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player = null, ItemID itemId = new())
        {
            if (!ArchipelagoFeatureHelper.GetFeature<UnlockExpeditionHandler>().Enabled) return;

            foreach (var e in FindExpeditions(stateTracker.GameData))
            {
                FeatureLogger.Debug($"Unlocked expedition {stateTracker.GameData.Regions.LookUpName(ExpeditionRegion)}");
                e.Accessibility = eExpeditionAccessibility.AlwaysAllow;
            }
            RundownHandler.UpdateAllCounts(); // Since a new icon is now visible
        }

        public override void OnItemLost(StateTracker stateTracker, ItemID itemId = new())
        {
            if (!ArchipelagoFeatureHelper.GetFeature<UnlockExpeditionHandler>().Enabled) return;
            if (stateTracker.CollectedItemCounts.GetValueOrDefault(itemId, 0) > 0) return;

            foreach (var e in FindExpeditions(stateTracker.GameData))
            {
                FeatureLogger.Debug($"Locked expedition {stateTracker.GameData.Regions.LookUpName(ExpeditionRegion)}");
                e.Accessibility = eExpeditionAccessibility.AlwayBlock;
            }
            RundownHandler.UpdateAllCounts(); // Since a new icon is now hidden
        }
    }

    /// <summary>
    /// A non-floating item which unlocks more expeditions the more you hold
    /// </summary>
    public class ProgressiveExpeditionUnlockItem : Item
    {
        public ProgressiveExpeditionUnlockItem() 
            : base(new ItemData() { IsProgression = true, IsRandomLike = true }) { }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
        {
            var expeditions = GetExpeditionsProgressiveOrdering(stateTracker);
            int count = stateTracker.CollectedItemCounts.GetValueOrDefault(stateTracker.GameData.Item_ProgressiveExpeditionUnlock);
            HashSet<RegionID> regions = expeditions.Take(count).ToHashSet();
            foreach (var exp in GetExpeditions())
            {
                if (Expedition.Data.TryGetFromExpedition(exp, out var result) && regions.Contains(result.Region_Expedition))
                {
                    FeatureLogger.Debug($"Unlocked expedition {result.ExpeditionName}");
                    exp.Accessibility = eExpeditionAccessibility.AlwaysAllow;
                }
            }
            RundownHandler.UpdateAllCounts(); // Since a new icon is now shown
        }
    }

    /// <summary>
    /// Connects the different expedition unlock path styles to the menu
    /// </summary>
    [Game.Callback]
    public void ConnectUnlockPaths(Game.Data data)
    {
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Menu,
            EndingRegion = data.Region_FloatingExpeditionPaths,
        });

        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Menu,
            EndingRegion = data.Region_ProgressiveExpeditionPaths,
        });
    }

    /// <summary>
    /// Add unlock logic for each expedition, including the paths in and the related unlock items.
    /// </summary>
    [Expedition.Callback]
    public void AddExpeditionUnlock(Expedition.Data data)
    {

        ItemID floatingUnlock = data.Item_FloatingExpedtionUnlock_Instance;
        data.AddPath(new Path()
        {
            Name = $"{data.ExpeditionName} ITEM Expdition Unlock",
            StartingRegion = data.Region_FloatingExpeditionPaths,
            EndingRegion = data.Region_Expedition,
            Reqs = new(Path.eType.Item, floatingUnlock, 1u),
        });
        data.AddFloatingItem(data.Region_Expedition, floatingUnlock);

        ItemID progressiveUnlock = data.Item_ProgressiveExpeditionUnlock;
        data.AddPath(new Path()
        {
            Name = $"{GetProgressiveSortKey(data)} - {data.ExpeditionName} PROGRESSIVE Expedition Unlock",
            StartingRegion = data.Region_ProgressiveExpeditionPaths,
            EndingRegion = data.Region_Expedition,
            Reqs = new(Path.eType.ItemGrowing, data.Item_ProgressiveExpeditionUnlock, 1u),
        });

        RegionID progressiveRegion = data.Region_ProgressiveExpeditionUnlock_ByExpedition;
        RegionID clearRegion;
        var layers = data.RealLayers.ToList();
        if (layers.Count < 3) clearRegion = layers.Last().Region_SectorCleared;
        else clearRegion = data.Region_PECleared;

        data.AddPath(new Path()
        {
            StartingRegion = clearRegion,
            EndingRegion = progressiveRegion,
        });

        if (layers.Count > 1)
        {
            data.AddPath(new Path()
            {
                StartingRegion = layers[0].Region_SectorCleared,
                EndingRegion = progressiveRegion,
                Reqs = new(Path.eType.Item, data.Item_AllowMainOnlyProgression, 1u),
            });
        }

        data.Locations.CreateValue(
            data.Location_ProgressiveExpeditionUnlock_Instance,
            progressiveRegion,
            new LocationData(),
            data.Item_ProgressiveExpeditionUnlock
        );

        var choices = GetOrCreateOptions(data);
        choices.Item1.ChoiceNames.Add(data.ExpeditionName);
        choices.Item1.ChoiceValues.Add(data.Region_Expedition.ID);
        choices.Item2.ChoiceNames.Add(data.ExpeditionName);
        choices.Item2.ChoiceValues.Add(floatingUnlock.ID);
    }
    
    /// <summary>
    /// Unlocks all expeditions, ignoring normal restrictions
    /// </summary>
    public static void UnlockAll()
    {
        FeatureLogger.Debug("Unlocking all expeditions");
        foreach (var expedition in GetExpeditions())
            expedition.Accessibility = eExpeditionAccessibility.AlwaysAllow;
    }

    /// <summary>
    /// Re-applies locks to any expeditions which should be locked
    /// </summary>
    public static void ResetLocks()
    {
        StateTracker stateTracker = StateTracker.Get();
        Game.Data data = stateTracker.MidManager.GetProcessedGameData();
        FeatureLogger.Debug("Resetting expedition locks");
        foreach (var expedition in GetExpeditions())
        {
            if (!data.TryGetExpeditionData(expedition, out var eData))
            {
                FeatureLogger.Warning("Failed to find data for expedition: " + expedition.Descriptive.Prefix + "; not applying lock!");
                continue;
            }

            ItemID reqItem = eData.Item_FloatingExpedtionUnlock_Instance;
            if (stateTracker.IsItemRandomized(eData.Region_Expedition, reqItem) && stateTracker.CollectedItemCounts.GetValueOrDefault(reqItem, 0) <= 0)
            {
                FeatureLogger.Debug($"Locked expedition {eData.ExpeditionName}");
                expedition.Accessibility = eExpeditionAccessibility.AlwayBlock;
            }
        }

        ItemID progressiveUnlockId = data.Item_ProgressiveExpeditionUnlock;
        data.Items.LookUpValueChecked(progressiveUnlockId).OnItemObtained(stateTracker, new(), null, progressiveUnlockId);
    }

    /// <summary>
    /// Grants progressive expedition unlock items when completing expeditions
    /// </summary>
    [ArchivePatch(typeof(RundownManager), nameof(RundownManager.OnExpeditionEnded))]
    public static class RundownManager__OnExpeditionEnded__Patch
    {
        public static void Postfix(ExpeditionEndState endState)
        {
            if (endState != ExpeditionEndState.Success)
                return;

            StateTracker stateTracker = StateTracker.Get();
            Expedition.Data data = Expedition.Data.GetFromCurrentExpedition();

            if (stateTracker.CollectedItemCounts.GetValueOrDefault(data.Item_AllowMainOnlyProgression) > 0)
            {
                stateTracker.NotifyFoundRegion(data.Region_ProgressiveExpeditionUnlock_ByExpedition, null);
                stateTracker.NotifyFoundLocation(data.Location_ProgressiveExpeditionUnlock_Instance, null);
                return;
            }

            if (data.HasSecondary && data.HasOverload)
            {
                if (WardenObjectiveManager.CurrentState.second_status == eWardenObjectiveStatus.WardenObjectiveItemSolved
                    && WardenObjectiveManager.CurrentState.third_status == eWardenObjectiveStatus.WardenObjectiveItemSolved)
                {
                    stateTracker.NotifyFoundRegion(data.Region_ProgressiveExpeditionUnlock_ByExpedition, null);
                    stateTracker.NotifyFoundLocation(data.Location_ProgressiveExpeditionUnlock_Instance, null);
                }
            }
            else if (data.HasSecondary)
            {
                if (WardenObjectiveManager.CurrentState.second_status == eWardenObjectiveStatus.WardenObjectiveItemSolved)
                {
                    stateTracker.NotifyFoundRegion(data.Region_ProgressiveExpeditionUnlock_ByExpedition, null);
                    stateTracker.NotifyFoundLocation(data.Location_ProgressiveExpeditionUnlock_Instance, null);
                }
            }
            else if (data.HasOverload)
            {
                if (WardenObjectiveManager.CurrentState.third_status == eWardenObjectiveStatus.WardenObjectiveItemSolved)
                {
                    stateTracker.NotifyFoundRegion(data.Region_ProgressiveExpeditionUnlock_ByExpedition, null);
                    stateTracker.NotifyFoundLocation(data.Location_ProgressiveExpeditionUnlock_Instance, null);
                }
            }
            else
            {
                stateTracker.NotifyFoundRegion(data.Region_ProgressiveExpeditionUnlock_ByExpedition, null);
                stateTracker.NotifyFoundLocation(data.Location_ProgressiveExpeditionUnlock_Instance, null);
            }
        }
    }

}
