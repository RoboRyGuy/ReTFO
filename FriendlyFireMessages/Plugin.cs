using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Player;
using SNetwork;
using System.Diagnostics.CodeAnalysis;

namespace ReTFO.GeoSpy;

[BepInPlugin(GUID, Name, Version)]
[BepInProcess("GTFO.exe")]
public class Plugin : BasePlugin
{
    public const string Name = "FriendlyFireMessages"; // Plugin name
    public const string Author = "RoboRyGuy";          // Plugin author
    public const string GUID = $"{Author}.{Name}";     // Plugin GUID, unique identifier used by BepInEx
    public const string Version = "1.0.0";             // Plugin version, can be used by System.Version

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
        ClassInjector.RegisterTypeInIl2Cpp(typeof(UpdateComponent));
        ClassInjector.RegisterTypeInIl2Cpp(typeof(UpdateCoroutine), new RegisterTypeOptions() { Interfaces = new Type[] { typeof(Il2CppSystem.Collections.IEnumerator) } });
        harmony.PatchAll(GetType());

        UpdateComponent comp = AddComponent<UpdateComponent>();
        UpdateCoroutine routine = new UpdateCoroutine(this);
        comp.UpdateCoroutine = comp.StartCoroutine(new Il2CppSystem.Collections.IEnumerator(routine.Pointer));
        
