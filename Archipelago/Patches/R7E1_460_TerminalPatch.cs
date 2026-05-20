using GameData;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace ReTFO.Archipelago.Patches;

/// <summary>
/// There's supposed to be a terminal in R7E1 ZONE_460. 
/// Most likely, it doesn't spawn because it's too crowded.
/// This patch sets resource container allocation to false, which I believe
///  delays resource container placement until after other things have spawned.
///  (ie does not allocate space for them ahead of time)
/// </summary>
[HarmonyPatch]
public static class R7E1_460_TerminalPatch
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
        const uint ID = 1044535672;
        const string NAME = "R7_E1_Reactor_Startup_L1";
        const int ALIAS_START = 442;
        const int ZONE_COUNT = 21;

        LevelLayoutDataBlock layout = LevelLayoutDataBlock.GetBlock(ID);
        if (layout.name != NAME
            || layout.ZoneAliasStart != ALIAS_START
            || layout.Zones.Count != ZONE_COUNT
            ) return;

        layout.Zones[18].AllowResourceContainerAllocation = false;
    }
}
