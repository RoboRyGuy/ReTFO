
using GameData;
using LevelGeneration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;

namespace ReTFO.Archipelago.ModdedInstanceData2.Callbacks;

// Trigger event processing for a few sources where processing require little extra context
public static class EventTriggerProcessors
{
    // Triggers some important zone events that don't really have a home elsewhere
    [ProcessZone.Callback]
    public static void AddZoneEvents(Manager manager, ProcessZone.Data data)
    {
        int region = manager.GetOrCreateRegion(data.ZoneName);
        if (data.Zone != null)
        {
            Tuple<string, Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData>>[] pairs =
            {
                Tuple.Create( $"{data.ZoneName} OnBossDeath",               data.Zone.EventsOnBossDeath ),
                Tuple.Create( $"{data.ZoneName} OnDoorScanDone",            data.Zone.EventsOnDoorScanDone ),
                Tuple.Create( $"{data.ZoneName} OnDoorScanStart",           data.Zone.EventsOnDoorScanStart ),
                Tuple.Create( $"{data.ZoneName} OnOpenDoor",                data.Zone.EventsOnOpenDoor ),
                Tuple.Create( $"{data.ZoneName} OnPortalWarp",              data.Zone.EventsOnPortalWarp ),
                Tuple.Create( $"{data.ZoneName} OnTerminalDeactivateAlarm", data.Zone.EventsOnTerminalDeactivateAlarm ),
                Tuple.Create( $"{data.ZoneName} OnUnlockDoor",              data.Zone.EventsOnUnlockDoor ),
            };
            foreach (var pair in pairs)
            {   // Basically, each event "could" occur infinite times. Events only occur up to an event break. We process accordingly
                int count = 0;
                foreach (var eventChain in pair.Item2.EventSplit())
                    manager.ProcessEvent.Invoke(manager, new ProcessEvent.Data(data, eventChain, region, $"{pair.Item1} ({++count})"));
            }

            // In-zone scans, which are event-triggered scans
            foreach (var scan in data.Zone.WorldEventChainedPuzzleDatas.Iter())
            {
                uint count = 0;
                foreach (var eventChain in scan.EventsOnScanDone.EventSplit())
                {
                    ++count;
                    int scanRegion = manager.GetOrCreateRegion($"{data.ZoneName} Custom Scan ({scan.WorldEventObjectFilter}) (Completion #{count})");
                    ProcessZone.Data scanZone = data; // It may be worth searching for the scan, if I can find a good method
                    manager.AddPath(new Path()
                    {
                        starting_region = manager.GetOrCreateRegion(scanZone.ZoneName),
                        ending_region = scanRegion,
                        required_item = $"Start Scan {scan.WorldEventObjectFilter}",
                        required_item_count = count,
                        alternate_item = null
                    });

                    manager.ProcessEvent.Invoke(manager, new(data, eventChain, region, $"{data.ZoneName} OnCompleteScan ({scan.WorldEventObjectFilter}) (Completion #{count})"));
                }
            }
        }
        else if (data.DimensionData != null)
        {   // Only event of note in dimension data is OnBossDeath. In vanilla, at most one boss can be fought per expedition, but might as well be safe
            int count = 0;
            foreach (var eventChain in data.DimensionData.EventsOnBossDeath.EventSplit())
                manager.ProcessEvent.Invoke(manager, new(data, eventChain, region, $"{data.ZoneName} OnBossDeath ({++count})"));
        }
    }

    public static Dictionary<string, Tuple<LayerType, eLocalZoneIndex>> WorldEventObjectOverrides = new Dictionary<string, Tuple<LayerType, eLocalZoneIndex>>()
    {
        { "Evt_Shuttlebox_Interact_R8A1", Tuple.Create(LayerType.Main, eLocalZoneIndex.Zone_4) },
        { "WE_Hearsay_Interact_02",       Tuple.Create(LayerType.Main, eLocalZoneIndex.Zone_7) },
    };

    [ProcessZone.Callback]
    public static void AddTriggerEvents(Manager manager, ProcessZone.Data data)
    {
        if (data.Zone == null) return;

        // Trigger events need to be sorted to the object which triggers it
        var triggers = data.Zone.EventsOnTrigger.Select(e => e.WorldEventTriggerObjectFilter).Distinct();

        foreach (var trigger in triggers)
        {
            // Skip null entries
            if (trigger == null) continue;

            // Try and identify the trigger's zone
            ProcessZone.Data sourceZone = data;
            if (WorldEventObjectOverrides.TryGetValue(trigger, out var overrideInfo))
                sourceZone = data.FindZoneExact(overrideInfo.Item1, overrideInfo.Item2) ?? throw new NullReferenceException("Failed to find world event object source zone");

            // Note: Skipping/ignoring event breaks
            manager.ProcessEvent.Invoke(manager, new(
                data, data.Zone.EventsOnTrigger.Where(e => e.WorldEventTriggerObjectFilter == trigger).Cast<WardenObjectiveEventData>().ToList(),
                manager.GetOrCreateRegion(sourceZone.ZoneName), $"{sourceZone.ZoneName} OnTrigger ({trigger})"
            ));
        }
    }

}
