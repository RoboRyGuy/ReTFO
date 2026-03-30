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

[EnableFeatureByDefault]
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

    // Triggers event processing for when unique commands are triggered
    [Terminal.Callback]
    public static void AddUniqueCommandEvents(Terminal.Data data)
    {
        foreach (var command in data.TerminalUniqueCommands)
        {
            string name = GetUniqueCommandRegionName(data, command);
            int commandRegion = data.GetOrCreateRegion(name);
            data.ProcessEvents(commandRegion, name, command.CommandEvents ??= new(1));
            data.AddPath(data.GetOrCreateRegion(data.TerminalName), commandRegion);
        }
    }

    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.ReceiveCommand))]
    public static class LG_ComputerTerminalCommandInterpreter__ReceiveCommand__Patch
    {
        public static void Postfix(LG_ComputerTerminalCommandInterpreter __instance, TERM_Command cmd, bool __runOriginal)
        {
            if (!__runOriginal) return;

            Plugin plugin = Plugin.Get();
            Terminal.Data? terminal = Terminal.Data.FromTerminal(__instance.m_terminal);
            if (terminal == null)
            {
                FeatureLogger.Error("Null terminal data for unique command detection! Is this a reactor terminal?");
                return;
            }

            if (cmd >= TERM_Command.UniqueCommand1 && cmd <= TERM_Command.UniqueCommand5)
                plugin.StateTracker.NotifyFoundRegion(
                    GetUniqueCommandRegionName(terminal, terminal.TerminalUniqueCommands[(int)cmd - (int)TERM_Command.UniqueCommand1]),
                    __instance.m_terminal.m_syncedInteractionSource
                );
        }
    }

    public static string GetUniqueCommandRegionName(Terminal.Data data, CustomTerminalCommand command)
        => $"{data.TerminalName} {command.Command}";

}

