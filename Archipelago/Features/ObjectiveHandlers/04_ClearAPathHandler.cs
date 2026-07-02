using ReTFO.Archipelago.FeaturesAPI;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class ClearAPathHandler_Tags
{
    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.ClearAPath;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension (Objective.Data data)
    {
        public RegionID Region_PathCleared
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Path Cleared", data => new("Region entered when extraction is reached for the ClearAPath objective type", data.Region_Objective));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class ClearAPathHandler : ArchipelagoFeature
{
    public override string Name => "Clear a Path Handler";
    public override string Description
        => "Handles the ClearAPath objective type.\n"
        + "Example: R2B1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Names of regions for this objective
    private static class ThisRegions
    {
        // Region reached when extraction is reached
        public static string PathCleared(Objective.Data data)
            => $"{data.ObjectiveName} Path Cleared";
    }

    // Objective requiring a player to enter the extraction zone. Assumes (requires?) forward extraction
    [Objective.Callback]
    public void HandleClearAPathObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.ClearAPath)
            return;

        // This objective is immediately completed upon reaching extraction
        // We could place the ObjectiveComplete item in the extraction zone, but I feel this method is more reliable and clearer
        RegionID objectiveCompleteRegion = data.Region_PathCleared;
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Objective,
            EndingRegion = objectiveCompleteRegion,
            ReqItem = new(Path.RequiredItem.eType.Item, data.Item_Extraction_Instance),
            ReqCount = 1u,
        });
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, objectiveCompleteRegion);

        // No OnActivate or OnSolve events for this objective
    }

}
