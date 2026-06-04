using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class CorruptedUplinkHandler_Tags
{
    extension(Game.Data data)
    {
        public TagResolver Tag_CorruptedUplinkTerminalLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Corrupted Uplink Terminal Locations", "Locations checked by finding Corrupted Uplink terminals", gd.Tag_Never));

        public TagResolver Tag_CorruptedUplinkTerminalItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Corrupted Uplink Terminal Items", "Terminal items used for Corrupted Uplinks", gd.Tag_Never));

        public TagResolver Tag_CorruptedUplinkCompletionLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Corrupted Uplink Completion Locations", "Locations checked by completing Corrupted Uplinks", gd.Tag_Never));

        public TagResolver Tag_CorruptedUplinkCompletionItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Corrupted Uplink Completion Items", "Represents a corrupted uplink having been completed", gd.Tag_Never));
    }

    extension(Objective.Data data)
    {
        public TagResolver Tag_CorruptedUplinkTerminalLocations_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Corrupted Uplink Terminal Locations", "Locations checked by finding Corrupted Uplink terminals for a particular objective", gd.Tag_CorruptedUplinkTerminalLocations));

        public TagResolver Tag_CorruptedUplinkTerminalItems_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Corrupted Uplink Terminal Items", "Terminal items used for Corrupted Uplinks for a particular objective", gd.Tag_CorruptedUplinkTerminalItems));

        public TagResolver Tag_CorruptedUplinkCompletionLocations_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Corrupted Uplink Completion Locations", "Locations checked by completing Corrupted Uplinks for a particular objective", gd.Tag_CorruptedUplinkCompletionLocations));

        public TagResolver Tag_CorruptedUplinkCompletionItems_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Corrupted Uplink Completion Items", "Represents a corrupted uplink having been completed for a particular objective", gd.Tag_CorruptedUplinkCompletionItems));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class CorruptedUplinkHandler : ArchipelagoFeature
{
    public override string Name => "Corrupted Uplink Handler";
    public override string Description
        => "Handles the CorruptedTerminalUplink objective type.\n"
        + "This handles specifically only the corrupted or \"dual\" uplink type"
        + "Example: R5C3";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.CorruptedTerminalUplink;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return $"Perform {data.Objective.Uplink_NumberOfTerminals} Corrupted Uplinks";
        }

        // True if This is the correct objective
        public static bool IsCorrectObjective(Objective.Data data)
            => data.Objective.Type == ObjectiveType;

        // Assert This is the correct objective, and log an error if it is not
        public static void CheckIsCorrectObjective(Objective.Data data)
        {
            if (!IsCorrectObjective(data))
                FeatureLogger.Error($"Wrong objective type! Expected {Enum.GetName(ObjectiveType)}, got {data.Objective.Type}");
        }
    }

    // Names of regions for this objective
    private static class ThisRegions
    {
        // Region reached when a terminal pair is successfully found (logged in to, ideally)
        public static string TerminalFound(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Found {count} Terminal Pair{(count == 1 ? "" : "s")}";

        // Region reached when an uplink is completed
        public static string UplinkComplete(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Uplink #{count} Completed";
    }

    private static class CorruptedUplink_TerminalLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Terminal Location #{count}", "A spawn location for a particular terminal", data.Tag_CorruptedUplinkTerminalLocations_PerObjective));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private class CorruptedUplink_TerminalItem : Item
    {
        public CorruptedUplink_TerminalItem(Objective.Data data, int count)
            : base(MakeTag(data, count), MakeRandData())
        {
            ObjectiveData = data;
            Count = count;
        }

        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Terminal #{count}", "A particular terminal", data.Tag_CorruptedUplinkTerminalItems_PerObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }

        public int Count { get; set; }

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, ObjectiveData.Tag_CorruptedUplinkTerminalItems_PerObjective);

        public override Expedition.Data? RequiredExpedition => ObjectiveData;
    }

    private static class CorruptedUplink_CompletionLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Completion Location #{count}", "A particular completion location", data.Tag_CorruptedUplinkCompletionLocations_PerObjective));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private class CorruptedUplink_CompletionItem : Item
    {
        public CorruptedUplink_CompletionItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Terminal", "A particular uplink completion item", data.Tag_CorruptedUplinkCompletionItems_PerObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, ObjectiveData.Tag_CorruptedUplinkCompletionItems_PerObjective);

        public override Expedition.Data? RequiredExpedition => ObjectiveData;
    }

    public static KeyedItem GetTerminalItem(Objective.Data data, int count)
    {
        if (data.TryLookupItem(CorruptedUplink_TerminalItem.MakeTag(data, count), out var item))
            return item;

        Item newItem = new CorruptedUplink_TerminalItem(data, count);
        return new(data.AddItem(newItem), newItem);
    }

    public static KeyedItem GetCompletionItem(Objective.Data data)
    {
        if (data.TryLookupItem(CorruptedUplink_CompletionItem.MakeTag(data), out var item))
            return item;

        Item newItem = new CorruptedUplink_CompletionItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    // Objective similar to a standard uplink, but requiring codes for the uplink be relayed from a second terminal
    [Objective.Callback]
    public void HandleCorruptedUplinkObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // Both terminals in a pair are always in the same zone (unless spawning hijinks ensue, but we're ignoring those)
        // Both will share a region set because that is the most limiting way to do this, and results in accurate archipelago placements
        List<List<RegionID>> regionSets = data.UnstuffPlacements(data.PlacementsToTerminalRegions(data.ObjectiveData.ZonePlacementDatas), data.Objective.Uplink_NumberOfTerminals).ToList();
        KeyedItem completionItem = GetCompletionItem(data);
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        RegionID last = data.ObjectiveStartRegion;
        for (int i = 1; i <= data.Objective.Uplink_NumberOfTerminals; i++)
        {
            // Add two terminal spawns - Note that it's logically necessary they use the same placement data
            KeyedItem term1 = GetTerminalItem(data, 2 * i - 1);
            data.AddLocation(
                CorruptedUplink_TerminalLocation.MakeTag(data, 2 * i - 1),
                regionSets[i - 1],
                CorruptedUplink_TerminalLocation.MakeRandData(),
                term1.ID
            );

            KeyedItem term2 = GetTerminalItem(data, 2 * i);
            data.AddLocation(
                CorruptedUplink_TerminalLocation.MakeTag(data, 2 * i),
                regionSets[i - 1],
                CorruptedUplink_TerminalLocation.MakeRandData(),
                term2.ID
            );

            // New region representing a pair of terminals has been found
            string foundName = ThisRegions.TerminalFound(data, i);
            RegionID foundRegion = data.LookupOrCreateRegion(foundName);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = foundRegion,
                ReqItem = term1.Item.PathReqs,
                ReqCount = 2u,
            });

            // Place completion item in that region
            data.AddLocation(
                CorruptedUplink_CompletionLocation.MakeTag(data, i),
                foundRegion,
                CorruptedUplink_CompletionLocation.MakeRandData(),
                completionItem.ID
            );

            // Add path to completion region
            string completionName = ThisRegions.UplinkComplete(data, i);
            RegionID completionRegion = data.LookupOrCreateRegion(completionName);
            data.AddPath(new Path()
            {
                StartingRegion = foundRegion,
                EndingRegion = completionRegion,
                ReqItem = completionItem.Item.PathReqs,
                ReqCount = 1u,
            });
            last = completionRegion;
            eventWrapper.Process(completionRegion, completionName);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, last);
    }

}
