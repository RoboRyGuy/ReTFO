using Agents;
using CharacterDestruction;
using Enemies;
using HarmonyLib;
using Player;
using ReTFO.DamageOverhaulCore.Data;
using UnityEngine;

namespace ReTFO.DamageOverhaulCore.HarmonyPatches;

/// <summary>
/// Harmony patch which modifies how enemies take damage
/// </summary>
internal static class DamagePatch
{
    // Reference to the plugin
    private static Plugin? _core = null;
    public static Plugin Core
    {
        get { return _core ??= Plugin.Get(); }
        private set { _core = value; }
    }

    // Reference to the plugin logger
    private static BepInEx.Logging.ManualLogSource Log => Core.Log;

    // Organizes data used by client calculation
    private struct ClientDataStruct
    {
        public ClientDataStruct() { }
#if DEBUG
        public static int NumberOfRunningEvents = 0;
#endif

        private Dam_EnemyDamageLimb? _targetLimb = null;
        public Dam_EnemyDamageLimb TargetLimb
        {
            readonly get { return _targetLimb ?? throw new NullReferenceException("ClientDataStruct was not provided with a TargetLimb"); }
            set { _targetLimb = value; }
        }
        private EnemyDamageData? _enemyData = null;
        public EnemyDamageData EnemyData
        {
            readonly get { return _enemyData ?? throw new NullReferenceException("ClientDataStruct was not provided with EnemyData"); }
            set { _enemyData = value; }
        }
        public readonly LimbDamageData LimbData => EnemyData.LimbDamageDatas[TargetLimb.m_limbID];
        public WeaponDamageData WeaponData = WeaponDamageData.Default;
        float _precisionPower = WeaponDamageData.Default.PrecisionPower;
        public float PrecisionPower         // Shortcut to get the correct precision power
        {
            readonly get { return _precisionPower == WeaponDamageData.Default.PrecisionPower ? WeaponData.PrecisionPower : _precisionPower; }
            set { _precisionPower = value; }
        }
        float _sleepingPower = WeaponDamageData.Default.SleepingPower;
        public float SleepingPower          // Shortcut to get the correct sleeping power
        {
            readonly get { return _sleepingPower == WeaponDamageData.Default.SleepingPower ? WeaponData.SleepingPower : _sleepingPower; }
            set { _sleepingPower = value; }
        }
        float _backstabPower = WeaponDamageData.Default.BackstabPower;
        public float BackstabPower          // Shortcut to get the correct backstab power
        {
            readonly get { return _backstabPower == WeaponDamageData.Default.BackstabPower ? WeaponData.BackstabPower : _backstabPower; }
            set { _backstabPower = value; }
        }
        public Vector3 AttackDirection = Vector3.zeroVector; // Direction of the attack, used for backstab calculations
        public uint GearCategoryId = 0;     // Gear category of the weapon, for patching a network bug
        public float InitialDamage = 0f;    // Initial damage, before modifiers, used when displaying hit markers
        public float LimbDamage = 0f;       // Damage dealt to the limb, before transmission to host, used when displaying hit markers
    }

    // Types of host events, for tracking / caching damage calculations
    private enum HostEventType
    {
        None,
        LocalPendingCalc,
        LocalWithCalc,
        RemotePendingCalc,
        RemoteWithCalc,
    }

    // Organizes data used by host calculations
    private struct HostDataStruct
    {
        public HostDataStruct() { }
#if DEBUG
        public static int NumberOfRunningEvents = 0;
#endif
        public HostEventType EventType = HostEventType.None;

        private EnemyDamageData? _enemyData = null;
        public EnemyDamageData EnemyData
        {
            readonly get { return _enemyData ?? throw new NullReferenceException("HostDataStruct was not provided with EnemyData"); }
            set { _enemyData = value; }
        }
        public WeaponDamageData WeaponData = WeaponDamageData.Default;
        float _precisionPower = WeaponDamageData.Default.PrecisionPower;
        public float PrecisionPower         // Shortcut to get the correct precision power
        {
            readonly get { return _precisionPower == WeaponDamageData.Default.PrecisionPower ? WeaponData.PrecisionPower : _precisionPower; }
            set { _precisionPower = value; }
        }
        float _sleepingPower = WeaponDamageData.Default.SleepingPower;
        public float SleepingPower          // Shortcut to get the correct sleeping power
        {
            readonly get { return _sleepingPower == WeaponDamageData.Default.SleepingPower ? WeaponData.SleepingPower : _sleepingPower; }
            set { _sleepingPower = value; }
        }
        float _backstabPower = WeaponDamageData.Default.BackstabPower;
        public float BackstabPower          // Shortcut to get the correct backstab power
        {
            readonly get { return _backstabPower == WeaponDamageData.Default.BackstabPower ? WeaponData.BackstabPower : _backstabPower; }
            set { _backstabPower = value; }
        }
        public Vector3 AttackDirection = Vector3.zeroVector;
        public float LimbDamage = 0f;
        public float FinalDamage = 0f;
    }

    // Data used by the client half of calculations
    private static ClientDataStruct ClientData = new();

    // Data used by the host half of calculations
    private static HostDataStruct HostData = new();

