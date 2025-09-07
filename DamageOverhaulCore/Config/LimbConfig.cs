using System.Reflection;
using System.Runtime.CompilerServices;
using ReTFO.DamageOverhaulCore.Data;

namespace ReTFO.DamageOverhaulCore.Config;

// Used by EnemyConfig to associate a limb with data and other limbs
public struct LimbConfig
{
    // Default constructor
    public LimbConfig() { }

    // Name of this config (optional unless matching reference entries by name)
    public string ConfigName { get; init; } = "";

    // Name of the limb being configured (optional; improves readability)
    public string LimbName { get; init; } = "";

    // Names of the limbs being configured (optional; improves readability)
    public List<string> LimbNames { get; init; } = new() { };

    // ID of the limb being configured
    public int LimbId { get; init; } = -1;

    // IDs of the limbs being configured
    public List<int> LimbIds { get; init; } = new(0);

    // Limb damage data to use
    public LimbDamageData? LimbDamageData { get; init; } = null;

    // Index of the LimbConfig in the reference config
    public int ReferenceIndex { get; init; } = -1;

    // ConfigName of the LimbConfig in the reference config
    public string ReferenceName { get; init; } = "";

    // Converts this into a list of configs, one for each ID it has
    public List<LimbConfig> Explode()
    {
        if (LimbId != -1)
        {
            if (LimbIds.Count != 0) throw new ArgumentException("Limb Config has both a single limb ID and a list of IDs");
            return new List<LimbConfig>() { this };
        }
        else
        {
            LimbConfig self = this;
            return LimbIds.Select(id => new LimbConfig()
            {
                ConfigName = self.ConfigName + $" ({id})",
                LimbName = self.LimbName,
                LimbNames = self.LimbNames,
                LimbId = id,
                LimbIds = new(),
                LimbDamageData = self.LimbDamageData,
                ReferenceIndex = self.ReferenceIndex,
                ReferenceName = self.ReferenceName,
            }).ToList();
        }
    }
}
