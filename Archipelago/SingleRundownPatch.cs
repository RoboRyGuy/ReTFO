
using GameData;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace ReTFO.Archipelago;

/// <summary>
/// Truncates loaded Rundown GameData to ensure there's only one rundown.
/// This forces the "Connect to Rundown" screen.
/// </summary>
internal static class SingleRundownPatch
{
    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GameDataInit), nameof(GameDataInit.Initialize));
        yield return AccessTools.Method(typeof(GameDataInit), nameof(GameDataInit.ReInitialize));
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        GameSetupDataBlock setup = GameSetupDataBlock.GetAllBlocks()[0];
        uint id = setup.RundownIdsToLoad[0];
    }
}
