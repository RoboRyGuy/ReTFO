using Clonesoft.Json;
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

[EnableFeatureByDefault]
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

    /*
     * TODO:
     *  Location: Detect an uplink being completed
     *  Item: Add ability to receive item over network
     *  Region: Currently detected using OnSolve events
     */

    private class UplinkCompleteItem : Item
    {
        public UplinkCompleteItem(string name, Objective.Data data)
            : base(name, eRandomizationType.None, new List<string>() { "All", "Objective Items", "Terminal Commands", "Uplink Completions", "Standard Uplink Completions" })
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.TerminalUplink;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"Complete {data.Objective.Uplink_NumberOfTerminals} Uplinks";
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
        return $"{ThisObjectiveName(data)} Uplink Completion";
    }

    private static string ThisLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Uplink Terminal #{count}";
    }

    private static string ThisRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} {count} Uplink{(count == 1 ? "" : "s")} Completed";
    }

    // Objective requiring one or more standard uplinks to be completed
    [Objective.Callback]
    public void HandleTerminalUplinkObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // Very similar to big pickups, but with terminal regions instead
        List<List<int>> regionSets = data.ObjectiveToTerminalRegionSets(data.Objective.Uplink_NumberOfTerminals).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        Item item = data.GetItem(new UplinkCompleteItem(ThisItemName(data), data));
        int last = data.ObjectiveStartRegion;
        for (int i = 1; i <= data.Objective.Uplink_NumberOfTerminals; i++)
        {
            data.AddLocation(
                ThisLocationName(data, i),
                regionSets[i - 1],
                eRandomizationType.None,
                true,
                item
            );

            string regionName = ThisRegionName(data, i);
            int newRegion = data.GetOrCreateRegion(regionName);
            Path path = data.AddPath(last, newRegion);
            path.RequiredItem = item.Name;
            path.RequiredItemCount = 1u;
            last = newRegion;

            eventWrapper.Process(newRegion, regionName);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), last);
    }

}
