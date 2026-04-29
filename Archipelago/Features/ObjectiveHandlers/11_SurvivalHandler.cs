using ReTFO.Archipelago.FeaturesAPI;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault, AutomatedFeature]
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

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.Survival;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return $"Survive {TimeSpan.FromSeconds(data.Objective.Survival_TimeToSurvive).ToString("c")}";
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

        // Helper to get the full name for This objective
        public static string ObjectiveName(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return data.ObjectiveName(ObjectiveSummary(data));
        }
    }

    // Names of regions for this objective
    private static class ThisRegions
    {
        // Region reached by starting the survival timer
        public static string Started(Objective.Data data)
            => $"{This.ObjectiveName(data)} Started";

        // Region reached by surviving the required duration
        public static string Survived(Objective.Data data)
            => $"{This.ObjectiveName(data)} Survived";
    }

    // Objective requiring prisoners survive a certain amount of time and reach extract
    [Objective.Callback]
    public void HandleSurvivalObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        string startedName = ThisRegions.Survived(data);
        RegionID startedRegion = data.LookupOrCreateRegion(startedName);
        Path path = new()
        {
            StartingRegion = data.ObjectiveStartRegion,
            EndingRegion = startedRegion,
        };

        // This is a rare case where we need to block access until we've completed the previous objectives
        if (data.ObjectiveIndex > 0)
        {
            path.ReqItem = SharedObjectiveHandler.GetCompleteObjectiveItem(data).PathReqs;
            path.ReqCount = (uint)data.ObjectiveIndex;
        }
        data.AddPath(path);

        // Events always trigger as long as survival is activated, though typically they activate on a delay
        data.ProcessEvents(startedRegion, startedName, data.Objective.EventsOnActivate ??= new(1));

        // Once it's started, there's no blockers to completion (though there are blockers for extraction, typically)
        string survivedName = ThisRegions.Survived(data);
        RegionID survivedRegion = data.LookupOrCreateRegion(survivedName);
        data.AddPath(new Path()
        {
            StartingRegion = startedRegion,
            EndingRegion = survivedRegion,
        });
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, survivedRegion);
    }
}
