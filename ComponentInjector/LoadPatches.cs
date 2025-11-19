
using AssetShards;
using HarmonyLib;
using UnityEngine;

namespace ReTFO.ComponentInjector;


[HarmonyPatch()]
internal static class LoadPatches
{

    [HarmonyPatch(typeof(AssetShardManager), nameof(AssetShardManager.RegisterManifest))]
    internal static void Prefix(AssetShardManifest manifest)
    {
        Plugin plugin = Plugin.Get();
        foreach (UnityEngine.Object? asset in manifest.Assets.values)
        {
            GameObject? go = asset?.TryCast<GameObject>();
            MonoBehaviour? mo = asset?.TryCast<MonoBehaviour>();
            if (go != null)
            {
                foreach (var comp in go.GetComponentsInChildren<MonoBehaviour>())
                {
                    plugin.ReplaceComponent(comp);
                }
            }
            else if (mo != null)
            {
                plugin.ReplaceComponent(mo);
            }
        }
    }

}