    // Helper, this will later check the config before logging
    private static void LogDamageStep(string message)
    {
        Log.LogDebug(message);
    }

    // This version of Pow will provide more consistency with negative/zero values
    private static float Pow(float b, float p)
    {
        if (b == 0f) return 0f;
        else if (p == 0f) return Mathf.Sign(b);
        else if (b > 0) return Mathf.Pow(b, p);
        else return -Mathf.Pow(-b, p);
    }

    // Applies roundup on networked floats, by default GTFO rounds down when reducing precision
    private static void ApplyRoundup(ref float value, float maxValue)
    {
        SNetwork.UFloat16 converter = new();
        converter.Set(value, maxValue);
        if (converter.Get(maxValue) < value && converter.internalValue != ushort.MaxValue)
        {
            converter.internalValue += 1;
            value = converter.Get(maxValue);
        }
    }

    /* == On hit, this is the general call order ======================================================================
     * Limb.BulletDamage                Note: The call orders are similar for Melee and Explosion damage
     *   Limb.ApplyWeakspotAndArmor
     *   Limb.ApplyBackdamage
     *   <Sleeping Mult, melee only>    Note: This is not a separate function, but an inline call
     *   Base.BulletDamage              Note: This seems to just push the call to the server (Network RPC?)
     *     Base.ReceiveBulletDamage
     *       Limb.DoDamage, Limb.DestroyLimb    Note: DoDamage cannot be patched (likely inlined)
     *       Base.ProcessReceivedDamage
     *   Limb.ShowHitIndicator
     */

    /* == Armor and bonuses order =====================================================================================
     * Start with weapon base damage
     *  + Precision bonus
     *  + Sleeping bonus
     *  + Backstab bonus
     *  - Limb flat armor (including piercing, if applicable)
     *  - Limb falloff armor
     *  + Networking roundup (~.01 damage)
     * Damage is dealt to limb
     *  + Transfer precision bonus
     *  + Transfer sleeping bonus
     *  + Transfer backstab bonus
     *  - Transfer damage mult
     *  - Base flat armor (including piercing, if applicable)
     *  - Base falloff armor
     *  + Limb break damage
     * Damage is dealt to base
     *  + Stagger multiplier
     * Stagger damage is dealt to base
     * 
     * The below functions roughly follow the call order, as needed
     */

    // Set up enemies to work with our damage system
    [HarmonyPatch(typeof(EnemyAgent), nameof(EnemyAgent.Setup))]
    [HarmonyPostfix]
    internal static void PostfixEnemySetup(EnemyAgent __instance)
    {
        EnemyDamageData data = Core.GetEnemyDamageData(__instance.Damage);

        if (__instance.Damage.DamageLimbs.Count != data.LimbDamageDatas.Length)
        {
            Log.LogError($"Enemy {__instance.EnemyData.name} has {__instance.Damage.DamageLimbs.Count} limbs, but its damage data has {data.LimbDamageDatas.Length} limb damage datas. Skipping setup!");
            return;
        }

        __instance.Damage.Health = __instance.Damage.GetHealthRel() * data.MaxHealth;
        __instance.Damage.HealthMax = data.MaxHealth;
        __instance.EnemyBalancingData._AllowDamgeBonusFromBehind_k__BackingField = true;

        var zip = Enumerable.Zip(
            __instance.Damage.DamageLimbs,
            data.LimbDamageDatas,
            (l, d) => new { limb = l, data = d }
        );

        foreach (var pair in zip)
        {
            pair.limb.m_health = pair.limb.GetHealthRel() * pair.data.MaxHealth;
            pair.limb.m_healthMax = pair.data.MaxHealth;
            pair.limb.m_type = eLimbDamageType.Weakspot;
            pair.limb.m_weakspotDamageMulti = 1f;
            pair.limb.m_armorDamageMulti = 1f;

            Dam_EnemyDamageLimb_Custom? custom = pair.limb.TryCast<Dam_EnemyDamageLimb_Custom>();
            if (custom != null)
                custom.m_clampDamageToLimbMaxHealth = false;
        }
    }

    #region ClientData Setup

