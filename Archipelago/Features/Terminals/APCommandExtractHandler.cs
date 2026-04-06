using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using PlayFab;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class APCommandExtractHandler : ArchipelagoFeature
{
    public override string Name => "AP Extract Command";
    public override string Description => "Adds the EXTRACT and RELEASE subcommands to the AP command";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public const int ItemsPerTerminal = 3;
    public const float ExtractDelay = 3.5f;
    public const float ReleaseDelay = 2.5f;

    private ExtractCommand? m_extractCommand = null;
    private ReleaseCommand? m_releaseCommand = null;

    public override void OnEnable()
    {
        base.OnEnable();
        APCommandHandler.RegisterCommand(m_extractCommand ??= new());
        APCommandHandler.RegisterCommand(m_releaseCommand ??= new());
    }

    public override void OnDisable()
    {
        base.OnEnable();
        APCommandHandler.UnregisterCommand(m_extractCommand ??= new());
        APCommandHandler.UnregisterCommand(m_releaseCommand ??= new());
    }

    private class TerminalExtractReleaseLocation : Location
    {
        public TerminalExtractReleaseLocation(Terminal.Data data, int count)
            : base(MakeName(data, count), data.GetOrCreateRegion(data.TerminalName), null) { }

        public static string MakeName(Terminal.Data data, int count)
            => $"{data.TerminalName} Generic Location #{count}";

        private static RandomizationData s_randData = new()
        {
            IsUseful = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    // Make the item codes used for extract / release
    private static IEnumerable<Tuple<string, string>> MakeItemCodes(LG_ComputerTerminal terminal)
    {
        Terminal.Data? terminalData = Terminal.Data.FromTerminal(terminal);
        if (terminalData == null)
            return Enumerable.Empty<Tuple<string, string>>();

        Random random = new(Tuple.Create(Plugin.Get().StateTracker.RootSeed, terminalData.TerminalName.GetHashCode()).GetHashCode());
        char r()
        {
            int choice = (int)(36d * random.NextDouble());
            return (char)(choice >= 10 ? 'A' + (choice - 10) : '0' + choice);
        }

        return Enumerable.Range(1, ItemsPerTerminal)
            .Select(i => Tuple.Create($"{r()}{r()}{r()}{r()}-{r()}{r()}-{r()}{r()}{r()}{r()}", TerminalExtractReleaseLocation.MakeName(terminalData, i)));
    }

    /// <summary>
    /// Handles the extract command
    /// </summary>
    private class ExtractCommand : APCommandHandler.SubCommand
    {
        public ExtractCommand()
        {
            SubCommandName = "EXTRACT";
        }

        public override string HelpText
            => "EXTRACT high-security item codes from this terminal\n"
            + "Each terminal contains unique high-security codes\n"
            + "Use the RELEASE command with high-security item codes to RELEASE items to the MULTIWORLD";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            terminal.m_command.AddOutput(TerminalLineType.SpinningWaitDone, "Extracting codes", ExtractDelay, TerminalSoundType.LineTypeDefault, TerminalSoundType.Positive);

            StateTracker stateTracker = StateTracker.Get();
            Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();
            var codes = MakeItemCodes(terminal);
            bool printedSomething = false;
            foreach (var pair in codes)
            {
                Location location = gameData.LookupLocation(pair.Item2);
                bool isEmpty = location.ItemID == 0 || stateTracker.HasLocation(pair.Item2);

                string itemName()   => location.ScoutedItem?.ItemDisplayName ?? gameData.LookupItem(location.ItemID).Name;
                string itemGame()   => location.ScoutedItem?.ItemGame ?? "GTFO";
                string itemPlayer() => location.ScoutedItem?.Player.Name ?? StateTracker.Config.Username;
                
                terminal.AddLine($"\n |--------------| {(isEmpty ? ""                   : " Item: " + itemName())}", false);
                terminal.AddLine(  $" | {pair.Item1} | {(isEmpty ? "-- MODULE EMPTY --" : "World: " + itemGame())}", false);
                terminal.AddLine(  $" |--------------| {(isEmpty ? ""                   : "Owner: " + itemPlayer())}", false);
                printedSomething = true;

            }
            if (printedSomething)
                terminal.AddLine($"\n   -- END OF LIST --", true);
            else
                terminal.AddLine($"\n   -- NOTHING TO EXTRACT --", true);
        }
    }

    /// <summary>
    /// Handles the release command
    /// </summary>
    private class ReleaseCommand : APCommandHandler.SubCommand
    {
        public ReleaseCommand()
        {
            SubCommandName = "RELEASE";
        }

        public override string HelpText
            => "Use a high-security item code from EXTRACT to release an item to the MULTIWORLD"
            + "\nExample: `AP RELEASE XXXX-XX-XXXX`";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            var pair = MakeItemCodes(terminal).FirstOrDefault(p => string.Compare(p.Item1, param2, StringComparison.OrdinalIgnoreCase) == 0);

            if (pair == null)
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitNoDone, "Releasing item " + param2.ToUpper(), ReleaseDelay, TerminalSoundType.LineTypeDefault, TerminalSoundType.Negative);
                terminal.AddLine("<#F00>Incorrect item code!</color>");
            }
            else
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitDone, $"Releasing item {param2.ToUpper()}", ReleaseDelay, TerminalSoundType.LineTypeDefault, TerminalSoundType.Positive);

                StateTracker stateTracker = StateTracker.Get();
                Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();
                Location location = gameData.LookupLocation(pair.Item2);

                if (location.ItemID == 0)
                {
                    terminal.AddLine("No item to release.");
                }
                else
                {
                    terminal.AddLine("Item released successfully: " + location.ScoutedItem?.ItemDisplayName ?? gameData.LookupItem(location.ItemID).Name);
                    terminal.m_command.OnEndOfQueue += new Il2CppAction(() => StateTracker.Get().NotifyFoundLocation(location.ID, terminal.m_syncedInteractionSource));
                }
            }
        }
    }

    /// <summary>
    /// Adds empty locations represnting items that can be released by this subcommand
    /// </summary>
    /// <param name="data">The terminal data for processing callback</param>
    [Terminal.Callback]
    public static void AddTerminalItems(Terminal.Data data)
    {
        for (int i = 1; i <= ItemsPerTerminal; i++)
        {
            data.GetLocation(new TerminalExtractReleaseLocation(data, i));
        }
    }

}
