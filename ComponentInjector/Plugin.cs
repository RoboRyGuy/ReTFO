using Agents;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using FluffyUnderware.DevTools.Extensions;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Runtime;
using Steamworks;
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
        
        ClassInjector.RegisterTypeInIl2Cpp<Il2CppAction>();
        harmony.PatchAll(GetType());
        harmony.PatchAll(typeof(Patches));
        
        //AssetShards.AssetShardManager.add_OnEnemyAssetsLoaded(new Il2CppAction(PostAssetsLoaded));
        //AssetShards.AssetShardManager.add_OnSharedAsssetLoaded(new Il2CppAction(PostAssetsLoaded));
        //AssetShards.AssetShardManager.add_OnStartupAssetsLoaded(new Il2CppAction(PostAssetsLoaded));
        
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

    // Callback for when assets are loaded
    private void PostAssetsLoaded()
    {
        foreach (var manifest in AssetShards.AssetShardManager.s_loadedManifests.Values)
        {
            foreach (var obj in manifest.Assets.Values)
            {
                GameObject? go = obj?.TryCast<GameObject>();
                if (go == null) continue;

                foreach (MonoBehaviour mo in go.GetComponentsInChildren<MonoBehaviour>())
                    TryReplaceComponent(mo);
            }
        }
    }

    // Try to replace a component with its injected variant. Returns true if replaced, false otherwise
    public bool TryReplaceComponent(MonoBehaviour comp)
    {
        if (!_injectedComponents.TryGetValue(comp.GetIl2CppType().Pointer, out Il2CppSystem.Type? targetType))
            return false;
        else
            Log.LogDebug($"Replacing instance of {comp.GetIl2CppType().Name} on {comp.name} with {targetType.Name}");

        GameObject go = comp.gameObject;
        MonoBehaviour newComp = go.AddComponent(targetType).TryCast<MonoBehaviour>()
            ?? throw new NullReferenceException($"Failed to add object of type \"{targetType.Name}\" to GameObject");
        //newComp.hideFlags = HideFlags.HideAndDontSave;

        //CopyProperties(comp, newComp);
        CopyFields(comp, newComp);
        ReplaceReferences(comp.gameObject, new Dictionary<IntPtr, Component>() { { comp.Pointer, newComp } });

        GameObject.Destroy(comp);
        return true;
    }

    // Takes a UnityObject and attempts all injections on it. If an injection is successful, a new replacement object is generated,
    //  which is returned via out; this replacement must be used instead of the original object
    public bool TryReplace(UnityEngine.Object? obj, [NotNullWhen(true)] out UnityEngine.Object? result)
    {
        result = null;

        GameObject? go = obj?.TryCast<GameObject>();
        if (go == null) return false;

        bool needsReplacement = false;
        foreach (var comp in go.GetComponentsInChildren<MonoBehaviour>())
            needsReplacement= needsReplacement|| _injectedComponents.ContainsKey(comp.GetIl2CppType().Pointer);
        if (!needsReplacement) return false;

        Dictionary<IntPtr, Component> pairs = new();
        GameObject resultGo = GetNewGameObject(go, ref pairs);
        ReplaceReferences(resultGo, pairs);
        result = resultGo;
        return true;
    }

    // Recursive GameObject generation
    private GameObject GetNewGameObject(GameObject go, ref Dictionary<IntPtr, Component> pairs)
    {
        GameObject resultGo = new(go.name + " <replacement>") { hideFlags = HideFlags.HideAndDontSave };
        resultGo.active = go.active;
        bool issue = false;
        foreach (var oldComp in go.GetComponents<Component>())
        {
            if (oldComp.ObjectClass == Il2CppClassPointerStore<Transform>.NativeClassPtr)
                continue;
            Component newComp = resultGo.AddComponent(oldComp.GetIl2CppType()); // Note that this is patched
            if (newComp == null)
            {
                issue = true;
                break;
            }
            CopyProperties(oldComp, newComp); // Copy properties first; if we can access the backing field, we'll overwrite it in CopyFields
            CopyFields(oldComp, newComp);
            newComp.hideFlags = HideFlags.HideAndDontSave;
            pairs.Add(oldComp.Pointer, newComp);
        }

        if (issue)
        {
            // Just duplicate it then. Skip replacements on children / self
            Log.LogWarning("Failed to duplicate one or more components during component injection. Skipping subobjects as a result");
            foreach (Component comp in go.GetComponents<Component>())
                pairs.Remove(comp.Pointer);
            GameObject.Destroy(resultGo);
            resultGo = GameObject.Instantiate(go);
            resultGo.active = go.active;
            return resultGo;
        }

        foreach (var child in go.transform)
        {
            Transform trans = child.TryCast<Transform>() ?? throw new NullReferenceException("Unkown error while fetching GameObject children");
            GameObject newChild = GetNewGameObject(trans.gameObject, ref pairs);
            newChild.transform.CopyFrom(trans);
            newChild.transform.SetParent(resultGo.transform, false);
        }

        return resultGo;
    }

    // Copy fields from oldComp to newComp
    private void CopyFields(Component oldComp, Component newComp)
    {
        Dam_EnemyDamageBase? dam1 = oldComp.TryCast<Dam_EnemyDamageBase>();
        Dam_EnemyDamageBase? dam2 = newComp.TryCast<Dam_EnemyDamageBase>();

        Il2CppSystem.Type? 
            type = oldComp.GetIl2CppType(),
            excludeType = Il2CppType.From(typeof(MonoBehaviour));

        Il2CppSystem.Reflection.BindingFlags bf = 0u
            | Il2CppSystem.Reflection.BindingFlags.Instance 
            | Il2CppSystem.Reflection.BindingFlags.Public 
            | Il2CppSystem.Reflection.BindingFlags.NonPublic 
            | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly
        ;

        while (type != null)
        {
            // Ignore nonpublic fields when copying Unity internal types
            //if (type.IsAssignableFrom(excludeType))
            //    bf &= ~Il2CppSystem.Reflection.BindingFlags.NonPublic;

            var fields = type.GetFields(bf)
                .Where(f => !f.IsStatic);

            foreach (var field in fields)
            {
                try
                {
                    Il2CppSystem.Object value = field.GetValue(oldComp);
                    field.SetValue(newComp, value);
                    Log.LogDebug($" -> Copied field {type.FullName}.{field.Name}");
                }
                catch (System.Reflection.TargetInvocationException e)
                {
                    if (e.InnerException is NullReferenceException)
                        continue;
                    Log.LogWarning($"Failed to copy field {field.Name} from type {oldComp.GetIl2CppType().Name} to type {newComp.GetIl2CppType().Name}");
                }
                catch
                {
                    Log.LogWarning($"Failed to copy field {field.Name} from type {oldComp.GetIl2CppType().Name} to type {newComp.GetIl2CppType().Name}");
                }
            }

            type = type.BaseType;
        }
    }

    // Copy properties from oldComp to newComp
    private void CopyProperties(Component oldComp, Component newComp)
    {
        Il2CppSystem.Type? 
            type = oldComp.GetIl2CppType(),
            excludeType = Il2CppType.From(typeof(MonoBehaviour));

        Il2CppSystem.Reflection.BindingFlags bf = 0
            | Il2CppSystem.Reflection.BindingFlags.Instance
            | Il2CppSystem.Reflection.BindingFlags.Public
            | Il2CppSystem.Reflection.BindingFlags.NonPublic
            | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly
        ;

        while (type != null)
        {
            // Ignore nonpublic fields when copying Unity internal types
            //if (type.IsAssignableFrom(excludeType))
            //    bf &= ~Il2CppSystem.Reflection.BindingFlags.NonPublic;

            var properties = type.GetProperties(bf)
                .Where(p => p.GetIndexParameters().Length <= 0)
                .Where(p => !((p.GetGetMethod() ?? p.GetSetMethod())?.IsStatic ?? false))
                .Where(p => p.CanRead && p.CanWrite);

            foreach (var prop in properties)
            {
                try
                {
                    Il2CppSystem.Object value = prop.GetValue(oldComp);
                    prop.SetValue(newComp, value);
                    Log.LogDebug($" -> Copied property {type.FullName}.{prop.Name}");
                }
                catch (System.Reflection.TargetInvocationException e)
                {
                    if (e.InnerException is NullReferenceException)
                        continue;
                    Log.LogWarning($"Failed to copy property {prop.Name} from type {oldComp.GetIl2CppType().Name} to type {newComp.GetIl2CppType().Name}");
                }
                catch
                {
                    Log.LogWarning($"Failed to copy property {prop.Name} from type {oldComp.GetIl2CppType().Name} to type {newComp.GetIl2CppType().Name}");
                }
            }

            type = type.BaseType;
        }
    }

    // Replace direct and array references. Won't help with other containers, however
    private void ReplaceReferences(GameObject go, Dictionary<IntPtr, Component> pairs)
    {
        Transform parent = go.transform;
        while (parent.parent != null) parent = parent.parent;

        var bf = 0u
            | Il2CppSystem.Reflection.BindingFlags.Instance
            | Il2CppSystem.Reflection.BindingFlags.Public
            | Il2CppSystem.Reflection.BindingFlags.NonPublic
            | Il2CppSystem.Reflection.BindingFlags.FlattenHierarchy
        ;

        foreach (Component mo in parent.GetComponentsInChildren<Component>())
        {
            var type = mo.GetIl2CppType();
            var fields = type.GetFields(bf);
            foreach (var field in fields)
            {
                IntPtr ptr = field.GetValue(mo)?.Pointer ?? IntPtr.Zero;
                if (pairs.TryGetValue(ptr, out var newComp))
                {
                    field.SetValue(mo, newComp);
                    Log.LogMessage($"Replaced field {type.FullName}.{field.Name} with new comp");
                }
                else if (field.FieldType.IsArray)
                {
                    Il2CppSystem.Array? arr = field.GetValue(mo)?.TryCast<Il2CppSystem.Array>();
                    if (arr == null) continue;
            
                    for (int i = 0; i < arr.Length; i++)
                    {
                        ptr = arr.GetValue(i)?.Pointer ?? IntPtr.Zero;
                        if (pairs.TryGetValue(ptr, out newComp))
                        {
                            arr.SetValue(newComp, i);
                            Log.LogMessage($"Replaced arr item {i} in field {type.FullName}.{field.Name} with new comp");
                        }
                    }
                }
            }

            var properties = type.GetProperties(bf);
            foreach (var property in properties)
            {
                if (!property.CanRead || !property.CanWrite)
                    continue;

                IntPtr ptr = property.GetValue(mo)?.Pointer ?? IntPtr.Zero;
                if (pairs.TryGetValue(ptr, out var newComp))
                {
                    property.SetValue(mo, newComp);
                    Log.LogMessage($"Replaced property {type.FullName}.{property.Name} with new comp");
                }
                else if (property.PropertyType.IsArray)
                {
                    Il2CppSystem.Array? arr = property.GetValue(mo)?.TryCast<Il2CppSystem.Array>();
                    if (arr == null) continue;
            
                    for (int i = 0; i < arr.Length; i++)
                    {
                        ptr = arr.GetValue(i)?.Pointer ?? IntPtr.Zero;
                        if (pairs.TryGetValue(ptr, out newComp))
                        {
                            arr.SetValue(newComp, i);
                            Log.LogMessage($"Replaced arr item {i} in property {type.FullName}.{property.Name} with new comp");
                        }
                    }
                }
            }
        }
    }
}
