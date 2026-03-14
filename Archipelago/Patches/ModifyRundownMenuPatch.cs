
using CellMenu;
using HarmonyLib;
using Il2CppInterop.Runtime;
using ReTFO.Archipelago.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TheArchive.Features.Dev;
using UnityEngine;

namespace ReTFO.Archipelago.Patches;

// Modifies the rundown menu post-setup to have buttons and themes for archipelago
[HarmonyPatch]
internal static class ModifyRundownMenuPatch
{

    public static CM_Item AddButton(CM_PageRundown_New __instance, float offset)
    {
        Vector3 vOffset = Vector3.down * offset;

        var sourceButton = __instance.m_buttonConnect;
        GameObject go = GameObject.Instantiate(sourceButton.gameObject);
        go.transform.parent = sourceButton.transform.parent;
        var newButton = go.GetComponent<CM_Item>();

        newButton.RectTrans.anchorMin = sourceButton.RectTrans.anchorMin;
        newButton.RectTrans.anchorMax = sourceButton.RectTrans.anchorMax;
        newButton.RectTrans.position = sourceButton.RectTrans.position + vOffset;

        newButton.Setup();
        newButton.SetupCMItem();

        return newButton;
    }

    [HarmonyPatch(typeof(CM_PageRundown_New), nameof(CM_PageRundown_New.Setup)), HarmonyPrefix]
    public static void PreMenuSetup(CM_PageRundown_New __instance)
    {
        const float margin = 25f;

        var connectButton = __instance.m_buttonConnect;
        connectButton.SetText("Connect to <#F0F><i>ARCHIPELAGO</i></color>");
        connectButton.add_OnBtnPressCallback(new Il2CppAction_int((int _) => Plugin.Get().StateTracker.Connect()));

        float gap = connectButton.GetSize().y + margin;
        var openSettingsButton = AddButton(__instance, gap);
        openSettingsButton.SetText("Server Settings");
        openSettingsButton.add_OnBtnPressCallback(
            new Il2CppAction_int((int _) => 
            { 
                MainMenuGuiLayer.Current.ChangePage(eCM_MenuPage.CMP_SETTINGS); 
                CatchNetworkSettingsSubmenuPatch.NetworkSettingsSubMenu?.Show(); 
            }
        ));

        var dumpMIDButton = AddButton(__instance, 2f * gap);
        dumpMIDButton.SetText("Export MID Data");
        dumpMIDButton.add_OnBtnPressCallback(new Il2CppAction_int((int _) => Plugin.Get().MidManager.ExportGameData()));

        // We set the connectButton as the parent so these will appear / disappear with it
        // We have to wait until now to set it to avoid duplicating buttons while creating new ones
        openSettingsButton.RectTrans.SetParent(connectButton.RectTrans, true);
        dumpMIDButton.RectTrans.SetParent(connectButton.RectTrans, true);
    }

    [HarmonyPatch(typeof(CM_PageRundown_New), nameof(CM_PageRundown_New.Setup)), HarmonyPostfix]
    public static void PostMenuSetup(CM_PageRundown_New __instance)
    {
        // Removing the connect callback so pressing the button does not open the rundown
        var connectButton = __instance.m_buttonConnect;
        foreach (var action in connectButton.OnBtnPressCallback.GetInvocationList())
        {
            if (action.Target?.GetIl2CppType().Pointer == Il2CppType.Of<CM_PageRundown_New>().Pointer)
                connectButton.remove_OnBtnPressCallback(action.Cast<Il2CppSystem.Action<int>>());
        }
    }

}
