using Clonesoft.Json;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class ReactorShutdownHandler : ArchipelagoFeature
{
    public override string Name => "Reactor Shutdown Handler";
    public override string Description
        => "Handles the Reactor Shutdown Startup objective type.\n"
        + "Example: R1D1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /*  Location: Currently auto-detects reactor on zone entry. Does this need to change?
     *  Item: Cannot receive reactor over network
     *  Region: Reactor completion regions detected using OnSolve events
     */

    private class ReactorShutdownReactorItem : Item
    {
        public ReactorShutdownReactorItem(string name, Objective.Data data)
            : base(name, eRandomizationType.None, new List<string>() { "All", "Objective Items", "Geomorphs", "Reactors" })
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.Reactor_Shutdown;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return "Reactor Shutdown";
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

    private static string ThisReactorItemName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Reactor";
    }

    private static string ThisReactorLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Reactor #{count}";
    }

    private static string ThisCompleteShutdownRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Completed {count} Reactor Startup";
    }

    // Objective requiring a single reactor be shut down
    [Objective.Callback]
    public void HandleReactorShutdownObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // Reactor region pickup
        // The shutdown can be initiated from any reachable reactor in the list (for some reason)
        int count = 0;
        Item reactorItem = data.GetItem(new ReactorShutdownReactorItem(ThisReactorItemName(data), data));
        void addReactor(Zone.Data zone)
        {
            ++count;
            data.AddLocation(
                ThisReactorLocationName(data, count),
                data.GetOrCreateRegion(zone.ZoneName),
                eRandomizationType.None,
                true, // TODO? Seems logical to immediately find it on entering the zone (or, at least, the correct area?)
                reactorItem
            );
        }

        foreach (var placement in data.ObjectiveData.ZonePlacementDatas.SelectMany(ps => ps.Iter()))
        {
            var targetZone = data.FindZoneByPlacement(placement);
            if (targetZone == null)
            {
                FeatureLogger.Error($"Failed to find reactor zone by placement: {ThisObjectiveName(data)}");
                continue;
            }
            addReactor(targetZone);
        }

        // If no reactors were placed, we can search all zones and hope to find a well-named geomorph
        if (count == 0)
        {
            foreach (var zone in data.AllZones)
            {
                if (zone.CustomGeo?.Contains("_reactor_", StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    FeatureLogger.Debug($"Using geomorph for reactor objective: {ThisObjectiveName(data)}");
                    addReactor(zone);
                    break;
                }
            }
            if (count == 0)
                FeatureLogger.Error($"No reactor placements: {ThisObjectiveName(data)}");
        }

        // OnActivateOnSolveItem
        if (!data.Objective.OnActivateOnSolveItem)
        {
            data.Objective.OnActivateOnSolveItem = true;
            data.Objective.EventsOnActivate?.Clear();
        }

        // If we can reach multiple reactors, then we can perform OnActivateOnSolve multiple times
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        count = 0;
        while (!eventWrapper.IsDone)
        {
            ++count;
            string eventName = ThisCompleteShutdownRegionName(data, count);
            int eventRegion = data.GetOrCreateRegion(eventName);
            Path path = data.AddPath(data.ObjectiveStartRegion, eventRegion);
            path.RequiredItem = reactorItem.Name;
            path.RequiredItemCount = (uint)count;
            eventWrapper.Process(eventRegion, eventName);
        }

        // Objective can be completed after the first reactor
        if (!data.Objective.DoNotSolveObjectiveOnReactorComplete)
            SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), data.GetOrCreateRegion(ThisCompleteShutdownRegionName(data, 1)));
    }

}