        Log.LogInfo($"{GUID} is loaded!");
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        return true;
    }

    // ================================================================================================================

    /// <summary>
    /// How often to check to report damage updates
    /// </summary>
    const float UPDATE_DELAY = .1f;

    /// <summary>
    /// How long to wait until reporting damage, to check for accumulation
    /// </summary>
    const float TIMEOUT = .4f;

    /// <summary>
    /// Component which handles running our coroutine
    /// </summary>
    public class UpdateComponent : UnityEngine.MonoBehaviour
    {
        /// <summary>
        /// Update coroutine
        /// </summary>
        public UnityEngine.Coroutine? UpdateCoroutine = null;
    }

    /// <summary>
    /// Coroutine used to perform updates
    /// </summary>
    public class UpdateCoroutine : Il2CppSystem.Object
    {
        public UpdateCoroutine(Plugin plugin) 
            : base(ClassInjector.DerivedConstructorPointer<UpdateCoroutine>())
        {
            ClassInjector.DerivedConstructorBody(this);
            this.plugin = plugin;
        }
        public UpdateCoroutine(IntPtr ptr) : base(ptr) { }

        private Plugin plugin = null!;
        public Il2CppSystem.Object Current => new UnityEngine.WaitForSeconds(UPDATE_DELAY);
        public bool MoveNext()
        {
            plugin.Update();
            return true;
        }
        public void Reset() { }
    }

    /// <summary>
    /// Used by our sorted list
    /// </summary>
    public class SimpleComparer : IComparer<(PlayerAgent?, PlayerAgent, bool)>
    {
        public int Compare((PlayerAgent?, PlayerAgent, bool) x, (PlayerAgent?, PlayerAgent, bool) y)
        {
            int test = (x.Item1?.Pointer ?? IntPtr.Zero).CompareTo((y.Item1?.Pointer ?? IntPtr.Zero));
            if (test != 0) return test;
            test = x.Item2.Pointer.CompareTo(y.Item2.Pointer);
            if (test != 0) return test;
            return x.Item3.CompareTo(y.Item3);
        }
    }

    /// <summary>
    /// Tracks accumulated damage.
    /// (Source, Target, IsSentry) => (FixedTime it was updated, CumulativeDamage, BulletCount)
    /// </summary>
    public SortedList<(PlayerAgent?, PlayerAgent, bool), (float, float, int)> cumulativeDamage { get; private init; } = new(new SimpleComparer());

    /// <summary>
    /// Process received bullet data to see if friendly fire occured and track it
    /// </summary>
    public static void ReceiveBullet(Dam_PlayerDamageBase __instance, pBulletDamageData data)
    {
        Agents.Agent sourceAgent;
        if (!data.source.TryGet(out sourceAgent))
            return;
        else if (sourceAgent.Type != Agents.AgentType.Player) 
            return;

        Plugin plugin = Plugin.Get();
        PlayerAgent? source = sourceAgent.TryCast<PlayerAgent>();

        // Note: The gear category is always 0 in vanilla, so this won't work without a patch... added below :)
        bool isSentry = source != null && data.gearCategoryId != 0
            && GameData.GearCategoryDataBlock.GetBlock(data.gearCategoryId).BaseItem == 97u;

        (PlayerAgent?, PlayerAgent, bool) key = (source, __instance.Owner, isSentry);
        if (__instance.Owner.Locomotion.CurrentState.Pointer == __instance.Owner.Locomotion.Downed.Pointer)
        {   // It's fun to know how much overkill was applied before a player died, 
            // but we don't really care about damage dealt while they were down
            if (!plugin.cumulativeDamage.ContainsKey(key)) return;
        }

        float damage = data.damage.Get(__instance.DamageMax);
        int count = 1;
        if (plugin.cumulativeDamage.TryGetValue(key, out (float, float, int) temp))
        {
            damage += temp.Item2;
            count += temp.Item3;
        }
        plugin.cumulativeDamage[key] = (UnityEngine.Time.fixedTime, damage, count);
    }

    [HarmonyPatch(typeof(Dam_PlayerDamageBase), nameof(Dam_PlayerDamageBase.ReceiveBulletDamage)), HarmonyPostfix]
    public static void ReceiveBullet_Synced(Dam_PlayerDamageBase __instance, pBulletDamageData data)
        => ReceiveBullet(__instance, data);

    [HarmonyPatch(typeof(Dam_PlayerDamageLocal), nameof(Dam_PlayerDamageLocal.ReceiveBulletDamage)), HarmonyPostfix]
    public static void ReceiveBullet_Local(Dam_PlayerDamageLocal __instance, pBulletDamageData data)
        => ReceiveBullet(__instance, data);

    /// <summary>
    /// Helper I swiped from my Archipelago mod. Just logs messages locally
    /// </summary>
    public void LogForLobby(string message)
    {
       Log.LogInfo(message);

        var logs = Enumerable.Empty<PUI_GameEventLog>()
            .Append(MainMenuGuiLayer.Current.PageLoadout?.m_gameEventLog)
            .Append(MainMenuGuiLayer.Current.PageMap?.m_gameEventLog)
            .Append(GuiManager.PlayerLayer?.m_gameEventLog)
        ;

        foreach (var log in logs)
        {
            if (log == null) continue;
            log.AddLogItem(message);

            // This part is important. It somehow prevents crashes :)
            log.UpdateHeightOffset();
        }
    }

    /// <summary>
    /// Report cumulative damage if it's hit its timeout
    /// </summary>
    public void Update()
    {
        float cutoffTime = UnityEngine.Time.fixedTime - TIMEOUT;

        for (int i = 0; i < cumulativeDamage.Count; i++)
        {
            var value = cumulativeDamage.Values[i];
            if (value.Item1 > cutoffTime) continue;
            var key = cumulativeDamage.Keys[i];
            cumulativeDamage.RemoveAt(i--);

            float percentDamage = (value.Item2 / key.Item2.Damage.m_playerData.health) * 100f;
            char hex(float x) => $"{(int)(x * 255.0):X2}"[0];
            string name(SNet_Player player) => $"<#{hex(player.PlayerColor.r)}{hex(player.PlayerColor.g)}{hex(player.PlayerColor.b)}>{player.NickName}</color>";

            LogForLobby($"{(key.Item1 == null ? "<i>Unknown</i>" : name(key.Item1.Owner))}{(key.Item3 ? "'s sentry" : string.Empty)} shot {name(key.Item2.Owner)} for {percentDamage:0.0}% ({value.Item3} bullets)");
        }
    }

    // ================================================================================================================
    // Set of patches to fix gear category not being networked

    private static uint s_cachedGearID = 0u;

    [HarmonyPatch(typeof(Gear.BulletWeapon), nameof(Gear.BulletWeapon.Fire)), HarmonyPrefix]
    public static void PreBulletFire(Gear.BulletWeapon __instance)
        => s_cachedGearID = __instance.GearCategoryData.persistentID;

    [HarmonyPatch(typeof(Gear.BulletWeaponSynced), nameof(Gear.BulletWeaponSynced.Fire)), HarmonyPrefix]
    public static void PreBulletFire_Synced(Gear.BulletWeapon __instance)
        => s_cachedGearID = __instance.GearCategoryData.persistentID;

    [HarmonyPatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.FireBullet)), HarmonyPrefix]
    public static void PreBulletHit(SentryGunInstance_Firing_Bullets __instance)
        => s_cachedGearID = __instance.m_core.TryCast<SentryGunInstance>()?.GearCategoryData.persistentID ?? 0u;

    [HarmonyPatch(typeof(Dam_SyncedDamageBase), nameof(Dam_SyncedDamageBase.BulletDamage)), HarmonyPrefix]
    public static bool PreBulletDamage(Dam_SyncedDamageBase __instance, float dam, Agents.Agent sourceAgent, UnityEngine.Vector3 position, UnityEngine.Vector3 direction, UnityEngine.Vector3 normal, bool allowDirectionalBonus, float staggerMulti, float precisionMulti, uint gearCategoryId)
    {
        // 10f is a magic number which I identified through trial and error. Glad it was a whole number :)
        gearCategoryId = s_cachedGearID;
        pBulletDamageData data = new();
        data.allowDirectionalBonus = allowDirectionalBonus;
        data.damage.Set(dam, __instance.DamageMax);
        data.direction.Value = direction;
        data.gearCategoryId = gearCategoryId;
        data.limbID = 0;
        data.localPosition.Set(__instance.transform.InverseTransformPoint(position), 10f);
        data.precisionMulti.Set(precisionMulti, 10f);
        data.source.Set(sourceAgent);
        data.staggerMulti.Set(staggerMulti, 10f);

        // I believe I did this section correctly, but it's hard to verify
        if (__instance.SendLocally())
            __instance.ReceiveBulletDamage(data);
        else if (__instance.SendPacket())
        {
            if (__instance.m_onlyToMaster)
            {
                __instance.m_bulletDamagePacket.Send(
                    data,
                    SNet_ChannelType.GameOrderCritical,
                    SNet.Master
                );
            }
            else
            {
                __instance.m_bulletDamagePacket.Send(
                    data,
                    SNet_ChannelType.GameOrderCritical
                );
            }
        }

        return false;
    }
}