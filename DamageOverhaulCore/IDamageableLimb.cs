
using Agents;
using System.Numerics;

namespace ReTFO.DamageOverhaulCore;

/// <summary>
/// Damage interface for a damageable agent's limb
/// Damageables are organized into limbs and bases. The limb is the physical presence in the world, and is expected 
///  to receive and modify damage before passing it on to its base. It may optionally track its own health as well
/// </summary>
public interface IDamageableLimb : IDamageableBase
{
    // Reference to the damage base owning this limb
    public abstract IDamageableBase? DamageBase { get; }

    // True if this limb is "broken", either by being intangible or just by being damaged
    public virtual bool IsBroken => IsAlive;

    // True if this limb is still tangible, ie if it should be possible to deal damage to this limb.
    // Commonly becomes false when the limb is broken (ie triker head being shot off)
    public virtual bool IsTangible => !IsBroken;

    // If the limb can be pierced by HEL rounds (ie using vanilla overpen mechanics)
    public virtual bool CanOverpen => IsBroken;

    // Damage modifiers used by this limb for passing damage to its base
    public abstract DamageModifiers TransferModifiers { get; }
}
