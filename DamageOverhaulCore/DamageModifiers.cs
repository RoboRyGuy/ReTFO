using System;

namespace ReTFO.DamageOverhaulCore;

/// <summary>
/// Wraps data needed for the new damage calculations; powers and bases
/// </summary>
public class  DamageModifiers
{
    public float PrecisionMult = 1f;
    public float PrecisionPower = 1f;

    public float StaggerMult = 1f;
    public float StaggerPower = 1f;

    public float BackMult = 1f;
    public float BackPower = 1f;

    public float SleeperMult = 1f;
    public float SleeperPower = 1f;

    public float EnvironmentalMult = 1f;
    public float EnvironmentalPower = 1f;

    public static void CalcMultipliers(DamageModifiers a, DamageModifiers b, float backMod, float sleepMod, out float pMult, out float stMult, out float slMult, out float bMult, out float eMult)
    {
        pMult  = MathF.Pow(a.PrecisionMult,     b.PrecisionPower)          * MathF.Pow(b.PrecisionMult,     a.PrecisionPower);
        stMult = MathF.Pow(a.StaggerMult,       b.StaggerPower)            * MathF.Pow(b.StaggerMult,       a.StaggerPower);
        slMult = MathF.Pow(a.SleeperMult,       b.SleeperPower * sleepMod) * MathF.Pow(b.SleeperMult,       a.SleeperPower * sleepMod);
        bMult  = MathF.Pow(a.BackMult,          b.BackPower    * backMod)  * MathF.Pow(b.BackMult,          a.BackPower    * backMod);
        eMult  = MathF.Pow(a.EnvironmentalMult, b.EnvironmentalPower)      * MathF.Pow(b.EnvironmentalMult, a.EnvironmentalPower);
    }
}
