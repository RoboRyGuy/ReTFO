
using AIGraph;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Player;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ReTFO.GeoSpy;

[BepInPlugin(GUID, Name, Version)]
[BepInProcess("GTFO.exe")]
public class Plugin : BasePlugin
{
    public const string Name = "GeoSpy";            // Plugin name
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
        harmony.PatchAll(GetType());
        Log.LogInfo($"{GUID} is loaded!");
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        return true;
    }

    // ================================================================================================================
    
    internal static void OverwriteFPS(PUI_Watermark __instance)
    {
        PlayerAgent? localPlayer = PlayerManager.GetLocalPlayerAgent();
        if (localPlayer?.CourseNode == null) return; // No nav info to utilize

        string positionName = localPlayer.CourseNode.m_area.m_geomorph.name;
        positionName = positionName.Substring(0, positionName.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase));

        bool foundPlug = false;
        foreach (var plug in localPlayer.CourseNode.m_area.m_geomorph.m_plugs)
        {
            Vector3 offset = plug.m_position - localPlayer.Position;
            if (MathF.Abs(offset.x) > 8f || MathF.Abs(offset.z) > 8f) continue;

            LG_PortalDivider? divider = plug.GetComponentInChildren<LG_PortalDivider>();
            if (divider == null) 
                divider = plug.m_pariedWith?.GetComponentInChildren<LG_PortalDivider>();
            if (divider == null)
                continue;

            positionName += $"\n{divider.name}";
            int clonePos = positionName.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
            if (clonePos > 0)
                positionName = positionName.Substring(0, clonePos);
            foundPlug = true;
            break;
        }
        if (!foundPlug)
        {
            int levelGeneration = localPlayer.CourseNode.m_area.name.IndexOf("(LevelGeneration", StringComparison.OrdinalIgnoreCase);
            if (levelGeneration > 0)
                positionName += $"\n{localPlayer.CourseNode.m_area.name.Substring(0, levelGeneration)}";
        }

        __instance.SetPosition(new(-20f, 10f));
        __instance.SetSize(new(600f, 100f));
        __instance.m_fpsText.SetText(positionName);
    }

    [HarmonyPatch(typeof(PUI_Watermark), nameof(PUI_Watermark.UpdateFPS), new Type[] { typeof(string) }), HarmonyPostfix]
    internal static void OverwriteFPS1(PUI_Watermark __instance) => OverwriteFPS(__instance);

    [HarmonyPatch(typeof(PUI_Watermark), nameof(PUI_Watermark.UpdateFPS), new Type[] { typeof(Il2CppStructArray<char>), typeof(int) }), HarmonyPostfix]
    internal static void OverwriteFPS2(PUI_Watermark __instance) => OverwriteFPS(__instance);

    [HarmonyPatch(typeof(WorldEventManager), nameof(WorldEventManager.Update)), HarmonyPostfix]
    public static void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))
        {
            const uint targetID = 25;
            Gear.GearIDRange range = new(GameData.PlayerOfflineGearDataBlock.GetBlock(targetID).GearJSON);
            GameData.ItemDataBlock item = GameData.ItemDataBlock.GetBlock(range.GetCompID(Gear.eGearComponent.BaseItem));

            foreach (var player in SNetwork.SNet.Slots.SlottedPlayers)
                PlayerBackpackManager.Current.EquipSyncGear(item.inventorySlot, range, player);
        }
    }

}