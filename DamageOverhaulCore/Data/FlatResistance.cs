using UnityEngine;

namespace ReTFO.DamageOverhaulCore.Data;

/// <summary>
/// Contains data used for calculating flat resistances
/// </summary>
public struct FlatResistance
{
    // The modes available for applying flat resistance
    public enum ResistanceMode
    {
        Capped,         // Flat resistance will never reduce damage below the HardCap
        Diminishing,    // Factorio-style flat resistance, with configurable hard and soft caps
        Uncapped,       // Flat resistance is always applied in full, ignoring the SoftCap and HardCap
    }
    
    // Default constructor
    public FlatResistance() { }

    // The mode used for calculating damage
    public ResistanceMode Mode { get; init; } = ResistanceMode.Capped;

    // The amount of flat resistance to be applied
    public float Resistance { get; init; } = 0;

    // The minimum damage allowed after applying flat resistance
    public float HardCap { get; init; } = 0;

    // When to begin applying diminished resistance
    public float SoftCap { get; init; } = 1f;

    // Calculates damage after applying the configured resistance
    public float ApplyResistance(float damage, float piercing, out float piercingUsed)
    {
        float outDamage;
        switch(Mode)
        {
            case ResistanceMode.Uncapped:
                outDamage = damage - Resistance;
                break;
            case ResistanceMode.Capped:
                outDamage = Mathf.Max(damage - Resistance, HardCap);
                break;
            case ResistanceMode.Diminishing:
                if (damage - Resistance > SoftCap)
                    outDamage = damage - Resistance;
                else
                    outDamage = HardCap + (SoftCap + HardCap) / (Resistance + SoftCap + 1 - damage);
                outDamage = Mathf.Min(outDamage, damage); // Some edge cases would incrase the damage
                break;
            default:
                throw new NotImplementedException($"Flat damage resistance mode {Mode} not recognized");
        }

        if (piercing >= 0)
            piercingUsed = Mathf.Min(piercing, damage - outDamage);
        else
            piercingUsed = Mathf.Max(piercing, outDamage - damage, -outDamage);
        return outDamage + piercingUsed;
    }
}
