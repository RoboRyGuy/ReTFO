
using System;
using System.Collections.Generic;

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

    public override string ToString() => name;
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
    public Location(string name, string item, List<int> regions, bool auto_discover)
    {
        this.name = name;
        this.item = item;
        this.regions = regions;
        this.auto_discover = auto_discover;
    }

    // Unique name of the location, used to identify it
    public string name { get; set; }

    // Item typically located in this location
    public string item { get; set; }

    // Regions this location can be in
    public List<int> regions { get; init; } = new(1);

    // When the containing region(s) are discovered, is this location automatically discovered?
    // If possible, make this false and add logic so players "find" the location correctly
    // Also, leave a note of which patch(es) implement said logic
    public bool auto_discover { get; set; }

    public override string ToString() => $"{name} -> {item}";
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

    public override string ToString() => $"{starting_region} => {ending_region}";
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
    public int num_sectors { get; set; } = 1;
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