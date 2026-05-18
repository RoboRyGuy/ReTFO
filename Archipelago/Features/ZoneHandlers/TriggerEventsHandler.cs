using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using EventList = Il2CppSystem.Collections.Generic.List<GameData.WardenObjectiveEventData>;

namespace ReTFO.Archipelago.Features.ZoneHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault, AutomatedFeature]
public class TriggerEventsHandler : ArchipelagoFeature
{
    public override string Name => "Trigger Events Handler";
    public override string Description
        => "Triggers processing of trigger events"
        + "Trigger events are the most versatile event type in GTFO, which are activated by world "
        + "event triggers. These include invisible trigger hitboxes and certain special interactions";
    public override FeatureGroup Group => FeatureGroups.ZoneHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // A few triggers in vanilla are in misleading locations. Currently, my best workaround is just to use this dict to correct errors
    private static Dictionary<string, Tuple<LayerType, eLocalZoneIndex>> WorldEventObjectOverrides
        = new Dictionary<string, Tuple<LayerType, eLocalZoneIndex>>()
    {
        { "Evt_Shuttlebox_Interact_R8A1", Tuple.Create(LayerType.Main, eLocalZoneIndex.Zone_4) }, // R8A1 shuttlebox (for MWP)
        { "WE_Hearsay_Interact_02",       Tuple.Create(LayerType.Main, eLocalZoneIndex.Zone_7) }, // I don't remember
    };

    private static Dictionary<string, Func<Zone.Data, KeyedItem>> PathReqsOverride
        = new Dictionary<string, Func<Zone.Data, KeyedItem>>()
    {
        { "Evt_Shuttlebox_Interact_R8A1", (data) => ObjectiveHandlers.RetrieveBigItemsHandler.GetItem(data.GetObjectiveDatas().ElementAt(0), 1) }, // R8A1 - Shuttlebox near the end. That MWP is a retrieval target, so this is a bit odd
        { "WE_Dataextractor_Interact", (data) => Pickups.BigPickupHandler.GetBigPickupItem(data, 181) }, // R8C2 - Fake "Process Item" objective at start
        { "Evt_Shuttlebox_Interact_R7B1", (data) => Pickups.BigPickupHandler.GetBigPickupItem(data, 173) }, // R7B1 - The collection case interaction
    };

