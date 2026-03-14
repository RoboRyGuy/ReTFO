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

[EnableFeatureByDefault, InjectToIl2Cpp]
public class EventHelper : ArchipelagoFeature
{
    public override string Name => "Big Pickups Helper";
    public override string Description => "Provides utilites used by other features to manage big pickups";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    [InjectToIl2Cpp]
    private class WrappedEvent : WorldEventConditionPair
    {
        public WrappedEvent(IntPtr ptr) : base(ptr) { }
        public WrappedEvent(WorldEventConditionPair sourcePair, WardenObjectiveEventData sourceEvent) 
            : base(ClassInjector.DerivedConstructorPointer<WrappedEvent>())
        {
            ClassInjector.DerivedConstructorBody(this);
            ConditionIndex = sourcePair.ConditionIndex;
            IsTrue = sourcePair.IsTrue;
            SourceEvent = sourceEvent;
        }
        public WardenObjectiveEventData? SourceEvent { get; init; } = null;
    }

    // Event type which causes a notification to the StateTracker for a region being found
    private const eWardenObjectiveEventType CheckRegionEventType = (eWardenObjectiveEventType)1_512_758_915;

    // Event type which causes a notification to the StateTracker for a location being found
    private const eWardenObjectiveEventType CheckLocationEventType = CheckRegionEventType + 1;

    // Extensions for working with these two event types
    public static WardenObjectiveEventData CreateCheckRegionEvent(Expedition.Data data, string regionName)
        => EventHelper.CreateCheckRegionEvent(data.GetOrCreateRegion(regionName));

    public static void ConvertToCheckRegionEvent(WardenObjectiveEventData e, Expedition.Data data, string regionName)
        => EventHelper.ConvertToCheckRegionEvent(e, data.GetOrCreateRegion(regionName));

    public static WardenObjectiveEventData CreateCheckLocationEvent(Expedition.Data data, string locationName)
        => EventHelper.CreateCheckLocationEvent(data.LookupLocation(locationName).ID);

    public static void ConvertToCheckLocationEvent(WardenObjectiveEventData e, Expedition.Data data, string locationName)
        => EventHelper.ConvertToCheckLocationEvent(e, data.LookupLocation(locationName).ID);

    // Store the original event data as a wrapped copy in the condition pair
    private static void WrapEventData(WardenObjectiveEventData e)
        => e.Condition = new WrappedEvent(e.Condition, e.MemberwiseClone().Cast<WardenObjectiveEventData>());

    // Attempt to retrieve event data from the stored wrapped data. On fail, returns null
    private static WardenObjectiveEventData? UnwrapEventData(WardenObjectiveEventData source)
        => source.Condition?.TryCast<WrappedEvent>()?.SourceEvent;

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
        ConvertToCheckRegionEvent(e, regionId);
        return e;
    }

    /// <summary>
    /// Converts an existing event to a check region event, overwriting existing event data
    /// </summary>
    /// <param name="e">The event to convert</param>
    /// <param name="regionId">ID of the region to check</param>
    public static void ConvertToCheckRegionEvent(WardenObjectiveEventData e, int regionId)
    {
        WrapEventData(e);
        e.Type = CheckRegionEventType;
        e.EnemyID = BitConverter.ToUInt32(BitConverter.GetBytes(regionId));
    }

    /// <summary>
    /// Create a brand new event which will check the provided location
    /// </summary>
    /// <param name="locationId">ID of the location to check</param>
    /// <returns>A new CheckLocation event</returns>
    public static WardenObjectiveEventData CreateCheckLocationEvent(long locationId)
    {
        WardenObjectiveEventData e = MakeBlankEvent();
        ConvertToCheckLocationEvent(e, locationId);
        return e;
    }

    /// <summary>
    /// Converts an existing event to a check location event, overwriting existing event data
    /// </summary>
    /// <param name="e">The event to convert</param>
    /// <param name="locationId">ID of the location to check</param>
    public static void ConvertToCheckLocationEvent(WardenObjectiveEventData e, long locationId)
    {
        WrapEventData(e);
        var bytes = BitConverter.GetBytes(locationId);
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
        public static bool Prefix(WardenObjectiveEventData eData, float currentDuration)
        {
            Action? action = eData.Type switch
            {
                CheckRegionEventType => () =>
                {
                    int id = BitConverter.ToInt32(BitConverter.GetBytes(eData.EnemyID));
                    Plugin.Get().StateTracker.NotifyFoundRegion(Expedition.Data.FromCurrentExpedition().RegionList[id].Name);
                },
                CheckLocationEventType => () =>
                {
                    byte[] bytes = new byte[8];
                    BitConverter.GetBytes(eData.EnemyID).CopyTo(bytes, 0);
                    BitConverter.GetBytes(eData.FogSetting).CopyTo(bytes, 4);
                    long id = BitConverter.ToInt64(bytes);
                    if (!Plugin.Get().StateTracker.NotifyFoundLocation(id))
                    {
                        var originalEvent = UnwrapEventData(eData);
                        if (originalEvent != null) // Assumed there was no original event on fail
                            WorldEventManager.DoExcecuteEvent(originalEvent);
                    }
                },
                _ => null,
            };

            if (action != null)
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
            else
                return true;
        }
    }

}
