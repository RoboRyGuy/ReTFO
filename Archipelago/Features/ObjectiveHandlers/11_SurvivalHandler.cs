using ReTFO.Archipelago.FeaturesAPI;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class SurvivalHandler : ArchipelagoFeature
{
    public override string Name => "Survival Handler";
    public override string Description
        => "Handles the Survival objective type.\n"
        + "The survival objective is any objective that puts a timer at the top of the screen.\n"
        + "Examples: R5B4, R8E1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /* TODO:
     *  Location: Not handled here
     *  Item: Not handled here
     *  Region: Not currently detected
     */

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.Survival;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"Survive {TimeSpan.FromSeconds(data.Objective.Survival_TimeToSurvive).ToString("c")}";
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
        return $"{ThisObjectiveName(data)} Survived";
    }

    // Objective requiring prisoners survive a certain amount of time and reach extract
    [Objective.Callback]
    public void HandleSurvivalObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // This is a rare case where we need to block access until we've completed the previous objectives
        string survivedName = ThisRegionName(data);
        int survivedRegion = data.GetOrCreateRegion(survivedName);
        Path path = data.AddPath(data.ObjectiveStartRegion, survivedRegion);
        if (data.ObjectiveIndex > 0)
        {
            path.RequiredItem = SharedObjectiveHandler.GetCompleteObjectiveItem(data).Name;
            path.RequiredItemCount = (uint)data.ObjectiveIndex;
        }

        // Events always trigger as long as survival is activated, though typically they activate on a delay
        data.ProcessEvents(survivedRegion, survivedName, data.Objective.EventsOnActivate ??= new(1));

        // Technically, the objective will auto-complete as long as you start it..
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), survivedRegion);
    }

}
