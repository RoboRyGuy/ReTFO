
using Agents;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using System.Numerics;

namespace ReTFO.DamageOverhaulCore;

/// <summary>
/// Extends GTFO's Dam_EnemyDamageLimb to implement IDamageableLimb
/// </summary>
public class DamExt_EnemyDamageLimb : Dam_EnemyDamageLimb, IDamageableLimb
{
    public DamExt_EnemyDamageLimb(IntPtr ptr) : base(ptr) { }
    public DamExt_EnemyDamageLimb() : base(ClassInjector.DerivedConstructorPointer<DamExt_EnemyDamageLimb>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    private DamageModifiers m_damageModifiers = new();
    private DamageModifiers m_transferModifiers = new();

    public Agent? DamageAgent => m_base?.Owner;
    [HideFromIl2Cpp]
    public IDamageableBase? DamageBase => m_base as IDamageableBase;
    [HideFromIl2Cpp]
    public float Health => m_health;
    [HideFromIl2Cpp]
    public float MaxHealth => m_healthMax;
    [HideFromIl2Cpp]
    public DamageModifiers DamageModifiers => m_damageModifiers;
    [HideFromIl2Cpp]
    public DamageModifiers TransferModifiers => m_transferModifiers;


    [HideFromIl2Cpp]
    public override float ApplyDamageFromBehindBonus(float dam, UnityEngine.Vector3 pos, UnityEngine.Vector3 dir, float backstabberMulti = 1) => dam;
    [HideFromIl2Cpp]
    public override float ApplyWeakspotAndArmorModifiers(float dam, float precisionMulti = 1) => dam;

    // Calc the correct back modifier from the current state and the incoming projectile info
    // Patch this method to modify behavior
    [HideFromIl2Cpp]
    public virtual float CalcBackModifier(Vector3 position, Vector3 direction)
    {
        // Same as vanilla, except vanilla adds 1 and use that as the multiplier
        return Math.Clamp(.25f + Vector3.Dot(direction.ToFlatNormal(), m_base.Owner.transform.forward.ToSystem().ToFlatNormal()), 0f, 1f);
    }

    // Calc the current sleeper modifer (0 to 1) from the current enemy state
    // Patch this method to modify behavior
    [HideFromIl2Cpp]
    public virtual float CalcSleeperModifier()
    {
        // TODO: Sleeper falloff from wakeup timer
        return m_base.Owner.AI.Mode != AgentMode.Agressive ? 1f : 0f;
    }

    public override void MeleeDamage(float dam, Agent sourceAgent, UnityEngine.Vector3 position, UnityEngine.Vector3 direction, float staggerMulti = 1, float precisionMulti = 1, float environmentMulti = 1, float backstabberMulti = 1, float sleeperMulti = 1, bool skipLimbDestruction = false, DamageNoiseLevel damageNoiseLevel = DamageNoiseLevel.Normal, uint gearCategoryId = 0)
    {
        DamageModifiers modifiers = new()
        {
            PrecisionMult = precisionMulti,
            SleeperMult = sleeperMulti,
            BackMult = backstabberMulti,
            StaggerMult = staggerMulti,
            EnvironmentalMult = environmentMulti,
        };
        MeleeDamage(dam, modifiers, sourceAgent, position.ToSystem(), direction.ToSystem());
    }
    [HideFromIl2Cpp]
    public void MeleeDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction)
    {
        float backMod = CalcBackModifier(position, direction);
        float sleepMod = CalcSleeperModifier();
        float pMult, stMult, slMult, bMult, eMult;

        DamageModifiers.CalcMultipliers(DamageModifiers, modifiers, backMod, sleepMod, out pMult, out stMult, out slMult, out bMult, out eMult);
        m_health -= dam * pMult * slMult * bMult;

        DamageModifiers.CalcMultipliers(TransferModifiers, modifiers, backMod, sleepMod, out pMult, out stMult, out slMult, out bMult, out eMult);
        if (DamageBase == null)
            throw new NullReferenceException("Damage base was null in DamExt_EnemyDamageLimb");
        DamageBase.MeleeDamage(dam * pMult * slMult * bMult, modifiers, sourceAgent, position, direction);
    }

