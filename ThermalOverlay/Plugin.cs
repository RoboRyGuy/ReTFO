using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ReTFO.ThermalOverlay.Config;
using ReTFO.ThermalOverlay.Factories;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.Playables;

namespace ReTFO.ThermalOverlay;

[BepInPlugin(GUID, Name, Version)]
[BepInProcess("GTFO.exe")]
public class Plugin : BasePlugin
{
    public const string Name = "ThermalOverlay";    // Plugin name
    public const string Author = "RoboRyGuy";       // Plugin author
    public const string GUID = $"{Author}.{Name}";  // Plugin GUID, unique identifier used by BepInEx
    public const string Version = "1.0.0";          // Plugin version, can be used by System.Version

    // Reference to plugin instance that is loaded by BepInEx
    private static Plugin? _plugin = null;

    // Instance of harmony used for patching
    protected Harmony harmony = new(GUID);

    // Get the plugin instance, throws an exception if it fails
    public static Plugin Get() => TryGet() ?? throw new NullReferenceException($"Tried to retrieve {Name}, but it was not loaded!");

    // Tries to get the plugin instance, returns null if it fails
    public static Plugin? TryGet() => _plugin ??= IL2CPPChainloader.Instance.Plugins.FirstOrDefault(p => p.Key == GUID).Value.Instance as Plugin;

    // Tries to get the plugin instance, returns true if it succeeds
    public static bool TryGet([NotNullWhen(true)] out Plugin? plugin)
    {
        plugin = TryGet();
        return plugin != null;
    }

    public override void Load()
    {
        _plugin = this;
        harmony.PatchAll(typeof(ItemSpawnPatcher));
        harmony.PatchAll(GetType());
        Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp(typeof(Il2CppAction));
        Log.LogInfo($"{GUID} is loaded!");
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        return true;
    }

    // ================================================================================================================

    // Name of any overlay gameobjects created by this plugin
    public const string OverlayGameObjectName = "Thermal Overlay";

    // Helper class used to get assets from the thermaloverlay asset bundle
    public AssetBundle AssetBundle { get; private set; } = new();

    // Manages configs, including loading them, descriptions, etc
    public ConfigManager ConfigManager { get; private set; } = new();

    // Manges factories (converters, generators, etc)
    public FactoryManager FactoryManager { get; private set; } = new();

    // Function used to get an ID from an item
    protected Func<ItemEquippable, uint> ItemIDMapper = DefaultIDMapper;

    // Allows mod developers to change the ID mode used by this mod, in the off-case that the checksum isn't unique enough
    public void ChangeIDMode(Func<ItemEquippable, uint> newMapper, IReadOnlyDictionary<uint, string> IdNames)
    {
        if (ConfigManager.TryGetUserConfigs(out var _))
            Log.LogWarning($"Changing ID mode after config file is generated; the config description will be inaccurate!");
        ItemIDMapper = newMapper;
        ConfigManager.OfflineGearNames = IdNames;
    }

    // Callback for an item that was created, but isn't fully spawned in yet.
    // Adds a callback to apply configs once it's fully spawned in
    public void OnItemCreated(ItemEquippable item, SNetwork.SNet_Player owner)
    {
        // This sometimes gets called a second time after the gear is spawned. It's incosistent, so we cut it out
        if (item.m_gearSpawnComplete) return;

        // Queue config application
        item.GearPartHolder.m_gearPartSpawner.add_OnAllPartsSpawned(
            new Il2CppAction(() => ApplyConfig(item, owner.IsLocal))
        );
    }

