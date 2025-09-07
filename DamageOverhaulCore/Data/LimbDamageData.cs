

namespace ReTFO.DamageOverhaulCore.Data;

/// <summary>
/// Contains damage data for a limb.
/// May be used by multiple enemies and multiple limbs at the same time.
/// </summary>
public class LimbDamageData
{
    // A shared copy of the default LimbDamageData
    public static readonly LimbDamageData Default = new();

    // The max health of the limb
    public float MaxHealth { get; init; } = 3f;

    // The exponential base used when calculating precision damage to this limb
    public float PrecisionBase { get; init; } = 1f;

    // The exponential base used when calculating backstab damage to this limb
    public float BackstabBase { get; init; } = 1f;

    // The exponential base used when calculating sleeping damage to this limb
    public float SleepingBase { get; init; } = 1f;

    // The exponential base used when calculating precision damage via this limb to the base
    public float TransferPrecisionBase { get; init; } = 1f;

    // The exponential base used when calculating backstab damage via this limb to the base
    public float TransferBackstabBase { get; init; } = 2f;

    // The exponential base used when calculating sleeping damage via this limb to the base
    public float TransferSleepingBase { get; init; } = 2f;

    // The multiplier for damage dealt to the enemy itself when this limb is hit
    public float TransferMultiplier { get; init; } = 1f;

    // The amount of damage dealt to the enemy itself when this limb breaks
    public float OnBreakDamage { get; init; } = 0f;

    // Flat resistance for this limb
    public FlatResistance FlatResistance { get; init; } = new();

    // Falloff resistance for this limb
    public FalloffResistance FalloffResistance { get; init; } = new();
}
