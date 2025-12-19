
using GameData;

namespace ReTFO.Archipelago.ModdedInstanceData;

// Disabling these to simplify definitions, plus I want property names to be snake_case for parsing into python later
#pragma warning disable IDE1006 // "Property names must be capitalized"
#pragma warning disable CS8618  // "Property must contain non-null value after exiting constructor"

// Item with a weight
public struct WeightedItem
{
    public string name { get; set; }
    public float weight { get; set; }
}

// Data for a zone placement
public struct ZonePosition
{
    public ZonePosition(eLocalZoneIndex zoneIndex, eDimensionIndex dimensionIndex)
    {
        local_index = (int)zoneIndex;
        dimension_index = (int)dimensionIndex;
    }

    public static ZonePosition Make(ZonePlacementData data)
        => new ZonePosition(data.LocalIndex, data.DimensionIndex);

    public int local_index { get; set; }     // Local index of the zone
    public int dimension_index { get; set; } // Which dimension the zone is in
}

// Data for a key or cell required by a zone
public struct KeyData
{
    public KeyData() { }

    // Zone the key unlocks; -1 = bulkhead key
    public int zone_alias { get; set; } = 0;

    // Zone(s) this key is found in
    public List<ZonePosition> positions { get; init; } = new(0);
}

// Data for a pickup (either a cell or a quest objective)
public struct PickupData
{
    public PickupData() { }

    // -1 is a cell, otherwise it's the objective number the item is for (which may also be a cell lol)
    //     -1 -> A cell spawned in by a progression lock on a door or specific warden objectives
    //  other -> An item added via warden objectives. Objectives that spawn explictly cells will use -1,
    //            but objectives spawning any generic big pickup will not use -1 for cells
    public int item_type { get; init; } = -1;

    // Zone(s) this can be found in
    public List<ZonePosition> positions { get; init; } = new(1);
}

// Wraps an event and event source
public class WardenEvent
{
    public class Source
    {
        public enum eType
        {
            DoorApproach,               // ApproachDoorEventSource    // Approaching / looking at a door
            LevelApproach,              // None                       // Special case of DoorApproach for approaching the door of the first zone in a level
            DoorUnlock,                 // ZonalEventSource           // Unlocking a zone door
            DoorScanStart,              // ZonalEventSource           // Starting the chained puzzle for a door
            DoorScanEnd,                // ZonalEventSource           // Completing the chained puzzle for a door
            DoorOpen,                   // ZonalEventSource           // Opening the zone door (after completing the chained puzzle)
            ZoneTrigger,                // ZoneTriggerEventSource     // Generic zone trigger, specified by the name of the GameObject which triggers it
            TerminalCommand,            // TerminalCommandEventSource // A custom / unique command on a terminal
            TerminalDeactivateAlarm,    // DeactivateAlarmEventSource // Deactivating an error alarm using level-generated deactivate-alarm
            PortalWarp,                 // ZonalEventSource           // Warping in the zone using a MWP. Only works in portal zones (ie R6B1 final zone)
            BossDeath,                  // ZonalEventSource           // Killing a boss in the zone. Only works for squid boss
            InZoneScan,                 // InZoneScanEventSource      // Completing a misc scan in the zone, ie a zone scan triggered by a terminal
            CompleteHSUScan,            // MultiZonalEventSource      // HSU scan, specifically one for the HSU objective
            CompleteReactorWave,        // ReactorWaveEventSource     // Start and survive a particular reactor wave
            CompleteReactorStartup,     // MultiZonalEventSource      // Complete an entire reactor startup sequence
            CompleteReactorShutdown,    // MultiZonalEventSource      // Complete an entire reactor shutdown sequence
            PickupSmallItem,            // SubObjectiveEventSource    // Pickup a small item for GatherSmallItems
            SpecialTerminalCommand,     // MultiZonalEventSource      // Performing a SpecialTerminalCommand (objective-based unique command)
            RetrieveBigPickup,          // SubObjectiveEventSource    // Pickup a big item during a RetrieveBigPickups objective
            PowerSoloGen,               // SubObjectiveEventSource    // Power a generator during a PowerCellDistribution objective (ie one not part of a cluster)
            PowerClusterGen,            // SubObjectiveEventSource    // Power a generator which is part of a CentralGeneratorCluster objective
            ProcessItem,                // SubObjectiveEventSource    // Process an item in the ActivateHSU objective
            StartSurvival,              // ObjectiveEventSource       // OnActivate for the survival objective type
            CompleteStandardUplink,     // SubObjectiveEventSource    // Complete a non-corrupted uplink
            CompleteCorruptedUplink,    // SubObjectiveEventSource    // Complete a corrupted (or "dual") uplink
            StartTimedSequenceRound,    // SubObjectiveEventSource    // Start a specific timed sequence round
            CompleteTimedSequenceRound, // SubObjectiveEventSource    // Successfully complete a timed sequence round
            FailTimdSequenceRound,      // SubObjectiveEventSource    // Fail a timed sequence round
            CompleteFullTimedSequence,  // SubObjectiveEventSource    // Complete all rounds of a timed sequence objective
            OnElevatorLand,             // None                       // When the level starts (and the players can start moving)
            ObjectiveComplete,          // ObjectiveEventSource       // Events triggered immediately after an objective is completed
            StartExitScan,              // ObjectiveEventSource       // Events triggered by initiating an exit scan
        }
    }

