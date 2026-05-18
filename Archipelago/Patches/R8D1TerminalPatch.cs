using GameData;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace ReTFO.Archipelago.Patches;

/// <summary>
/// There's a bad terminal spawn in R8D1 (Main) Zone_422.
/// This bumps its spawn location to somewhere it can actually be accessed.
/// </summary>
[HarmonyPatch]
public static class R8D1TerminalPatch
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
        // I figure this is enough to ensure we're looking at the right block
        const uint ID = 1556013495;
        const string NAME = "R8_D1_Empty_L1";
        const int ALIAS_START = 420;
        const int ZONE_COUNT = 10;

        LevelLayoutDataBlock layout = LevelLayoutDataBlock.GetBlock(ID);
        if (layout.name != NAME
            || layout.ZoneAliasStart != ALIAS_START
            || layout.Zones.Count != ZONE_COUNT
            ) return;

        layout.Zones[2].MarkerSubSeed = 0; // This is not particular, it just happens to work
    }

}
