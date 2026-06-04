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
        public TagResolver Tag_ReactorShutdownReactorLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Reactor Shutdown Reactor Locations", "Locations checked by finding a reactor shutdown reactor", gd.Tag_Never));

        public TagResolver Tag_ReactorShutdownReactorItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Reactor Shutdown Reactors", "Items representing a reactor used for a reactor shutdown objective", gd.Tag_Never));
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

    private static class ThisRegions
    {
        // Region reached when a shutdown is successfully completed
        public static string CompletedShutdown(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Completed {count} Reactor Startup";
    }

    private static class ReactorShutdownReactorLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Reactor #{count} Location", "A particular reactor location", gd.Tag_ReactorShutdownReactorLocations));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private class ReactorShutdownReactorItem : Item
    {
        public ReactorShutdownReactorItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Reactor", "A particular reactor", gd.Tag_ReactorShutdownReactorItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }

        public override Expedition.Data? RequiredExpedition => ObjectiveData;
    }

    public static KeyedItem GetReactorItem(Objective.Data data)
    {
        if (data.TryLookupItem(ReactorShutdownReactorItem.MakeTag(data), out var item))
            return item;

        Item newItem = new ReactorShutdownReactorItem(data);
        return new(data.AddItem(newItem), newItem);
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
        KeyedItem reactorItem = GetReactorItem(data);
        void addReactor(Zone.Data zone)
        {
            ++count;
            data.AddLocation(
                ReactorShutdownReactorLocation.MakeTag(data, count),
                data.LookupOrCreateRegion(zone.ZoneName),
                ReactorShutdownReactorLocation.MakeRandData(),
                reactorItem.ID
            );
        }

        foreach (var placement in data.ObjectiveData.ZonePlacementDatas.SelectMany(ps => ps.Iter()))
        {
            var targetZone = data.FindZoneByPlacement(placement);
            if (targetZone == null)
            {
                FeatureLogger.Error($"Failed to find reactor zone by placement: {data.ObjectiveName()}");
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
                    FeatureLogger.Debug($"Using geomorph for reactor objective: {data.ObjectiveName()}");
                    addReactor(zone);
                    break;
                }
            }
            if (count == 0)
                FeatureLogger.Error($"No reactor placements: {data.ObjectiveName()}");
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
            string eventName = ThisRegions.CompletedShutdown(data, count);
            RegionID eventRegion = data.LookupOrCreateRegion(eventName);
            data.AddPath(new Path()
            {
                StartingRegion = data.ObjectiveStartRegion,
                EndingRegion = eventRegion,
                ReqItem = reactorItem.Item.PathReqs,
                ReqCount = (uint)count
            });
            eventWrapper.Process(eventRegion, eventName);
        }

        // Objective can be completed after the first reactor
        if (!data.Objective.DoNotSolveObjectiveOnReactorComplete)
            SharedObjectiveHandler.AddObjectiveCompleteItem(data, data.LookupOrCreateRegion(ThisRegions.CompletedShutdown(data, 1)));
    }

}
