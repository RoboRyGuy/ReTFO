using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Enemies;
using GameData;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;
using ReTFO.DamageOverhaulCore.Config;
using ReTFO.DamageOverhaulCore.Data;
using SNetwork;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace ReTFO.DamageOverhaulCore;

/// <summary>
/// Main plugin class for DamageOverhaulCore, used by BepInEx to load the mod.
/// </summary>
/// <remarks>
/// Please see <see cref="IPlugin"/> for the public interface of this plugin.
/// </remarks>
[BepInPlugin(GUID, Name, Version)]
[BepInProcess("GTFO.exe")]
[BepInDependency("com.dak.MTFO")]
[BepInDependency(ComponentInjector.Plugin.GUID)]
public class Plugin : BasePlugin
{
    public const string Name = "DamageOverhaulCore";    // Plugin name
    public const string Author = "RoboRyGuy";           // Plugin author
    public const string GUID = $"{Author}.{Name}";      // Plugin GUID, unique identifier used by BepInEx
    public const string Version = "1.0.0";              // Plugin version

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

    // Does this really need a comment?
    public override void Load()
    {
        _plugin = this;

        harmony.PatchAll(typeof(Plugin));
        //harmony.PatchAll(typeof(DamagePatch));
        //harmony.PatchAll(typeof(DamageReporter));

        ClassInjector.RegisterTypeInIl2Cpp<DamExt_EnemyDamageBase>();
        ClassInjector.RegisterTypeInIl2Cpp<DamExt_AntiGCHelper>();
        ClassInjector.RegisterTypeInIl2Cpp<DamExt_EnemyDamageLimb>();

        ApplyNativeHook<SNet_ReplicationManager<pEnemySpawnData, EnemyReplicator>, TwoParamR>(
            nameof(SNet_ReplicationManager<pEnemySpawnData, EnemyReplicator>.GetInstanceReplicator), typeof(EnemyReplicator).FullName!, new string[] { typeof(pEnemySpawnData).FullName!, typeof(GameObject).FullName! },
            new_GetInstanceReplicator, out old_GetInstanceReplicator
        );

        MethodLogger.AddMethod<EnemyAgent>(nameof(EnemyAgent.Setup));
        MethodLogger.AddMethod<EnemyAllocator>(nameof(EnemyAllocator.SpawnEnemy));
        MethodLogger.AddMethod<EnemyAllocator>(nameof(EnemyAllocator.OnEnemySpawned));
        MethodLogger.AddMethod<EnemyAllocator>(nameof(EnemyAllocator.GetEnemyPrefabs));
        MethodLogger.AddMethod<EnemyAllocator.EnemyReplicationManager>(nameof(EnemyAllocator.EnemyReplicationManager.OnSpawn));
        MethodLogger.AddMethod<EnemySync>(nameof(EnemySync.OnSpawn));
        MethodLogger.AddMethod<Dam_EnemyDamageBase>(nameof(Dam_EnemyDamageBase.Setup));
        MethodLogger.AddMethod<Dam_EnemyDamageLimb>(nameof(Dam_EnemyDamageLimb.Setup));
        MethodLogger.AddMethod<EnemyPrefabManager>(nameof(EnemyPrefabManager.GenerateAllEnemyPrefabs));

        EnemyPrefabManager.add_OnEnemiesGenerated(new ComponentInjector.Il2CppAction(CleanupPrefabs));

        harmony.PatchAll(typeof(MethodLogger));

        ComponentInjector.Plugin ciPlugin = ComponentInjector.Plugin.Get();
        ciPlugin.InjectComponent<Dam_EnemyDamageBase, DamExt_EnemyDamageBase>();
        //ciPlugin.InjectComponent<Dam_EnemyDamageLimb, DamExt_EnemyDamageLimb>();

        Log.LogInfo($"{GUID} is loaded!");
    }

    static Cache? cache = null;

