using Clonesoft.Json;
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

[EnableFeatureByDefault]
public class ReactorStartupHandler : ArchipelagoFeature
{
    public override string Name => "Reactor Startup Handler";
    public override string Description
        => "Handles the Reactor Startup objective type.\n"
        + "Example: R1C1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /*
     * TODO:
     *  Reactor codes can be randomized. Codes can be provided in chat once the relevant wave
     *   is reached. Free code locations are 
     * 
     *  Location: Currently auto-detects reactor on zone entry. Does this need to change?
     *            Codes currently auto-discovered on terminal login / wave completed
     *  Item: Cannot receive reactor over network
     *        Reactor code is currently not received over network
     *  Region: Wave completion detected via WaveEvents
     *          Reactor completion regions detected using OnSolve events
     */

    private class ReactorStartupReactorItem : Item
    {
        public ReactorStartupReactorItem(string name, Objective.Data data)
            : base(name, eRandomizationType.None, new List<string>() { "All", "Objective Items", "Geomorphs", "Reactors" })
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }
    }

    private class ReactorStartupCodeItem : Item
    {
        public ReactorStartupCodeItem(string name, int index, Objective.Data data)
            : base(name, eRandomizationType.None, new List<string> { "All", "Objective Items", "Logs", "Reactor Codes" })
        {
            objective_data = data;
            this.index = index;
        }

        [JsonIgnore]
        public Objective.Data objective_data { get; set; }

        [JsonIgnore]
        public int index { get; set; }
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.Reactor_Startup;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return "Reactor Startup";
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

    private static string ThisCodeItemName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Reactor Code #{count}";
    }

    private static string ThisCodeLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Reactor Code #{count}";
    }

    private static string ThisSurviveRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Survived Wave #{count}";
    }

    private static string ThisCompleteStartupRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Completed {count} Reactor Startup";
    }

    [Objective.Callback]
    public void HandleReactorStartupObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // Reactor region pickup
        // The startup can be initiated from any reachable reactor in the list (for some reason)
        int count = 0;
        var reactorItem = data.GetItem(new ReactorStartupReactorItem(ThisReactorItemName(data), data));
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

        if (count == 0)
        {   // If no reactors were placed, we can search all zones and hope to find a well-named geomorph
            foreach (var zone in data.AllZones)
            {
                if (zone.CustomGeo == null) continue;
                if (zone.CustomGeo.Contains("hall", StringComparison.OrdinalIgnoreCase)) continue;
                if (zone.CustomGeo.Contains("reactor", StringComparison.OrdinalIgnoreCase))
                {
                    FeatureLogger.Debug($"Using geomorph for reactor objective: {ThisObjectiveName(data)}");
                    addReactor(zone);
                    break;
                }
            }
            if (count == 0)
                FeatureLogger.Error($"No reactor placements: {ThisObjectiveName(data)}");
        }

        // For each wave, there will be a "survive wave" region
        int last = data.ObjectiveStartRegion;
        string reqItemName = reactorItem.Name;
        count = 0;
        foreach (var wave in data.Objective.ReactorWaves.Iter())
        {
            ++count;

            // Region for surviving a particular reactor wave
            string surviveName = ThisSurviveRegionName(data, count);
            int surviveRegion = data.GetOrCreateRegion(surviveName);
            Path path = data.AddPath(last, surviveRegion);
            path.RequiredItem = reqItemName;
            path.RequiredItemCount = 1u;
            last = surviveRegion;

            // Rewards for surviving!
            data.ProcessEvents(surviveRegion, surviveName, wave.Events ??= new(1));

            // Verification (code and code placement)
            var codeItem = data.GetItem(new ReactorStartupCodeItem(ThisCodeItemName(data, count), count, data));
            reqItemName = codeItem.Name;
            if (wave.VerifyInOtherZone)
            {
                Zone.Data codeZone = data.FindZoneByIndex(wave.ZoneForVerification)
                    ?? throw new NullReferenceException($"Failed to find zone for reactor code placement!");
                List<int> placement = codeZone.TerminalDatas.Select(t => data.GetOrCreateRegion(t.TerminalName)).ToList();
                data.AddLocation(
                    ThisCodeLocationName(data, count),
                    placement,
                    eRandomizationType.None,
                    true, // TODO: Detect reading of relevant logs
                    codeItem
                );
            }
            else
            {
                data.AddLocation(
                    ThisCodeLocationName(data, count),
                    surviveRegion,
                    eRandomizationType.None,
                    true,
                    codeItem
                );
            }
        }

        // If we can reach multiple reactors, then we can (probably) perform OnActivateOnSolve multiple times
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        count = 0;
        while (!eventWrapper.IsDone) // By design, always runs at least once
        {
            ++count;
            string eventName = ThisCompleteStartupRegionName(data, count);
            int eventRegion = data.GetOrCreateRegion(eventName);
            Path path = data.AddPath(last, eventRegion);

            // This fun bit of logic ensures we get the (1) final code, then get a new reactor for all other events
            path.RequiredItem = reqItemName;
            path.RequiredItemCount = 1u;
            reqItemName = reactorItem.Name;

            last = eventRegion;
            eventWrapper.Process(eventRegion, eventName);
        }

        // Objective can be completed after the first reactor
        if (!data.Objective.DoNotSolveObjectiveOnReactorComplete)
            SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveName(data), data.GetOrCreateRegion(ThisCompleteStartupRegionName(data, 1)));
    }

}
