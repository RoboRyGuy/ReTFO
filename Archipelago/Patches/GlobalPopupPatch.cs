using HarmonyLib;

namespace ReTFO.Archipelago.Patches;

/// <summary>
/// There's something wrong with the intel popup, so we simply patch in the
///  correct text before it can be shown
/// </summary>
[HarmonyPatch(typeof(GlobalPopupMessageManager), nameof(GlobalPopupMessageManager.ShowPopup))]
public static class GlobalPopupPatch
{
    public static void Prefix(GlobalPopupMessageManager __instance, ref PopupMessage message)
    {
        if (message.PopupType != PopupType.RundownInfo) return;
        message.Header = MainMenuGuiLayer.Current.PageRundownNew.m_currentRundownData.StorytellingData.Title.ToString();
    }

}