    // Remove references on prefabs; they'll get garbage-collected anyway...
    internal static void CleanupPrefabs()
    {
        Get().Log.LogWarning($"{Indent}CleanupPrefabs");

        foreach (var prefab in EnemyPrefabManager.Current.m_enemyPrefabs.Values)
        {
            Dam_EnemyDamageBase dam = prefab.GetComponent<Dam_EnemyDamageBase>();
            DamExt_AntiGCHelper helper = prefab.AddComponent<DamExt_AntiGCHelper>();
            helper.DamageLimbs = dam.DamageLimbs;
            helper.DamageLimbsWithDestruction = dam.DamageLimbsWithDestruction;
            helper.GlueTargetComps = dam.GlueTargetComps;
        }

        Get().Log.LogWarning($"{DeIndent}CleanupPrefabs");
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.Setup)), HarmonyPrefix]
    internal static void PreDamageSetup(Dam_EnemyDamageBase __instance)
    {
        foreach (var limb in __instance.gameObject.GetComponentsInChildren<Dam_EnemyDamageLimb>())
        {
            if (__instance.ObjectClass == Il2CppClassPointerStore<DamExt_EnemyDamageBase>.NativeClassPtr)
                limb.SetBaseDamagable(__instance);
            else
                Get().Log.LogWarning("Somehow added non-DamExt component!?");
        }
    }

    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.Setup)), HarmonyPostfix]
    internal static void PostDamageSetup(Dam_EnemyDamageBase __instance)
    {
        DamExt_AntiGCHelper helper        = __instance.gameObject.GetComponent<DamExt_AntiGCHelper>();
        helper.DamageLimbs                = __instance.DamageLimbs                ;
        helper.DamageLimbsWithDestruction = __instance.DamageLimbsWithDestruction ;
        helper.GlueTargetComps            = __instance.GlueTargetComps            ;
    }

    internal static int indent = 0;
    internal static string Indent => new string(' ', indent++) + "->";
    internal static string DeIndent => new string(' ', --indent) + "--";
    internal static string Tab => new string(' ', indent) + " - ";

    public static unsafe BepInEx.Unity.IL2CPP.Hook.INativeDetour ApplyNativeHook<TClass, TDelegate>(string methodName, string returnType, string[] paramTypes, TDelegate to, out TDelegate original)
       where TClass : Il2CppSystem.Object
       where TDelegate : Delegate
    {
        IntPtr classPtr = Il2CppClassPointerStore<TClass>.NativeClassPtr;
        if (classPtr == IntPtr.Zero) throw new ArgumentException($"{typeof(TClass).Name} does not exist in il2cpp domain");

        IntPtr methodPtr = IL2CPP.GetIl2CppMethod(classPtr, false, methodName, returnType, paramTypes);
        if (methodPtr == IntPtr.Zero) throw new ArgumentException($"\"{returnType} {typeof(TClass).Name}.{methodName}({string.Join(", ", paramTypes)})\" does not exist in the il2cpp domain");

        Il2CppSystem.Reflection.MethodInfo methodInfo = new(IL2CPP.il2cpp_method_get_object(methodPtr, classPtr));
        INativeMethodInfoStruct il2cppMethodInfo = UnityVersionHandler.Wrap((Il2CppMethodInfo*)IL2CPP.il2cpp_method_get_from_reflection(methodInfo.Pointer));

        return BepInEx.Unity.IL2CPP.Hook.INativeDetour.CreateAndApply(il2cppMethodInfo.MethodPointer, to, out original);
    }
    public unsafe delegate IntPtr   TwoParamR(IntPtr self, IntPtr _1, IntPtr _2,            Il2CppMethodInfo* methodInfo);
    private static       TwoParamR? old_GetInstanceReplicator = null;
    private static unsafe TwoParamR new_GetInstanceReplicator = new_GetInstanceReplicator_Impl;
    private static unsafe IntPtr new_GetInstanceReplicator_Impl(IntPtr self, IntPtr spawnData, IntPtr outGameObject, Il2CppMethodInfo* methodInfo)
    {
        Get().Log.LogWarning($"{Indent}GetInstanceReplicator");

        EnemyAllocator.EnemyReplicationManager Manager = new(self);
        pEnemySpawnData* SpawnData = (pEnemySpawnData*)spawnData;

        GameObject prefab = Manager.m_prefabs[SpawnData->replicationData.PrefabID];
        Dam_EnemyDamageBase dam = prefab.GetComponent<Dam_EnemyDamageBase>();
        DamExt_AntiGCHelper helper = prefab.GetComponent<DamExt_AntiGCHelper>();

        if (old_GetInstanceReplicator == null) throw new NullReferenceException();
        IntPtr result = old_GetInstanceReplicator.Invoke(self, spawnData, outGameObject, methodInfo);

        Get().Log.LogWarning($"{DeIndent}GetInstanceReplicator");
        return result;
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        return true;
    }

    // Static shortcut to the log source - throws on failure
    internal static ManualLogSource Logger => Get().Log;

    // All gun damage data (by Archetype PersistentID)
    protected Dictionary<uint, WeaponDamageData> GunDamageDatas = new(10);

    // All melee damage data (by MeleeArchetype PersistentID)
    protected Dictionary<uint, WeaponDamageData> MeleeDamageDatas = new(5);

    // All explosives damage data (by GearCategory PersistentID)
    protected Dictionary<uint, WeaponDamageData> ExplosivesDamageDatas = new(1);

    // All enemy damage data
    protected Dictionary<uint, EnemyDamageData> EnemyDamageDatas = new(20);

    /// <summary>
    /// Gets a <see cref="WeaponDamageData"/> for a gun given its Archetype PersistentId
    /// </summary>
    /// <param name="id">The PersistentId of the Archetype to use</param>
    /// <returns>The weapon damage data. Creates default data if needed</returns>
    public WeaponDamageData GetGunDamageData(uint id)
    {
        if (GunDamageDatas.TryGetValue(id, out WeaponDamageData? data))
            return data;

        Log.LogWarning($"Generating default weapon damage data for gun: {id} - {ArchetypeDataBlock.GetBlock(id).name}");
        data = WeaponDamageData.Default;
        GunDamageDatas.Add(id, data);
        return data;
    }

    /// <summary>
    /// Gets a <see cref="WeaponDamageData"/> for a melee weapon given its Archetype PersistentId
    /// </summary>
    /// <param name="id">The PersistentId of the MeleeArchetype to use</param>
    /// <returns>The weapon damage data. Creates default data if needed</returns>
    public WeaponDamageData GetMeleeDamageData(uint id)
    {
        if (MeleeDamageDatas.TryGetValue(id, out WeaponDamageData? data))
            return data;

        Log.LogWarning($"Generating default weapon damage data for melee: {id} - {MeleeArchetypeDataBlock.GetBlock(id).name}");
        data = WeaponDamageData.Default;
        MeleeDamageDatas.Add(id, data);
        return data;
    }

    /// <summary>
    /// Gets a <see cref="WeaponDamageData"/> for an explosive based on its GearCategory
    /// </summary>
    /// <param name="id">The PersistentId of the GearCategory to use</param>
    /// <returns>The weapon damage data. Creates default data if needed</returns>
    public WeaponDamageData GetExplosiveDamageData(uint id)
    {
        if (ExplosivesDamageDatas.TryGetValue(id, out WeaponDamageData? data))
            return data;

        Log.LogWarning($"Generating default weapon damage data for explosive: {id} - {GearCategoryDataBlock.GetBlock(id).name}");
        data = WeaponDamageData.Default;
        ExplosivesDamageDatas.Add(id, data);
        return data;
    }

    /// <summary>
    /// Gets a <see cref="LimbDamageData"/> for an enemy's limb given its <see cref="Dam_EnemyDamageLimb"/>
    /// </summary>
    /// <param name="limb">The limb's damage instance</param>
    /// <returns>The LimbDamageData. Creates default data if necessary</returns>
    /// <exception cref="ArgumentNullException">If the input is null</exception>
    public LimbDamageData GetLimbDamageData(Dam_EnemyDamageLimb limb)
    {
        if (limb == null) throw new ArgumentNullException(nameof(limb));
        EnemyDamageData enemyInfo = GetEnemyDamageData(limb.m_base);
        return enemyInfo.LimbDamageDatas[limb.m_limbID];
    }

    /// <summary>
    /// Get an <see cref="EnemyDamageData"/> for an enemy given its <see cref="Dam_EnemyDamageBase"/>
    /// </summary>
    /// <param name="_base">The enemy's damage instance</param>
    /// <returns>The EnemyDamageData. Creates default data if necessary</returns>
    /// <exception cref="ArgumentNullException">If the input is null</exception>
    public EnemyDamageData GetEnemyDamageData(Dam_EnemyDamageBase _base)
    {
        if (_base == null) throw new ArgumentNullException(nameof(_base));
        uint enemyId = _base.Owner.EnemyDataID;
        if (EnemyDamageDatas.TryGetValue(enemyId, out EnemyDamageData? enemyData))
            return enemyData;

        Log.LogWarning($"Using default damage data for enemy {_base.Owner.EnemyDataID}: {_base.Owner.EnemyData.name}");
        Log.LogWarning($"Limbs: " + string.Join(", ", _base.DamageLimbs.Select(l => l.m_limbID.ToString() + " - " + l.name)));
        enemyData = new EnemyDamageData()
        {
            MaxHealth = _base.HealthMax,
            LimbDamageDatas = _base.DamageLimbs.Select(
                limb => new LimbDamageData() {
                    MaxHealth = limb.m_healthMax,
                    PrecisionBase = limb.m_type == eLimbDamageType.Weakspot ? limb.m_weakspotDamageMulti : 1f,
                    BackstabBase = _base.Owner.EnemyBalancingData.AllowDamgeBonusFromBehind ? 2f : 1f,
                    FalloffResistance = limb.m_type == eLimbDamageType.Armor ? new FalloffResistance() { HardCap = .01f } : new()
                }).ToArray()
        };
        EnemyDamageDatas[enemyId] = enemyData;
        return enemyData;
    }

    /// <summary>
    /// Loads in enemy damage information from the provided file(s)
    /// </summary>
    /// <param name="configFilePath">The file path for the <see cref="DamageOverhaulConfig"/></param>
    /// <param name="referenceFilePath">The file path for the <see cref="DamageOverhaulReference"/></param>
    /// <returns>True if successful, false otherwise</returns>
    public bool LoadDamageConfig(string configFilePath, string? referenceFilePath = null)
    {
        if (referenceFilePath == null)
        {
            if (!TryLoadFiles(configFilePath, out var config))
                return false;
            LoadDamageConfig(config);
        }
        else
        {
            if (!TryLoadFiles(configFilePath, referenceFilePath, out var config, out var reference))
                return false;
            LoadDamageConfig(config, reference);
        }
        return true;
    }

    /// <summary>
    /// Attempts to load config objects from the given file locations
    /// </summary>
    /// <param name="configFilePath">The file path for the <see cref="DamageOverhaulConfig"/></param>
    /// <param name="config">The resulting config</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool TryLoadFiles(string configFilePath, [NotNullWhen(true)] out DamageOverhaulConfig? config)
    {
        if (!File.Exists(configFilePath))
        {
            Log.LogError($"Could not find file: {configFilePath}");
            config = null;
            return false;
        }

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        string configJson = File.ReadAllText(configFilePath);
        config = JsonSerializer.Deserialize<DamageOverhaulConfig>(configJson, options)
            ?? throw new NullReferenceException($"Loaded null from {configFilePath}");
        return true;
    }

    /// <summary>
    /// Attempts to load config objects from the given file locations
    /// </summary>
    /// <param name="configFilePath">The file path for the <see cref="DamageOverhaulConfig"/></param>
    /// <param name="referenceFilePath">The file path for the <see cref="DamageOverhaulReference"/></param>
    /// <param name="config">The resulting config</param>
    /// <param name="reference">The resulting reference</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool TryLoadFiles(string configFilePath, string referenceFilePath,
        [NotNullWhen(true)] out DamageOverhaulConfig? config, [NotNullWhen(true)] out DamageOverhaulReference? reference)
    {
        bool fail = false;
        if (!File.Exists(referenceFilePath))
        {
            Log.LogError($"Could not find file: {referenceFilePath}");
            fail = true;
        }

        if (!TryLoadFiles(configFilePath, out config) || fail)
        {
            reference = null;
            return false;
        }

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        string referenceJson = File.ReadAllText(referenceFilePath);
        reference = JsonSerializer.Deserialize<DamageOverhaulReference>(referenceJson, options)
            ?? throw new NullReferenceException($"Loaded null from {referenceFilePath}");
        return true;
    }

    /// <summary>
    /// Loads damage information directly from config objects, skipping file reads
    /// </summary>
    /// <param name="mainConfig">The main config</param>
    /// <param name="referenceConfig">An optional reference object</param>
    public void LoadDamageConfig(DamageOverhaulConfig mainConfig, DamageOverhaulReference? referenceConfig = null)
    {
        foreach (WeaponConfig weaponConfig in mainConfig.GunConfigs)
            AddWeaponDamageData(GunDamageDatas, weaponConfig);

        foreach (WeaponConfig weaponConfig in mainConfig.MeleeConfigs)
            AddWeaponDamageData(MeleeDamageDatas, weaponConfig);

        foreach (WeaponConfig weaponConfig in mainConfig.ExplosiveConfig)
            AddWeaponDamageData(ExplosivesDamageDatas, weaponConfig);

        foreach (EnemyConfig enemyConfig in mainConfig.EnemyConfigs)
        {
            Log.LogDebug($"Loading enemy config: {enemyConfig.ConfigName} for enemy(s) {string.Join(", ", enemyConfig.EnemyIds)}");
            EnemyDamageData enemyDamageData = ToEnemyDamageData(enemyConfig, referenceConfig);

            foreach (uint enemyId in enemyConfig.EnemyIds)
                EnemyDamageDatas[enemyId] = enemyDamageData;
        }
    }

    protected void AddWeaponDamageData(IDictionary<uint, WeaponDamageData> dict, WeaponConfig weaponConfig)
    {
        Log.LogDebug($"Loading weapon config: {weaponConfig.ConfigName} for weapon(s) {(weaponConfig.WeaponIds.Count > 0 ? string.Join(", ", weaponConfig.WeaponIds) : weaponConfig.WeaponId.ToString())}");
        if (weaponConfig.WeaponDamageData == null)
        {
            Log.LogDebug("  ->  Data was NULL; skipping!");
            return;
        }

        if (weaponConfig.WeaponId < 0)
        {
            foreach (uint weaponId in weaponConfig.WeaponIds)
                dict[weaponId] = weaponConfig.WeaponDamageData;
        }
        else
        {
            if (weaponConfig.WeaponIds.Count > 0)
            {
                Log.LogError($" -> WeaponConfig {weaponConfig.ConfigName} defined both WeaponId and WeaponIds; ignoring config");
                return;
            }
            dict[(uint)weaponConfig.WeaponId] = weaponConfig.WeaponDamageData;
        }
    }

    /// <summary>
    /// Converts an enemy config into <see cref="EnemyDamageData"/>, for use when loading damage data
    /// </summary>
    /// <param name="config">The config to convert</param>
    /// <param name="reference">An optional reference block with data</param>
    /// <returns>The converted data</returns>
    public EnemyDamageData ToEnemyDamageData(EnemyConfig config, DamageOverhaulReference? reference = null)
    {
        string configName = $"\"{(config.ConfigName == "" ? "<Unnamed-Config>" : config.ConfigName)}\"";
        EnemyReferenceConfig? configReference = null;
        if (config.EnemyDamageData != null && config.ReferenceIndex != -1)
            Log.LogError($"EnemyConfig {configName} defined both DamageData and ReferenceIndex; ignoring config");
        else if (config.EnemyDamageData != null && config.ReferenceName != "")
            Log.LogError($"EnemyConfig {configName} defined both DamageData and ReferenceName; ignoring config");
        else if (config.ReferenceIndex != -1 && config.ReferenceName != "")
            Log.LogError($"EnemyConfig {configName} defined both ReferenceIndex and ReferenceName; ignoring config");

        else if (config.EnemyDamageData != null)
            configReference = config.EnemyDamageData;
        else if (config.ReferenceIndex >= 0)
        {
            if (reference == null)
                Log.LogError($"EnemyConfig {configName} uses a reference index, but no reference config was found");
            else if (config.ReferenceIndex >= reference.LimbReferences.Count)
                Log.LogError($"EnemyConfig {configName} uses a reference index, but the reference does not have enough entries");
            else
                configReference = reference.EnemyReferences[config.ReferenceIndex];
        }
        else if (config.ReferenceName != "")
        {
            if (reference == null)
                Log.LogError($"EnemyConfig {configName} uses a reference name, but no reference config was found");
            else
            {
                List<EnemyReferenceConfig> test = reference.EnemyReferences.Where(x => x.ConfigName == config.ReferenceName).ToList();
                if (test.Count == 0)
                    Log.LogError($"EnemyConfig {configName} uses a reference name, but the reference config does not contain a EnemyConfig with that name");
                else if (test.Count > 1)
                    Log.LogError($"EnemyConfig {configName} uses a reference name, but the reference config has multiple EnemyConfig with that name");
                else
                    configReference = test[0];
            }
        }
        else
            Log.LogError($"EnemyConfig {configName} does not define damage data or a reference of any kind; it is being ignored");
        if (configReference != null)
        {
            return new EnemyDamageData()
            {
                MaxHealth = configReference.Value.MaxHealth,
                FlatResistance = configReference.Value.FlatResistance,
                FalloffResistance = configReference.Value.FalloffResistance,
                LimbDamageDatas = ToLimbDamageDatas(config.LimbConfigs, reference)
            };
        }
        else
        {
            return new EnemyDamageData()
            {
                LimbDamageDatas = ToLimbDamageDatas(config.LimbConfigs, reference)
            };
        }
    }

    /// <summary>
    /// Converts multiple <see cref="LimbConfig"/> into an array of <see cref="LimbDamageData"/>
    /// </summary>
    /// <param name="configs">The configs to convert</param>
    /// <param name="reference">Optional reference data</param>
    /// <returns>The converted data</returns>
    public LimbDamageData[] ToLimbDamageDatas(List<LimbConfig> configs, DamageOverhaulReference? reference = null)
    {
        var explodedConfigs = configs.SelectMany(c => c.Explode());
        var data = Enumerable.Repeat(LimbDamageData.Default, explodedConfigs.Count()).ToArray();
        foreach (var config in explodedConfigs)
            data[config.LimbId] = ToLimbDamageData(config, reference);
        return data;
    }

    /// <summary>
    /// Converts a single <see cref="LimbConfig"/> into a <see cref="LimbDamageData"/>
    /// </summary>
    /// <param name="config">The config to convert</param>
    /// <param name="reference">Optional reference data</param>
    /// <returns>The converted data</returns>
    public LimbDamageData ToLimbDamageData(LimbConfig config, DamageOverhaulReference? reference)
    {
        string configName = $"\"{(config.ConfigName == "" ? "<Unnamed-Config>" : config.ConfigName)}\"";
        LimbDamageData result = LimbDamageData.Default;
        if (config.LimbDamageData != null && config.ReferenceIndex != -1)
            Log.LogError($"LimbConfig {configName} defined both DamageData and ReferenceIndex; ignoring config");
        else if (config.LimbDamageData != null && config.ReferenceName != "")
            Log.LogError($"LimbConfig {configName} defined both DamageData and ReferenceName; ignoring config");
        else if (config.ReferenceIndex != -1 && config.ReferenceName != "")
            Log.LogError($"LimbConfig {configName} defined both ReferenceIndex and ReferenceName; ignoring config");

        else if (config.LimbDamageData != null) 
            result = config.LimbDamageData;
        else if (config.ReferenceIndex >= 0)
        {
            if (reference == null)
                Log.LogError($"LimbConfig {configName} uses a reference index, but no reference config was found");
            else if (config.ReferenceIndex >= reference.LimbReferences.Count)
                Log.LogError($"LimbConfig {configName} uses a reference index, but the reference does not have enough entries");
            else
                result = reference.LimbReferences[config.ReferenceIndex];
        }
        else if (config.ReferenceName != "")
        {
            if (reference == null)
                Log.LogError($"LimbConfig {configName} uses a reference name, but no reference config was found");
            else
            {
                List<LimbReferenceConfig> test = reference.LimbReferences.Where(x => x.ConfigName == config.ReferenceName).ToList();
                if (test.Count == 0)
                    Log.LogError($"LimbConfig {configName} uses a reference name, but the reference config does not contain a LimbConfig with that name");
                else if (test.Count > 1)
                    Log.LogError($"LimbConfig {configName} uses a reference name, but the reference config has multiple LimbConfigs with that name");
                else
                    result = test[0];
            }
        }
        else
            Log.LogError($"LimbConfig {configName} does not define damage data or a reference of any kind; it is being ignored");
        return result;
    }

}