    [Zone.Callback]
    public void AddTriggerEvents(Zone.Data data)
    {
        // I know these statements can be condensed, but the compiler gives warnings about null references if I don't check each explicitly
        if (data.Zone == null) return;
        if (data.Zone.EventsOnTrigger == null) return;
        if (data.Zone.EventsOnTrigger.Count == 0) return;

        // Faux list is required since this event list uses a different underlying type
        // Of note, faux list will be sorted
        EventList fauxList = new(data.Zone.EventsOnTrigger.Count);
        foreach (var item in data.Zone.EventsOnTrigger.Iter().GroupBy(e => e.WorldEventTriggerObjectFilter).SelectMany(g => g))
            fauxList.Add(item);
        data.Zone.EventsOnTrigger.Clear();

        // We cannot use the event wrapper here, so we use a bit of custom processing
        int eventStart = 0;
        for (int eventEnd = 1; eventEnd <= fauxList.Count; eventEnd++)
        {
            WorldEventFromSourceData previousItem = fauxList[eventStart].Cast<WorldEventFromSourceData>();
            WorldEventFromSourceData? newItem = (eventEnd == fauxList.Count) ? null : fauxList[eventEnd].Cast<WorldEventFromSourceData>();

            if (previousItem.WorldEventTriggerObjectFilter == newItem?.WorldEventTriggerObjectFilter)
                continue;

            // Some triggers are null. Fortunately, we can skip those
            string? trigger = previousItem.WorldEventTriggerObjectFilter;
            if (trigger != null)
            {
                // Identify the zone. Currently using a simple override system for cases where trigger data is in the wrong zone
                Zone.Data sourceZone = data;
                if (WorldEventObjectOverrides.TryGetValue(trigger, out var overrideInfo))
                {
                    sourceZone = data.FindZoneExact(overrideInfo.Item1, overrideInfo.Item2)
                        ?? throw new Exception("Warden Event Trigger has zone override, but the override zone could not be found");
                }

                // Identify the item needed to trigger the event. Again, using a simple override to identify this
                Path.RequiredItem reqs = new();
                if (PathReqsOverride.TryGetValue(trigger, out var itemGetter))
                {
                    reqs = itemGetter.Invoke(data).Item.PathReqs;
                }

                // Process the events
                // Note: Skipping/ignoring event breaks, since how would those work here?
                string eventName = $"{sourceZone.ZoneName} OnTrigger ({trigger})";
                RegionID eventRegion = data.LookupOrCreateRegion(eventName);
                data.AddPath(new Path() {
                    StartingRegion = data.LookupOrCreateRegion(sourceZone.ZoneName), 
                    EndingRegion = eventRegion,
                    ReqItem = reqs,
                    ReqCount = reqs.IsNull ? 0u : 1u,
                });
                Event.Data eventData = data.ProcessEvents(eventRegion, eventName, fauxList, eventStart, eventEnd - eventStart);

                // Update based on entries added/removed
                eventStart = eventData.EventStart;
                eventEnd = eventData.EventStart + eventData.EventCount;
            }

            // Copying the results over to the original list, including converting event data where necessary
            for (int j = eventStart; j < eventEnd; j++)
            {
                var e = fauxList[j].TryCast<WorldEventFromSourceData>();
                if (e != null) data.Zone.EventsOnTrigger.Add(e);
                else data.Zone.EventsOnTrigger.Add(new()
                {
                    WorldEventTriggerObjectFilter = trigger,
                    AchievementKey = fauxList[j].AchievementKey,
                    ChainPuzzle = fauxList[j].ChainPuzzle,
                    ClearDimension = fauxList[j].ClearDimension,
                    Condition = fauxList[j].Condition,
                    Count = fauxList[j].Count,
                    CustomSubObjective = fauxList[j].CustomSubObjective,
                    CustomSubObjectiveHeader = fauxList[j].CustomSubObjectiveHeader,
                    Delay = fauxList[j].Delay,
                    DialogueID = fauxList[j].DialogueID,
                    DimensionIndex = fauxList[j].DimensionIndex,
                    Duration = fauxList[j].Duration,
                    Enabled = fauxList[j].Enabled,
                    EnemyID = fauxList[j].EnemyID,
                    EnemyWaveData = fauxList[j].EnemyWaveData,
                    FogSetting = fauxList[j].FogSetting,
                    FogTransitionDuration = fauxList[j].FogTransitionDuration,
                    Layer = fauxList[j].Layer,
                    LocalIndex = fauxList[j].LocalIndex,
                    Position = fauxList[j].Position,
                    SoundID = fauxList[j].SoundID,
                    SoundSubtitle = fauxList[j].SoundSubtitle,
                    SustainedEventDelay = fauxList[j].SustainedEventDelay,
                    SustainedEventSlotIndex = fauxList[j].SustainedEventSlotIndex,
                    SustainedEventStateCount = fauxList[j].SustainedEventStateCount,
                    SustainedEventStateDuration = fauxList[j].SustainedEventStateDuration,
                    TerminalCommand = fauxList[j].TerminalCommand,
                    TerminalCommandRule = fauxList[j].TerminalCommandRule,
                    Trigger = fauxList[j].Trigger,
                    Type = fauxList[j].Type,
                    UseStaticBioscanPoints = fauxList[j].UseStaticBioscanPoints,
                    WardenIntel = fauxList[j].WardenIntel,
                    WorldEventObjectFilter = fauxList[j].WorldEventObjectFilter,
                });
            }
            eventStart = eventEnd;
        }
    }

}
