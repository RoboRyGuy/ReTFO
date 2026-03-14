using ReTFO.Archipelago.FeaturesAPI;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
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

    /*
     * TODO:
     *  Region: Currently not detected; no activate or solve events to use
     */

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.ClearAPath;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return "Clear a Path";
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

    private static string ThisRegionName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Path Cleared";
    }

    // Objective requiring a player to enter the extraction zone. Assumes (requires?) forward extraction
    [Objective.Callback]
    public void HandleClearAPathObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // This objective is immediately completed upon reaching extraction
        // We could place the ObjectiveComplete item in the extraction zone, but I feel this method is more reliable and clearer
        string objectiveCompleteName = ThisRegionName(data);
        int objectiveCompleteRegion = data.GetOrCreateRegion(objectiveCompleteName);
        Path path = data.AddPath(data.ObjectiveStartRegion, objectiveCompleteRegion);
        path.RequiredItem = ExtractionHandler.GetExtractionReachableItem(data).Name;
        path.RequiredItemCount = 1;

        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), objectiveCompleteRegion);

        // No OnActivate or OnSolve events for this objective
    }

}
