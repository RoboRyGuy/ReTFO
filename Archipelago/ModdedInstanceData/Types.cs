
using GameData;
using System.Collections.Generic;

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

    public enum eType
    {
        SimpleKey,
        BulkheadKey,
        Cell,
        PasswordPart,
    }

    // Type of key
    public eType type { get; set; } = eType.SimpleKey;

    // Zone the key unlocks, if a simple key. Zone containing the terminal if a password part
    public int zone_alias { get; set; } = 0;

    // Which terminal in the zone the password part is for
    public int terminal_index { get; set; } = 0;

    // Zone(s) this key is found in
    public List<ZonePosition> positions { get; init; } = new(0);
}

// Data for a pickup (either a cell or a quest objective)
public struct BigPickupData
{
    // Item Datablock ID
    public uint item_type { get; init; }
}

// Wraps an event
public class WardenEvent
{
    public class Action { }

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

    public WardenEvent(eType t, Action data) { type = t; action_data = data; }

    public eType type { get; set; }         // Type of action being performed
    public Action action_data { get; set; } // Additional data, if necessary
}

// Wraps an event involving approaching a zone from inside a zone
public class ApproachZoneEvent : WardenEvent
{
    public ApproachZoneEvent(eType type, Action data, int target) : base(type, data) { target_alias = target; }
    public int target_alias { get; set; }
}

// Wraps an event involving a world event trigger
public class TriggerEvent : WardenEvent
{
    public TriggerEvent(eType type, Action data, string name) : base(type, data) { trigger_name = name; }
    public string trigger_name { get; set; }
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
    {
        CollectDNASample,
        ReactorStartup,         // ReactorStartupObjectiveData
        ReactorStartup_Empty,   // If DoNotSolveObjectiveOnReactorComplete
        ReactorShutdown,
        ReactorShutdown_Empty,  // If DoNotSolveObjectiveOnReactorComplete
        GatherSmallItems,
        ClearAPath,
        SpecialTerminalCommand,
        RetrieveBigItems,
        PowerCellDistribution,
        TerminalUplink,
        CentralGeneratorCluster,
        ProcessItem,
        ProcessItem_Empty,      // If not ActivateHSU_ObjectiveCompleteAfterInsertion
        SurviveWardenProtocol,
        GatherTerminals,        
        CorruptedTerminalUplink,
        Empty,
        TimedTerminalSequence   // TimedSequenceObjectiveData
    }

    public eType objective_type { get; set; }
    public int sub_objective_count { get; set; }
    public List<List<ZonePosition>> positions { get; init; } = new(0); // What this represents varies by objective
    public List<List<WardenEvent>> events_on_activate { get; init; } = new(0);
    public List<WardenEvent> events_on_goto_win { get; init; } = new(0);
}

// Wraps data for a ReactorStartup objective
public class ReactorStartupObjectiveData : ObjectiveData
{
    public int wave_count { get; set; } // Number of waves in this reactor startup
    public List<List<WardenEvent>> events_on_finish_wave { get; init; } = new(0);
}

// Wraps data for a TimedSequence objective
public class TimedSequenceObjectiveData : ObjectiveData
{
    public int num_rounds { get; set; } // Number of times the timed sequence must be performed
    public List<List<WardenEvent>> events_on_start_round { get; init; } = new(0);
    public List<List<WardenEvent>> events_on_succeed_round { get; init; } = new(0);
    public List<List<WardenEvent>> events_on_fail_round { get; init; } = new(0);
}

// Data for a command on a terminal
public class CommandData
{
    public CommandData(string name) { command_name = name; }
    public string command_name { get; set; }
    public List<WardenEvent> events { get; init; } = new(0);
}

// Data for a terminal
public class TerminalData
{
    public int password_count { get; set; } = 0;
    public List<CommandData> commands { get; init; } = new(0);
    public List<string> logs { get; set; } = new(0);
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

    public int alias { get; set; } // Not local index, but actual number shown in-game
    public int entrance_index { get; set; }
    public eLockType lock_type { get; set; }
    public List<TerminalData> terminals { get; init; } = new(0);
    public List<BigPickupData> big_pickups { get; init; } = new(0);

    public List<WardenEvent> events_on_unlock_door { get; init; } = new(0);
    public List<WardenEvent> events_on_door_scan_start { get; init; } = new(0);
    public List<WardenEvent> events_on_door_scan_done { get; init; } = new(0);
    public List<WardenEvent> events_on_open_door { get; init; } = new(0);
    public List<WardenEvent> events_on_boss_death { get; init; } = new(0);
    public List<WardenEvent> events_on_portal_warp { get; init; } = new(0);

    public List<TriggerEvent> events_on_trigger { get; init; } = new(0);
    public List<ApproachZoneEvent> events_on_approach_zone { get; init; } = new(0);

}

// Data for a generatable level
public class LevelData
{
    public int start_zone { get; set; } = 0; // Local index of the first zone in the level. Typically 0
    public List<ZoneData> zones { get; init; } = new(0);
    public List<KeyData> keys { get; init; } = new(0);
    public List<ObjectiveData> objectives { get; init; } = new(0);
    public List<ApproachZoneEvent> events_on_approach_level { get; init; } = new(0); // Same as events_on_approach_zone, but if the source is outside the level
}

// Data for attaching secondary and overload to other levels
public struct BuildFromData
{
    public int layer_index { get; set; }
    public int zone_index { get; set; }
}

// Data for an expedition
public class ExpeditionData
{
    public string name { get; set; }
    public LevelData? main_level { get; set; } = null;      // Null if failed to generate data (invalid expedition)
    public LevelData? secondary_level { get; set; } = null; // Null if disabled
    public BuildFromData secondary_build_from { get; set; }
    public LevelData? overload_level { get; set; } = null;  // Null if disabled
    public BuildFromData overload_build_from { get; set; }
    public Dictionary<int, LevelData> dimension_data { get; init; } = new(0);
    public List<WardenEvent> events_on_elevator_land { get; init; } = new(0);
    public List<WardenEvent> events_on_progress_exit_scan { get; init; } = new(0);
}

// Data for a copy of the game running a particular set of mods
public class ModdedInstanceData
{
    public List<ExpeditionData> expeditions { get; init; } = new(0);
    public List<string> gear_names { get; init; } = new(0);
    public List<WeightedItem> filler_items { get; init; } = new(0);
    public List<WeightedItem> trap_items { get; init; } = new(0);
}

#pragma warning restore IDE1006
#pragma warning restore CS8618
