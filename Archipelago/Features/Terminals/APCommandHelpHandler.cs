using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class APCommandHelpHandler : ArchipelagoFeature
{
    public override string Name => "AP Help Command";
    public override string Description => "Adds the HELP subcommand to the AP command";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private HelpCommand? m_helpCommand = null;

    public override void OnEnable()
    {
        base.OnEnable();
        APCommandHandler.RegisterCommand(m_helpCommand ??= new());
    }

    public override void OnDisable()
    {
        base.OnEnable();
        APCommandHandler.UnregisterCommand(m_helpCommand ??= new());
    }

    // Name of releasable locations in archipelago
    private static string ReleaseLocationName(Terminal.Data data, int count)
        => $"{data.TerminalName} Generic Location #{count}";

    /// <summary>
    /// Handles the help command
    /// </summary>
    private class HelpCommand : APCommandHandler.SubCommand
    {
        public HelpCommand()
        {
            SubCommandName = "HELP";
        }

        public override string HelpText => "Shows this menu";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            terminal.AddLine("AP is an ARCHIPELAGO utility tool for accessing MUTLTIWORLD items.");
            terminal.AddLine("Available SUBCOMMANDs:");
            foreach (var item in APCommandHandler.SubCommands)
                terminal.m_command.AddOutputWithPrefixAndTabbedIndentation(item.Key, item.Value.HelpText);
        }
    }

}