    public class Action
    {
        public enum eType
        {
            UnlockZoneDoor,           // SpecificZoneEventAction // Unlock, but do not open, a zone door
            OpenZoneDoor,             // SpecificZoneEventAction // Immediately open a zone door
            StepObjectiveProgression, // ObjectiveEventAction    // Increment the objective progression by 1 step
            ForceCompleteObjective,   // ObjectiveEventAction    // Instantly complete the objective and spawn extraction (Completes all objectives)
            ForceInstantWin,          // ObjectiveEventAction    // Instantly complete the expedition
            ActivateWinOnDeath,       // ObjectiveEventAction    // Queue an instant win for when all players are downed
            DimensionWarp,            // WarpEventAction         // Warp the entire team to a specified zone in a dimension
            StartScan,                // StartScanEventAction    // Start a scan (chained puzzle)
        }
    }

    public Source.eType source_type { get; set; }
    public Source source { get; set; }
    public Action.eType action_type { get; set; }
    public Action action { get; set; }
}

// An event source which is located in a specific zone
public class ZonalEventSource : WardenEvent.Source
{
    public int zone_local_index { get; set; }
}

// An event trigger by approaching a zone door
public class ApproachDoorEventSource : ZonalEventSource
{
    public int target_local_index { get; set; }
}

// An event triggered by a trigger gameobject
public class ZoneTriggerEventSource : ZonalEventSource
{
    public string trigger_name { get; set; }
}

// An event triggered by a misc scan in a zone
public class InZoneScanEventSource : ZonalEventSource
{
    public string scan_name { get; set; } // Name of the event object used to trigger the scan
}

// An event source which is a command on a specific terminal in a specific zone
public class TerminalCommandEventSource : ZonalEventSource
{
    public int terminal_local_index { get; set; }
    public string command_name { get; set; }
}

// An event source which is a deactivate alarm command in a specific zone
public class DeactivateAlarmEventSource : TerminalCommandEventSource
{
    public int alarm_zone_index { get; set; }
}

// An event source which can be located in multiple zones
public class MultiZonalEventSource : WardenEvent.Source
{
    public List<ZonePosition> zones { get; init; } = new(1);
}

// An event triggered by an objective
public class ObjectiveEventSource : WardenEvent.Source
{
    public int objective_num { get; set; } // Which objective triggered the event (relevant if there are chained objectives)
}

// An event triggered by a subobjective (or part of a subobjective)
public class SubObjectiveEventSource : ObjectiveEventSource
{
    public int subobjective_num { get; set; } // Which subobjective
}

// An event source triggered by a specific reactor wave
public class ReactorWaveEventSource : SubObjectiveEventSource
{
    public int wave_index { get; set; } // Wave which must be completed to trigger the event
}

// An event action which occurs in a specific zone
public class SpecificZoneEventAction : WardenEvent.Action
{
    public int target_zone_local_index { get; set; }
    public int target_zone_layer { get; set; }
    public int target_zone_dimension { get; set; }
}

// An event action which starts a scan in a specific zone
public class StartScanEventAction : SpecificZoneEventAction
{
    public string scan_name { get; set; }
}

// An event action which warps the team to a specific zone in a specific dimension
public class WarpEventAction : WardenEvent.Action
{
    public int target_dimension_index { get; set; }
    public int target_zone_local_index { get; set; }
}

// An event action which directly modifies objective completion in some way
public class ObjectiveEventAction : WardenEvent.Action
{
    public int objective_layer { get; set; }
}

