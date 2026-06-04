using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections;
using ReTFO.Archipelago.Utilities;
using System.Runtime.CompilerServices;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class EventHelper_Tags
{
    extension (Game.Data gameData)
    {
        /// <summary>
        /// Parent tag for all event items
        /// </summary>
        public TagResolver Tag_EventItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Event Items", "Items corresponding to in-game events", gd.Tag_AllItems));

        /// <summary>
        /// Parent tag for all event locations
        /// </summary>
        public TagResolver Tag_EventLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Event Locations", "Locations found by triggering particular events", gd.Tag_AllLocations));

        /// <summary>
        /// Tag for a particular event type
        /// </summary>
        /// <param name="type">The type of event the tag is for</param>
        public TagResolver Tag_EventLocation_ByType(eWardenObjectiveEventType type)
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag($"Event Locations for {Enum.GetName(type)} Event", "Locations found by triggering a particular event type", gd.Tag_EventLocations));
    }
}

[EnableFeatureByDefault, AutomatedFeature, InjectToIl2Cpp]
public class EventHelper : ArchipelagoFeature
{
    public override string Name => "Events Helper";
    public override string Description => "Provides utilites used by other features to manage events";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private class EventLocation : Location
    {
        public EventLocation(Event.Data data, WardenObjectiveEventData sourceEvent, int count)
            : base(MakeTag(data, sourceEvent, count), data.EventRegion, MakeRandData())
        {
            SourceEvent = sourceEvent;
        }

        public static TagResolver MakeTag(Event.Data data, WardenObjectiveEventData sourceEvent, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.EventName} {Enum.GetName(sourceEvent.Type)} #{count}", "A particular event instance", gd.Tag_EventLocation_ByType(sourceEvent.Type)));

        public static LocationData MakeRandData() => new LocationData();

        /// <summary>
        /// Original event data for this location
        /// </summary>
        public WardenObjectiveEventData SourceEvent { get; init; }
    }

    // Event type which causes a notification to the StateTracker for a region being found
    private const eWardenObjectiveEventType CheckRegionEventType = (eWardenObjectiveEventType)1_512_758_915;

    // Event type which causes a notification to the StateTracker for a location being found
    private const eWardenObjectiveEventType CheckLocationEventType = CheckRegionEventType + 1;

    // Makes a new WardenObjectiveEventData with reasonable default values in most places
    private static WardenObjectiveEventData MakeBlankEvent()
    {
        return new WardenObjectiveEventData()
        {
            Type = eWardenObjectiveEventType.None,
            Condition = new()
            {
                ConditionIndex = -1,
                IsTrue = false,
            },
            ChainPuzzle = 0u,
            DialogueID = 0u,
            SoundID = 0u,
            WardenIntel = 0u,
            CustomSubObjective = 0u,
            CustomSubObjectiveHeader = 0u,
        };
    }

    /// <summary>
    /// Create a brand new event which will check the provided region
    /// </summary>
    /// <param name="regionId">ID of the region to check</param>
    /// <returns>A new CheckRegion event</returns>
    public static WardenObjectiveEventData CreateCheckRegionEvent(RegionID regionId)
    {
        WardenObjectiveEventData e = MakeBlankEvent();
        e.Type = CheckRegionEventType;

        var bytes = BitConverter.GetBytes(regionId.AsId);
        e.Type = CheckRegionEventType;
        e.EnemyID = BitConverter.ToUInt32(bytes, 0);
        e.FogSetting = BitConverter.ToUInt32(bytes, 4);
        return e;
    }

    /// <summary>
    /// Converts an existing event to a check location event, overwriting existing event data
    /// </summary>
    /// <param name="data">Event data for the event</param>
    /// <param name="e">The event to convert</param>
    /// <param name="count">
    /// Unique count of this event. Typically 1-indexed with respect to a specific event type.
    /// This ensures that if an event type occurs more than once it's still treated as a distinct event.
    /// </param>
    /// <param name="item">The item associated with the location</param>
    public static void ConvertToCheckLocationEvent(Event.Data data, WardenObjectiveEventData e, int count, ItemID item)
    {
        Location loc = new EventLocation(data, e.MemberwiseClone().Cast<WardenObjectiveEventData>(), count);
        loc.ItemID = item;
        LocationID id = data.AddLocation(loc);
        if (id.IsNull)
        {   // Location already added?
            FeatureLogger.Warning("Failed to add event location!");
            return; 
        }

        var bytes = BitConverter.GetBytes(id.AsId);
        e.Type = CheckLocationEventType;
        e.EnemyID = BitConverter.ToUInt32(bytes, 0);
        e.FogSetting = BitConverter.ToUInt32(bytes, 4);
    }

