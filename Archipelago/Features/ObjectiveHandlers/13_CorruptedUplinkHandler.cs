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
        public LocationID Location_CorruptedUplinkTerminals
            => LocationID.From(data, "Corrupted Uplink Terminal Locations", data => new("Locations checked by finding Corrupted Uplink terminals", data.Location_Never));

        public ItemID Item_CorruptedUplinkTerminals
            => ItemID.From(data, "Corrupted Uplink Terminal Items", data => new("Terminal items used for Corrupted Uplinks", data.Item_Never));

        public LocationID Location_CorruptedUplinkCompletions
            => LocationID.From(data, "Corrupted Uplink Completion Locations", data => new("Locations checked by completing Corrupted Uplinks", data.Location_Never));

        public ItemID Item_CorruptedUplinkCompletions
            => ItemID.From(data, "Corrupted Uplink Completion Items", data => new("Represents a corrupted uplink having been completed", data.Item_Never));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.CorruptedTerminalUplink;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension(Objective.Data data)
    {
        // Region reached when a terminal pair is successfully found (logged in to, ideally)
        public RegionID Region_CorruptedUplinkPairFound(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Found {count} Terminal Pair{(count == 1 ? "" : "s")}", data => new("Region entered by finding a number of corrupted uplink terminal pairs", data.Region_Objective));

        // Region reached when an uplink is completed
        public RegionID Region_CorruptedUplinkCompleted(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Uplink #{count} Completed", data => new("Region entered by completing a numer of corrupted uplink", data.Region_Objective));


        public LocationID Location_CorruptedUplinkTerminals_PerObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Corrupted Uplink Terminal Locations", data => new("Locations checked by finding Corrupted Uplink terminals for a particular objective", data.Location_CorruptedUplinkTerminals));

        public ItemID Item_CorruptedUplinkTerminals_PerObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Corrupted Uplink Terminal Items", data => new("Terminal items used for Corrupted Uplinks for a particular objective", data.Item_CorruptedUplinkTerminals));

        public LocationID Location_CorruptedUplinkCompletions_PerObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Corrupted Uplink Completion Locations", data => new("Locations checked by completing Corrupted Uplinks for a particular objective", data.Location_CorruptedUplinkCompletions));

        public ItemID Item_CorruptedUplinkCompletions_PerObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Corrupted Uplink Completion Items", data => new("Represents a corrupted uplink having been completed for a particular objective", data.Item_CorruptedUplinkCompletions));


        public LocationID Location_CorruptedUplinkTerminal_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Corrupted Uplink Terminal Location #{count}", data => new("Location checked by finding a particular Corrupted Uplink terminal", data.Location_CorruptedUplinkTerminals_PerObjective));

        public ItemID Item_CorruptedUplinkTerminal_Instance(int count)
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Corrupted Uplink Terminal #{count}", 
                data => new("Terminal item used for a particular corrupted Uplinks", data.Item_CorruptedUplinkTerminals_PerObjective),
                new CorruptedUplinkHandler.CorruptedUplink_TerminalItem(data.Region_Objective, count)
            );

        public LocationID Location_CorruptedUplinkCompletion_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Corrupted Uplink Completion Location #{count}", data => new("Location checked by completing a particular Corrupted Uplink", data.Location_CorruptedUplinkCompletions_PerObjective));

        public ItemID Item_CorruptedUplinkCompletion_Instance(int count)
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Corrupted Uplink Completion #{count}", 
                data => new("Represents a particular corrupted uplink having been completed", data.Item_CorruptedUplinkCompletions_PerObjective),
                new CorruptedUplinkHandler.CorruptedUplink_CompletionItem(data.Region_Objective, count)
            );
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

    public class CorruptedUplink_TerminalItem : Item
    {
        public CorruptedUplink_TerminalItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public int Count { get; private init; }
    }

    public class CorruptedUplink_CompletionItem : Item
    {
        public CorruptedUplink_CompletionItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public int Count { get; private init; }
    }

    // Objective similar to a standard uplink, but requiring codes for the uplink be relayed from a second terminal
    [Objective.Callback]
    public void HandleCorruptedUplinkObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.CorruptedTerminalUplink)
            return;

        // Both terminals in a pair are always in the same zone (unless spawning hijinks ensue, but we're ignoring those)
        // Both will share a region set because that is the most limiting way to do this, and results in accurate archipelago placements
        List<List<RegionID>> regionSets = data.UnstuffPlacements(data.PlacementsToTerminalRegions(data.ObjectiveData.ZonePlacementDatas), data.Objective.Uplink_NumberOfTerminals).ToList();
        ItemID terminalCategory = data.Item_CorruptedUplinkTerminals_PerObjective;
        ItemID completionCategory = data.Item_CorruptedUplinkCompletions_PerObjective;
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        RegionID last = data.Region_Objective;
        for (int i = 1; i <= data.Objective.Uplink_NumberOfTerminals; i++)
        {
            // Add two terminal spawns - Note that it's logically necessary they use the same placement data
            ItemID term1 = data.Item_CorruptedUplinkTerminal_Instance(2 * i - 1);
            data.Locations.CreateValue(
                data.Location_CorruptedUplinkTerminal_Instance(2 * i - 1),
                regionSets[i - 1],
                new LocationData() { IsAutoDiscovered = true },
                term1
            );

            ItemID term2 = data.Item_CorruptedUplinkTerminal_Instance(2 * i);
            data.Locations.CreateValue(
                data.Location_CorruptedUplinkTerminal_Instance(2 * i),
                regionSets[i - 1],
                new LocationData() { IsAutoDiscovered = true },
                term2
            );

            // New region representing a pair of terminals has been found
            RegionID foundRegion = data.Region_CorruptedUplinkPairFound(i);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = foundRegion,
                ReqItem = new(Path.PathReq.eType.Category, terminalCategory),
                ReqCount = (uint)(2 * i),
            });

            // Place completion item in that region
            data.Locations.CreateValue(
                data.Location_CorruptedUplinkCompletion_Instance(i),
                foundRegion,
                new LocationData() { IsAutoDiscovered = true },
                data.Item_CorruptedUplinkCompletion_Instance(i)
            );

            // Add path to completion region
            RegionID completionRegion = data.Region_CorruptedUplinkCompleted(i);
            data.AddPath(new Path()
            {
                StartingRegion = foundRegion,
                EndingRegion = completionRegion,
                ReqItem = new(Path.PathReq.eType.Category, completionCategory),
                ReqCount = (uint)i,
            });
            last = completionRegion;
            eventWrapper.Process(completionRegion);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, last);
    }

}
