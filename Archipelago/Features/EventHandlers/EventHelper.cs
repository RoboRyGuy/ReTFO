using Clonesoft.Json;
using GameData;
using Il2CppInterop.Runtime.Injection;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections;
using ReTFO.Archipelago.Utilities;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Runtime.CompilerServices;

[EnableFeatureByDefault, InjectToIl2Cpp]
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
        public EventLocation(Event.Data data, WardenObjectiveEventData sourceEvent, int count, Item item)
            : base($"{data.EventName} {Enum.GetName(sourceEvent.Type)} #{count}", data.EventRegion, item)
        {
            SourceEvent = sourceEvent;
        }

        /// <summary>
        /// Original event data for this location
        /// </summary>
        [JsonIgnore]
        public WardenObjectiveEventData SourceEvent { get; init; }

        private static RandomizationData s_randData = new()
        {
            IsProgression = true,
            Categories = new() { "NoEvents" }
        };

        public override RandomizationData RandData => s_randData;
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
    public static WardenObjectiveEventData CreateCheckRegionEvent(int regionId)
    {
        WardenObjectiveEventData e = MakeBlankEvent();
        e.Type = CheckRegionEventType;
        e.EnemyID = BitConverter.ToUInt32(BitConverter.GetBytes(regionId));
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
    public static void ConvertToCheckLocationEvent(Event.Data data, WardenObjectiveEventData e, int count, Item item)
    {
        Location loc = data.AddLocation(new EventLocation(data, e.MemberwiseClone().Cast<WardenObjectiveEventData>(), count, item));
        var bytes = BitConverter.GetBytes(loc.ID);
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
            Action? action = null;

            if (eData.Type == CheckRegionEventType)
            {
                action = () =>
                {
                    int id = BitConverter.ToInt32(BitConverter.GetBytes(inputEvent.EnemyID));
                    Plugin.Get().StateTracker.NotifyFoundRegion(Expedition.Data.FromCurrentExpedition().RegionList[id].Name, null);
                };
            }
            else if (eData.Type == CheckLocationEventType)
            {
                byte[] bytes = new byte[8];
                BitConverter.GetBytes(inputEvent.EnemyID).CopyTo(bytes, 0);
                BitConverter.GetBytes(inputEvent.FogSetting).CopyTo(bytes, 4);
                long id = BitConverter.ToInt64(bytes);

                StateTracker stateTracker = StateTracker.Get();
                action = () =>
                {
                    stateTracker.NotifyFoundLocation(id, null);
                };

                // Check if the location is randomized. If not, continue with the original event
                Location loc = stateTracker.MidManager.GetProcessedGameData().LookupLocation(id);
                if (!stateTracker.TestRandomization(loc).IsTreatedAsRandom)
                {
                    if (loc is EventLocation eLoc)
                        eData = eLoc.SourceEvent;
                    else
                        FeatureLogger.Error($"Failed to retrieve original event data from event for location: [{loc.ID}] {loc.Name}");
                    return true;
                }
            }
            
            if (action != null) // This is one of our custom event types
            {
                // Note that .Enabled is not used to indicate an event is enabled
                // It's probably used by a different subevent which I don't care to track down
                if (eData.Condition != null && eData.Condition.ConditionIndex >= 0)
                {
                    if (WorldEventManager.GetCondition(eData.Condition.ConditionIndex) != eData.Condition.IsTrue)
                        return false;
                }

                if (eData.Delay > currentDuration)
                    TriggerDelayedWorldEvent(action, eData.Delay - currentDuration);
                else
                    action.Invoke();
                return false;
            }

            // Wasn't our custom event, so proceed as usual
            return true;
        }
    }

}
