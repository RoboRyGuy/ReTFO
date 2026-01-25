
using GameData;
using LevelGeneration;
using MS.Internal.Xml.XPath;
using ReTFO.Archipelago.ModdedInstanceData;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ReTFO.Archipelago.ModdedInstanceData2;

/* Represents a region in archipelago. We organize GTFO regions as such:
 * - Menu: Standard archipelago starting area, allows access to all unlocked expeditions
 * - Expedition: One expedition. Always connects to Main Menu, and always to one Zone (the elevator zone)
 * - Zone: One zone in an expedition
 * - Terminal: A terminal in a zone. This is a separate region because terminals can be password-locked, so it simplifies our logic.
 * - Objective: One per objective in a layer, chained together (for chained objectives). 
 */  
public class Region
{
    public Region(string name) { this.name = name; }

    // Unique name of the region, used to identify it
    public string name { get; set; }
}

/*
 * Represents a location in archipelago. Some example locations:
 * - Key spawn locations
 * - Big pickup (e.g. cell) spawn locations
 * - Event triggers (split into sub-locations, one for each event action in the chain)
 * - Objective items / logical locations (e.g. the ClearAPath objective has no pickup, but requires reaching a specific point)
 */
public class Location
{
    // Unique name of the location, used to identify it
    public string name { get; set; }

    // Item typically located in this location
    public string item { get; set; }

    // Regions this location can be in
    public List<int> regions { get; init; } = new(1);
}

/*
 * In archipelago, this would be a connection between two entrances in a region (as well as the entrances)
 * Examples of a path:
 * - Starting an expedition (dropping in)
 * - Zone door from one door to another
 * - A terminal in a zone (may be locked)
 * - Teleporting
 * - Completing an objective
 */
public class Path
{
    // Region this path starts in
    public int starting_region { get; set; }

    // Region this path ends in
    public int ending_region { get; set; }

    // Item required to traverse this path
    public string? required_item { get; set; } = null;

    // Number of required items needed to traverse this path
    public uint required_item_count { get; set; } = 0;

    // Alternate item required to traverse this path
    // - If there is no required item, this is ignored (by design)
    // - The alternate item is assumed to only require one count to traverse the path
    // - This is intended for door unlock events (since all zone doors can be force unlocked via an event)
    public string? alternate_item { get; set; } = null;
}

// Associates an item with a weight for randomization purposes
public struct WeightedItem
{
    public WeightedItem(string name, float weight)
    {
        this.name = name;
        this.weight = weight;
    }
    public string name { get; set; }
    public float weight { get; set; }
}

// Bundles regions, paths, and items together for one expedition
public class Expedition
{
    public string name { get; set; }
    public List<Region> regions { get; init; } = new();
    public List<Location> locations { get; init; } = new();
    public List<Path> paths { get; init; } = new();
    public int start_region { get; set; } = 0;
}

// Contains and formats modded instance data so it can be easily serialized / deserialized
public class ModdedInstanceData
{
    public string plugin_version { get; init; } = Plugin.Version;
    public List<Expedition> expeditions { get; init;} = new();
    public List<Tuple<string, List<string>>> optional_items { get; init; } = new();
    public List<WeightedItem> filler_items { get; init; } = new();
    public List<WeightedItem> trap_items { get; init; } = new();
}

// Packages event data for processing expeditions
public class ProcessExpeditionData
{
    public ProcessExpeditionData(RundownDataBlock rundown, ExpeditionInTierData expedition, eRundownTier tier, int indexInTier)
    {
        Rundown = rundown;
        Expedition = expedition;
        Tier = tier;
        IndexInTier = indexInTier;
    }

    public RundownDataBlock Rundown { get; init; }
    public ExpeditionInTierData Expedition { get; init;}
    public eRundownTier Tier { get; init;}
    public int IndexInTier { get; init;}
}

// Wraps the LG_LayerType and eDimensionIndex enums and combines them into one, to simplify 
//  methods which deal with both types of layers
// Implictly castable from both
public struct LayerType
{
    public int value;

    public static implicit operator LayerType(LG_LayerType layerType)
        => new LayerType() { value = -(int)layerType };

    public static implicit operator LayerType(eDimensionIndex dimensionIndex)
        => new LayerType() { value = (int)dimensionIndex };
    
