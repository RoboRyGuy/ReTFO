using ReTFO.DamageOverhaulCore.Data;

namespace ReTFO.DamageOverhaulCore.Config;

/// <summary>
/// This class is used to store/read <see cref="EnemyDamageData"/> from file.
/// </summary>
public class DamageOverhaulConfig
{
    // Gun configs contained by this config
    public List<WeaponConfig> GunConfigs { get; init; } = new();

    // Melee configs contained by this config
    public List<WeaponConfig> MeleeConfigs { get; init; } = new();

    // Explosives configs contained by this config (Namely, configs for the mine deployer)
    public List<WeaponConfig> ExplosiveConfig { get; init; } = new();

    // Enemy configs contained by this config
    public List<EnemyConfig> EnemyConfigs { get; init; } = new();
}
