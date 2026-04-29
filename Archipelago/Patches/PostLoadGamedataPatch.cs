
using GameData;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ReTFO.Archipelago.Patches;

// React to gamedata loading and inject our modifications
[HarmonyPatch]
internal static class PostLoadGamedataPatch
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
        // Invalidate our current data
        if (!Plugin.TryGet(out Plugin? plugin))
            throw new NotImplementedException("PostLoadGamedataPatch could not access plugin!");
        plugin.MidManager.InvalidateModdedInstanceData();

        // Overwrite the rundowns to be loaded - This changes the menu which is loaded to the "Connect to Rundown" menu.
        // This will later be copied to Globals.Global, which we will then modify to increase the number of rundowns as needed.
        GameSetupDataBlock setup = GameSetupDataBlock.GetAllBlocks()[0];
        uint oldId = setup.RundownIdsToLoad[0];
        setup.RundownIdsToLoad = new(1);
        setup.RundownIdsToLoad.Add(oldId);
    }

}
