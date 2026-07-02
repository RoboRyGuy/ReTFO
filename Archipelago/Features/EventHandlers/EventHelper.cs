using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections;
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
        /// Parent tag for all event locations
        /// </summary>
        public LocationID Location_Event
            => LocationID.From(gameData, "Event Locations", data => new("Locations found by triggering particular events", data.Location_All));

        /// <summary>
        /// Tag for a particular event type
        /// </summary>
        /// <param name="type">The type of event the tag is for</param>
        public LocationID Location_Event_ByType(eWardenObjectiveEventType type)
            => LocationID.From(gameData, $"Event Locations for {Enum.GetName(type)} Event", data => new("Locations found by triggering a particular event type", data.Location_Event));

        /// <summary>
        /// Parent tag for all event items
        /// </summary>
        public ItemID Item_Event
            => ItemID.From(gameData, "Event Items", data => new("Items corresponding to in-game events", data.Item_All));
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

    public class EventLocation : Location
    {
        public EventLocation(RegionList regions, LocationData randData, WardenObjectiveEventData sourceEvent, ItemID itemId = new())
            : base(regions, randData, itemId)
        {
            SourceEvent = sourceEvent;
        }

        /// <summary>
        /// Original event data for this location
        /// </summary>
        public WardenObjectiveEventData SourceEvent { get; init; }
    }

    /// <summary>
    /// Event type which causes a notification to the StateTracker for a region being found
    /// </summary>
    public const eWardenObjectiveEventType CheckRegionEventType = (eWardenObjectiveEventType)1_512_758_915; // This is just a random value to avoid collisions with other mods

    /// <summary>
    /// Event type which causes a notification to the StateTracker for a location being found
    /// </summary>
    public const eWardenObjectiveEventType CheckLocationEventType = CheckRegionEventType + 1;

    /// <summary>
    /// Category used by options in the event category
    /// </summary>
    public const string EVENT_OPTION_CATEGORY = "Events";

    /// <summary>
    /// Clear values / fields from an event data, making it into a blank event
    /// </summary>
    /// <param name="e">The event to clear data from</param>
    public static void ClearEvent(WardenObjectiveEventData e)
    {
        e.AchievementKey = string.Empty;
        e.ChainPuzzle = 0u;
        e.ClearDimension = false;
        e.Condition = new()
        {
            ConditionIndex = -1,
            IsTrue = false,
        };
        e.Count = 0;
        e.CustomSubObjective = 0u;
        e.CustomSubObjectiveHeader = 0u;
        e.Delay = 0f;
        e.DialogueID = 0u;
        e.DimensionIndex = 0u;
        e.Duration = 0f;
        e.Enabled = false;
        e.EnemyID = 0u;
        e.EnemyWaveData = new()
        {
            AreaDistance = 2,
            IntelMessage = 0u,
            SpawnDelay = 0f,
            TriggerAlarm = false,
            WavePopulation = 0u,
            WaveSettings = 0u,
            WorldEventObjectFilterSpawnPoint = string.Empty,
        };
        e.FogSetting = 0u;
        e.FogTransitionDuration = 0f;
        e.Layer = LevelGeneration.LG_LayerType.MainLayer;
        e.LocalIndex = 0u;
        e.Position = UnityEngine.Vector3.zero;
        e.SoundID = 0u;
        e.SoundSubtitle = 0u;
        e.SustainedEventDelay = 0f;
        e.SustainedEventSlotIndex = 0;
        e.SustainedEventStateCount = 0;
        e.SustainedEventStateDuration = 0f;
        e.TerminalCommand = LevelGeneration.TERM_Command.None;
        e.TerminalCommandRule = LevelGeneration.TERM_CommandRule.Normal;
        e.Trigger = eWardenObjectiveEventTrigger.None;
        e.Type = eWardenObjectiveEventType.None;
        e.UseStaticBioscanPoints = false;
        e.WardenIntel = 0u;
        e.WorldEventObjectFilter = string.Empty;
    }

    /// <summary>
    /// Create a brand new event which will check the provided region
    /// </summary>
    /// <param name="source">An event from the same list which will be copied to ensure type safety</param>
    /// <param name="regionId">ID of the region to check</param>
    /// <returns>A new CheckRegion event</returns>
    public static WardenObjectiveEventData CreateCheckRegionEvent(WardenObjectiveEventData source, RegionID regionId)
    {
        WardenObjectiveEventData e = source.MemberwiseClone().Cast<WardenObjectiveEventData>();
        ClearEvent(e);
        ConvertToCheckRegionEvent(e, regionId);
        return e;
    }

    /// <summary>
    /// Convert an existing event in-place to a check region event, overwriting existing event data
    /// </summary>
    /// <param name="source">The event to convert</param>
    /// <param name="regionId">ID of the region to check</param>
    public static void ConvertToCheckRegionEvent(WardenObjectiveEventData source, RegionID regionId)
    {
        source.Type = CheckRegionEventType;
        source.EnemyID = regionId.ID;
    }

    /// <summary>
    /// Create a brand new event which will check the provided location
    /// </summary>
    /// <param name="source">An event from the same list which will be copied to ensure type safety</param>
    /// <param name="locationId">ID of the location to check</param>
    /// <returns>A new CheckLocation event</returns>
    public static WardenObjectiveEventData CreateCheckLocationEvent(WardenObjectiveEventData source, LocationID locationId)
    {
        WardenObjectiveEventData e = source.MemberwiseClone().Cast<WardenObjectiveEventData>();
        ClearEvent(e);
        ConvertToCheckLocationEvent(e, locationId);
        return e;
    }

    /// <summary>
    /// Convert an existing event in-place to a check location event, overwriting existing event data
    /// </summary>
    /// <param name="source">The event to convert</param>
    /// <param name="locationId">ID of the location to check</param>
    public static void ConvertToCheckLocationEvent(WardenObjectiveEventData source, LocationID locationId)
    {
        source.Type = CheckRegionEventType;
        source.EnemyID = locationId.ID;
    }

    /// <summary>
    /// Creates a new location for this event, and converts the event to a check location event for that location ID.
    /// This is the preferred way to create locations based on events because this will handle invoking the original
    ///  event if the location is not randomized.
    /// </summary>
    /// <param name="data">Event data for the event</param>
    /// <param name="originalEvent">The event to convert</param>
    /// <param name="count">Unique count of this event. Typically 1-indexed with respect to a specific event type</param>
    /// <param name="item">The item associated with the location</param>
    /// <param name="randData">The rand data to use for the location</param>
    /// <returns>The ID of the created location</returns>
    public static LocationID CreateEventLocation(Event.Data data, WardenObjectiveEventData originalEvent, int count, ItemID item, LocationData randData = new())
    {
        WardenObjectiveEventData eventCopy = originalEvent.MemberwiseClone().Cast<WardenObjectiveEventData>();
        Location loc = new EventLocation(data.Region_Event, randData, eventCopy, item);
        LocationID id = data.Locations.Create(
            $"{data.EventName} {Enum.GetName(originalEvent.Type)} #{count}", 
            new("A specific event location", 
            data.Location_Event_ByType(originalEvent.Type)), 
            loc
        );

        originalEvent.Type = CheckLocationEventType;
        originalEvent.EnemyID = id.ID;

        return id;
    }

    /// <summary>
    /// Add options relating to events and event tagging
    /// </summary>
    [Game.Callback]
    public void AddEventOptions(Game.Data data)
    {
        LocationID tag = data.Location_Event;
        data.AddOption(new OptionLocationTagOption(
            displayName: "Event Randomization",
            description:
                "Controls randomization of all supported events. This includes events which open/unlock doors,"
                + " events which start scans, events which trigger warps, and instant win events."
                + OptionTagOption.DESC_SUFFIX,
            category: EVENT_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, tag),
            condition: new(),
            tag: tag,
            defaultValue: 0
        ));

        tag = data.Location_Event_ByType(eWardenObjectiveEventType.UnlockSecurityDoor);
        data.AddOption(new OptionLocationTagOption(
            displayName: "Open/Unlock Door Event Randomization",
            description:
                "Controls randomization of open and unlock door events. For players' safety, these "
                + "are all converted to unlock events."
                + OptionTagOption.DESC_SUFFIX,
            category: EVENT_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, tag),
            condition: new(),
            tag: tag,
            defaultValue: 1
        ));

        tag = data.Location_Event_ByType(eWardenObjectiveEventType.WinOnDeath);
        data.AddOption(new OptionLocationTagOption(
            displayName: "Win Event Randomization",
            description: 
                "Controls randomization of win events, which immediately clear main on the expedition." 
                + " These events are how R8A2 Secondary, R8C2, and R8E2 end. Note that they only ever "
                + " clear main and do not clear optional sectors."
                + OptionTagOption.DESC_SUFFIX,
            category: EVENT_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, tag),
            condition: new(),
            tag: tag,
            defaultValue: 1
        ));
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
        public static bool Prefix(ref WardenObjectiveEventData eData, float currentDuration)
        {
            // Non ref copy of eData for lambdas
            WardenObjectiveEventData inputEvent = eData;

            // Check if the event's condition is satisfied
            bool checkCondition()
                => (inputEvent.Condition != null)
                && inputEvent.Condition.ConditionIndex >= 0
                && (WorldEventManager.GetCondition(inputEvent.Condition.ConditionIndex) != inputEvent.Condition.IsTrue);

            bool test = checkCondition();

            if (eData.Type == CheckRegionEventType)
            {
                if (checkCondition()) return false; // Event is ignored

                // Create the region callback
                RegionID id = new() { ID = eData.EnemyID };
                void regionCallback() => StateTracker.Get().NotifyFoundRegion(id, null);
                if (currentDuration >= inputEvent.Delay) regionCallback();
                else TriggerDelayedWorldEvent(regionCallback, inputEvent.Delay - currentDuration);

                return false;
            }
            else if (eData.Type == CheckLocationEventType)
            {
                // Fetch ID
                LocationID id = new() { ID = eData.EnemyID };

                // Check if the location is randomized. If not, spwap in the original event
                StateTracker stateTracker = StateTracker.Get();
                Location? loc = stateTracker.MidManager.GetProcessedGameData().Locations.LookUpValue(id);
                if (!(loc?.RandData.IsTreatedAsRandom ?? true))
                {
                    if (loc is EventLocation eLoc)
                        eData = eLoc.SourceEvent;
                    return true;
                }

                if (checkCondition()) return false; // Event is ignored

                // Set up our location to be discovered
                void locationCallback() => StateTracker.Get().NotifyFoundLocation(id, null);
                if (currentDuration >= inputEvent.Delay) locationCallback();
                else TriggerDelayedWorldEvent(locationCallback, inputEvent.Delay - currentDuration);

                return false;
            }

            // Wasn't a custom event, so proceed as usual
            return true;
        }
    }
}
