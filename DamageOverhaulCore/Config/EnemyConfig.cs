using ReTFO.DamageOverhaulCore.Data;
using System.Text.Json.Serialization;

namespace ReTFO.DamageOverhaulCore.Config;

/// <summary>
/// This struct associates enemy IDs with <see cref="DamageData"/>
/// This includes damage data for each limb as well as for enemy itself
/// </summary>
public struct EnemyConfig
{
    // Default constructor
    public EnemyConfig() { }

    // Config name (optional; makes configs more readable)
    public string ConfigName { get; init; } = "";
    
    // Names of the enemies (optional; makes configs more readable)
    public List<string> EnemyNames { get; init; } = new(0);

    // Ids of the enemies being configured
    public List<uint> EnemyIds { get; init; } = new(0);

    // Damage data to use for this enemy's base
    public EnemyReferenceConfig? EnemyDamageData { get; init; } = null;

    // Index of this enemy's damage data in the reference sheet
    public int ReferenceIndex { get; init; } = -1;

    // ConfigName of this enemy's damage data in the config
    public string ReferenceName { get; init; } = "";

    // Configs for each limb. Make sure to have one for each limb,
    //  but you can combine multiple into one 
    public List<LimbConfig> LimbConfigs { get; init; } = new(0);
}
