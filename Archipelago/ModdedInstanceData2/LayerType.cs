using System.Diagnostics.CodeAnalysis;
using LevelGeneration;

namespace ReTFO.Archipelago.ModdedInstanceData2;

/* Wraps the LG_LayerType and eDimensionIndex enums and combines them into one, to simplify 
 *  methods which deal with both types of layers
 * Implictly castable from both
 */
public struct LayerType
{
    public int value;

    public LayerType(LG_LayerType layer, eDimensionIndex dimension)
    {
        if (dimension == eDimensionIndex.Reality) this = layer;
        else this = dimension;
    }
    public LayerType(eDimensionIndex dimension, LG_LayerType layer)
    {
        if (dimension == eDimensionIndex.Reality) this = layer;
        else this = dimension;
    }

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

    public string GetName()
    {
        return value switch
        {
            0 => "Main",
            -1 => "Secondary",
            -2 => "Overload",
            _ => $"Dim #{value}",
        };
    }

    public bool IsReality => !IsDimension;
    public bool IsMainLayer => value == 0;
    public bool IsSecondaryLayer => value == -1;
    public bool IsOverloadLayer => value == -2;
    public bool IsDimension => value < -2 || value > 0;

    public static bool operator ==(LayerType left, LayerType right) => left.Equals(right);
    public static bool operator !=(LayerType left, LayerType right) => !left.Equals(right);
    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is LayerType type) return value.Equals(type.value);
        else return false;
    }
    public override int GetHashCode() => value.GetHashCode();
}
