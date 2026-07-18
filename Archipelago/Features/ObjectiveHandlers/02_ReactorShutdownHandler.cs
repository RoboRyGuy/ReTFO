using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class ReactorShutdownHandler_Tags
{
    extension (Game.Data data)
    {
        public LocationID Location_ReactorShutdownReactors
            => LocationID.From(data, "Reactor Shutdown Reactor Locations", data => new("Locations checked by finding a reactor shutdown reactor", data.Location_Never));

        public ItemID Item_ReactorShutdownReactors
            => ItemID.From(data, "Reactor Shutdown Reactor Items", data => new("Items representing a reactor used for a reactor shutdown objective", data.Item_Never));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.Reactor_Shutdown;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension (Objective.Data data)
    {
        public RegionID Region_CompletedShutdown(int count)
            => RegionID.From(data, $"{data.ObjectiveName} Completed {count} Reactor Startup", data => new("Region entered when a certain count of reactor shutdowns are completed", data.Region_Objective));


        public LocationID Location_ReactorShutdownReactors_PerObjective
            => LocationID.From(data, $"{data.ObjectiveName} Reactor Shutdown Reactor Locations", data => new("Locations checked by finding a reactor shutdown reactor for a particular objective", data.Location_ReactorShutdownReactors));

        public ItemID Item_ReactorShutdownReactors_PerObjective
            => ItemID.From(data, $"{data.ObjectiveName} Reactor Shutdown Reactor Items", data => new("Items representing a reactor used for a particular reactor shutdown objective", data.Item_ReactorShutdownReactors));


        public LocationID Location_ReactorShutdownReactor_Instance(int count)
            => LocationID.From(data, $"{data.ObjectiveName} Reactor Shutdown Reactor Locations #{count}", data => new("A particular reactor shutdown reactor location", data.Location_ReactorShutdownReactors_PerObjective));

        public ItemID Item_ReactorShutdownReactor_Instance(int count)
            => ItemID.From(
                data, 
                $"{data.ObjectiveName} Reactor Shutdown Reactor Items #{count}", 
                data => new("A particular reactor shutdown reactor", data.Item_ReactorShutdownReactors_PerObjective),
                new ReactorShutdownHandler.ReactorShutdownReactorItem(data.Region_Objective, count)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.Reactor_Shutdown;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return "Reactor Shutdown";
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

    public class ReactorShutdownReactorItem : Item
    {
        public ReactorShutdownReactorItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }
        
        public int Count { get; private init; }
    }

    // Objective requiring a single reactor be shut down
    [Objective.Callback]
    public void HandleReactorShutdownObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // Reactor region pickup
        // The shutdown can be initiated from any reachable reactor in the list (for some reason)
        int count = 0;
        void addReactor(Zone.Data zone)
        {
            ++count;
            data.Locations.CreateValue(
                data.Location_ReactorShutdownReactor_Instance(count),
                zone.Region_Zone,
                new LocationData() { IsAutoDiscovered = true },
                data.Item_ReactorShutdownReactor_Instance(count)
            );
        }

        foreach (var placement in data.ObjectiveData.ZonePlacementDatas.SelectMany(ps => ps.Iter()))
        {
            var targetZone = data.FindZoneByPlacement(placement);
            if (targetZone == null)
            {
                FeatureLogger.Error($"Failed to find reactor zone by placement: {data.ObjectiveName}");
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
                    FeatureLogger.Debug($"Using geomorph for reactor objective: {data.ObjectiveName}");
                    addReactor(zone);
                    break;
                }
            }
            if (count == 0)
                FeatureLogger.Error($"No reactor placements: {data.ObjectiveName}");
        }

        // OnActivateOnSolveItem
        if (!data.Objective.OnActivateOnSolveItem)
        {
            data.Objective.OnActivateOnSolveItem = true;
            data.Objective.EventsOnActivate?.Clear();
        }

        // If we can reach multiple reactors, then we can perform OnActivateOnSolve multiple times
        ItemID category = data.Item_ReactorShutdownReactors_PerObjective;
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        count = 0;
        while (!eventWrapper.IsDone)
        {
            ++count;
            RegionID eventRegion = data.Region_CompletedShutdown(count);
            data.AddPath(new Path()
            {
                StartingRegion = data.Region_Objective,
                EndingRegion = eventRegion,
                Reqs = new(Path.eType.Category, category, (uint)count),
            });
            eventWrapper.Process(eventRegion);
        }

        // Objective can be completed after the first reactor
        if (!data.Objective.DoNotSolveObjectiveOnReactorComplete)
            SharedObjectiveHandler.AddObjectiveCompleteItem(data, data.Region_CompletedShutdown(1));
    }

}