// Wraps data for a generic WardenObjective
public class ObjectiveData
{
    public enum eType
    {   // If not stated or _Empty, uses ObjectiveData type
        CollectDNASample,
        ReactorStartup,         // ReactorStartupObjectiveData
        ReactorStartup_Empty,   // If DoNotSolveObjectiveOnReactorComplete
        ReactorShutdown,
        ReactorShutdown_Empty,  // If DoNotSolveObjectiveOnReactorComplete
        GatherSmallItems,       // GatherItemsObjectiveData
        ClearAPath,
        SpecialTerminalCommand,
        RetrieveBigItems,
        PowerCellDistribution,
        TerminalUplink,
        CentralGeneratorCluster,
        ProcessItem,
        ProcessItem_Empty,      // If not ActivateHSU_ObjectiveCompleteAfterInsertion
        SurviveWardenProtocol,
        GatherTerminals,        // GatherItemsObjectiveData
        CorruptedTerminalUplink,
        Empty,
        TimedTerminalSequence   // TimedSequenceObjectiveData
    }

    // Type of objective
    public eType objective_type { get; set; }

    // Number of sub-objectives which need to be solved for the full objective to be solved
    public int sub_objective_count { get; set; }
}

// Wraps data for a ReactorStartup objective
public class ReactorStartupObjectiveData : ObjectiveData
{
    public int wave_count { get; set; } // Number of waves in this reactor startup
}

// Wraps data for a GatherSmallItems objective
public class GatherItemsObjectiveData : ObjectiveData
{
    public int req_count { get; set; } // Number of pikcups which must be available
}

// Wraps data for a TimedSequence objective
public class TimedSequenceObjectiveData : ObjectiveData
{
    public int num_rounds { get; set; } // Number of times the timed sequence must be performed
}

// Data for a zone
public class ZoneData
{
    public enum eLockType
    {
        None,        // Zone is unlocked
        SimpleKey,   // Zone requires a basic key from some other zone
        GenAndCell,  // Zone requires a generator to be powered using a cell
        Locked,      // Zone starts locked, usually (but not always) unlocked by an event
        BulkheadKey, // Zone requires a bulkhead key, opened via a DC
    }

    // Copy of original zone for use during data processing
    [Newtonsoft.Json.JsonIgnore]
    public ExpeditionZoneData? originalZone { get; set; }

    // Zone number
    public int alias { get; set; }

    // How many terminals are in the zone
    public int terminal_count { get; set; }

    // Zone providing physical access to this one
    public int entrance_index { get; set; }

    // Lock type for this zone
    public eLockType lock_type { get; set; }
}

// Data for a generatable level
public class LevelData
{
    // Zone we start in (local index). Typically, but not necessarily, zone 0
    public int start_zone { get; set; } = 0;

    // Zones in the level
    public List<ZoneData> zones { get; init; } = new(0);

    // Keys in the level
    public List<KeyData> keys { get; init; } = new(0);

    // Big pickups in the level
    public List<PickupData> pickups { get; init; } = new(0);

    // Events in this level
    public List<WardenEvent> events { get; init; } = new(0);

    // Objective data, if any (dimensions don't have objectives)
    public List<ObjectiveData> objectives { get; init; } = new(0);
}

// Data for attaching secondary and overload to other levels
public struct BuildFromData
{
    // Layer to build from
    public int layer_index { get; set; }

    // Zone to build from
    public int zone_index { get; set; }
}

// Data for an expedition
public class ExpeditionData
{
    // Name of the expedition (ie "R1A1")
    public string name { get; set; }

    // Main level data
    public LevelData? main_level { get; set; } = null;

    // Secondary data, if secondary is enabled
    public LevelData? secondary_level { get; set; } = null;

    // Where to build secondary from
    public BuildFromData secondary_build_from { get; set; }

    // Overload data, if overload is enabled
    public LevelData? overload_level { get; set; } = null;

    // Where to build overload from
    public BuildFromData overload_build_from { get; set; }

    // Dimension data. May be (usually is) empty
    public Dictionary<int, LevelData> dimension_data { get; init; } = new(0);
}

// Data for a copy of the game running a particular set of mods
public class ModdedInstanceData
{
    // Expeditions in this instance of the game
    public List<ExpeditionData> expeditions { get; init; } = new(0);

    // Gear available in this instance of the game
    public List<string> gear_names { get; init; } = new(0);

    // Filler items that can be used
    public List<WeightedItem> filler_items { get; init; } = new(0);

    // Traps that can be used
    public List<WeightedItem> trap_items { get; init; } = new(0);
}

#pragma warning restore IDE1006
#pragma warning restore CS8618
