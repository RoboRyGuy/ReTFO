using ReTFO.Archipelago.FeaturesAPI;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using GameData;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using ReTFO.Archipelago.Utilities;
using System.Collections.Generic;
using System.Linq;

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
            return $"Survive {TimeSpan.FromSeconds(data.Objective.Survival_TimeToSurvive):c}";
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
        // Region reached by starting the survival timer
        public static string Started(Objective.Data data)
            => $"{data.ObjectiveName()} Started";

        // Region reached by surviving the required duration
        public static string Survived(Objective.Data data, float duration)
            => $"{data.ObjectiveName()} Survived {TimeSpan.FromSeconds(duration):c}";
    }

    // Objective requiring prisoners survive a certain amount of time and reach extract
    [Objective.Callback]
    public void HandleSurvivalObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        string startedName = ThisRegions.Started(data);
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

        // We'll split the events for this objective by the delay
        SortedList<float, Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData>> events = new();
        foreach (var e in data.Objective.EventsOnActivate ??= new())
        {
            if (e.Type == eWardenObjectiveEventType.EventBreak) break; // Only process to first event break
            Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData>? sublist;
            if (!events.TryGetValue(e.Delay, out sublist))
            {
                sublist = new(2);
                events.Add(e.Delay, sublist);
            }
            sublist.Add(e);
        }

        // We'll create regions for each sublist of events and perform processing for those regions
        RegionID last = startedRegion;
        foreach (var pair in events)
        {
            string survivedName = ThisRegions.Survived(data, pair.Key);
            RegionID survivedRegion = data.LookupOrCreateRegion(survivedName);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = survivedRegion,
            });
            last = survivedRegion;
            data.ProcessEvents(survivedRegion, survivedName, pair.Value);

            // Some events will be added with no delay; we can fix that :)
            foreach (var e in pair.Value) 
                e.Delay = pair.Key;
        }

        // Stitch the event list back together
        data.Objective.EventsOnActivate = new(events.Sum(pair => pair.Value.Count));
        foreach (var e in events.SelectMany(pair => pair.Value.Iter())) 
            data.Objective.EventsOnActivate.Add(e);

        // Finally, we just need to add the final region and place the objective completion in it
        RegionID finalSurvivedRegion = data.LookupOrCreateRegion(ThisRegions.Survived(data, data.Objective.Survival_TimeToSurvive));

        // If the key was processed earlier, a path will be defined; otherwise, we need to add one
        if (!events.ContainsKey(data.Objective.Survival_TimeToSurvive))
        {   // Find the last region occurs before our required survival time
            last = startedRegion;
            foreach (var pair in events.Reverse())
            {
                if (pair.Key < data.Objective.Survival_TimeToSurvive)
                {
                    last = data.LookupOrCreateRegion(ThisRegions.Survived(data, pair.Key));
                    break;
                }
            }
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = finalSurvivedRegion
            });
        }

        SharedObjectiveHandler.AddObjectiveCompleteItem(data, finalSurvivedRegion);
    }
}
