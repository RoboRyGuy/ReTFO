using ReTFO.DamageOverhaulCore.Data;

namespace ReTFO.DamageOverhaulCore.Config;

/// <summary>
/// Similar to an EnemyDamageData, but with a ConfigName and no limb info
/// </summary>
public struct EnemyReferenceConfig
{
    // Default constructor
    public EnemyReferenceConfig() { }

    // Name of this config
    public string ConfigName { get; init; } = "";

    // The max health of the enemy
    public float MaxHealth { get; init; } = 30f;

    // Flat resistance for post-transfer damage
    public FlatResistance FlatResistance { get; init; } = new();

    // Falloff resistance for post-transfer damage
    public FalloffResistance FalloffResistance { get; init; } = new();
}
