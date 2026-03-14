using Clonesoft.Json;
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

[EnableFeatureByDefault]
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

    /* TODO:
     *  Location: Detect uplink being completed
     *  Item: Add ability to receive item over network
     *  Region: Currently detected using OnSolve events
     */

    private class UplinkCompleteItem : Item
    {
        public UplinkCompleteItem(string name, Objective.Data data)
            : base(name, eRandomizationType.None, new List<string>() { "All", "Objective Items", "Terminal Commands", "Uplink Completions", "Corrupted Uplink Completions" })
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.CorruptedTerminalUplink;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"Perform {data.Objective.Uplink_NumberOfTerminals} Corrupted Uplinks";
    }

    private static bool ThisIsCorrectObjective(Objective.Data data)
        => data.Objective.Type == ThisObjectiveType;

    private static void CheckThisIsCorrectObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            FeatureLogger.Error($"Wrong objective type! Expected {Enum.GetName(ThisObjectiveType)}, got {data.Objective.Type}");
    }

    private static string ThisObjectiveName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return data.ObjectiveName(ThisObjectiveSummary(data));
    }

    private static string ThisItemName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Uplink Complete";
    }

    private static string ThisLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Terminal #{count}";
    }

    private static string ThisRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Uplink #{count} Completed";
    }

    // Objective similar to a standard uplink, but requiring codes for the uplink be relayed from a second terminal
    [Objective.Callback]
    public void HandleCorruptedUplinkObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // Both terminals in a pair are always in the same zone (unless spawning hijinks ensue, but we're ignoring those)
        // Both will share a region set because that is the most limiting way to do this, and results in accurate archipelago placements
        List<List<int>> regionSets = data.UnstuffPlacements(data.PlacementsToTerminalRegions(data.ObjectiveData.ZonePlacementDatas), data.Objective.Uplink_NumberOfTerminals).ToList();
        Item item = data.GetItem(new UplinkCompleteItem(ThisItemName(data), data));
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        int last = data.ObjectiveStartRegion;
        for (int i = 1; i <= data.Objective.Uplink_NumberOfTerminals; i++)
        {
            // Add two terminal spawns
            data.AddLocation(
                ThisLocationName(data, 2 * i - 1),
                regionSets[i - 1],
                eRandomizationType.None,
                true,
                item
            );
            data.AddLocation(
                ThisLocationName(data, 2 * i),
                regionSets[i - 1],
                eRandomizationType.None,
                true,
                item
            );

            // New region representing "progress"
            string newName = ThisRegionName(data, i);
            int newRegion = data.GetOrCreateRegion(newName);
            Path path = data.AddPath(last, newRegion);
            path.RequiredItem = item.Name;
            path.RequiredItemCount = 2u;
            last = newRegion;

            eventWrapper.Process(newRegion, newName);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), last);
    }

}
