using HarmonyLib;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features;

/// <summary>
/// Adds patches to feature classes to ensure FeatureLogger has the correct Logger associated with it
/// </summary>]
[ForceDisable("Too much overhead.\nPotential memory leak issues.")]
public class StatefulFeatureLogger : Feature
{
    public override string Name => "Stateful Feature Logging";
    public override string Description 
        => "Modifies Archipelago features to track which is executing.\n"
        + "Makes some logs (errors, warnings, etc) appear to come from the currently executing feature.";
    public override FeatureGroup Group => FeatureGroups.Archipelago;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private Harmony? m_harmony = null;
    public Harmony Harmony
    {
        get => m_harmony ?? new($"{Plugin.GUID}.StatefulFeatureLogger");
        protected set => m_harmony = value;
    }

    public override void OnEnable()
    {
        Harmony.PatchAll(typeof(__OnExecute__Patch));
    }

    public override void OnDisable()
    {
        Harmony.UnpatchSelf();
    }

    /// <summary>
    /// Wraps callbacks and patches in an ArchipelagoFeature and updates FeatureLogger.Current
    /// </summary>
    [HarmonyPatch]
    private static class __OnExecute__Patch
    {
        // Retrieve callback and patch methods associated with ArchipelagoFeature classes
        // Performs a recursive search on all declared types
        public static IEnumerable<MethodBase> GetMethodsRecursive(Type type, Type? featureType = null)
        {
            const BindingFlags bf = 0
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly
            ;

            if (type.IsAssignableTo(typeof(ArchipelagoFeature)))
                featureType = type;

            IEnumerable<MethodBase> result = type.GetNestedTypes().SelectMany(t => GetMethodsRecursive(t, featureType));
            if (featureType != null)
            {
                var callbacks = type.GetMethods(bf).Where(m => m.CustomAttributes.Any(ca => ca.AttributeType.IsAssignableTo(typeof(Game.IProcessor.CallbackBase))));
                var patches = type.GetNestedTypes()
                    .Where(t => t.CustomAttributes.Any(ca => ca.AttributeType.IsAssignableTo(typeof(ArchivePatch))))
                    .SelectMany(t => t.GetMethods(bf));
                result = result.Concat(callbacks).Concat(patches);
            }
            return result;
        }

        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch (ReflectionTypeLoadException e)
                    {
                        return e.Types.OfType<Type>();
                    }
                }
                )
                .SelectMany(t => GetMethodsRecursive(t));
        }

        [HarmonyPrefix]
        public static void Prefix(IArchiveLogger __state, MethodBase __originalMethod)
        {
            const BindingFlags bf = 0
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly
            ;

            __state = FeaturesAPI.FeatureLogger.Current;

            // Recursive helper to find the FeatureLogger. We're guaranteed to get ArchipelagoFeature.FeatureLogger at the very least
            static PropertyInfo Search(Type type)
                => type.GetProperty(nameof(FeatureLogger), bf) ?? Search(type.DeclaringType!);

            FeaturesAPI.FeatureLogger.Current = (Search(__originalMethod.DeclaringType!).GetValue(null) as IArchiveLogger)!;
        }

        [HarmonyPostfix]
        public static void Postfix(IArchiveLogger __state, MethodBase __originalMethod)
        {
            FeaturesAPI.FeatureLogger.Current = __state;
        }
    }
}
