# Damage Overhaul Core

A bepinex plugin that changes how enemies take damage

## Warning

This plugin only changes how damage is dealt, and does not rebalance the game to work with that new system.

## What is different?

There are a few key differences in how damage is handled:

- Precision damage is now power-based
  - The formula is `damage *= Mathf.Pow(limb.PrecisionBase, gun.PrecisionMultiplier)`
  - Precision base is a new variable and defaults to 1. Precision multiplier is the existing multiplier, and should be changed
  - This new formula allows for weapons to more cleanly scale between ignoring precision and being very precision-strict
- Limbs now deal bonus damage when they break
  - Hurting a limb can deal reduced damage to the host, but breaking it can deal flat bonus damage to compensate
  - This encourages focusing fire on a few limbs to break them, rather than spreading damage across all limbs
  - TODO: Guns no longer stagger, stagger multipliers instead deal bonus limb break damage
- Enemies can now have flat resistance to damage
  - Flat resistance reduces all damage dealt by a flat (set) amount
    - If flat resistance is greater than the damage dealt, the damage dealt is reduced to near 0, not 0
  - This punishes machine guns (ie the machine pistol) since their low damage per shot gets almost entirely negated
  - This rewards precision shots, as the high damage per shot means the flat resistance is less impactful
- Enemies can now have soft and hard damage caps
  - The formula is a geometric interpolation from the original damage to the soft cap when above the soft cap
  - The formula is `damage = Mathf.Pow(softCap, damageFalloffPower) * Mathf.Pow(damage, 1 - damageFalloffPower)` when above the soft cap
  - The soft cap reduces damage dealt that is greater than a certain threshold
  - The hard cap is the maximum damage that can be dealt to an enemy in a single hit, and can make certain breakpoints impossible to reach
  - This can punish precision and high-damage weapons, as their damage is significantly reduced
  - It also rewards low-damage weapons, as damage below the soft cap is unaffected
  - High-damage weapons will always deal more damage than low-damage weapons (as long as the hard cap is not reached), but the difference can become negligible
- Back damage is calculated the same, and applied before the resistances
- All configs are per-limb, giving flexibility in the configuration

