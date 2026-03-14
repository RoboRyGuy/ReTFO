using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.Features.EventHandlers;
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

    // Name of releasable locations in archipelago
    private static string ReleaseLocationName(Terminal.Data data, int count)
        => $"{data.TerminalName} Generic Location #{count}";

    // Make the item codes used for extract / release
    private static IEnumerable<Tuple<string, string>> MakeItemCodes(LG_ComputerTerminal terminal)
    {
        Terminal.Data? terminalData = Terminal.Data.FromTerminal(terminal);
        if (terminalData == null)
            return Enumerable.Empty<Tuple<string, string>>();

        Random random = new(Plugin.Get().StateTracker.RootSeed + terminalData.TerminalName.GetHashCode());
        char r()
        {
            int choice = (int)(36d * random.NextDouble());
            return (char)(choice >= 10 ? 'A' + (choice - 10) : '0' + choice);
        }

        return Enumerable.Range(1, ItemsPerTerminal)
            .Select(i => Tuple.Create($"{r()}{r()}{r()}{r()}-{r()}{r()}-{r()}{r()}{r()}{r()}", ReleaseLocationName(terminalData, i)));
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

            var codes = MakeItemCodes(terminal);
            bool printedSomething = false;
            foreach (var pair in codes)
            {
                Location location = Expedition.Data.FromCurrentExpedition().LookupLocation(pair.Item2);
                bool isEmpty = location.ScoutedItem == null || Plugin.Get().StateTracker.HasLocation(pair.Item2);
                terminal.AddLine($"\n |--------------| {(isEmpty ? "" : " Item: " + location.ScoutedItem!.ItemDisplayName)}", false);
                terminal.AddLine($" | {pair.Item1} | {(isEmpty ? "-- MODULE EMPTY --" : "World: " + location.ScoutedItem!.ItemGame)}", false);
                terminal.AddLine($" |--------------| {(isEmpty ? "" : "Owner: " + location.ScoutedItem!.Player.Name)}", false);
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
                Location location = Expedition.Data.FromCurrentExpedition().LookupLocation(pair.Item2);
                bool isEmpty = location.ScoutedItem == null || Plugin.Get().StateTracker.HasLocation(pair.Item2);

                if (isEmpty)
                {
                    terminal.AddLine("No item to release.");
                }
                else
                {
                    terminal.AddLine("Item released successfully: " + location.ScoutedItem!.ItemDisplayName);
                    var e = EventHelper.CreateCheckLocationEvent(location.ID);
                    e.Delay = ReleaseDelay;
                    WorldEventManager.ExecuteEvent(e);
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
            data.AddLocation(
                ReleaseLocationName(data, i),
                data.GetOrCreateRegion(data.TerminalName),
                eRandomizationType.Useful,
                false,
                null
            );
        }
    }

}
