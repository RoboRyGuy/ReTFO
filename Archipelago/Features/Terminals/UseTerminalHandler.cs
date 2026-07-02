
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class UseTerminalHandler_Tags
{

}

[EnableFeatureByDefault, AutomatedFeature]
public class UseTerminalHandler : ArchipelagoFeature
{
    public override string Name => "Terminal Region Handler";
    public override string Description 
        => "Identifies when a terminal region is accessed during play and connects terminal regions to the graph";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Add terminals (and paths to them) to zones
    [Terminal.Callback]
    public void AddTerminalRegions(Terminal.Data data)
    {
        Path path = new()
        {
            StartingRegion = data.Region_Zone,
            EndingRegion = data.Region_Terminal,
        };

        // Note the .Count > 0 check; this is to account for R8A2, which has the locked secret terminal
        // The thought is "if this password is impossible to find in-game, it must be readily available"
        if (data.TerminalStartingStateData.PasswordProtected && data.TerminalStartingStateData.TerminalZoneSelectionDatas.Count > 0)
        {
            path = new(path)
            {
                ReqItem = new(Path.RequiredItem.eType.Category, data.Item_TerminalPasswords_ByTerminal),
                ReqCount = (uint)data.TerminalStartingStateData.PasswordPartCount,
            };
        }

        data.AddPath(path);
    }

    // Detect when a terminal is interacted with
    [ArchivePatch(typeof(LG_ComputerTerminal), nameof(LG_ComputerTerminal.OnInteract))]
    public static class LG_ComputerTerminal__OnInteract__Patch
    {
        public static void Postfix(LG_ComputerTerminal __instance)
        {
            if (__instance.IsPasswordProtected) return;

            var result = Terminal.Data.FromTerminal(__instance);
            Terminal.Data? terminalData = result.Data;
            if (terminalData == null)
            {
                if (!result.IsReactorTerminal)
                    FeatureLogger.Error("Entered unknown terminal!");
                return;
            }

            StateTracker.Get().NotifyFoundRegion(
                terminalData.TerminalName,
                __instance.m_syncedInteractionSource
            );
        }
    }
}
