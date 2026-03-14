using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

// Same as an item, but includes a "weight" used for various randomization purposes
public class WeightedItem : Item
{
    public WeightedItem(string name, eRandomizationType type, float weight)
        : base(name, type, new List<string>(0))
    {
        this.Weight = weight;
    }

    public float Weight { get; set; }
}