    // Set up client event data
    [HarmonyPatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.BulletDamage))]
    [HarmonyPrefix]
    internal static void PrefixLimbBulletDamage(Dam_EnemyDamageLimb __instance, Agent sourceAgent, Vector3 direction, float dam, float precisionMulti, ref bool allowDirectionalBonus, uint gearCategoryId)
    {
#if DEBUG
        ClientDataStruct.NumberOfRunningEvents += 1;
        if (ClientDataStruct.NumberOfRunningEvents > 1)
            Log.LogError($"Running {ClientDataStruct.NumberOfRunningEvents} client damage events at once. Unexpected behavior!");
#endif
        WeaponDamageData? weaponData = null;
        PlayerAgent? player = sourceAgent?.TryCast<PlayerAgent>();
        if (player != null)
        {
            if (gearCategoryId != 0)
            {
                if (PlayerBackpackManager.TryGetItem(player.Owner, InventorySlot.GearClass, out BackpackItem item))
                {
                    ItemEquippable? equip = item.Instance.TryCast<ItemEquippable>();
                    if (equip != null)
                        weaponData = Core.GetGunDamageData(equip.ArchetypeID);
                    else
                        Log.LogWarning("Failed to get ItemEquippable (Sentry) from BackpackItem");
                }
                else
                    Log.LogWarning("Failed to get Tool (Sentry) item from player backpack");
            }
            else
                weaponData = Core.GetGunDamageData(player.Inventory.WieldedItem.ArchetypeID);
        }
        else
            Log.LogWarning("Failed to get PlayerAgent from sourceAgent");

        ClientData = new()
        {
            TargetLimb = __instance,
            EnemyData = Core.GetEnemyDamageData(__instance.m_base),
            WeaponData = weaponData ?? WeaponDamageData.Default,
            GearCategoryId = gearCategoryId,
            PrecisionPower = precisionMulti,
            InitialDamage = dam,
            AttackDirection = direction,
        };

        allowDirectionalBonus = true;
        LogDamageStep($"Dealing {ClientData.InitialDamage} bullet damage to {__instance.m_base.Owner.EnemyData.name} - {__instance.name}");
    }

    // Set up client event data
    [HarmonyPatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.MeleeDamage))]
    [HarmonyPrefix]
    internal static void PrefixMeleeDamage(Dam_EnemyDamageLimb __instance, Agent sourceAgent, Vector3 direction, float dam, float precisionMulti, float backstabberMulti, ref float sleeperMulti, float staggerMulti, uint gearCategoryId)
    {
#if DEBUG
        ClientDataStruct.NumberOfRunningEvents += 1;
        if (ClientDataStruct.NumberOfRunningEvents > 1)
            Log.LogError($"Running {ClientDataStruct.NumberOfRunningEvents} client damage events at once. Unexpected behavior!");
#endif
        WeaponDamageData? weaponData = null;
        PlayerAgent? player = sourceAgent?.TryCast<PlayerAgent>();
        if (player != null)
            weaponData = Core.GetMeleeDamageData(player.Inventory.WieldedItem.MeleeArchetypeData.persistentID);
        else
            Log.LogWarning("Failed to get PlayerAgent from sourceAgent");

        ClientData = new()
        {
            TargetLimb = __instance,
            EnemyData = Core.GetEnemyDamageData(__instance.m_base),
            WeaponData = weaponData ?? WeaponDamageData.Default,
            PrecisionPower = precisionMulti,
            BackstabPower = backstabberMulti,
            SleepingPower = sleeperMulti,
            GearCategoryId = gearCategoryId,
            InitialDamage = dam,
            AttackDirection = direction,
        };

        sleeperMulti = 1f; // We can't stop GTFO from using this, so make it negligible
        LogDamageStep($"Dealing {ClientData.InitialDamage} melee damage to {__instance.m_base.Owner.EnemyData.name} - {__instance.name}");
    }

    // Set up client event data
    [HarmonyPatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.ExplosionDamage))]
    [HarmonyPrefix]
    internal static void PrefixExplosionDamage(Dam_EnemyDamageLimb __instance, Vector3 force, float dam, uint gearCategoryId)
    {
#if DEBUG
        ClientDataStruct.NumberOfRunningEvents += 1;
        if (ClientDataStruct.NumberOfRunningEvents > 1)
            Log.LogError($"Running {ClientDataStruct.NumberOfRunningEvents} client damage events at once. Unexpected behavior!");
#endif
        ClientData = new()
        {
            TargetLimb = __instance,
            EnemyData = Core.GetEnemyDamageData(__instance.m_base),
            WeaponData = Core.GetExplosiveDamageData(gearCategoryId),
            GearCategoryId = gearCategoryId,
            InitialDamage = dam,
            AttackDirection = force
        };

        LogDamageStep($"Dealing {ClientData.InitialDamage} explosion damage to {__instance.m_base.Owner.EnemyData.name} - {__instance.name}");
    }
    #endregion

    // Apply armor calculations only
    [HarmonyPatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.ApplyWeakspotAndArmorModifiers))]
    [HarmonyPrefix, HarmonyPriority(Priority.Low)]
    internal static bool ApplyWeakspotAndArmorModifiers(Dam_EnemyDamageLimb __instance, float dam, ref float __result)
    {
        // Precision bonus
        float precisionMult = Pow(ClientData.LimbData.PrecisionBase, ClientData.PrecisionPower);
        __result = dam * precisionMult;
        if (precisionMult != 1f)
            LogDamageStep($" - Precision damage: {__result}, precision mult: {precisionMult}");

        // Sleeping bonus
        if (__instance.m_base.Owner.AI.Mode != AgentMode.Agressive)
        {
            float sleepingMult = ClientData.LimbData.SleepingBase == 0 ? 0f : Pow(ClientData.LimbData.SleepingBase, ClientData.PrecisionPower);
            __result *= sleepingMult;
            if (sleepingMult != 1f)
                LogDamageStep($" - Sleeping damage: {__result}, sleeping mult: {sleepingMult}");
        }
        
        return false; // Do not do original calculation
    }

    // New backstab calcuation
    [HarmonyPatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.ApplyDamageFromBehindBonus))]
    [HarmonyPrefix, HarmonyPriority(Priority.Low)]
    internal static bool ApplyDamageFromBehindBonus(Dam_EnemyDamageLimb __instance, float dam, Vector3 dir, ref float __result)
    {
        float mult = Mathf.Clamp(.25f + Vector3.Dot(dir.Flat().normalized, __instance.m_base.Owner.transform.forward.Flat().normalized), 0, 1);
        float backMult = Pow(ClientData.LimbData.BackstabBase, ClientData.BackstabPower * mult);
        __result = dam * backMult;
        if (__result != dam)
            LogDamageStep($" - Back damage: {__result}, backMult: {backMult}, angleMult: {mult}");

        // Armors
        dam = __result;
        __result = ClientData.LimbData.FlatResistance.ApplyResistance(__result, ClientData.WeaponData.Puncturing, out float punctureUsed);
        if (__result != dam || punctureUsed != 0)
            LogDamageStep($" - Flattened damage: {__result} ({dam - __result} damage flattened, {punctureUsed} punctured)");

        dam = __result;
        __result = ClientData.LimbData.FalloffResistance.ApplyResistance(__result, ClientData.WeaponData.FalloffPower);
        if (__result != dam)
            LogDamageStep($" - Falloff damage: {__result} ({dam - __result} damage fell off)");

        return false; // Do not do original calulation
    }

    #region Networking Fixes

    /* There are three networking issues to patch
     *  - The GearCategoryId is always 0 (the devs probably forgot to pass it to the function, so it defaults)
     *  - Damage is compressed and capped when sent over the network, which we don't want
     *  - Float values are rounded down when compressed, which can prevent precise breakpoints from being hit
     *  To correct these issues, we swap the gearCategory and damage fields prior to network transmission,
     *   and swap them back on the host's side to make things appear consistent.
     *  We also roundup floating point values
     *  
     *  Notes:
     *   - Despite what is said, data.damage is a UFloat16, not an SFloat16
     *   - I've made it so data.damage is rounded up instead of down, so precise breakpoints are hit
     *   - gearCategoryId is capped to 16 bits in this process, as opposed to its normal 32 bits
     *   - Patching priorities are set to try and minimize disruptions to other patches. Try and avoid 
     *     using VeryLow and VeryHigh priorities if you need to interoperate with this plugin
     *   - These patches change base.HealthMax in order to raise base.DamageMax in the ReceiveXDamage
     *     functions, which can impact limb destructions and more. Be aware (and cautious) of this
     */

    // These three functions perform the swap

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.BulletDamage))]
    [HarmonyPrefix, HarmonyPriority(Priority.VeryLow)]
    internal static void PrefixBaseBulletDamage_NetworkFix(Dam_EnemyDamageBase __instance, ref float dam, ref float staggerMulti, ref float precisionMulti, ref uint gearCategoryId)
    {
        ClientData.LimbDamage = dam;
        gearCategoryId = ClientData.GearCategoryId;
        LogDamageStep($" Damage to {ClientData.TargetLimb.name}: {dam}, new health: "
            + $"{(ClientData.TargetLimb.m_health > 100_000_000 ? "inf" : (ClientData.TargetLimb.m_health - dam).ToString())}/{(ClientData.TargetLimb.m_healthMax > 100_000_000 ? "inf" : ClientData.TargetLimb.m_healthMax.ToString())}");
        
        SNetwork.UFloat16 converter = new();
        converter.internalValue = (ushort)gearCategoryId;
        gearCategoryId = BitConverter.SingleToUInt32Bits(dam);
        dam = converter.Get(__instance.DamageMax);

        ApplyRoundup(ref staggerMulti, 10f);
        ApplyRoundup(ref precisionMulti, 10f);
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.MeleeDamage))]
    [HarmonyPrefix, HarmonyPriority(Priority.VeryLow)]
    internal static void PrefixBaseMeleeDamage_NetworkFix(Dam_EnemyDamageBase __instance, ref float dam, ref float staggerMulti, ref float precisionMulti, ref float backstabberMulti, ref float sleeperMulti, ref uint gearCategoryId)
    {
        ClientData.LimbDamage = dam;
        sleeperMulti = ClientData.SleepingPower; // We changed this to 1 earlier, we need to restore so the host gets the right number
        gearCategoryId = ClientData.GearCategoryId;
        LogDamageStep($" Damage to {ClientData.TargetLimb.name}: {dam}, new health: "
            + $"{(ClientData.TargetLimb.m_health > 100_000_000 ? "inf" : (ClientData.TargetLimb.m_health - dam).ToString())}/{(ClientData.TargetLimb.m_healthMax > 100_000_000 ? "inf" : ClientData.TargetLimb.m_healthMax.ToString())}");

        SNetwork.UFloat16 converter = new();
        converter.internalValue = (ushort)gearCategoryId;
        gearCategoryId = BitConverter.SingleToUInt32Bits(dam);
        dam = converter.Get(__instance.DamageMax);

        ApplyRoundup(ref staggerMulti , 10f);
        ApplyRoundup(ref precisionMulti , 10f);
        ApplyRoundup(ref backstabberMulti, 10f);
        ApplyRoundup(ref sleeperMulti, 10f);
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ExplosionDamage))]
    [HarmonyPrefix, HarmonyPriority(Priority.VeryLow)]
    internal static void PrefixBaseExplosionDamage_NetworkFix(Dam_EnemyDamageBase __instance, ref float dam, ref uint gearCategoryId)
    {
        ClientData.LimbDamage = dam;
        gearCategoryId = ClientData.GearCategoryId;
        LogDamageStep($" Damage to {ClientData.TargetLimb.name}: {dam}, new health: "
            + $"{(ClientData.TargetLimb.m_health > 100_000_000 ? "inf" : (ClientData.TargetLimb.m_health - dam).ToString())}/{(ClientData.TargetLimb.m_healthMax > 100_000_000 ? "inf" : ClientData.TargetLimb.m_healthMax.ToString())}");

        SNetwork.UFloat16 converter = new();
        converter.internalValue = (ushort)gearCategoryId;
        gearCategoryId = BitConverter.SingleToUInt32Bits(dam);
        dam = converter.Get(__instance.DamageMax);
    }

    // These three undo the swap in case any other patches reference it - if there are no other postfixes, this has no effect

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.BulletDamage))]
    [HarmonyPostfix, HarmonyPriority(Priority.VeryHigh)]
    internal static void PostfixBaseBulletDamage_NetworkFix(Dam_EnemyDamageBase __instance, ref float dam, ref uint gearCategoryId)
    {
        SNetwork.UFloat16 converter = new();
        converter.Set(dam, __instance.DamageMax);
        dam = BitConverter.UInt32BitsToSingle(gearCategoryId);
        ApplyRoundup(ref dam, __instance.DamageMax);
        gearCategoryId = converter.internalValue;
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.MeleeDamage))]
    [HarmonyPostfix, HarmonyPriority(Priority.VeryHigh)]
    internal static void PostfixBaseMeleeDamage_NetworkFix(Dam_EnemyDamageBase __instance, ref float dam, ref uint gearCategoryId)
    {
        SNetwork.UFloat16 converter = new();
        converter.Set(dam, __instance.DamageMax);
        dam = BitConverter.UInt32BitsToSingle(gearCategoryId);
        ApplyRoundup(ref dam, __instance.DamageMax);
        gearCategoryId = converter.internalValue;
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ExplosionDamage))]
    [HarmonyPostfix, HarmonyPriority(Priority.VeryHigh)]
    internal static void PostfixBaseExplosionDamage_NetworkFix(Dam_EnemyDamageBase __instance, ref float dam, ref uint gearCategoryId)
    {
        SNetwork.UFloat16 converter = new();
        converter.Set(dam, __instance.DamageMax);
        dam = BitConverter.UInt32BitsToSingle(gearCategoryId);
        ApplyRoundup(ref dam, __instance.DamageMax);
        gearCategoryId = converter.internalValue;
    }

    // These functions undo the swap on the host's side, and change HealthMax to allow bigger values to be stored as damage

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveBulletDamage))]
    [HarmonyPrefix, HarmonyPriority(Priority.VeryHigh)]
    internal static void PrefixReceiveBulletDamage_NetworkFix(Dam_EnemyDamageBase __instance, ref pBulletDamageData data)
    {
        float actualDamage = BitConverter.UInt32BitsToSingle(data.gearCategoryId);
        data.gearCategoryId = data.damage.internalValue;
        __instance.HealthMax = Mathf.Max(actualDamage, __instance.HealthMax);

        SNetwork.UFloat16 converter = new();
        converter.Set(actualDamage, __instance.HealthMax); // Not sure where this 2x comes from
        if (converter.internalValue != ushort.MaxValue) 
            converter.internalValue += 1;
        data.damage.internalValue = converter.internalValue;
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveMeleeDamage))]
    [HarmonyPrefix, HarmonyPriority(Priority.VeryHigh)]
    internal static void PrefixReceiveMeleeDamage_NetworkFix(Dam_EnemyDamageBase __instance, ref pFullDamageData data)
    {
        float actualDamage = BitConverter.UInt32BitsToSingle(data.gearCategoryId);
        data.gearCategoryId = data.damage.internalValue;
        __instance.HealthMax = Mathf.Max(actualDamage, __instance.HealthMax);

        SNetwork.UFloat16 converter = new();
        converter.Set(actualDamage, __instance.DamageMax);
        if (converter.internalValue != ushort.MaxValue)
            converter.internalValue += 1;
        data.damage.internalValue = converter.internalValue;
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveExplosionDamage))]
    [HarmonyPrefix, HarmonyPriority(Priority.VeryHigh)]
    internal static void PrefixReceiveExplosionDamage_NetworkFix(Dam_EnemyDamageBase __instance, ref pExplosionDamageData data)
    {
        float actualDamage = BitConverter.UInt32BitsToSingle(data.gearCategoryId);
        data.gearCategoryId = data.damage.internalValue;
        __instance.HealthMax = Mathf.Max(actualDamage, __instance.HealthMax);

        SNetwork.UFloat16 converter = new();
        converter.Set(actualDamage, __instance.DamageMax);
        if (converter.internalValue != ushort.MaxValue)
            converter.internalValue += 1;
        data.damage.internalValue = converter.internalValue;
    }

    // These three functions undo the change to HealthMax - no need to swap the damage and gear data again

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveBulletDamage))]
    [HarmonyPostfix, HarmonyPriority(Priority.VeryLow)]
    internal static void PostfixReceiveBulletDamage_NetworkFix(Dam_EnemyDamageBase __instance)
    {
        __instance.HealthMax = HostData.EnemyData.MaxHealth;
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveMeleeDamage))]
    [HarmonyPostfix, HarmonyPriority(Priority.VeryLow)]
    internal static void PostfixReceiveMeleeDamage_NetworkFix(Dam_EnemyDamageBase __instance)
    {
        __instance.HealthMax = HostData.EnemyData.MaxHealth;
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveExplosionDamage))]
    [HarmonyPostfix, HarmonyPriority(Priority.VeryLow)]
    internal static void PostfixReceiveExplosionDamage_NetworkFix(Dam_EnemyDamageBase __instance)
    {
        __instance.HealthMax = HostData.EnemyData.MaxHealth;
    }

    #endregion

    #region HostData Setup

    // Set up host event data
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveBulletDamage))]
    [HarmonyPrefix]
    internal static void PrefixReceiveBulletDamage(Dam_EnemyDamageBase __instance, pBulletDamageData data)
    {
#if DEBUG
        HostDataStruct.NumberOfRunningEvents += 1;
        if (HostDataStruct.NumberOfRunningEvents > 1)
            Log.LogError($"Running {HostDataStruct.NumberOfRunningEvents} host damage events at once. Unexpected behavior!");
#endif
        WeaponDamageData? weaponData = null;
        bool isLocalEvent = false;
        if (data.source.TryGet(out Agent sourceAgent))
        {
            PlayerAgent? player = sourceAgent.TryCast<PlayerAgent>();
            if (player != null)
            {
                isLocalEvent = player.IsLocallyOwned;
                if (data.gearCategoryId != 0)
                {
                    if (PlayerBackpackManager.TryGetItem(player.Owner, InventorySlot.GearClass, out BackpackItem item))
                    {
                        ItemEquippable? equip = item.Instance.TryCast<ItemEquippable>();
                        if (equip != null)
                            weaponData = Core.GetGunDamageData(equip.ArchetypeID);
                        else
                            Log.LogError("Failed to get ItemEquippable (Sentry) from BackpackItem");
                    }
                    else
                        Log.LogWarning("Failed to get Tool (Sentry) item from player backpack");
                }
                else
                    weaponData = Core.GetGunDamageData(player.Inventory.WieldedItem.ArchetypeID);
            }
            else
                Log.LogWarning("Failed to get PlayerAgent from sourceAgent");
        }
        else
            Log.LogWarning("Failed to get sourceAgent from networking data!");

        HostData = new()
        {
            WeaponData = weaponData ?? WeaponDamageData.Default,
            EnemyData = Core.GetEnemyDamageData(__instance),
            EventType = isLocalEvent ? HostEventType.LocalPendingCalc : HostEventType.RemotePendingCalc,
            LimbDamage = data.damage.Get(__instance.HealthMax), // Still not sure why this is needed
            PrecisionPower = data.precisionMulti.Get(10f),
            AttackDirection = data.direction.Value,
        };
    }

    // Set up host event data
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveMeleeDamage))]
    [HarmonyPrefix]
    internal static void PrefixReceiveMeleeDamage(Dam_EnemyDamageBase __instance, pFullDamageData data)
    {
#if DEBUG
        HostDataStruct.NumberOfRunningEvents += 1;
        if (HostDataStruct.NumberOfRunningEvents > 1)
            Log.LogError($"Running {HostDataStruct.NumberOfRunningEvents} host damage events at once. Unexpected behavior!");
#endif
        WeaponDamageData? weaponData = null;
        bool isLocalEvent = false;
        if (data.source.TryGet(out Agent sourceAgent))
        {
            PlayerAgent? player = sourceAgent?.TryCast<PlayerAgent>();
            if (player != null)
            {
                weaponData = Core.GetMeleeDamageData(player.Inventory.WieldedItem.MeleeArchetypeData.persistentID);
                isLocalEvent = player.IsLocallyOwned;
            }
            else
                Log.LogWarning("Failed to get PlayerAgent from sourceAgent");
        }
        else
            Log.LogWarning("Failed to get sourceAgent from networking data!");

        HostData = new()
        {
            WeaponData = weaponData ?? WeaponDamageData.Default,
            EnemyData = Core.GetEnemyDamageData(__instance),
            EventType = isLocalEvent ? HostEventType.LocalPendingCalc : HostEventType.RemotePendingCalc,
            LimbDamage = data.damage.Get(__instance.DamageMax),
            PrecisionPower = data.precisionMulti.Get(10f),
            SleepingPower = data.sleeperMulti.Get(10f),
            BackstabPower = data.backstabberMulti.Get(10f),
            AttackDirection = data.direction.Value,
        };
        data.backstabberMulti.Set(1f, 10f);
    }

    // Set up host event data
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveExplosionDamage))]
    [HarmonyPrefix]
    internal static void PrefixReceiveExplosionDamage(Dam_EnemyDamageBase __instance, pExplosionDamageData data)
    {
#if DEBUG
        HostDataStruct.NumberOfRunningEvents += 1;
        if (HostDataStruct.NumberOfRunningEvents > 1)
            Log.LogError($"Running {HostDataStruct.NumberOfRunningEvents} host damage events at once. Unexpected behavior!");
#endif
        HostData = new()
        {
            WeaponData = Core.GetExplosiveDamageData(data.gearCategoryId),
            EnemyData = Core.GetEnemyDamageData(__instance),
            EventType = HostEventType.RemotePendingCalc, // We make an assumption
            LimbDamage = data.damage.Get(__instance.DamageMax),
            AttackDirection = data.force.vector.Value,
        };
    }
    #endregion

    // Calculates damage done to the base from limb damage and relevant limb info
    //  bool isPrediction -> if true, don't cache the result. Also helps pick whether to report or not
    private static float CalcBaseDamage(Dam_EnemyDamageBase __instance, float damage, int limbID, bool isPrediction)
    {
        Dam_EnemyDamageLimb limb = __instance.DamageLimbs[limbID];
        EnemyDamageData enemyData;
        LimbDamageData limbData;
        WeaponDamageData weaponData;
        float precisionPower;
        float sleepingPower;
        float backstabPower;
        Vector3 attackDirection;

        // Check if we're logging info
        bool doReports = false;

        switch (HostData.EventType)
        {
            case HostEventType.LocalWithCalc:
            case HostEventType.RemoteWithCalc:
                return HostData.FinalDamage;

            case HostEventType.None:
                doReports = true;
                LogDamageStep($" - Predicting networked damage: {damage} (cap: {__instance.DamageMax})");
                enemyData = ClientData.EnemyData;
                limbData = ClientData.LimbData;
                weaponData = ClientData.WeaponData;
                precisionPower = ClientData.PrecisionPower;
                sleepingPower = ClientData.SleepingPower;
                backstabPower = ClientData.BackstabPower;
                attackDirection = ClientData.AttackDirection;
                break;

            case HostEventType.LocalPendingCalc:
                if (!isPrediction)
                {
                    doReports = true;
                    LogDamageStep($" - Networked damage: {damage} (cap: {__instance.DamageMax})");
                }
                goto case HostEventType.RemotePendingCalc;
            
            case HostEventType.RemotePendingCalc:
                enemyData = HostData.EnemyData;
                limbData = enemyData.LimbDamageDatas[limbID];
                weaponData = HostData.WeaponData;
                precisionPower = HostData.PrecisionPower;
                sleepingPower = HostData.SleepingPower;
                backstabPower = HostData.BackstabPower;
                attackDirection = HostData.AttackDirection;
                break;

            default:
                throw new NotImplementedException($"Cannot handle host event type: {HostData.EventType}");
        }

        // When limbs take damage is rather inconsistent, I think these checks are right
        bool limbBreak;
        if (__instance.WillDamageKill(damage) || (limb.m_health == limb.m_healthMax))
            limbBreak = (limb.m_health < damage) && (limb.m_health > 0);
        else
            limbBreak = (limb.m_health < 0) && (limb.m_health > -damage);

        // Transfer precision bonus
        float precisionMult = Pow(limbData.TransferPrecisionBase, precisionPower);
        damage *= precisionMult;
        if (doReports && precisionMult != 1f) 
            LogDamageStep($" - Precision damage: {damage}, precision mult: {precisionMult}");

        // Transfer sleeping bonus
        if (__instance.Owner.AI.Mode != AgentMode.Agressive && sleepingPower != 0)
        {
            float sleepingMult = limbData.TransferSleepingBase == 0 ? 0f : Pow(limbData.TransferSleepingBase, sleepingPower);
            damage *= sleepingMult;
            if (doReports && sleepingMult != 1f)
                LogDamageStep($" - Sleeping damage: {damage}, sleeping mult: {sleepingMult}");
        }

        // Transfer backstab bonus
        float mult = Mathf.Clamp(.25f + Vector3.Dot(attackDirection.Flat().normalized, __instance.Owner.transform.forward.Flat().normalized), 0, 1);
        float backMult = Pow(ClientData.LimbData.BackstabBase, ClientData.BackstabPower * mult);
        damage *= backMult;
        if (backMult != 1f)
            LogDamageStep($" - Back damage: {damage}, backMult: {backMult}, angleMult: {mult}");


        // Damage transfer
        damage *= enemyData.LimbDamageDatas[limbID].TransferMultiplier;
        if (doReports && enemyData.LimbDamageDatas[limbID].TransferMultiplier != 1f)
            LogDamageStep($" - Transfer damage: {damage}, transfer mult: {enemyData.LimbDamageDatas[limbID].TransferMultiplier}");

        // Flat resistance
        float dam = damage;
        enemyData.LimbDamageDatas[limbID].FlatResistance.ApplyResistance(damage, weaponData.Puncturing, out float puncturingUsed); // Get piercing used already
        damage = enemyData.FlatResistance.ApplyResistance(damage, weaponData.Puncturing - puncturingUsed, out puncturingUsed);
        if (doReports && dam != damage || puncturingUsed != 0)
            LogDamageStep($" - Flattened damage: {damage} ({dam - damage} damage flattened, {puncturingUsed} punctured)");

        // Falloff resistance
        dam = damage;
        damage = enemyData.FalloffResistance.ApplyResistance(damage, weaponData.FalloffPower);
        if (doReports && dam != damage)
            LogDamageStep($" - Falloff damage: {damage} ({dam - damage} damage fell off)");

        // Limb breaks!
        if (limbBreak)
        {
            damage += enemyData.LimbDamageDatas[limbID].OnBreakDamage;
            if (doReports) LogDamageStep($" - Limb break: {damage} (+{enemyData.LimbDamageDatas[limbID].OnBreakDamage})");
        }

        if (doReports)
            LogDamageStep($" Damage to base: {damage}, new health: {(__instance.Health > 100_000_000 ? "inf" : (__instance.Health - damage).ToString())}/{(__instance.HealthMax > 100_000_000 ? "inf" : __instance.HealthMax.ToString())}\n");

        return damage;
    }

    // Handles base damage recalculations and resistances
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ProcessReceivedDamage))]
    [HarmonyPrefix]
    internal static void PrefixProcessReceivedDamage(Dam_EnemyDamageBase __instance, ref float damage, int limbID)
    {
        damage = CalcBaseDamage(__instance, damage, limbID, false);
    }

    // Stores the damage done to the base, so we can use it later. It's a postfix to try and be as compatible with other patches as possible
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ProcessReceivedDamage))]
    [HarmonyPostfix]
    internal static void PostfixProcessReceivedDamage(Dam_EnemyDamageBase __instance, float damage)
    {
        if (HostData.EventType == HostEventType.LocalPendingCalc)
        {
            HostData.FinalDamage = damage;
            HostData.EventType = HostEventType.LocalWithCalc;
        }
        else if (HostData.EventType == HostEventType.RemotePendingCalc)
        {
            HostData.FinalDamage = damage;
            HostData.EventType = HostEventType.RemoteWithCalc;
        }
    }

    // Makes limb breaks more consistent, lowering severity from death to severe if damage won't actually kill
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.CheckDestruction))]
    [HarmonyPrefix, HarmonyPriority(Priority.High)]
    internal static void PrefixCheckDestruction(Dam_EnemyDamageBase __instance, Dam_EnemyDamageLimb limb, int limbID, ref CD_DestructionSeverity severity, ref bool tryForceHitreact, ref ES_HitreactType hitreact)
    {
        if (limb.IsDestroyed) return; // save some calculation time

        // I'm taking a guess at how the game calculates severity levels
        CD_DestructionSeverity oldSeverity = severity;
        float predictedDamage = CalcBaseDamage(__instance, HostData.LimbDamage, limbID, true);
        if (__instance.WillDamageKill(predictedDamage)) severity = CD_DestructionSeverity.Death;
        else severity = CD_DestructionSeverity.Severe;
        // The game doesn't seem to use "Wound", or at least I haven't seen it

        LogDamageStep($" - Destruction: {severity} (was {oldSeverity}), hitReact: {hitreact}{(tryForceHitreact ? "!" : "?")}");
    }

    // Makes hit indicators more accurate and useful (since technically everything is an armored weakpoint now)
    [HarmonyPatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.ShowHitIndicator))]
    [HarmonyPrefix, HarmonyPriority(Priority.High)]
    internal static void PrefixShowHitIndicator(Dam_EnemyDamageLimb __instance, ref bool hitWeakspot, ref bool willDie, ref bool hitArmor)
    {
        float predictedDamage = CalcBaseDamage(__instance.m_base, ClientData.LimbDamage, __instance.m_limbID, true);

        hitWeakspot = predictedDamage > ClientData.InitialDamage * 1.25f;
        hitArmor    = predictedDamage < ClientData.InitialDamage * .5f;
        willDie   = __instance.m_base.Owner.IsLocallyOwned ? __instance.m_base.Health <= 0 : predictedDamage > __instance.m_base.Health;
    }

    #region HostData / ClientData Cleanup

