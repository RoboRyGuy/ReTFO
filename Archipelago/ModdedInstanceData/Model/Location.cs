
using Archipelago.MultiClient.Net.Models;
using Clonesoft.Json;
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Represents a location in archipelago. Some example locations:
/// <list type="bullet">
///  <item>Key spawn locations</item>
///  <item>Big pickup (e.g. cell) spawn locations</item>
///  <item>Event triggers (split into sub-locations, one for each event action in the chain)</item>
///  <item>Objective items / logical locations (e.g. extraction can't actually be picked up, but has a location)</item>
/// </list>
/// In GTFO, locations are considered reachable if and only if all regions they can be located in are reachable.
/// Note that in actual gameplay, it is still possible to reach locations without access to all possible regions.
/// </summary>
public class Location
{
    /// <summary>
    /// Standard constructor
    /// </summary>
    /// <param name="name">Name of the location</param>
    /// <param name="id">ID of the location. The first ID is 1</param>
    /// <param name="regions">
    /// The regions the location can be found in. 
    /// Archipelago will require all listed regions be reachable for this location to be reachable.
    /// </param>
    /// <param name="type">The randomization type of this location</param>
    /// <param name="autoDiscover">If true, the region is immediately discovered when all its regions are discovered</param>
    /// <param name="item">The item in this location, if there is one</param>
    public Location(string name, long id, RegionList regions, eRandomizationType type, bool autoDiscover, Item? item = null)
    {
        Name = name;
        ID = id;
        OwningRegionIds = regions;
        Type = type;
        AutoDiscover = autoDiscover;
        ItemID = item?.ID ?? 0L;
    }

    /// <summary>
    /// Unique name of the location, used to identify it
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Unique ID of the location. IDs range from 1 to 2^53-1
    /// </summary>
    public long ID { get; set; }

    /// <summary>
    /// Regions this location can be in
    /// </summary>
    public List<int> OwningRegionIds { get; init; } = new(1);

    /// <summary>
    /// Type of location. This will be used to determine which items are placed here, or 
    ///  if the location gets randomized at all
    /// </summary>
    public eRandomizationType Type { get; set; }

    /// <summary>
    /// When the containing region(s) are discovered, is this location automatically discovered?
    /// If possible, make this false and add logic so players "find" the location correctly.
    /// </summary>
    [JsonIgnore]
    public bool AutoDiscover { get; set; }

    /// <summary>
    /// Item typically located in this location. If null, this is simply a spare location archipelago can choose to fill
    /// </summary>
    public long ItemID { get; set; } = 0;

    /// <summary>
    /// Scouted location retrieved from archipelago during play. Null if not yet scouted (or if the location is scouted as empty)
    /// </summary>
    public ScoutedItemInfo? ScoutedItem { get; set; } = null;

    public override string ToString() => Name;
}
