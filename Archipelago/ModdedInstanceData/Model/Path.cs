using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Represents a directed path between two regions, implying an entrance and exit which connect the two regions.
/// <br />
/// Examples of a path:
/// <list type="bullet">
///  <item>Sec and bulkhead doors which connect zones</item>
///  <item>Getting acess to a zone's terminal (since it may be locked)</item>
///  <item>Teleporting between dimensions</item>
///  <item>Progressing or completing an objective</item>
/// </list>
/// </summary>
public class Path
{
    /// <summary>
    /// Comparer for two regions using their starting region as the key
    /// </summary>
    public class ByStartingRegionComparer : IComparer<Path>
    {
        public int Compare(Path? x, Path? y)
            => Comparer<int?>.Default.Compare(x?.StartingRegion, y?.StartingRegion);
    }

    /// <summary>
    /// Comparer for two regions using their ending region as the key
    /// </summary>
    public class ByEndingRegionComparer : IComparer<Path>
    {
        public int Compare(Path? x, Path? y)
            => Comparer<int?>.Default.Compare(x?.EndingRegion, y?.EndingRegion);
    }

    /// <summary>
    /// Construct a path with the given start and end points.
    /// Typically, you should use <see cref="Game.Data.AddPath"/> or similar instead of this method
    /// </summary>
    /// <param name="startingRegion">Region the path starts in</param>
    /// <param name="endingRegion">Region the path ends in</param>
    public Path(int startingRegion, int endingRegion)
    {
        StartingRegion = startingRegion;
        EndingRegion = endingRegion;
    }

    /// <summary>
    /// Optional name for this path
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Region this path starts in
    /// </summary>
    public int StartingRegion { get; set; }

    /// <summary>
    /// Region this path ends in
    /// </summary>
    public int EndingRegion { get; set; }

    /// <summary>
    /// Item required to traverse this path.
    /// If null and category item is null, no item required
    /// If both required_item and category_item, both items (at their respective counts) are required
    /// </summary>
    public string? RequiredItem { get; set; } = null;

    /// <summary>
    /// Count of the required item needed to traverse this path
    /// </summary>
    public uint RequiredItemCount { get; set; } = 0;

    /// <summary>
    /// Item category required to use this path
    /// If null and required_item item is null, no item required
    /// If both required_item and category_item, both items (at their respective counts) are required
    /// </summary>
    public string? CategoryItem { get; set; } = null;

    /// <summary>
    /// Number of items in category required to use this path
    /// </summary>
    public uint CategoryItemCount { get; set; }

    /// <summary>
    /// Alternate item required to traverse this path
    /// <list type="=bullet">
    ///  <item>If there is no required or category item, this is ignored (by design)</item>
    ///  <item>The alternate item is assumed to only require one count to traverse the path</item>
    ///  <item>This is intended for situations such as door unlock events (since all zone doors can be force unlocked via an event)</item>
    /// </list>
    /// </summary>
    public string? AlternateItem { get; set; } = null;

    /// <summary>
    /// Helper for visualizing in debugger
    /// </summary>
    public override string ToString() => $"{StartingRegion} => {EndingRegion}";
}
