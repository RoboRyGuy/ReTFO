using UnityEngine;

namespace ReTFO.DamageOverhaulCore.Data;

/// <summary>
/// Contains data used for calculating falloff resistance
/// </summary>
public struct FalloffResistance
{
    // Default constructor
    public FalloffResistance() { }

    // When to start applying falloff, only applicable if ResistanceBase != 1
    // Must be 0 or more, else things start acting up
    public float SoftCap { get; init; } = 10f;

    // The upper cap of damage that can be applied; zero means no cap
    // Can be negative, though I don't recommend it
    public float HardCap { get; init; } = 0f;
    
    // The base used for calculating falloff interpolation
    // 0 = 100% falloff, 1 = no falloff
    // Negative = Falloff is less damage than was input
    // Above 1 = Falloff is negative; weapons deal more damage than input
    public float ResistanceBase { get; init; } = 1f;

    // When taking negative damage, temporarily make it positive for the calculation?
    // Note that disabling this may cause issues, since the formulas aren't built for negatives
    public bool FlipForNegatives { get; init; } = true;

    // Calculates damage after applying the configured resistance
    public float ApplyResistance(float damage, float falloffPower)
    {
        bool flip = damage < 0 && FlipForNegatives;
        if (flip) damage = -damage;

        if (damage > SoftCap)
        {
            if (falloffPower == 0f || ResistanceBase == 1f)
            { } //damage = damage;
            else if (ResistanceBase == 0f)
                damage = SoftCap;
            else
            {
                float c;
                if (ResistanceBase > 0)
                    c = Mathf.Pow(ResistanceBase, falloffPower);
                else
                    c = -Mathf.Pow(-ResistanceBase, falloffPower);
                damage = Mathf.Pow(damage, c) * Mathf.Pow(SoftCap, 1f - c);
            }
        }

        if (HardCap != 0 && damage > HardCap)
            damage = HardCap;

        if (flip) return -damage;
        else return damage;
    }
}
