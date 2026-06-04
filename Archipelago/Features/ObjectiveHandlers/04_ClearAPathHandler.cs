using ReTFO.Archipelago.FeaturesAPI;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

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


    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.ClearAPath;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return "Clear a Path";
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
        // Region reached when extraction is reached
        public static string PathCleared(Objective.Data data)
            => $"{data.ObjectiveName()} Path Cleared";
    }

    // Objective requiring a player to enter the extraction zone. Assumes (requires?) forward extraction
    [Objective.Callback]
    public void HandleClearAPathObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // This objective is immediately completed upon reaching extraction
        // We could place the ObjectiveComplete item in the extraction zone, but I feel this method is more reliable and clearer
        string objectiveCompleteName = ThisRegions.PathCleared(data);
        RegionID objectiveCompleteRegion = data.LookupOrCreateRegion(objectiveCompleteName);
        data.AddPath(new Path()
        {
            StartingRegion = data.ObjectiveStartRegion,
            EndingRegion = objectiveCompleteRegion,
            ReqItem = ExtractionHandler.GetExtractionReachableItem(data).Item.PathReqs,
            ReqCount = 1u,
        });
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, objectiveCompleteRegion);

        // No OnActivate or OnSolve events for this objective
    }

}
