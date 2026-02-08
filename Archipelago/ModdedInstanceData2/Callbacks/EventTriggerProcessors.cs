
using GameData;
using System;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData2.Callbacks;

// Trigger event processing for a few sources where processing require little extra context
public static class EventTriggerProcessors
{
    [ProcessZone.Callback]
    public static void AddZoneEvents(Manager manager, ProcessZone.Data data)
    {
        int region = manager.GetOrCreateRegion(data.ZoneName);
        if (data.Zone != null)
        {
            Tuple<string, Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData>>[] pairs =
            {
                Tuple.Create( $"{data.ZoneName} OnApproachZone",            data.Zone.EventsOnApproachDoor ),
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
                foreach (var eventChain in pair.Item2.Split(e => e.Type == eWardenObjectiveEventType.EventBreak))
                    manager.ProcessEvent.Invoke(manager, new ProcessEvent.Data(data, eventChain, region, $"{pair.Item1} ({++count})"));
            }

            // Trigger events need to be sorted to the object which triggers it
            var triggers = data.Zone.EventsOnTrigger.Select(e => e.WorldEventTriggerObjectFilter).Distinct();
            foreach (var trigger in triggers)
            {   // We're not bothering with event breaks here. If they're needed, too bad!
                manager.ProcessEvent.Invoke(manager, new(
                    data, data.Zone.EventsOnTrigger.Where(e => e.WorldEventTriggerObjectFilter == trigger), 
                    region, $"{data.ZoneName} OnTrigger ({trigger})"
                ));
            }

            // In-zone scans, which are event-triggered scans
            foreach (var scan in data.Zone.WorldEventChainedPuzzleDatas)
            {
                uint count = 0;
                foreach (var eventChain in scan.EventsOnScanDone.Split(e => e.Type == eWardenObjectiveEventType.EventBreak))
                {
                    ++count;
                    int scanRegion = manager.GetOrCreateRegion($"{data.ZoneName} Custom Scan ({scan.WorldEventObjectFilter}) (Completion #{count})");
                    manager.AddPath(new Path()
                    {
                        starting_region = region,
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
            foreach (var eventChain in data.DimensionData.EventsOnBossDeath.Split(e => e.Type == eWardenObjectiveEventType.EventBreak))
                manager.ProcessEvent.Invoke(manager, new(data, eventChain, region, $"{data.ZoneName} OnBossDeath ({++count})"));
        }
    }

    // Triggers event processing for when unique commands are triggered
    [ProcessTerminal.Callback]
    public static void AddUniqueCommandEvents(Manager manager, ProcessTerminal.Data data)
    {
        foreach (var command in data.TerminalData.UniqueCommands)
        {
            manager.ProcessEvent.Invoke(manager, new(
                data, command.CommandEvents.Iter(),
                manager.GetOrCreateRegion(data.TerminalName), $"{data.TerminalName} Unique Command (\"{command.Command}\")"
            ));
        }
    }

}
