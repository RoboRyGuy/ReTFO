using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using GameData;
using InControl;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class TerminalUplinkHandler_Tags
{
    extension(Game.Data data)
    {
        public TagResolver Tag_TerminalUplinkTerminalLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Standard Uplink Terminal Locations", "Locations checked by finding an uplink terminal", gd.Tag_Never));

        public TagResolver Tag_TerminalUplinkCompletionLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Standard Uplink Locations", "Locations checked by completing a standard uplink", gd.Tag_Never));

        public TagResolver Tag_TerminalUplinkTerminalItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Standard Uplink Terminal Items", "Items indicating an uplink terminal is reachable", gd.Tag_Never));

        public TagResolver Tag_TerminalUplinkCompletionItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Standard Uplink Completion Items", "Items indicating an uplink is completed", gd.Tag_Never));
    }

    extension(Objective.Data data)
    {
        public TagResolver Tag_TerminalUplinkTerminalLocations_ByObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Standard Uplink Terminal Locations", "Locations checked by finding an uplink terminal for a particular objective", gd.Tag_TerminalUplinkTerminalLocations));

        public TagResolver Tag_TerminalUplinkCompletionLocations_ByObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Standard Uplink Locations", "Locations checked by completing a standard uplink for a particular objective", gd.Tag_TerminalUplinkCompletionLocations));

        public TagResolver Tag_TerminalUplinkTerminalItems_ByObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Standard Uplink Terminal Items", "Items indicating an uplink terminal is reachable for a particular objective", gd.Tag_TerminalUplinkTerminalItems));

        public TagResolver Tag_TerminalUplinkCompletionItems_ByObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Standard Uplink Completion Items", "Items indicating an uplink is completed for a particular objective", gd.Tag_TerminalUplinkCompletionItems));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class TerminalUplinkHandler : ArchipelagoFeature
{
    public override string Name => "Standard Uplink Handler";
    public override string Description
        => "Handles the TerminalUplink objective type.\n"
        + "This handles specifically only the standard uplink and not the corrupted or \"dual\" uplink"
        + "Example: R2B3";
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
            = eWardenObjectiveType.TerminalUplink;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return $"Complete {data.Objective.Uplink_NumberOfTerminals} Uplinks";
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
        // Region reached when an uplink terminal is found
        public static string TerminalFound(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Found {count} Terminal{(count == 1 ? "" : "s")}";

        // Region reached when an uplink is completed
        public static string UplinkCompleted(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Completed {count} Uplink{(count == 1 ? "" : "s")}";
    }

    private static class TerminalUplink_TerminalLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Uplink Terminal Location {count}", "A particular standard uplink terminal location", data.Tag_TerminalUplinkTerminalLocations_ByObjective));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private static class TerminalUplink_CompletionLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Uplink Completion Location {count}", "A particular standard uplink completion location", data.Tag_TerminalUplinkCompletionLocations_ByObjective));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private class TerminalUplink_TerminalItem : Item
    {
        public TerminalUplink_TerminalItem(Objective.Data data, int count)
            : base(MakeTag(data, count), MakeRandData())
        {
            ObjectiveData = data;
            Count = count;
        }

        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Uplink Terminal Item {count}", "A particular standard uplink terminal", data.Tag_TerminalUplinkTerminalItems_ByObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }

        public int Count { get; set; }

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, ObjectiveData.Tag_TerminalUplinkTerminalItems_ByObjective);

        public override Expedition.Data? RequiredExpedition => ObjectiveData;
    }

    private class TerminalUplink_CompletionItem : Item
    {
        public TerminalUplink_CompletionItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Uplink Completion Item", "A particular standard uplink completion", data.Tag_TerminalUplinkCompletionItems_ByObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, ObjectiveData.Tag_TerminalUplinkCompletionItems_ByObjective);

        public override Expedition.Data? RequiredExpedition => ObjectiveData;
    }

    public static KeyedItem GetTerminalItem(Objective.Data data, int count)
    {
        if (data.TryLookupItem(TerminalUplink_TerminalItem.MakeTag(data, count), out var item))
            return item;

        Item newItem = new TerminalUplink_TerminalItem(data, count);
        return new(data.AddItem(newItem), newItem);
    }

    public static KeyedItem GetCompletionItem(Objective.Data data)
    {
        if (data.TryLookupItem(TerminalUplink_CompletionItem.MakeTag(data), out var item))
            return item;

        Item newItem = new TerminalUplink_CompletionItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    // Objective requiring one or more standard uplinks to be completed
    [Objective.Callback]
    public void HandleTerminalUplinkObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // Very similar to big pickups, but with terminal regions instead
        List<List<RegionID>> regionSets = data.ObjectiveToTerminalRegionSets(data.Objective.Uplink_NumberOfTerminals).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        KeyedItem completionItem = GetCompletionItem(data);
        RegionID last = data.ObjectiveStartRegion;
        for (int i = 1; i <= data.Objective.Uplink_NumberOfTerminals; i++)
        {
            KeyedItem terminalItem = GetTerminalItem(data, i);
            data.AddLocation(
                TerminalUplink_TerminalLocation.MakeTag(data, i),
                regionSets[i - 1],
                TerminalUplink_TerminalLocation.MakeRandData(),
                terminalItem.ID
            );

            string foundTerminalRegionName = ThisRegions.TerminalFound(data, i);
            RegionID foundTerminalRegion = data.LookupOrCreateRegion(foundTerminalRegionName);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = foundTerminalRegion,
                ReqItem = terminalItem.PathReqs,
                ReqCount = 1u,
            });

            data.AddLocation(
                TerminalUplink_CompletionLocation.MakeTag(data, i),
                foundTerminalRegion,
                TerminalUplink_CompletionLocation.MakeRandData(),
                completionItem.ID
            );

            string completionRegionName = ThisRegions.UplinkCompleted(data, i);
            RegionID completionRegion = data.LookupOrCreateRegion(completionRegionName);
            data.AddPath(new Path()
            {
                StartingRegion = foundTerminalRegion,
                EndingRegion = completionRegion,
                ReqItem = completionItem.PathReqs,
                ReqCount = 1u,
            });
            last = completionRegion;
            eventWrapper.Process(completionRegion, completionRegionName);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, last);
    }

}
