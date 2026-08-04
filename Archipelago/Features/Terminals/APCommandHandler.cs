using GameData;
using LevelGeneration;
using Localization;
using ReTFO.Archipelago.FeaturesAPI;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.ModdedInstanceData.Model;

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

    private static SortedList<string, SubCommand> m_subCommands = new();
    public static IReadOnlyDictionary<string, SubCommand> SubCommands => m_subCommands;

    /// <summary>
    /// Registers a subcommand with the ap command
    /// </summary>
    /// <param name="command">The subcommand handler</param>
    public static void RegisterCommand(SubCommand command)
    {
        if (!m_subCommands.TryAdd(command.SubCommandName.ToUpper(), command))
            FeatureLogger.Error($"Failed to add duplicate command with name \"{command.SubCommandName}\"");
    }

    /// <summary>
    /// Unregisters a subcommand with the ap command.
    /// Logs a warning if the subcommand is not found, but otherwise has no errors
    /// </summary>
    /// <param name="command">The subcommand handler</param>
    public static void UnregisterCommand(SubCommand command)
    {
        if (!m_subCommands.Remove(command.SubCommandName.ToUpper()))
            FeatureLogger.Warning($"Could not to remove subcommand \"{command.SubCommandName}\" because it was not registered");
    }

    /// <summary>
    /// Adds formatted scouting info about a location to detailed terminal info.
    /// This helps keep formatting consistent between location sources.
    /// </summary>
    /// <param name="st">The state tracker to use when performing this operation</param>
    /// <param name="detailedInfo">The detailed info to insert location data into</param>
    /// <param name="locationGroupName">The group name for the locations (ie how they're obtained)</param>
    /// <param name="location">The location to insert data for. Can be null</param>
    /// <param name="scout">If true, also scouts the locations</param>
    public static void InsertLocationDataInDetailedInfo(StateTracker st, Il2CppSystem.Collections.Generic.List<string> detailedInfo, string locationGroupName, LocationID location, bool scout = true)
    {
        int index = 1;
        while (index < detailedInfo.Count && !detailedInfo[index].StartsWith("----")) ++index;

        Location? actualLocation = location.IsNull ? null : st.GameData.Locations.LookUpValueChecked(location);

        if (actualLocation?.RandData.IsTreatedAsRandom ?? false)
        {
            string name = actualLocation.ScoutedPlayerName ?? "DEBUG";
            string item = actualLocation.ScoutedItemName ?? (actualLocation.ItemID.IsNull ? "null" : st.GameData.Items.LookUpName(actualLocation.ItemID));
            detailedInfo.Insert(index++, $"{locationGroupName}: ({name}) {item}");
        }
        else
        {
            detailedInfo.Insert(index++, $"{locationGroupName}: -- EMPTY --");
        }
        if (scout) st.ScoutLocation(location);
    }

    /// <summary>
    /// Adds formatted scouting info about locations to detailed terminal info.
    /// This helps keep formatting consistent between location sources.
    /// </summary>
    /// <param name="st">The state tracker to use when performing this operation</param>
    /// <param name="detailedInfo">The detailed info to insert location data into</param>
    /// <param name="locationGroupName">The group name for the locations (ie how they're obtained)</param>
    /// <param name="locations">The locations to insert data for</param>
    /// <param name="scout">If true, also scouts the locations</param>
    public static void InsertLocationDataInDetailedInfo(StateTracker st, Il2CppSystem.Collections.Generic.List<string> detailedInfo, string locationGroupName, IEnumerable<LocationID> locations, bool scout = true)
    {
        int index = 1;
        while (index < detailedInfo.Count && !detailedInfo[index].StartsWith("----")) ++index;

        List<Location> actualLocations = locations
            .Select(st.GameData.Locations.LookUpValueChecked)
            .Where(l => l.RandData.IsTreatedAsRandom)
            .Where(l => l.ScoutedItemName != null)
            .ToList();

        detailedInfo.Insert(index++, $"{locationGroupName}:");
        foreach (var loc in actualLocations)
        {
            string name = loc.ScoutedPlayerName ?? "DEBUG";
            string item = loc.ScoutedItemName ?? (loc.ItemID.IsNull ? "null" : st.GameData.Items.LookUpName(loc.ItemID));
            detailedInfo.Insert(index++, $"  ({name}) {item}");
        }
        detailedInfo.Insert(index++, "   -- END OF LIST --");

        if (scout && locations.Any()) st.ScoutLocations(locations);
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
