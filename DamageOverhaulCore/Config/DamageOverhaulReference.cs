

namespace ReTFO.DamageOverhaulCore.Config;

/// <summary>
/// Class used to store/read reference data for use when reading a DamageOverhaulConfig
/// </summary>
public class DamageOverhaulReference
{
    // List of enemy configs to reference
    public List<EnemyReferenceConfig> EnemyReferences { get; init; } = new(0);

    // List of limb configs to reference
    public List<LimbReferenceConfig> LimbReferences { get; init; } = new(0);
}
