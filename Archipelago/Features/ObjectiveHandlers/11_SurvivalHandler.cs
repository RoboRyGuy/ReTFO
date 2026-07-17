using GameData;
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

public static class SurvivalHandler_Tags
{
    extension (Game.Data data)
    {

    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.Survival;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension (Objective.Data data)
    {
        public RegionID Region_SurvivalStarted
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Started", data => new("Region entered by starting the survival portion of a survival objective", data.Region_Objective));

        public RegionID Region_SurvivalSurvived(float duration)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Survived {TimeSpan.FromSeconds(duration):c}", data => new("Region entered by surviving a specific duration of a survival objective", data.Region_Objective));
    }

}

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

    // Objective requiring prisoners survive a certain amount of time and reach extract
    [Objective.Callback]
    public void HandleSurvivalObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.Survival)
            return;

        RegionID startedRegion = data.Region_SurvivalStarted;
        Path path = new()
        {
            StartingRegion = data.Region_Objective,
            EndingRegion = startedRegion,
        };

        // This is a rare case where we need to block access until we've completed the previous objectives
        if (data.ObjectiveIndex > 0)
        {
            path = new(path)
            {
                ReqItem = new(Path.PathReq.eType.Category, data.Item_CompleteObjective_Instance),
                ReqCount = (uint)data.ObjectiveIndex,
            };
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
            RegionID survivedRegion = data.Region_SurvivalSurvived(pair.Key);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = survivedRegion,
            });
            last = survivedRegion;
            data.ProcessEvents(survivedRegion, pair.Value);

            // Some events will be added with no delay; we can fix that :)
            foreach (var e in pair.Value) 
                e.Delay = pair.Key;
        }

        // Stitch the event list back together
        data.Objective.EventsOnActivate = new(events.Sum(pair => pair.Value.Count));
        foreach (var e in events.SelectMany(pair => pair.Value.Iter())) 
            data.Objective.EventsOnActivate.Add(e);

        // Finally, we just need to add the final region and place the objective completion in it
        RegionID finalSurvivedRegion = data.Region_SurvivalSurvived(data.Objective.Survival_TimeToSurvive);

        // If the key was processed earlier, a path will be defined; otherwise, we need to add one
        if (!events.ContainsKey(data.Objective.Survival_TimeToSurvive))
        {   // Find the last region which occurs before our required survival time
            last = startedRegion;
            foreach (var pair in events.Reverse())
            {
                if (pair.Key < data.Objective.Survival_TimeToSurvive)
                {
                    last = data.Region_SurvivalSurvived(pair.Key);
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
