using CellMenu;
using HarmonyLib;

namespace ReTFO.Archipelago.Patches;

/// <summary>
/// Fixes the issue where clicking out of a setting without pressing enter leaves
///  the setting unmodified
/// </summary>
/// <remarks>
/// Normally, we'd patch SetReadingActive to detect when the setting is done being
///  modified. However, that method can't be patched, so instead we're detecting
///  when the user clicks out by checking if the reading state changed from active
///  to inactive during an input event
/// </remarks>
[HarmonyPatch(typeof(CM_SettingsInputField), nameof(CM_SettingsInputField.OnBtnPressAnywhere))]
public static class AutoEnterFeatureSettings
{
    public static void Prefix(CM_SettingsInputField __instance, ref bool __state)
    {
        __state = __instance.m_readingActive;
    }

    public static void Postfix(CM_SettingsInputField __instance, bool __state)
    {
        if (!__instance.m_readingActive && __state)
            __instance.SetNewValue(__instance.m_currentValue);
    }
}