    // Apply a config immediately
    public void ApplyConfig(ItemEquippable item, bool isLocal) 
    { 
        uint id = ItemIDMapper(item);
        if (!ConfigManager.IsIDEnabled(id)) return;

        ThermalConfig? config = ConfigManager.GetConfig(id);
        ConversionContext context = new(this, item, config);
        FactoryManager.RunThermalConverter(config?.Handler, context);

        if (ConfigManager.UserConfigs.MakeGunsCold && isLocal)
        {
            foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>())
            {
                if (renderer.sharedMaterial.HasProperty("_ShadingType"))
                    renderer.sharedMaterial.SetFloat("_ShadingType", 0f);
            }
        }
    }

    public static uint DefaultIDMapper(ItemEquippable item)
        => item.GearIDRange.GetChecksum();

    /*
    #region dumb
    
    // This chunk of code intercepts unique shader property names and logs them

    static HashSet<string> seenNames = new();

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalBuffer), new Type[] { typeof(string), typeof(ComputeBuffer) })]
    [HarmonyPostfix]
    internal static void SetGlobalBuffer_String1(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalBuffer with name {name}");
    }

    //[HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalBuffer), new Type[] { typeof(string), typeof(GraphicsBuffer) })]
    //[HarmonyPostfix]
    //internal static void SetGlobalBuffer_String2(string name)
    //{
    //    if (seenNames.Contains(name)) return;
    //    seenNames.Add(name);
    //    Get().Log.LogMessage($"Called SetGlobalBuffer with name {name}");
    //}

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalColor), new Type[] { typeof(string), typeof(Color) })]
    [HarmonyPostfix]
    internal static void SetGlobalColor_String(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalColor  with name {name}");
    }

    //[HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalConstantBuffer), new Type[] { typeof(string), typeof(ComputeBuffer), typeof(int), typeof(int) })]
    //[HarmonyPostfix]
    internal static void SetGlobalConstantBuffer_String1(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalConstantBuffer with name {name}");
    }

    //[HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalConstantBuffer), new Type[] { typeof(string), typeof(GraphicsBuffer), typeof(int), typeof(int) })]
    //[HarmonyPostfix]
    internal static void SetGlobalConstantBuffer_String2(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalConstantBuffer with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalFloat), new Type[] { typeof(string), typeof(float) })]
    [HarmonyPostfix]
    internal static void SetGlobalFloat_String(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalFloat with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalFloatArray), new Type[] { typeof(string), typeof(Il2CppStructArray<float>) })]
    [HarmonyPostfix]
    internal static void SetGlobalFloatArray_String1(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalFloatArray with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalFloatArray), new Type[] { typeof(string), typeof(Il2CppSystem.Collections.Generic.List<float>) })]
    [HarmonyPostfix]
    internal static void SetGlobalFloatArray_String2(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalFloatArray with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalInt), new Type[] { typeof(string), typeof(int) })]
    [HarmonyPostfix]
    internal static void SetGlobalInt_String(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalInt with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalMatrix), new Type[] { typeof(string), typeof(Matrix4x4) })]
    [HarmonyPostfix]
    internal static void SetGlobalMatrix_String(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalMatrix with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalMatrixArray), new Type[] { typeof(string), typeof(Il2CppStructArray<Matrix4x4>) })]
    [HarmonyPostfix]
    internal static void SetGlobalMatrixArray_String1(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalMatrixArray with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalMatrixArray), new Type[] { typeof(string), typeof(Il2CppSystem.Collections.Generic.List<Matrix4x4>) })]
    [HarmonyPostfix]
    internal static void SetGlobalMatrixArray_String2(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalMatrixArray with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalTexture), new Type[] { typeof(string), typeof(Texture) })]
    [HarmonyPostfix]
    internal static void SetGlobalTexture_String1(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalTexture with name {name}");
    }

    //[HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalTexture), new Type[] { typeof(string), typeof(RenderTexture), typeof(UnityEngine.Rendering.RenderTextureSubElement) })]
    //[HarmonyPostfix]
    internal static void SetGlobalTexture_String2(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalTexture with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalVector), new Type[] { typeof(string), typeof(Vector4) })]
    [HarmonyPostfix]
    internal static void SetGlobalVector_String(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalVector with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalVectorArray), new Type[] { typeof(string), typeof(Il2CppStructArray<Vector4>) })]
    [HarmonyPostfix]
    internal static void SetGlobalVectorArray_String1(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalVectorArray with name {name}");
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalVectorArray), new Type[] { typeof(string), typeof(Il2CppSystem.Collections.Generic.List<Vector4>) })]
    [HarmonyPostfix]
    internal static void SetGlobalVectorArray_String2(string name)
    {
        if (seenNames.Contains(name)) return;
        seenNames.Add(name);
        Get().Log.LogMessage($"Called SetGlobalVectorArray with name {name}");
    }

    static Dictionary<int, string> idToName = new();

    [HarmonyPatch(typeof(Shader), nameof(Shader.PropertyToID))]
    [HarmonyPostfix]
    internal static void PropertyToID(string name, int __result)
    {
        idToName[__result] = name;
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalBuffer), new Type[] { typeof(int), typeof(ComputeBuffer) })]
    [HarmonyPostfix]
    internal static void SetGlobalBuffer_Int1(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalBuffer_String1(name);
        }
    }

    //[HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalBuffer), new Type[] { typeof(int), typeof(GraphicsBuffer) })]
    //[HarmonyPostfix]
    //internal static void SetGlobalBuffer_Int2(int nameID)
    //{
    //    if (idToName.ContainsKey(nameID))
    //    {
    //        string name = idToName[nameID];
    //        SetGlobalBuffer_String2(name);
    //    }
    //}

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalColor), new Type[] { typeof(int), typeof(Color) })]
    [HarmonyPostfix]
    internal static void SetGlobalColor_Int(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalColor_String(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalConstantBuffer), new Type[] { typeof(int), typeof(ComputeBuffer), typeof(int), typeof(int) })]
    [HarmonyPostfix]
    internal static void SetGlobalConstantBuffer_Int1(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalConstantBuffer_String1(name);
        }
    }

    //[HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalConstantBuffer), new Type[] { typeof(int), typeof(GraphicsBuffer), typeof(int), typeof(int) })]
    //[HarmonyPostfix]
    //internal static void SetGlobalConstantBuffer_Int2(int nameID)
    //{
    //    if (idToName.ContainsKey(nameID))
    //    {
    //        string name = idToName[nameID];
    //        SetGlobalConstantBuffer_String2(name);
    //    }
    //}

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalFloat), new Type[] { typeof(int), typeof(float) })]
    [HarmonyPostfix]
    internal static void SetGlobalFloat_Int(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalFloat_String(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalFloatArray), new Type[] { typeof(int), typeof(Il2CppStructArray<float>) })]
    [HarmonyPostfix]
    internal static void SetGlobalFloatArray_Int1(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalFloatArray_String1(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalFloatArray), new Type[] { typeof(int), typeof(Il2CppSystem.Collections.Generic.List<float>) })]
    [HarmonyPostfix]
    internal static void SetGlobalFloatArray_Int2(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalFloatArray_String2(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalFloatArray), new Type[] { typeof(int), typeof(Il2CppStructArray<float>), typeof(int) })]
    [HarmonyPostfix]
    internal static void SetGlobalFloatArray_Int3(int name)
    {
        if (idToName.ContainsKey(name))
        {
            string nameID = idToName[name];
            SetGlobalFloatArray_String1(nameID);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalInt), new Type[] { typeof(int), typeof(int) })]
    [HarmonyPostfix]
    internal static void SetGlobalInt_Int(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalInt_String(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalMatrix), new Type[] { typeof(int), typeof(Matrix4x4) })]
    [HarmonyPostfix]
    internal static void SetGlobalMatrix_Int(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalMatrix_String(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalMatrixArray), new Type[] { typeof(int), typeof(Il2CppStructArray<Matrix4x4>) })]
    [HarmonyPostfix]
    internal static void SetGlobalMatrixArray_Int1(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalMatrixArray_String1(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalMatrixArray), new Type[] { typeof(int), typeof(Il2CppSystem.Collections.Generic.List<Matrix4x4>) })]
    [HarmonyPostfix]
    internal static void SetGlobalMatrixArray_Int2(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalMatrixArray_String2(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalTexture), new Type[] { typeof(int), typeof(Texture) })]
    [HarmonyPostfix]
    internal static void SetGlobalTexture_Int1(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalTexture_String1(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalTexture), new Type[] { typeof(int), typeof(RenderTexture), typeof(UnityEngine.Rendering.RenderTextureSubElement) })]
    [HarmonyPostfix]
    internal static void SetGlobalTexture_Int2(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalTexture_String2(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalVector), new Type[] { typeof(int), typeof(Vector4) })]
    [HarmonyPostfix]
    internal static void SetGlobalVector_Int(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalVector_String(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalVectorArray), new Type[] { typeof(int), typeof(Il2CppStructArray<Vector4>) })]
    [HarmonyPostfix]
    internal static void SetGlobalVectorArray_Int1(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalVectorArray_String1(name);
        }
    }

    [HarmonyPatch(typeof(Shader), nameof(Shader.SetGlobalVectorArray), new Type[] { typeof(int), typeof(Il2CppSystem.Collections.Generic.List<Vector4>) })]
    [HarmonyPostfix]
    internal static void SetGlobalVectorArray_Int2(int nameID)
    {
        if (idToName.ContainsKey(nameID))
        {
            string name = idToName[nameID];
            SetGlobalVectorArray_String2(name);
        }
    }



    #endregion
    
     */
}