    /// <summary>
    /// Triggers the provided action after the provided delay, in seconds
    /// </summary>
    /// <param name="action">The action to perform</param>
    /// <param name="delay">The delay before performing it</param>
    /// <remarks>
    /// Actions delayed using this method are synced with the WorldEventManager
    /// <list type="bullet">
    ///  <item>These actions will be cancelled if players leave the expedition</item>
    ///  <item>These actions are synced with checkpoints, including reseting to the correct time if a checkpoint is loaded</item>
    /// </list>
    /// </remarks>
    public static void TriggerDelayedWorldEvent(Action action, float delay)
    {
        // Helper for invoking delayed actions
        static IEnumerator ActionAfterDelayCoroutine(Action action, float delay)
        {
            yield return new UnityEngine.WaitForSeconds(delay);
            action.Invoke();
        }

        var coroutine = WorldEventManager.Current.StartCoroutine(new Il2CppEnumerator(ActionAfterDelayCoroutine(action, delay)));
        WorldEventManager.m_worldEventEventCoroutines.Add(coroutine);
    }

    /// <summary>
    /// When events are invoked, catch our custom events and handle them
    /// </summary>
    [ArchivePatch(typeof(WorldEventManager), nameof(WorldEventManager.ExecuteEvent_Internal))]
    public static class WorldEventManager__ExecuteEvent_Internal__Patch
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool Prefix(ref WardenObjectiveEventData eData, float currentDuration)
        {
            WardenObjectiveEventData inputEvent = eData;
            bool checkCondition()
                => (inputEvent.Condition != null && inputEvent.Condition.ConditionIndex >= 0)
                && (WorldEventManager.GetCondition(inputEvent.Condition.ConditionIndex) != inputEvent.Condition.IsTrue);

            if (eData.Type == CheckRegionEventType)
            {
                if (!checkCondition()) return false;

                // Fetch ID
                byte[] bytes = new byte[8];
                BitConverter.GetBytes(inputEvent.EnemyID).CopyTo(bytes, 0);
                BitConverter.GetBytes(inputEvent.FogSetting).CopyTo(bytes, 4);
                RegionID id = new() { AsId = BitConverter.ToInt64(bytes) };

                // Perform the region callback
                void regionCallback() => StateTracker.Get().NotifyFoundRegion(Expedition.Data.FromCurrentExpedition().LookupRegion(id).Name, null);
                if (currentDuration >= inputEvent.Delay) regionCallback();
                else TriggerDelayedWorldEvent(regionCallback, inputEvent.Delay - currentDuration);

                return false;
            }
            else if (eData.Type == CheckLocationEventType)
            {
                if (checkCondition()) return false;

                // Fetch ID
                byte[] bytes = new byte[8];
                BitConverter.GetBytes(inputEvent.EnemyID).CopyTo(bytes, 0);
                BitConverter.GetBytes(inputEvent.FogSetting).CopyTo(bytes, 4);
                LocationID id = new() { AsId = BitConverter.ToInt64(bytes) };

                // Set up our location to be discovered
                void locationCallback() => StateTracker.Get().NotifyFoundLocation(id, null);
                if (currentDuration >= inputEvent.Delay) locationCallback();
                else TriggerDelayedWorldEvent(locationCallback, inputEvent.Delay - currentDuration);

                // Check if the location is randomized. If not, spawp in the original event
                StateTracker stateTracker = StateTracker.Get();
                Location loc = stateTracker.MidManager.GetProcessedGameData().LookupLocation(id);
                if (!loc.RandData.IsTreatedAsRandom)
                {
                    if (loc is EventLocation eLoc)
                        eData = eLoc.SourceEvent;
                    else
                        FeatureLogger.Error($"Failed to retrieve original event data from event for location: [{id.AsId}] {stateTracker.MidManager.GetProcessedGameData().LookupTagDef(loc.NameTag).Name}");
                    return true;
                }

                return false;
            }
            
            // Wasn't our custom event, so proceed as usual
            return true;
        }
    }

}
