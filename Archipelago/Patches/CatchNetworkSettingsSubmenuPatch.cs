using HarmonyLib;
using ReTFO.Archipelago.Features;
using System;
using System.Collections.Generic;
using System.Reflection;
using TheArchive.Features.Dev;

namespace ReTFO.Archipelago.Patches;

/// <summary>
/// TheArchive doesn't expose its submenus when it builds the settings menu,
///  so this patch will catch the one I care about and keep a reference to it
/// </summary>
[HarmonyPatch]
internal static class CatchNetworkSettingsSubmenuPatch
{
    public static ModSettings.SubMenu? NetworkSettingsSubMenu { get; set; } = null;

    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Constructor(typeof(ModSettings.SubMenu), new Type[] { typeof(string), typeof(string) });
    }

    public static void Postfix(ModSettings.SubMenu __instance, string identifier)
    {
        var feature = StateTracker.Get();
        if (feature.Identifier == identifier)
            NetworkSettingsSubMenu = __instance;
    }
}
