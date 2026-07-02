using GameData;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Members;
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
        /// Parent tag of all items which unlock expeditions
        /// </summary>
        public ItemID Item_ExpeditionUnlocks
            => ItemID.From(data, "Expedition Unlock Items", data => new("Items which trigger expeditions to be unlocked", data.Item_All));
    }

    extension (Expedition.Data data)
    {
        /// <summary>
        /// Item which unlocks a particular expedition
        /// </summary>
        public ItemID Item_ExpedtionUnlock_Instance
            => ItemID.From(
                data, 
                $"{data.ExpeditionName} Expedition Unlock Item", 
                data => new("Item which unlocks a particular expedition", data.Item_ExpeditionUnlocks),
                new UnlockExpeditionHandler.ExpeditionUnlockItem(data.Region_Expedition)
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

    public const string EXPEDITION_OPTION_CATEGORY = "Expeditions";
    private OptionChoice? m_choices = null;
    private OptionChoice GetOrCreateOptions(Game.Data data)
    {
        if (m_choices != null) return m_choices;

        ItemID tag = data.Item_ExpeditionUnlocks;
        uint[] sort = Option.MakeSortKey(data, tag);

        OptionID unlockRange = data.AddOption(new OptionRange(
            displayName: "Number of Unlocked Expeditions",
            description:
                "The number of random expeditions which should start unlocked."
                + " Ensure at least one expedition is unlocked either here or below."
                + "\nA value of `-1` will unlock all expeditions.",
            category: EXPEDITION_OPTION_CATEGORY,
            categorySort: sort,
            defaultValue: 1,
            condition: new(),
            min: -1,
            max: 99
        ));
        OptionID randomizationEnabled = data.AddOption(new OptionDoesNotEqualOperation(unlockRange, -1));

        data.AddOption(new OptionAddToSet(
            target: Option.eSetTarget.ItemWhitelist,
            tag: tag,
            condition: randomizationEnabled
        ));
        data.AddOption(new OptionAddToSet(
            target: Option.eSetTarget.ItemBlacklist,
            tag: tag,
            condition: data.AddOption(new OptionNotOperation(randomizationEnabled))
        ));

        data.AddOption(new OptionAddCount(
            target: Option.eDictTarget.StartVouchers,
            tag: tag,
            count: unlockRange,
            condition: randomizationEnabled
        ));

        OptionID choice = data.AddOption(m_choices = new OptionChoice(
            displayName: "Starting Expedition",
            description:
                "Choose a single expedition to guarantee unlocked. This should be one of the expeditions" 
                + " you chose in \"Required Expeditions\" or \"None\". This will be in addition to expeditions"
                + " randomly unlocked.",
            category: EXPEDITION_OPTION_CATEGORY,
            categorySort: sort,
            defaultValue: new ItemID().ID,
            condition: randomizationEnabled,
            choiceNames: new() { "None" },
            choiceValues: new() { new ItemID().ID }
        ));

        data.AddOption(new OptionAddToSet(
            target: Option.eSetTarget.ItemBlacklist,
            tag: choice,
            condition: randomizationEnabled
        ));

        OptionID earlyRange = data.AddOption(new OptionRange(
            displayName: "Number of Early Expeditions",
            description:
                "The number of random expeditions which should be guaranteed reachable before any item is collected."
                + Option.EARLY_WARNING_SUFFIX,
            category: EXPEDITION_OPTION_CATEGORY,
            categorySort: sort,
            defaultValue: 1,
            condition: new(),
            min: 0,
            max: 99
        ));

        data.AddOption(new OptionAddCount(
            target: Option.eDictTarget.EarlyItems,
            tag: tag,
            count: earlyRange,
            condition: randomizationEnabled
        ));

        return m_choices;
    }

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
    /// The item which unlocks an expedition
    /// </summary>
    public class ExpeditionUnlockItem : Item
    {
        public ExpeditionUnlockItem(RegionID expedition)
            : base(MakeRandData())
        {
            ExpeditionRegion = expedition;
        }

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true, IsCollectedByDefault = true };

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
            if (ArchipelagoFeatureHelper.GetFeature<UnlockExpeditionHandler>().Enabled)
            {
                foreach (var e in FindExpeditions(stateTracker.GameData)) 
                    e.Accessibility = eExpeditionAccessibility.AlwaysAllow;
                StateTracker.UpdateLocationCounts(); // Since a new icon is now visible
            }
        }

        public override void OnItemLost(StateTracker stateTracker, ItemID itemId = new())
        {
            if (ArchipelagoFeatureHelper.GetFeature<UnlockExpeditionHandler>().Enabled)
            {
                foreach (var e in FindExpeditions(stateTracker.GameData)) 
                    e.Accessibility = eExpeditionAccessibility.AlwayBlock;
                StateTracker.UpdateLocationCounts(); // Since a new icon is now hidden
            }
        }
    }

    /// <summary>
    /// Add an unlock item for each expedition (as well as a path).
    /// Update the choice option.
    /// </summary>
    [Expedition.Callback]
    public void AddExpeditionUnlock(Expedition.Data data)
    {
        ItemID item = data.Item_ExpedtionUnlock_Instance;
        data.AddFloatingItem(data.Region_Expedition, item);
        data.AddPath(new Path()
        {
            Name = $"{data.ExpeditionName} Expdition Unlock",
            StartingRegion = data.Region_Menu,
            EndingRegion = data.Region_Expedition,
            ReqItem = new(Path.RequiredItem.eType.Item, item),
            ReqCount = 1u,
        });

        OptionChoice choice = GetOrCreateOptions(data);
        choice.ChoiceNames.Add(data.ExpeditionName);
        choice.ChoiceValues.Add(item.ID);
    }
    
    /// <summary>
    /// Unlocks all expeditions, ignoring normal restrictions
    /// </summary>
    public static void UnlockAll()
    {
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
        foreach (var expedition in GetExpeditions())
        {
            if (!data.TryGetExpeditionData(expedition, out var eData))
            {
                FeatureLogger.Warning("Failed to find data for expedition: " + expedition.Descriptive.Prefix + "; not applying lock!");
                continue;
            }

            ItemID reqItem = eData.Item_ExpedtionUnlock_Instance;
            if (stateTracker.IsItemRandomized(eData.Region_Expedition, reqItem) && stateTracker.CollectedItemCounts.GetValueOrDefault(reqItem, 0) <= 0)
                expedition.Accessibility = eExpeditionAccessibility.AlwayBlock;
        }
    }

}
