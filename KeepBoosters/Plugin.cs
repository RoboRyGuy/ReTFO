
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System.Diagnostics.CodeAnalysis;

namespace ReTFO.KeepBoosters;

[BepInPlugin(GUID, Name, Version)]
[BepInProcess("GTFO.exe")]
public class Plugin : BasePlugin
{
    public const string Name = "KeepBoosters";      // Plugin name
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

    // Vanity, to help players feel assured their boosters will stick
    [HarmonyPostfix, HarmonyPatch(typeof(BoosterImplant), nameof(BoosterImplant.GetCompositPublicName), new Type[] { typeof(bool) })]
    public static void PostGetPublicName(BoosterImplant __instance) => __instance.Uses = 10;

    // Prevent the game session from registering which boosters we're using
    [HarmonyPrefix, HarmonyPatch(typeof(DropServerManager), nameof(DropServerManager.NewGameSession))]
    public static void PreNewGameSession(ref Il2CppStructArray<uint> boosterIds) => boosterIds = new Il2CppStructArray<uint>(0L)!;

    // Prevents the game session from issuing ConsumeBooster commands
    [HarmonyPrefix, HarmonyPatch(typeof(DropServerGameSession), nameof(DropServerGameSession.ConsumeBoosters))]
    public static bool PreIssueConsumeBoosters() => false;
}