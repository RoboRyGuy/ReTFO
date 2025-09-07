using UnityEngine;
using System.Text.Json.Serialization;

namespace ReTFO.DamageOverhaulCore.Data;

/// <summary>
/// Contains damage data for an enemy.
/// May be used by multiple enemies at the same time.
/// </summary>
public class EnemyDamageData
{
    // The max health of the enemy
    public float MaxHealth { get; init; } = 30f;

    // Flat resistance for post-transfer damage
    public FlatResistance FlatResistance { get; init; } = new();

    // Falloff resistance for post-transfer damage
    public FalloffResistance FalloffResistance { get; init; } = new();

    // LimbDamageData for each limb this enemy has
    public LimbDamageData[] LimbDamageDatas { get; init; } = Array.Empty<LimbDamageData>();
}
