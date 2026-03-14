using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;

namespace ReTFO.Archipelago.FeaturesAPI;

/// <summary>
/// Contains helper methods for Archipelago Features, which can be called directly
/// where necessary
/// </summary>
public static class ArchipelagoFeatureHelper
{

    [ForceDisable("Internal archipelago feature used only for finding features"), HideInModSettings]
    internal class FakeFeature : ArchipelagoFeature
    {
        public override string Name => "Archipelago Fake Feature";
        public override string Description => "Feature used by Archipelago for hash lookups";
        public override FeatureGroup Group => FeatureGroups.Archipelago;

        public Type? WrappedType { get; set; }
        public FakeFeature()
            => WrappedType = null;
        public FakeFeature(Type intendedType)
            => WrappedType = intendedType;
        public override bool Equals(object? obj)
            => WrappedType?.Equals(obj?.GetType()) ?? obj == null;
        public override int GetHashCode()
            => WrappedType?.GetHashCode() ?? 0;
    }

    /// <summary>
    /// Get the feature of the provided type from the set of registered of features
    /// </summary>
    /// <typeparam name="TFeature">The type of feature to retrieve</typeparam>
    /// <returns>The registered feature</returns>
    /// <exception cref="KeyNotFoundException">There is no feature of the given type registered</exception>
    /// <exception cref="NotSupportedException">The found feature was somehow of the wrong type</exception>
    public static TFeature GetFeature<TFeature>()
        where TFeature : ArchipelagoFeature
    {
        Exception? e = null;
        if (!FeatureManager.Instance.RegisteredFeatures.TryGetValue(new FakeFeature(typeof(TFeature)), out Feature? feature))
        {
            e = new KeyNotFoundException($"Could not find a registered feature of type {typeof(TFeature).FullName}");
            FeatureLogger.Exception(e);
            throw e;
        }

        if (feature is not TFeature tFeature)
        {
            e = new NotSupportedException($"When searching for registered feature of type {typeof(TFeature).FullName}, instead got a feature of type {feature.GetType().FullName}");
            FeatureLogger.Exception(e);
            throw e;
        }

        return tFeature;
    }

    /// <summary>
    /// Get processor callback method infos for a particular type.
    /// </summary>
    /// <param name="type">The type to get callbacks for</param>
    /// <returns>MethodInfos for the callbacks</returns>
    public static IEnumerable<MethodInfo> GetProcessorCallbacks(Type type)
    {
        const BindingFlags bf = 0
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic
        ;

        return type.GetMethods(bf)
                .Where(m => m.CustomAttributes.Any(a => a.AttributeType.IsAssignableTo(typeof(Game.IProcessor.CallbackBase))));
    }

    /// <summary>
    /// Get processor callback method infos for a particular type which are instanced (non-static) methods.
    /// </summary>
    /// <param name="type">The type to get callbacks for</param>
    /// <returns>MethodInfos for the callbacks</returns>
    public static IEnumerable<MethodInfo> GetInstancedProcessorCallbacks(Type type)
        => GetProcessorCallbacks(type).Where(m => !m.IsStatic);

    /// <summary>
    /// Register callbacks (Game.Callback, Expedition.Callback, etc) from the given type to Game.Data
    /// </summary>
    /// <param name="instance">The object with callbacks to register</param>
    public static void RegisterInstancedCallbacks(object instance)
    {
        var methods = GetInstancedProcessorCallbacks(instance.GetType());
        var gameData = Plugin.Get().MidManager.GetUnprocessedGameData();
        foreach (var method in methods)
        {
            var attribute = method.GetCustomAttribute<Game.IProcessor.CallbackBase>();
            if (attribute == null)
                throw new NotSupportedException("After checking for the Callback attribute, it was still somehow null!");

            Game.IProcessor processor = gameData.GetProcessor(attribute.DataType);
            Delegate? del = Delegate.CreateDelegate(attribute.DelegateType, instance, method, false);
            if (del == null)
                FeatureLogger.Error($"Failed to bind delegate! Method: {method.DeclaringType!.FullName}.{method.Name} Delegate Type: {attribute.DelegateType.DeclaringType}.{attribute.DelegateType.Name}");
            else
                processor.UntypedRegisterCallback(del);
        }
    }

    /// <summary>
    /// Unregister callbacks (Game.Callback, Expedition.Callback, etc) from the given type to Game.Data
    /// </summary>
    /// <param name="instance">The object with callbacks to register</param>
    public static void UnregisterInstancedCallbacks(object instance)
    {
        var methods = GetInstancedProcessorCallbacks(instance.GetType());
        var gameData = Plugin.Get().MidManager.GetUnprocessedGameData();
        foreach (var method in methods)
        {
            var attribute = method.GetCustomAttribute<Game.IProcessor.CallbackBase>();
            if (attribute == null)
                throw new NotSupportedException("After checking for the Callback attribute, it was still somehow null!");

            Game.IProcessor processor = gameData.GetProcessor(attribute.DataType);
            Delegate del = Delegate.CreateDelegate(attribute.DelegateType, instance, method);
            processor.UntypedUnregisterCallback(del);
        }
    }

}
