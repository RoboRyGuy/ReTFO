using Agents;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace ReTFO.ComponentInjector;

[BepInPlugin(GUID, Name, Version)]
[BepInProcess("GTFO.exe")]
public class Plugin : BasePlugin
{
    public const string Name = "ComponentInjector"; // Plugin name
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
        harmony.PatchAll(typeof(LoadPatches));
        Log.LogInfo($"{GUID} is loaded!");
    }

    public override bool Unload()
    {
        harmony.UnpatchSelf();
        return true;
    }

    // ================================================================================================================

    // Internal map of which components to replace with which
    private Dictionary<IntPtr, Il2CppSystem.Type> _injectedComponents = new();
    
    // Add a new type to inject
    public bool InjectComponent<TOriginal, TNew>()
        => InjectComponent(Il2CppClassPointerStore<TOriginal>.NativeClassPtr, Il2CppClassPointerStore<TNew>.NativeClassPtr);

    // Add a new type to inject
    public bool InjectComponent(Type originalType, Type newType)
        => InjectComponent(Il2CppClassPointerStore.GetNativeClassPointer(originalType), Il2CppClassPointerStore.GetNativeClassPointer(newType));

    // Add a new type to inject
    public bool InjectComponent(IntPtr originalClassPointer, IntPtr newClassPointer)
        => InjectComponent(Il2CppType.TypeFromPointer(originalClassPointer), Il2CppType.TypeFromPointer(newClassPointer));
    
    // Add a new type to inject
    public bool InjectComponent(Il2CppSystem.Type originalType, Il2CppSystem.Type newType)
    {
        if (!newType.IsSubclassOf(originalType))
        {
            Log.LogError($"Failed to inject type \"{newType.Name}\" as \"{originalType.Name}\"; it does not inherit from the original type");
            return false;
        }

        if (!_injectedComponents.TryAdd(originalType.Pointer, newType))
        {
            Log.LogError($"Failed to inject type \"{newType.Name}\" as \"{originalType.Name}\"; it is already mapped to \"{_injectedComponents[originalType.Pointer].Name}\"");
            return false;
        }
        else return true;
    }

    // Gets all type mappings
    public IReadOnlyDictionary<IntPtr, Il2CppSystem.Type> InjectedComponents => _injectedComponents;

    // Try to replace a component with the injected variant. Returns true if replaced, false otherwise
    public bool ReplaceComponent(MonoBehaviour comp)
    {
        if (!_injectedComponents.TryGetValue(comp.GetIl2CppType().Pointer, out Il2CppSystem.Type? targetType))
            return false;

        MonoBehaviour newComp = comp.gameObject.AddComponent(targetType).TryCast<MonoBehaviour>() 
            ?? throw new NullReferenceException($"Failed to add object of type \"{targetType.Name}\" to GameObject");

        CopyFields(comp.GetIl2CppType(), comp, newComp);
        GameObject.Destroy(comp);
        return true;
    }

    // Recursive field copy, stops just before copying MonoBehaviour fields
    private static void CopyFields(Il2CppSystem.Type? type, MonoBehaviour source, MonoBehaviour dest)
    {
        if (type == null || type.ObjectClass == Il2CppClassPointerStore<MonoBehaviour>.NativeClassPtr)
            return;

        var bf = 0
            | Il2CppSystem.Reflection.BindingFlags.Public
            | Il2CppSystem.Reflection.BindingFlags.NonPublic
            | Il2CppSystem.Reflection.BindingFlags.Instance
            | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly
        ;
        var fields = type.GetFields(bf);
        foreach (var field in fields)
            field.SetValue(dest, field.GetValue(source));

        CopyFields(type.BaseType, source, dest);
    }
}
