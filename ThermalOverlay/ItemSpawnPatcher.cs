using HarmonyLib;
using Player;

namespace ReTFO.ThermalOverlay;

// Harmony patch for when gear is spawned in
[HarmonyPatch(typeof(PlayerBackpack))]
internal static class ItemSpawnPatcher
{
    [HarmonyPatch(nameof(PlayerBackpack.CheckAndCreateBackpackItem)), HarmonyPostfix]
    internal static void OnCreate(PlayerBackpack __instance, ref BackpackItem bpItem, ItemEquippable gearItem, Gear.GearIDRange gearIDRange)
    {
        Plugin.Get().OnItemCreated(gearItem, __instance.Owner);
    }
}