    public static LayerType Main => LG_LayerType.MainLayer;
    public static LayerType Secondary => LG_LayerType.SecondaryLayer;
    public static LayerType Overload => LG_LayerType.ThirdLayer;
    public static LayerType Dimension_1 = eDimensionIndex.Dimension_1;
    public static LayerType Dimension_2 = eDimensionIndex.Dimension_2;
    public static LayerType Dimension_3 = eDimensionIndex.Dimension_3;
    public static LayerType Dimension_4 = eDimensionIndex.Dimension_4;
    public static LayerType Dimension_5 = eDimensionIndex.Dimension_5;

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is LayerType type)
            return value.Equals(type.value);
        return false;
    }

    public static bool operator ==(LayerType left, LayerType right) => left.Equals(right);
    public static bool operator !=(LayerType left, LayerType right) => !left.Equals(right);
}

public class ProcessLayerData
{
    public ProcessLayerData(ProcessExpeditionData expeditionData, LayerType layerType)
    {
        ExpeditionData = expeditionData;
        LayerType = layerType;
    }

    public ProcessExpeditionData ExpeditionData { get; init; }
    public RundownDataBlock Rundown => ExpeditionData.Rundown;
    public ExpeditionInTierData Expedition => ExpeditionData.Expedition;
    public eRundownTier Tier => ExpeditionData.Tier;
    public int IndexInTier => ExpeditionData.IndexInTier;

    public LayerType LayerType { get; init; }

    public static implicit operator ProcessExpeditionData(ProcessLayerData data) => data.ExpeditionData;
}

public class ProcessZoneData
{
    public ProcessZoneData(ProcessLayerData layer, ExpeditionZoneData? zone)
    {
        Layer = layer;
        Zone = zone;
    }

    public ProcessExpeditionData ExpeditionData => Layer.ExpeditionData;
    public RundownDataBlock Rundown => ExpeditionData.Rundown;
    public ExpeditionInTierData Expedition => ExpeditionData.Expedition;
    public eRundownTier Tier => ExpeditionData.Tier;
    public int IndexInTier => ExpeditionData.IndexInTier;

    public ProcessLayerData Layer { get; init; }
    public LayerType LayerType => Layer.LayerType;

    public ExpeditionZoneData? Zone { get; init; } // Null if processing dimension with no layout

    public static implicit operator ProcessExpeditionData(ProcessZoneData data) => data.Layer.ExpeditionData;
    public static implicit operator ProcessLayerData(ProcessZoneData data) => data.Layer;
}

public class ProcessTerminalData
{
    public ProcessTerminalData(ProcessZoneData zoneData, TerminalPlacementData terminal, int terminalIndex)
    {
        ZoneData = zoneData;
        Terminal = terminal;
        TerminalIndex = terminalIndex;
    }

    public ProcessExpeditionData ExpeditionData => Layer.ExpeditionData;
    public RundownDataBlock Rundown => ExpeditionData.Rundown;
    public ExpeditionInTierData Expedition => ExpeditionData.Expedition;
    public eRundownTier Tier => ExpeditionData.Tier;
    public int IndexInTier => ExpeditionData.IndexInTier;

    public ProcessLayerData Layer => ZoneData.Layer;
    public LayerType LayerType => Layer.LayerType;

    public ProcessZoneData ZoneData { get; init; }
    public ExpeditionZoneData? Zone => ZoneData.Zone;

    public TerminalPlacementData Terminal { get; init; }
    public int TerminalIndex { get; init; }

    public static implicit operator ProcessExpeditionData(ProcessTerminalData data) => data.ExpeditionData;
    public static implicit operator ProcessLayerData(ProcessTerminalData data) => data.Layer;
    public static implicit operator ProcessZoneData(ProcessTerminalData data) => data.ZoneData;
}

public class ProcessEventSourceData
{
    public ProcessEventSourceData(ProcessLayerData layer, string sourceName, int sourceRegion, IEnumerable<WardenObjectiveEventData> events)
    {
        Layer = layer;
        SourceName = sourceName;
        SourceRegion = sourceRegion;
        Events = events;
    }

    public ProcessLayerData Layer { get; init; }
    public string SourceName { get; init; }
    public int SourceRegion { get; init; }
    public IEnumerable<WardenObjectiveEventData> Events { get; init; }
}