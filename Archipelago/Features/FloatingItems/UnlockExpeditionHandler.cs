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
        public TagResolver Tag_ExpeditionUnlocks
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Expedition Unlock Items", "Items which trigger expeditions to be unlocked", gd.Tag_FloatingItems));
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

        OptionID unlockRange = data.AddOption(new OptionRange()
        {
            DisplayName = "Number of Unlocked Expeditions",
            Description =
                "The number of random expeditions which should start unlocked."
                + " Ensure at least one expedition is unlocked either here or below.",
            Category = EXPEDITION_OPTION_CATEGORY,
            DefaultValue = 1,
            Condition = new(),
            Min = -1,
            Max = 99,
        });
        OptionID randomizationEnabled = data.AddOption(new OptionDoesNotEqualOperation() { LParam = unlockRange, RParam = -1 });

        RandomizationTag tag = data.Tag_ExpeditionUnlocks.SelfResolve();
        data.AddOption(new OptionAddToSet()
        {
            Target = Option.eTarget.Whitelist,
            Tag = tag,
            Condition = randomizationEnabled,
        });
        data.AddOption(new OptionAddToSet()
        {
            Target = Option.eTarget.Blacklist,
            Tag = tag,
            Condition = data.AddOption(new OptionNotOperation() { Param = randomizationEnabled }),
        });

        data.AddOption(new OptionAddCount()
        {
            Target = Option.eTarget.StartVouchers,
            Tag = tag,
            Count = unlockRange,
            Condition = randomizationEnabled,
        });

        OptionID choice = data.AddOption(m_choices = new OptionChoice()
        {
            DisplayName = "Starting Expedition",
            Description =
                "Choose a single expedition to guarantee unlocked. This should be one of the expeditions" 
                + " you chose in \"Required Expeditions\" or \"none\". This will be in addition to expeditions"
                + " randomly unlocked.",
            Category = EXPEDITION_OPTION_CATEGORY,
            DefaultValue = new RandomizationTag().AsId,
            Condition = randomizationEnabled,
            ChoiceNames = new() { "None" },
            ChoiceValues = new() { new RandomizationTag().AsId },
        });

        data.AddOption(new OptionAddToSet()
        {
            Target = Option.eTarget.Blacklist,
            Tag = choice,
            Condition = randomizationEnabled,
        });

        OptionID earlyRange = data.AddOption(new OptionRange()
        {
            DisplayName = "Number of Early Expeditions",
            Description =
                "The number of random expeditions which should be guaranteed reachable before any item is collected."
                + Option.EARLY_WARNING_SUFFIX,
            Category = EXPEDITION_OPTION_CATEGORY,
            DefaultValue = 1,
            Condition = new(),
            Min = 0,
            Max = 99,
        });

        data.AddOption(new OptionAddCount()
        {
            Target = Option.eTarget.EarlyItems,
            Tag = tag,
            Count = earlyRange,
            Condition = randomizationEnabled,
        });

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
    private class ExpeditionUnlockItem : Item
    {
        public ExpeditionUnlockItem(Expedition.Data expedition)
            : base(MakeTag(expedition), MakeRandData())
        {
            ExpeditionData = expedition;
            Tag2 = expedition.Tag_UnlockItems; // Used internally for path traversal checks
        }

        public static TagResolver MakeTag(Expedition.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Expedition Unlock", "Item which unlocks a particular expedition", data.Tag_ExpeditionUnlocks));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true, IsCollectedByDefault = true };

        public Expedition.Data ExpeditionData { get; set; }

        public override Expedition.Data? RequiredExpedition => ExpeditionData;

        private List<ExpeditionInTierData> FindExpeditions()
        {
            List<ExpeditionInTierData> expeditions = GetExpeditions()
                .Where(e => Expedition.Data.TryFromExpedition(e)?.IsSameExpedition(ExpeditionData) ?? false)
                .ToList();

            if (expeditions.Count == 0)
                FeatureLogger.Error($"Failed to find expedition during lock/unlock event: {ExpeditionData.ExpeditionName}");

            return expeditions;
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player = null)
        {
            if (ArchipelagoFeatureHelper.GetFeature<UnlockExpeditionHandler>().Enabled)
            {
                foreach (var e in FindExpeditions()) 
                    e.Accessibility = eExpeditionAccessibility.AlwaysAllow;
                StateTracker.UpdateLocationCounts(); // Since a new icon is now visible
            }
        }

        public override void OnItemLost(StateTracker stateTracker)
        {
            if (ArchipelagoFeatureHelper.GetFeature<UnlockExpeditionHandler>().Enabled)
            {
                foreach (var e in FindExpeditions()) 
                    e.Accessibility = eExpeditionAccessibility.AlwayBlock;
                StateTracker.UpdateLocationCounts(); // Since a new icon is now hidden
            }
        }
    }

    /// <summary>
    /// Get the expedition unlock item for the provided expedition
    /// </summary>
    public static KeyedItem GetExpeditionUnlockItem(Expedition.Data data)
    {
        if (data.TryLookupItem(ExpeditionUnlockItem.MakeTag(data), out var item))
            return item;

        Item newItem = new ExpeditionUnlockItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    /// <summary>
    /// Add an unlock item for each expedition (as well as a path).
    /// Update the choice option.
    /// </summary>
    [Expedition.Callback]
    public void AddExpeditionUnlock(Expedition.Data data)
    {
        KeyedItem reqItem = GetExpeditionUnlockItem(data);
        data.AddFloatingItem(reqItem.ID);
        data.AddPath(new Path()
        {
            Name = $"{data.ExpeditionName} Expdition Unlock",
            StartingRegion = data.MenuRegion,
            EndingRegion = data.StartingRegion,
            ReqItem = reqItem.Item.PathReqs,
            ReqCount = 1u,
        });

        OptionChoice choice = GetOrCreateOptions(data);
        choice.ChoiceNames.Add(data.ExpeditionName);
        choice.ChoiceValues.Add(reqItem.Item.NameTag.AsId);
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
            if (!data.TryLookupExpedition(expedition.Descriptive.Prefix, out var eData))
            {
                FeatureLogger.Warning("Failed to find data for expedition: " + expedition.Descriptive.Prefix + "; not applying lock!");
                continue;
            }

            KeyedItem reqItem = GetExpeditionUnlockItem(eData);
            if (reqItem.Item.RandData.ShouldBeRandomized && stateTracker.CollectedItemCounts.GetValueOrDefault(reqItem.ID, 0) <= 0)
                expedition.Accessibility = eExpeditionAccessibility.AlwayBlock;
        }
    }

}
