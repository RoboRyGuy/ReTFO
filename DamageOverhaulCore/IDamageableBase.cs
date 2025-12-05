
using Agents;
using Il2CppInterop.Runtime.Attributes;
using System.Numerics;

namespace ReTFO.DamageOverhaulCore;

/// <summary>
/// Damage interface for a damageable agent's base
/// Damageables are organized into limbs and bases. The base represents the actual health of the
///  agent, and there is exactly one DamageableBase per object that can be damaged
/// </summary>
public interface IDamageableBase
{
    // The agent which owns this damageable (the one which will get hurt), if any
    [HideFromIl2Cpp]
    public abstract Agent? DamageAgent { get; }

    // Current health of the agent
    [HideFromIl2Cpp]
    public abstract float Health { get; }

    // Current max health of the agent
    [HideFromIl2Cpp]
    public abstract float MaxHealth { get; }

    // Current relative health of the agent
    [HideFromIl2Cpp]
    public virtual float HealthRel => Health / MaxHealth;

    // True if the agent is active and responding to damage; false otherwise
    [HideFromIl2Cpp]
    public virtual bool IsAlive => HealthRel > float.Epsilon;

    // Damage modifiers used when receiving damage
    [HideFromIl2Cpp]
    public abstract DamageModifiers DamageModifiers { get; }

    [HideFromIl2Cpp]
    public abstract void MeleeDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction);
    [HideFromIl2Cpp]
    public abstract void PushDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction);
    [HideFromIl2Cpp]
    public abstract void StaggerDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction);
    [HideFromIl2Cpp]
    public abstract void FallDamage(float dam, DamageModifiers modifiers);

    [HideFromIl2Cpp]
    public abstract void BulletDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position, Vector3 direction);
    [HideFromIl2Cpp]
    public abstract void ExplosionDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 sourcePos, Vector3 force);
    [HideFromIl2Cpp]
    public abstract void GlueDamage(float glueAmount, DamageModifiers modifiers, Agent? sourceAgent);

    [HideFromIl2Cpp]
    public abstract void TentacleAttackDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent, Vector3 position);
    [HideFromIl2Cpp]
    public abstract void TentacleTankGrabDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent);
    [HideFromIl2Cpp]
    public abstract void TentacleGrabStop();
    [HideFromIl2Cpp]
    public abstract void ShooterProjectileDamage(float dam, DamageModifiers modifiers, Vector3 position);

    //  -- (not implemented?) --
    [HideFromIl2Cpp]
    public virtual void FireDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent) => throw new NotImplementedException();
    [HideFromIl2Cpp]
    public virtual void FreezeDamage(float dam, DamageModifiers modifiers, Agent? sourceAgent) => throw new NotImplementedException();
    [HideFromIl2Cpp]
    public virtual void NoAirDamage(float dam, DamageModifiers modifiers) => throw new NotImplementedException();
    [HideFromIl2Cpp]
    public virtual void TentacleTrapGrabDamage(float dam, DamageModifiers modifiers, uint staticEnemyID) => throw new NotImplementedException();
    [HideFromIl2Cpp]
    public virtual void ParasiteDamage(float dam, DamageModifiers modifiers) => throw new NotImplementedException();
    [HideFromIl2Cpp]
    public virtual void ParasiteTrapDamage(float dam, DamageModifiers modifiers, uint staticEnemyID) => throw new NotImplementedException();
}
