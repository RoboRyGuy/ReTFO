
using AssetShards;
using HarmonyLib;
using UnityEngine;

namespace ReTFO.ComponentInjector;


[HarmonyPatch]
internal static class Patches
{
    //[HarmonyPatch(typeof(AssetShardManager), nameof(AssetShardManager.RegisterManifest)), HarmonyPrefix]
    //internal static void PrefixRegisterManifest(AssetShardManifest manifest)
    //{
    //    Plugin plugin = Plugin.Get();
    //    var keys = manifest.Assets.keys;
    //    foreach (var key in keys)
    //    {
    //        if (plugin.TryReplace(manifest.Assets[key], out UnityEngine.Object? obj))
    //        {
    //            UnityEngine.Object.Destroy(manifest.Assets[key]);
    //            manifest.Assets[key] = obj;
    //        }
    //    }
    //}

    [HarmonyPatch(typeof(AssetShardManager), nameof(AssetShardManager.RegisterManifest)), HarmonyPostfix]
    internal static void PostfixRegisterManifest(AssetShardManifest manifest)
    {
        Plugin plugin = Plugin.Get();
        foreach (var key in manifest.Assets.keys)
        {
            GameObject? go = manifest.Assets[key]?.TryCast<GameObject>();
            if (go == null) continue;
    
            foreach (var comp in go.GetComponents<MonoBehaviour>())
                plugin.TryReplaceComponent(comp);
        }
    }

    [HarmonyPatch(typeof(GameObject), nameof(GameObject.AddComponent), new Type[] { typeof(Il2CppSystem.Type) }), HarmonyReversePatch]
    internal static Component OriginalAddComponent(GameObject __instance, Il2CppSystem.Type componentType)
    {
        throw new NotImplementedException("A method which should have been reversed patched has been called");
    }

    [HarmonyPatch(typeof(GameObject), nameof(GameObject.AddComponent), new Type[] { typeof(Il2CppSystem.Type) }), HarmonyPrefix]
    internal static void PrefixAddComponent(GameObject __instance, ref Il2CppSystem.Type componentType)
    {
        if (Plugin.Get().InjectedComponents.TryGetValue(componentType.Pointer, out var newType))
            componentType = newType;
    }

}