using ReTFO.DamageOverhaulCore.Data;

namespace ReTFO.DamageOverhaulCore.Config;

// Config wrapping WeaponDamageData for one or more PlayerOfflineGear IDs
public struct WeaponConfig
{
    // Default constructor
    public WeaponConfig() { }
    
    // Name of this config (optional)
    public string ConfigName { get; init; } = "";

    // Name of the limb being configured (optional; improves readability)
    public string WeaponName { get; init; } = "";

    // Names of the limbs being configured (optional; improves readability)
    public List<string> WeaponNames { get; init; } = new() { };

    // ID of the limb being configured
    public int WeaponId { get; init; } = -1;

    // IDs of the limbs being configured
    public List<uint> WeaponIds { get; init; } = new(0);

    // Weapon damage data to use
    public WeaponDamageData? WeaponDamageData { get; init; } = null;
}
