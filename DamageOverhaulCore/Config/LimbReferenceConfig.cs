using ReTFO.DamageOverhaulCore.Data;

namespace ReTFO.DamageOverhaulCore.Config;

/// <summary>
/// Same as a LimbDamageData, but with some extra config info
/// </summary>
public class LimbReferenceConfig : LimbDamageData
{
    // Name of this config
    public string ConfigName { get; init; } = "";

    // Strips extra properties and converts this to a LimbDamageData
    public LimbDamageData ToData()
    {
        LimbDamageData data = new LimbDamageData();
        foreach (var field in typeof(LimbDamageData).GetFields())
            field.SetValue(data, field.GetValue(this));
        return data;
    }
}