    public override void PushDamage(float dam, Agent sourceAgent, UnityEngine.Vector3 position, UnityEngine.Vector3 direction)
    {
        PushDamage(dam, new DamageModifiers(), sourceAgent, position.ToSystem(), direction.ToSystem());
    }
    [HideFromIl2Cpp]
    public void PushDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void StaggerDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction)
    {
        // TODO: Implement this
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void FallDamage(float dam, DamageModifiers modifiers)
    {
        // Enemies don't take fall damage!
        return;
    }

    public override void BulletDamage(float dam, Agent? sourceAgent, UnityEngine.Vector3 position, UnityEngine.Vector3 direction, UnityEngine.Vector3 normal, bool allowDirectionalBonus = false, float staggerMulti = 1, float precisionMulti = 1, uint gearCategoryId = 0)
    {
        DamageModifiers inDam = new()
        {
            PrecisionMult = precisionMulti,
            StaggerMult = staggerMulti,
            BackPower = allowDirectionalBonus ? 1f : 0f,
        };
        BulletDamage(dam, inDam, sourceAgent, position.ToSystem(), direction.ToSystem());
    }
    [HideFromIl2Cpp]
    public void BulletDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction)
    {
        float backMod = CalcBackModifier(position, direction);
        float sleepMod = CalcSleeperModifier();
        float pMult, stMult, slMult, bMult, eMult;

        DamageModifiers.CalcMultipliers(DamageModifiers, modifiers, backMod, sleepMod, out pMult, out stMult, out slMult, out bMult, out eMult);
        m_health -= dam * pMult * slMult * bMult;

        DamageModifiers.CalcMultipliers(TransferModifiers, modifiers, backMod, sleepMod, out pMult, out stMult, out slMult, out bMult, out eMult);
        if (DamageBase == null)
            throw new NullReferenceException("Damage base was null in DamExt_EnemyDamageLimb");
        DamageBase.BulletDamage(dam * pMult * slMult * bMult, modifiers, sourceAgent, position, direction);
    }

    public override void ExplosionDamage(float dam, UnityEngine.Vector3 sourcePos, UnityEngine.Vector3 force, uint gearCategoryId = 0)
    {
        ExplosionDamage(dam, new DamageModifiers(), null, sourcePos.ToSystem(), force.ToSystem());
    }
    [HideFromIl2Cpp]
    public void ExplosionDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 sourcePos, Vector3 force)
    {
        float backMod, sleeperMod;
        float precisionMult, backMult, sleeperMult, staggerMult;

        backMod = CalcBackModifier(sourcePos, force);
        sleeperMod = CalcSleeperModifier();

        precisionMult = MathF.Pow(DamageModifiers.PrecisionMult, modifiers.PrecisionPower) * MathF.Pow(modifiers.PrecisionMult, DamageModifiers.PrecisionPower);
        backMult = MathF.Pow(DamageModifiers.BackMult, modifiers.BackPower * backMod) * MathF.Pow(modifiers.BackMult, DamageModifiers.BackPower * backMod);
        sleeperMult = MathF.Pow(DamageModifiers.SleeperMult, modifiers.SleeperPower * sleeperMod) * MathF.Pow(modifiers.SleeperMult, DamageModifiers.SleeperPower * sleeperMod);
        //staggerMult   = MathF.Pow(DamageModifiers.StaggerMult,   modifiers.StaggerPower)              * MathF.Pow(modifiers.StaggerMult,   DamageModifiers.StaggerPower);

        m_health -= dam * precisionMult * backMult * sleeperMult;

        precisionMult = MathF.Pow(TransferModifiers.PrecisionMult, modifiers.PrecisionPower) * MathF.Pow(modifiers.PrecisionMult, TransferModifiers.PrecisionPower);
        backMult = MathF.Pow(TransferModifiers.BackMult, modifiers.BackPower * backMod) * MathF.Pow(modifiers.BackMult, TransferModifiers.BackPower * backMod);
        sleeperMult = MathF.Pow(TransferModifiers.SleeperMult, modifiers.SleeperPower * sleeperMod) * MathF.Pow(modifiers.SleeperMult, TransferModifiers.SleeperPower * sleeperMod);
        staggerMult = MathF.Pow(TransferModifiers.StaggerMult, modifiers.StaggerPower) * MathF.Pow(modifiers.StaggerMult, TransferModifiers.StaggerPower);

        DamageBase?.ExplosionDamage(dam * precisionMult * backMult * sleeperMult, modifiers, sourceAgent, sourcePos, force);
    }

    [HideFromIl2Cpp]
    public void FireDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void FreezeDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void GlueDamage(float glueAmount, DamageModifiers modifiers, Agent? sourceAgent)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void NoAirDamage(float dam, DamageModifiers modifiers)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void ParasiteDamage(float dam, DamageModifiers modifiers)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void ParasiteTrapDamage(float dam, DamageModifiers modifiers, uint staticEnemyID)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void ShooterProjectileDamage(float dam, DamageModifiers modifiers, Vector3 position)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void TentacleAttackDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void TentacleTankGrabDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void TentacleTrapGrabDamage(float dam, DamageModifiers modifiers, uint staticEnemyID)
    {
        throw new NotImplementedException();
    }
}
