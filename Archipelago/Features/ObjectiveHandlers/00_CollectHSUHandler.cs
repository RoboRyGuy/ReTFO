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
public class CollectHSUHandler : ArchipelagoFeature
{
    public override string Name => "Collect HSU Handler";
    public override string Description
        => "Handles the Collect DNA Sample objective type.\n"
        + "Example: R1A1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /* TODO:
     *  Location: Detect HSU scan start/complete
     *  Item: Add ability to receive item over network
     *  Region: Currently detected using OnSolve events
     */

    private class CollectHSUItem : Item
    {
        public CollectHSUItem(string name, Objective.Data data)
            : base(name, eRandomizationType.None, new List<string>() { "All", "Objective Items", "Scans", "DNA Samples" })
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.HSU_FindTakeSample;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return "Collect HSU";
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
        return $"{ThisObjectiveName(data)} HSU DNA Sample";
    }

    private static string ThisLocationName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} HSU";
    }

    private static string ThisRegionName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Sample Collected";
    }

    [Objective.Callback]
    public void HandleCollectHSUSample(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data)) return;

        // Add HSU item to expedition
        var hsuItem = data.GetItem(new CollectHSUItem(ThisItemName(data), data));
        data.AddLocation(
            ThisLocationName(data),
            data.PlacementsToZoneRegions(data.ObjectiveData.ZonePlacementDatas[0]).Select(info => info.Region).ToList(),
            eRandomizationType.None,
            true,
            hsuItem
        );

        // Region representing the objective being completed
        string regionName = ThisRegionName(data);
        int region = data.GetOrCreateRegion(regionName);
        Path path = data.AddPath(data.ObjectiveStartRegion, region);
        path.RequiredItem = hsuItem.Name;
        path.RequiredItemCount = 1;

        data.MakeOrWrapOnSolveEvents()
            .Process(region, regionName);

        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), region);
    }

}
