using GameData;
using LevelGeneration;
using Localization;
using ReTFO.Archipelago.FeaturesAPI;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

[EnableFeatureByDefault, AutomatedFeature]
public class APCommandHandler : ArchipelagoFeature
{
    public override string Name => "AP Command";
    public override string Description => "Adds the AP command to terminals, and allows other handlers to add subcommands to AP";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public const TERM_Command CommandSlot = TERM_Command.UniqueCommand5;
    public const float CommandDelay = 3f;

    /// <summary>
    /// Class representing a subcommand handler
    /// </summary>
    public abstract class SubCommand
    {
        /// <summary>
        /// Name of the command. Case-insensitive for detection, but prefer uppercase for presentation reasons
        /// </summary>
        public string SubCommandName { get; init; } = "";

        /// <summary>
        /// Short command description which is shown by AP HELP
        /// </summary>
        public abstract string HelpText { get; }

        /// <summary>
        /// Execute the command using the given input
        /// </summary>
        /// <param name="terminal">The terminal which called the command</param>
        /// <param name="fullLine">The full text for the command. I believe this is truncated to 25 characters by GTFO</param>
        /// <param name="subCommand">The sub command which was executed</param>
        /// <param name="param2">Any third param</param>
        public abstract void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2);
    }

    public override void OnEnable()
    {
        base.OnEnable();
        var terminals = LG_ComputerTerminalManager.Current?.m_terminals?.values;
        if (terminals == null) return;
        foreach (var terminal in terminals)
            LG_ComputerTerminalCommandInterpreter__SetupCommands__Patch.Postfix(terminal.m_command);
    }

    public override void OnDisable()
    {
        base.OnDisable();

        // I can't find how to remove it, so we're setting it hidden instead
        var terminals = LG_ComputerTerminalManager.Current?.m_terminals?.values;
        if (terminals == null) return;
        foreach (var terminal in terminals)
            terminal.TrySyncSetCommandHidden(CommandSlot);
    }

    private static Dictionary<string, SubCommand> m_subCommands = new();
    public static IReadOnlyDictionary<string, SubCommand> SubCommands => m_subCommands;

    public static void RegisterCommand(SubCommand command)
    {
        if (!m_subCommands.TryAdd(command.SubCommandName.ToUpper(), command))
            FeatureLogger.Error($"Failed to add duplicate command with name \"{command.SubCommandName}\"");
    }

    public static void UnregisterCommand(SubCommand command)
    {
        if (!m_subCommands.Remove(command.SubCommandName.ToUpper()))
            FeatureLogger.Warning($"Could not to remove subcommand \"{command.SubCommandName}\" because it was not registered");
    }

    /// <summary>
    /// Adds the AP command to terminals when they spawn in
    /// </summary>
    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.SetupCommands))]
    public static class LG_ComputerTerminalCommandInterpreter__SetupCommands__Patch
    {
        public static void Postfix(LG_ComputerTerminalCommandInterpreter __instance)
        {
            const string textName = "Archipelago - Command Help";
            TextDataBlock? commandHelp = TextDataBlock.GetBlock(textName);
            if (commandHelp == null)
            {
                commandHelp = new()
                {
                    internalEnabled = true,
                    name = textName,
                    SkipLocalization = false,
                    MachineTranslation = true,
                    English = "ARCHIPELAGO utility tool. Run `AP HELP` for more details",
                    Description = "",
                    CharacterMetaData = 1,
                    ImportVersion = 1,
                    ExportVersion = 1,
                };
                commandHelp.French = commandHelp.Italian = commandHelp.German = commandHelp.Spanish = commandHelp.Russian = commandHelp.Portuguese_Brazil
                    = commandHelp.Polish = commandHelp.Japanese = commandHelp.Korean = commandHelp.Chinese_Traditional = commandHelp.Chinese_Simplified
                    = new()
                    {
                        Translation = commandHelp.English,
                        ShouldTranslate = true
                    };
                TextDataBlock.AddBlock(commandHelp);
            }

            LocalizedText help = new()
            {
                Id = commandHelp.persistentID,
                OldId = 0u,
                UntranslatedText = commandHelp.English
            };

            __instance.AddCommand(CommandSlot, "AP", help, TERM_CommandRule.Normal);
        }
    }

    // Intersects commands on terminals and handles the AP command
    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.ReceiveCommand))]
    public static class LG_ComputerTerminalCommandInterpreter__ReceiveCommand__Patch
    {
        public static bool Prefix(LG_ComputerTerminalCommandInterpreter __instance, TERM_Command cmd, string inputLine, string param1, string param2)
        {
            if (!inputLine.StartsWith("AP"))
                return true;

            // Our patch blocks this from happening normally
            __instance.ResetLinesSinceCommand();
            __instance.AddOutput(__instance.NewLineStart() + inputLine, false);

            string subCommandName = param1?.ToUpper() ?? string.Empty;
            if (m_subCommands.TryGetValue(subCommandName, out var subCommand))
            {
                subCommand.Execute(__instance.m_terminal, inputLine, subCommandName, param2);
            }
            else
            {
                if (subCommandName.Length > 0)
                    __instance.AddOutput($"<#F00>Sub-commmand not recognized: {subCommandName}</color>");
                __instance.AddOutput("Use the command <#FF0>AP HELP</color> to see a list of commands.");
            }
            return false;
        }
    }

}
