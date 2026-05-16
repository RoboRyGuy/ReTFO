
using CellMenu;
using HarmonyLib;
using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ReTFO.Archipelago.Patches;

// Modifies the rundown menu post-setup to have buttons and themes for archipelago
[HarmonyPatch]
internal static class ModifyRundownMenuPatch
{
    // Helper which clones the Connect To Rundown button to create a new button
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
        
        // Overwriting the connect button so it starts archipelago
        var connectButton = __instance.m_buttonConnect;
        connectButton.SetText("Connect to <#F0F><i>ARCHIPELAGO</i></color>");
        connectButton.add_OnBtnPressCallback(new Il2CppAction_int((int _) => StateTracker.Get().Connect()));

        // Button which opens the settings menu
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

        // Button which dumps modded instance data
        //var dumpMIDButton = AddButton(__instance, 2f * gap);
        //dumpMIDButton.SetText("Export MID Data");
        //dumpMIDButton.add_OnBtnPressCallback(new Il2CppAction_int((int _) => Plugin.Get().MidManager.ExportMidData()));

        // We set the connectButton as the parent so these will appear / disappear with it
        // We have to wait until now to set it to avoid duplicating buttons while creating new ones
        openSettingsButton.RectTrans.SetParent(connectButton.RectTrans, true);
        //dumpMIDButton.RectTrans.SetParent(connectButton.RectTrans, true);
    }

    [HarmonyPatch(typeof(CM_PageRundown_New), nameof(CM_PageRundown_New.Setup)), HarmonyPostfix]
    public static void PostMenuSetup(CM_PageRundown_New __instance)
    {
        // Removing the connect callback so pressing the button does not open the rundown
        var connectButton = __instance.m_buttonConnect;
        foreach (var action in connectButton.OnBtnPressCallback.GetInvocationList())
        {
            // We're removing function __instance.setup_b__102_0
            if ((action.Target?.Pointer ?? IntPtr.Zero) == __instance.Pointer)
                connectButton.remove_OnBtnPressCallback(action.Cast<Il2CppSystem.Action<int>>());
        }

        // Convenient time to set up replication
        StateTracker.Get().SetupReplication();
    }

    /// <summary>
    /// Setting "m_selectionIsRevealed" allows the JoinLobby button to work
    /// </summary>
    [HarmonyPatch]
    public static class RevealJoinLobbyPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CM_PageRundown_New), nameof(CM_PageRundown_New.OnCortexDone));
            yield return AccessTools.Method(typeof(CM_PageRundown_New), nameof(CM_PageRundown_New.SetPageActive));
        }

        public static void Postfix(CM_PageRundown_New __instance)
        {
            __instance.m_selectionIsRevealed = true;
        }
    }

}
