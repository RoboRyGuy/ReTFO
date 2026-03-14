using Clonesoft.Json;
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Represents a region in archipelago. Some examples of regions:
/// <list type="bullet">
///  <item>The main menu</item>
///  <item>Each zone</item>
///  <item>Terminals in zones</item>
///  <item>Objective steps (objectives are built out as traversable graphs)</item>
/// </list>
/// </summary>
public class Region
{
    public Region(string name) { Name = name; }

    /// <summary>
    /// Unique name of the region, used to identify it
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Whether this region is reachable, populated during the graph traversal check
    /// </summary>
    public bool Reachable { get; set; } = false;

    /// <summary>
    /// All paths starting in this region
    /// </summary>
    public SortedSet<Path> ConnectedPaths { get; init; } = new(new Path.ByEndingRegionComparer());

    /// <summary>
    /// List of locations that can be discovered in this region.
    /// In GTFO, locations are discoverable if and only if all regions they can be in are discoverable.
    /// </summary>
    [JsonIgnore]
    public SortedSet<long> ConnectedLocationIds { get; init; } = new();

    /// <summary>
    /// Prints this region as a string.
    /// </summary>
    /// <remarks>
    /// This particular format makes it easier to read in the debugger.
    /// Regions are not intended to be converted to strings.
    /// </remarks>
    public override string ToString() => $"{(Reachable ? "    Reachable" : "Unreachable")} - {Name}";
}
