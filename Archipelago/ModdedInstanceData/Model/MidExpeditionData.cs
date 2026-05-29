using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Data associated with a particular expedition used exclusively for
///  exporting that data
/// </summary>
[DataContract]
public struct MidExpeditionData
{
    /// <summary>
    /// Name of the expedition
    /// </summary>
    [DataMember(Name = "name")]
    public string Name { get; set; }

    /// <summary>
    /// List of regions that can be reached from this expedition
    /// </summary>
    [DataMember(Name = "reachable_regions")]
    public List<RegionID> ReachableRegions { get; set; }
}
