
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Represents a "state" of the game in which the player(s) have decided to consume items to unlock
/// particular paths.
/// </summary>
[DataContract]
public readonly struct ChoiceState
{
    /// <summary>
    /// The consume paths are used to enter this choice
    /// </summary>
    [DataMember(Name = "choice_paths")]
    public PathID[] ChoicePaths { get; init; }

    /// <summary>
    /// Regions relevant to this state
    /// </summary>
    [DataMember(Name = "regions")]
    public RegionID[] Regions { get; init; }

    /// <summary>
    /// A compressed format of regions; each item is a start and end (inclusive)
    /// of a range of consecutive regions relevant to the state
    /// </summary>
    [DataMember(Name = "region_ranges")]
    public (RegionID, RegionID)[] RegionRanges { get; init; }
}
