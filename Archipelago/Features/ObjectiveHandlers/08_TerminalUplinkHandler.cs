using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class TerminalUplinkHandler_Tags
{
    extension(Game.Data data)
    {
        public LocationID Location_TerminalUplinkTerminals
            => LocationID.From(data, "Standard Uplink Terminal Locations", data => new("Locations checked by finding an uplink terminal", data.Location_Never));

        public LocationID Location_TerminalUplinkCompletions
            => LocationID.From(data, "Standard Uplink Locations", data => new("Locations checked by completing a standard uplink", data.Location_Never));

        public ItemID Item_TerminalUplinkTerminals
            => ItemID.From(data, "Standard Uplink Terminal Items", data => new("Items indicating an uplink terminal is reachable", data.Item_Never));

        public ItemID Item_TerminalUplinkCompletions
            => ItemID.From(data, "Standard Uplink Completion Items", data => new("Items indicating an uplink is completed", data.Item_Never));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.TerminalUplink;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension(Objective.Data data)
    {
        public RegionID Region_StandardUplinkTerminalFound(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Found {count} Terminal{(count == 1 ? "" : "s")}", data => new("Region entered by finding a number of uplink terminals", data.Region_Objective));

        public RegionID Region_StandardUplinkCompleted(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Completed {count} Uplink{(count == 1 ? "" : "s")}", data => new("Region entered by completing a number of terminal uplinks", data.Region_Objective));


        public LocationID Location_TerminalUplinkTerminals_ByObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Standard Uplink Terminal Locations", data => new("Locations checked by finding an uplink terminal for a particular objective", data.Location_TerminalUplinkTerminals));

        public LocationID Location_TerminalUplinkCompletions_ByObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Standard Uplink Locations", data => new("Locations checked by completing a standard uplink for a particular objective", data.Location_TerminalUplinkCompletions));

        public ItemID Item_TerminalUplinkTerminals_ByObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Standard Uplink Terminal Items", data => new("Items indicating an uplink terminal is reachable for a particular objective", data.Item_TerminalUplinkTerminals));

        public ItemID Item_TerminalUplinkCompletions_ByObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Standard Uplink Completion Items", data => new("Items indicating an uplink is completed for a particular objective", data.Item_TerminalUplinkCompletions));


        public LocationID Location_TerminalUplinkTerminal_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Standard Uplink Terminal Location #{count}", data => new("Locations checked by finding an uplink terminal for a particular objective", data.Location_TerminalUplinkTerminals_ByObjective));

        public LocationID Location_TerminalUplinkCompletion_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Standard Uplink Location #{count}", data => new("Locations checked by completing a standard uplink for a particular objective", data.Location_TerminalUplinkCompletions_ByObjective));

        public ItemID Item_TerminalUplinkTerminal_Instance(int count)
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Standard Uplink Terminal #{count}", 
                data => new("Items indicating an uplink terminal is reachable for a particular objective", data.Item_TerminalUplinkTerminals_ByObjective),
                new TerminalUplinkHandler.TerminalUplink_TerminalItem(data.Region_Objective, count)
            );

        public ItemID Item_TerminalUplinkCompletion_Instance(int count)
            => ItemID.From(
                Checked(data),
                $"{data.ObjectiveName} Standard Uplink Completion #{count}", 
                data => new("Items indicating an uplink is completed for a particular objective", data.Item_TerminalUplinkCompletions_ByObjective),
                new TerminalUplinkHandler.TerminalUplink_CompletionItem(data.Region_Objective, count)
            );
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

    public class TerminalUplink_TerminalItem : Item
    {
        public TerminalUplink_TerminalItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public int Count { get; private init; }
    }

    public class TerminalUplink_CompletionItem : Item
    {
        public TerminalUplink_CompletionItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }
        
        public int Count { get; private init; }
    }

    // Objective requiring one or more standard uplinks to be completed
    [Objective.Callback]
    public void HandleTerminalUplinkObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.TerminalUplink)
            return;

        // Very similar to big pickups, but with terminal regions instead
        List<List<RegionID>> regionSets = data.ObjectiveToTerminalRegionSets(data.Objective.Uplink_NumberOfTerminals).ToList();
        ItemID terminalCategory = data.Item_TerminalUplinkTerminals_ByObjective;
        ItemID completionCategory = data.Item_TerminalUplinkCompletions_ByObjective;
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        RegionID last = data.Region_Objective;
        for (int i = 1; i <= data.Objective.Uplink_NumberOfTerminals; i++)
        {
            data.Locations.CreateValue(
                data.Location_TerminalUplinkTerminal_Instance(i),
                regionSets[i - 1],
                new LocationData() { IsAutoDiscovered = true },
                data.Item_TerminalUplinkTerminal_Instance(i)
            );

            RegionID foundTerminalRegion = data.Region_StandardUplinkTerminalFound(i);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = foundTerminalRegion,
                ReqItem = new(Path.RequiredItem.eType.Category, terminalCategory),
                ReqCount = 1u,
            });

            data.Locations.CreateValue(
                data.Location_TerminalUplinkCompletion_Instance(i),
                foundTerminalRegion,
                new LocationData() { IsAutoDiscovered = true },
                data.Item_TerminalUplinkCompletion_Instance(i)
            );

            RegionID completionRegion = data.Region_StandardUplinkCompleted(i);
            data.AddPath(new Path()
            {
                StartingRegion = foundTerminalRegion,
                EndingRegion = completionRegion,
                ReqItem = new(Path.RequiredItem.eType.Category, completionCategory),
                ReqCount = 1u,
            });
            last = completionRegion;
            eventWrapper.Process(completionRegion);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, last);
    }

}