#if DEBUG
    // Clean up host event data
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveBulletDamage))]
    [HarmonyPostfix]
    internal static void PostfixReceiveLimbBulletDamage()
    {
        HostDataStruct.NumberOfRunningEvents -= 1;
    }

    // Clean up host event data
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveMeleeDamage))]
    [HarmonyPostfix]
    internal static void PostfixReceiveMeleeDamage()
    {
        HostDataStruct.NumberOfRunningEvents -= 1;
    }

    // Clean up host event data
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveExplosionDamage))]
    [HarmonyPostfix]
    internal static void PostfixReceiveExplosionDamage()
    {
        HostDataStruct.NumberOfRunningEvents -= 1;
    }
#endif

#if DEBUG
    // Clean up client event data
    [HarmonyPatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.BulletDamage))]
    [HarmonyPostfix]
    internal static void PostfixLimbBulletDamage()
    {
        ClientDataStruct.NumberOfRunningEvents -= 1;
    }

    // Set up client event data
    [HarmonyPatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.MeleeDamage))]
    [HarmonyPostfix]
    internal static void PostfixMeleeDamage()
    {
        ClientDataStruct.NumberOfRunningEvents -= 1;
    }

    // Clean up client event data
    [HarmonyPatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.ExplosionDamage))]
    [HarmonyPostfix]
    internal static void PostfixExplosionDamage()
    {
        ClientDataStruct.NumberOfRunningEvents -= 1;
    }
#endif
    #endregion
}