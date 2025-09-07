using BepInEx;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace ReTFO.ThermalOverlay;

public class AssetBundle
{

    public const string AssetBundleName = "thermaloverlay";
    public static string AssetDirectory => Path.Combine(Path.GetDirectoryName(Paths.PluginPath) ?? string.Empty, "Assets");
    public static string AssetBundlePath => Path.Combine(AssetDirectory, "ThermalOverlay", AssetBundleName);

    public T? TryLoadAsset<T>(string assetName) where T : Il2CppSystem.Object
    {
        UnityEngine.AssetBundle assetBundle = UnityEngine.AssetBundle.LoadFromFile(AssetBundlePath);
        if (assetBundle == null)
        {
            Plugin? plugin = Plugin.TryGet();
            plugin?.Log.LogError($"Failed to load asset bundle {AssetBundleName}\n - Expected location: \"{AssetBundlePath}\"");
            return default(T);
        }
        
        Il2CppSystem.Object? obj = assetBundle?.LoadAsset(assetName);
        if (obj == null)
            Plugin.TryGet()?.Log.LogError($"Failed to load {assetName} from asset bundle {AssetBundleName}");

        T? result = obj?.TryCast<T>();
        if (result == null && obj != null)
            Plugin.TryGet()?.Log.LogError($"Failed to convert {assetName} to type {typeof(T).Name} because it is of type {obj.GetIl2CppType().Name}");
        
        return result;
    }

    public bool TryLoadAsset<T>(string assetName, [NotNullWhen(true)] out T? result) where T : Il2CppSystem.Object
    {
        result = TryLoadAsset<T>(assetName);
        return result != null;
    }

    public T LoadAsset<T>(string assetName) where T : Il2CppSystem.Object
    {
        return TryLoadAsset<T>(assetName)
            ?? throw new NullReferenceException($"Failed to load asset {assetName}");
    }

    private Shader? _thermalOverlayShader = null;
    public Shader? ThermalOverlayShader
    {
        get { return _thermalOverlayShader ??= TryLoadAsset<Shader>("Unlit_HolographicSight_ThermalOverlay.shader"); }
        set { _thermalOverlayShader = value ?? _thermalOverlayShader; }
    }

}
