using GameData;
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault, AutomatedFeature]
public class UniqueCommandsHandler : ArchipelagoFeature
{
    public override string Name => "Unique Command Handler";
    public override string Description 
        => "Adds unique terminal commands and events triggered by those commands to Archipelago";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // TODO: Unique commands sometimes need to be activated via another event
    //       They can be enabled and disabled, which could cause complications in modded rundowns

    // Triggers event processing for when unique commands are triggered
    [Terminal.Callback]
    public void AddUniqueCommandEvents(Terminal.Data data)
    {
        foreach (var command in data.TerminalUniqueCommands)
        {
            string name = GetUniqueCommandRegionName(data, command);
            RegionID commandRegion = data.LookupOrCreateRegion(name);
            data.ProcessEvents(commandRegion, name, command.CommandEvents ??= new(1));
            data.AddPath(new Path()
            {
                StartingRegion = data.LookupOrCreateRegion(data.TerminalName),
                EndingRegion = commandRegion,
            });
        }
    }

    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.ReceiveCommand))]
    public static class LG_ComputerTerminalCommandInterpreter__ReceiveCommand__Patch
    {
        public static void Postfix(LG_ComputerTerminalCommandInterpreter __instance, TERM_Command cmd, bool __runOriginal)
        {
            if (!__runOriginal) return;

            var result = Terminal.Data.FromTerminal(__instance.m_terminal);
            Terminal.Data? terminal = result.Data;
            if (terminal == null)
            {
                if (!result.IsReactorTerminal)
                    FeatureLogger.Error("Null terminal data for unique command detection!");
                return;
            }

            if (cmd >= TERM_Command.UniqueCommand1 && cmd <= TERM_Command.UniqueCommand5)
                StateTracker.Get().NotifyFoundRegion(
                    GetUniqueCommandRegionName(terminal, terminal.TerminalUniqueCommands[(int)cmd - (int)TERM_Command.UniqueCommand1]),
                    __instance.m_terminal.m_syncedInteractionSource
                );
        }
    }

    public static string GetUniqueCommandRegionName(Terminal.Data data, CustomTerminalCommand command)
        => $"{data.TerminalName} {command.Command}";

}

