using CellMenu;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using System;
using System.Collections.Generic;
using System.Reflection;
using TheArchive.Core.FeaturesAPI.Settings;
using TheArchive.Features.Dev;

namespace ReTFO.Archipelago.Patches;

/// <summary>
/// Modifies the Archive's ModSettings feature to allow string settings to be private
/// </summary>
[HarmonyPatch, InjectToIl2Cpp]
public static class PrivateFeatureSettingsPatch
{
    [AttributeUsage(AttributeTargets.Property)]
    public class FSOptionallyPrivate : Attribute { }

    public interface IOptionallyPrivate
    {
        public bool IsCurrentlyPrivate { get; }
    }

    /// <summary>
    /// Wrapper around the an input receiver which will signal if we need to make the the input private
    /// </summary>
    [InjectToIl2Cpp(typeof(iStringInputReceiver))]
    private class WrappedCustomStringReceiver : Il2CppSystem.Object
    {
        public WrappedCustomStringReceiver(IntPtr ptr) : base(ptr) { }
        public WrappedCustomStringReceiver(iStringInputReceiver wrappedReceiver, Func<bool>? isCurrentlyPrivate)
            : base(ClassInjector.DerivedConstructorPointer<WrappedCustomStringReceiver>())
        {
            ClassInjector.DerivedConstructorBody(this);
            WrappedReceiver = wrappedReceiver;
            IsCurrentlyPrivate = isCurrentlyPrivate;
        }

        public string GetStringValue(eCellSettingID setting)
            => WrappedReceiver.GetStringValue(setting);

        public string SetStringValue(eCellSettingID setting, string value)
            => WrappedReceiver.SetStringValue(setting, value);

        [HideFromIl2Cpp]
        public iStringInputReceiver WrappedReceiver { get; set; } = null!;

        [HideFromIl2Cpp]
        public Func<bool>? IsCurrentlyPrivate { get; set; } = null;
    }

    /// <summary>
    /// When TheArchive creates the receiver, catch it and replace it with our wrapper if necessary
    /// </summary>
    [HarmonyPatch]
    public static class ModSettings__SettingsCreationHelper__CreateStringSetting__Patch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type type = typeof(ModSettings);
            Type nestedType = type.GetNestedType("SettingsCreationHelper", AccessTools.all)!;
            yield return AccessTools.Method(nestedType, "CreateStringSetting");
            yield return AccessTools.Method(nestedType, "CreateNumberSetting");
        }

        [HarmonyPostfix]
        public static void Postfix(FeatureSetting setting)
        {
            var attribute = setting.Prop.GetCustomAttribute<FSOptionallyPrivate>();
            if (attribute == null) return;
            if (setting is NumberSetting num && num.HasSlider) return;

            object wrappedInstance = AccessTools.Property(typeof(FeatureSetting), "WrappedInstance")!.GetValue(setting)!;
            IOptionallyPrivate? unwrappedInstance = wrappedInstance as IOptionallyPrivate;
            if (unwrappedInstance == null)
                throw new NullReferenceException();

            CM_SettingsItem settingsItem = (setting.CM_SettingsItem as Il2CppSystem.Object)!.Cast<CM_SettingsItem>();
            CM_SettingsInputField inputField = settingsItem.m_inputAlign.GetChild(settingsItem.m_inputAlign.childCount - 1).GetComponent<CM_SettingsInputField>();

            WrappedCustomStringReceiver newReceiver = new(inputField.m_stringReceiver, () => unwrappedInstance.IsCurrentlyPrivate);
            inputField.m_stringReceiver = new iStringInputReceiver(newReceiver.Pointer);
        }
    }
    
    /// <summary>
    /// When input fields update, detect our wrapper and hide the field if necessary
    /// </summary>
    [HarmonyPatch]
    public static class CM_SettingsInputField__Patch
    {
        [HarmonyPatch(typeof(CM_SettingsInputField), nameof(CM_SettingsInputField.Update)), HarmonyPostfix]
        public static void PostUpdate(CM_SettingsInputField __instance)
        {
            if (__instance.m_readingActive) return;
        
            WrappedCustomStringReceiver? receiver = new Il2CppSystem.Object(__instance.m_stringReceiver.Pointer).TryCast<WrappedCustomStringReceiver>();
            if (receiver == null) return;
        
            if (receiver.IsCurrentlyPrivate?.Invoke() ?? true)
                __instance.m_text.SetText(new string('*', __instance.m_text.text.Length));
            else
                __instance.m_text.SetText(__instance.m_currentValue);
        }
    }

}
