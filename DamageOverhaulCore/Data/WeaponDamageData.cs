
namespace ReTFO.DamageOverhaulCore.Data;

/// <summary>
/// Damage data for a weapon. Of note, values here do not override in-game values if they exist
/// </summary>
public class WeaponDamageData
{
    // Shared default data
    public static WeaponDamageData Default { get; } = new();

    // Exponential power used to calculating the precision mult
    public float PrecisionPower { get; init; } = 1f;

    // Exponential power used to calculating the backstab mult
    public float BackstabPower { get; init; } = 1f;

    // Exponential power used to calculating the sleeping mult
    public float SleepingPower { get; init; } = 0f;

    // How much damage ignores flat resistance
    // This can be used up by the limb, leaving no puncture for the base's resistance
    public float Puncturing { get; init; } = 0f;

    // How susceptible the weapon is to falloff resistance
    // 0 = immune, 1 = standard, negative -> damage falls up
    public float FalloffPower { get; init; } = 1f;
}
