
using Agents;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.InteropTypes.Fields;
using System.Numerics;

namespace ReTFO.DamageOverhaulCore;

/// <summary>
/// Extends GTFO's Dam_EnemyDamageBase to implement IDamageableBase
/// </summary>
public class DamExt_EnemyDamageBase : Dam_EnemyDamageBase, IDamageableBase
{
    public DamExt_EnemyDamageBase(IntPtr ptr) : base(ptr) { }
    public DamExt_EnemyDamageBase() : base(ClassInjector.DerivedConstructorPointer<DamExt_EnemyDamageBase>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public override string DebugName => "DamExt_EnemyDamageBase";

    private DamageModifiers m_damageModifiers = new();

    [HideFromIl2Cpp]
    public Agent? DamageAgent => Owner;
    [HideFromIl2Cpp]
    public float MaxHealth => HealthMax;
    [HideFromIl2Cpp]
    public DamageModifiers DamageModifiers => m_damageModifiers;

    // Calc the correct back modifier from the current state and the incoming projectile info
    // Patch this method to modify behavior
    [HideFromIl2Cpp]
    public virtual float CalcBackModifier(Vector3 position, Vector3 direction)
    {
        // Same as vanilla, except vanilla adds 1 and use that as the multiplier
        return Math.Clamp(.25f + Vector3.Dot(direction.ToFlatNormal(), Owner.transform.forward.ToSystem().ToFlatNormal()), 0f, 1f);
    }

    // Calc the current sleeper modifer (0 to 1) from the current enemy state
    // Patch this method to modify behavior
    [HideFromIl2Cpp]
    public virtual float CalcSleeperModifier()
    {
        // TODO: Sleeper falloff from wakeup timer
        return Owner.AI.Mode != AgentMode.Agressive ? 1f : 0f;
    }

    public override void BulletDamage(float dam, Agent sourceAgent, UnityEngine.Vector3 position, UnityEngine.Vector3 direction, UnityEngine.Vector3 normal, bool allowDirectionalBonus = false, float staggerMulti = 1, float precisionMulti = 1, uint gearCategoryId = 0)
    {
        BulletDamage(
            dam * 1000f, // Debug
            new DamageModifiers()
            {
                PrecisionMult = precisionMulti,
                BackPower = allowDirectionalBonus ? 1f : 0f,
                StaggerMult = staggerMulti
            },
            sourceAgent,
            position.ToSystem(),
            direction.ToSystem()
        );
    }

    [HideFromIl2Cpp]
    public void BulletDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction)
    {
        float backMod = CalcBackModifier(position, direction);
        float sleepMod = CalcSleeperModifier();
        float pMult, stMult, slMult, bMult, eMult;
        DamageModifiers.CalcMultipliers(DamageModifiers, modifiers, backMod, sleepMod, out pMult, out stMult, out slMult, out bMult, out eMult);
        base.BulletDamage(dam * pMult * slMult * bMult, sourceAgent, position.ToUnity(), direction.ToUnity(), UnityEngine.Vector3.zero, false, stMult, 1f, 0u);
    }

    public override void ExplosionDamage(float dam, UnityEngine.Vector3 sourcePos, UnityEngine.Vector3 force, uint gearCategoryId = 0)
    {
        ExplosionDamage(
            dam,
            new DamageModifiers(),
            null,
            sourcePos.ToSystem(),
            force.ToSystem()
        );
    }

    [HideFromIl2Cpp]
    public void ExplosionDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 sourcePos, Vector3 force)
    {
        float backMod = CalcBackModifier(sourcePos, force);
        float sleepMod = CalcSleeperModifier();
        float pMult, stMult, slMult, bMult, eMult;
        DamageModifiers.CalcMultipliers(DamageModifiers, modifiers, backMod, sleepMod, out pMult, out stMult, out slMult, out bMult, out eMult);
        base.ExplosionDamage(dam * pMult * slMult * bMult, sourcePos.ToUnity(), force.ToUnity());
    }

    [HideFromIl2Cpp]
    public void FallDamage(float dam, DamageModifiers modifiers)
    {
        throw new NotImplementedException();
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
    public void MeleeDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction)
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
    public void PushDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void ShooterProjectileDamage(float dam, DamageModifiers modifiers, Vector3 position)
    {
        throw new NotImplementedException();
    }

    [HideFromIl2Cpp]
    public void StaggerDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction)
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
    /**/
}