public static class MethodLogger
{
    public static List<MethodBase> methods = new List<MethodBase>();
    public static void AddMethod<T>(string name) where T : Il2CppSystem.Object => methods.AddRange(typeof(T).GetMethods().Where(m => m.Name == name).Where(m => !m.IsGenericMethod));
    [HarmonyTargetMethods] public static IEnumerable<MethodBase> TargetMethods() => methods.AsEnumerable<MethodBase>();
    
    [HarmonyPrefix]
    public static void Prefix(MethodBase __originalMethod)
    {
        Plugin.Get().Log.LogWarning($"{Plugin.Indent}{__originalMethod.DeclaringType}.{__originalMethod.Name}");
    }

    [HarmonyPostfix]
    public static void Postfix(MethodBase __originalMethod)
    {
        Plugin.Get().Log.LogWarning($"{Plugin.DeIndent}{__originalMethod.DeclaringType}.{__originalMethod.Name}");
    }
}

// For some reason, three references on Dam_EnemyDamageBase will be garbage collected no matter what I do
// This MonoBehaviour gets added on setup to hold the references for it, so it doesn't get collected
public class DamExt_AntiGCHelper : MonoBehaviour
{
    public DamExt_AntiGCHelper(IntPtr ptr) : base(ptr) { }
    public DamExt_AntiGCHelper() : base(ClassInjector.DerivedConstructorPointer<DamExt_AntiGCHelper>())
    { ClassInjector.DerivedConstructorBody(this); }

    public Il2CppReferenceArray<Dam_EnemyDamageLimb>? DamageLimbs { get; set; } = null;
    public Il2CppSystem.Collections.Generic.List<Dam_EnemyDamageLimb>? DamageLimbsWithDestruction { get; set; } = null;
    public Il2CppSystem.Collections.Generic.List<MonoBehaviour>? GlueTargetComps { get; set; } = null;
}