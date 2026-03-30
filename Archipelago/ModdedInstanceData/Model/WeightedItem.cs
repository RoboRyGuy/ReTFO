
namespace ReTFO.Archipelago.ModdedInstanceData.Model;

// Same as an item, but includes a "weight" used for various randomization purposes
public abstract class WeightedItem : Item
{
    public WeightedItem(string name, float weight)
        : base(name)
    {
        Weight = weight;
    }

    public float Weight { get; set; }
}
