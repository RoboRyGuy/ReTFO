using HarmonyLib;

namespace ReTFO.DamageOverhaulCore.HarmonyPatches;

// When patched, will post logs reporting damage done
internal static class DamageReporter
{
    private static float baseHealth = 0f;
    private static float limbHealth = 0f;

    internal static void MyPrefix(Dam_EnemyDamageBase b, int limbId)
    {
        baseHealth = b.Health;
        limbHealth = b.DamageLimbs[limbId].m_health;
    }

    internal static void MyPostfix(Dam_EnemyDamageBase b, int limbId)
    {
        Dam_EnemyDamageLimb l = b.DamageLimbs[limbId];
        float lh = l.m_health < -5_000_000 ? 0 : l.m_health;
        Plugin.Logger.LogDebug($"{l.name} {{{limbHealth - lh} damage, {lh}/{l.m_healthMax} health}}, base {{{baseHealth - b.Health} damage, {b.Health}/{b.HealthMax} health}}");
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveBulletDamage))]
    [HarmonyPrefix]
    internal static void PrefixBullet(Dam_EnemyDamageBase __instance, pBulletDamageData data)
    {
        MyPrefix(__instance, data.limbID);
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveExplosionDamage))]
    [HarmonyPrefix]
    internal static void PrefixExplosion(Dam_EnemyDamageBase __instance, pExplosionDamageData data)
    {
        MyPrefix(__instance, data.limbID);
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveMeleeDamage))]
    [HarmonyPrefix]
    internal static void PrefixMelee(Dam_EnemyDamageBase __instance, pFullDamageData data)
    {
        MyPrefix(__instance, data.limbID);
    }


    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveBulletDamage))]
    [HarmonyPostfix]
    internal static void PostfixBullet(Dam_EnemyDamageBase __instance, pBulletDamageData data)
    {
        MyPostfix(__instance, data.limbID);
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveExplosionDamage))]
    [HarmonyPostfix]
    internal static void PostfixExplosion(Dam_EnemyDamageBase __instance, pExplosionDamageData data)
    {
        MyPostfix(__instance, data.limbID);
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveMeleeDamage))]
    [HarmonyPostfix]
    internal static void PostfixMelee(Dam_EnemyDamageBase __instance, pFullDamageData data)
    {
        MyPostfix(__instance, data.limbID);
    }



}
