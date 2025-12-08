
using GameData;
using HarmonyLib;
using System.Reflection;

namespace ReTFO.Archipelago;

/// <summary>
/// Truncates loaded Rundown GameData to ensure there's only one rundown.
/// This forces the "Connect to Rundown" screen.
/// </summary>
internal class SingleRundownPatch
{
    [HarmonyTargetMethods]
    public IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GameDataInit), nameof(GameDataInit.Initialize));
        yield return AccessTools.Method(typeof(GameDataInit), nameof(GameDataInit.ReInitialize));
    }

    [HarmonyPostfix]
    public void Postfix()
    {
        uint id = RundownDataBlock.GetAllBlocks()[0].persistentID;
        //GameSetupDataBlock setup = GameSetupDataBlock.GetAllBlocks()[0];
        //setup.RundownIdsToLoad = new(1);
        //setup.RundownIdsToLoad.Add(id);
    }

}
