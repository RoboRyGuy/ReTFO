using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class APCommandExtractHandler_Tags
{
    extension (Game.Data data)
    {
        public TagResolver Tag_TerminalExtractLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Terminal Extract Locations", "Empty locations checked by running the EXTRACT and RELEASE commands on terminals", gd.Tag_AllLocations));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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
        base.OnDisable();
        APCommandHandler.UnregisterCommand(m_extractCommand ??= new());
        APCommandHandler.UnregisterCommand(m_releaseCommand ??= new());
    }

    private static class TerminalExtractReleaseLocation
    {
        public static TagResolver MakeTag(Terminal.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.TerminalName} Extract Location #{count}", "A location checked by running the EXTRACT and RELEASE commands on terminals", gd.Tag_TerminalExtractLocations));

        public static LocationData MakeRandData() => new LocationData() { IsEmpty = true };
    }

    // Make the item codes used for extract / release
    private static IEnumerable<Tuple<string, KeyedLocation>> MakeItemCodes(StateTracker stateTracker, LG_ComputerTerminal terminal)
    {
        Terminal.Data? terminalData = Terminal.Data.FromTerminal(terminal).Data;
        if (terminalData == null)
            return Enumerable.Empty<Tuple<string, KeyedLocation>>();

        Random random = new(Tuple.Create(Plugin.Get().StateTracker.RootSeed, terminalData.TerminalName.GetHashCode()).GetHashCode());
        char r()
        {
            int choice = (int)(36d * random.NextDouble());
            return (char)(choice >= 10 ? 'A' + (choice - 10) : '0' + choice);
        }

        Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();
        Tuple<string, KeyedLocation>[] results = new Tuple<string, KeyedLocation>[ItemsPerTerminal];
        for (int i = 0; i < ItemsPerTerminal; i++)
        {
            RandomizationTag tag = TerminalExtractReleaseLocation.MakeTag(terminalData, i + 1);
            if (!gameData.TryLookupLocation(tag, out var loc))
                FeatureLogger.Error($"Failed to lookup terminal extraction location: {gameData.LookupTagDef(tag).Name}");
            results[i] = Tuple.Create($"{r()}{r()}{r()}{r()}-{r()}{r()}-{r()}{r()}{r()}{r()}", loc);
        }

        return results;
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
            var codes = MakeItemCodes(stateTracker, terminal);
            bool printedSomething = false;
            foreach (var pair in codes)
            {
                bool isEmpty = pair.Item2.IsNull || pair.Item2.ItemID.IsNull || stateTracker.HasLocation(pair.Item2.ID);

                Location location = pair.Item2.Location;
                string itemName()   => location.ScoutedItem?.ItemDisplayName ?? gameData.LookupTagDef(gameData.LookupItem(location.ItemID).NameTag).Name;
                string itemGame()   => location.ScoutedItem?.ItemGame ?? "DEBUG";
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
            StateTracker stateTracker = StateTracker.Get();
            var pair = MakeItemCodes(stateTracker, terminal).FirstOrDefault(p => string.Compare(p.Item1, param2, StringComparison.OrdinalIgnoreCase) == 0);
            Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();

            if (pair == null)
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitNoDone, "Releasing item " + param2.ToUpper(), ReleaseDelay, TerminalSoundType.LineTypeDefault, TerminalSoundType.Negative);
                terminal.AddLine("<#F00>Incorrect item code!</color>");
            }
            else
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitDone, $"Releasing item {param2.ToUpper()}", ReleaseDelay, TerminalSoundType.LineTypeDefault, TerminalSoundType.Positive);


                if (pair.Item2.Location.ItemID.IsNull)
                {
                    terminal.AddLine("No item to release.");
                }
                else
                {
                    terminal.AddLine("Item released successfully: " + pair.Item2.Location.ScoutedItem?.ItemDisplayName ?? gameData.LookupTagDef(gameData.LookupItem(pair.Item2.Location.ItemID).NameTag).Name);
                    terminal.m_command.OnEndOfQueue += new Il2CppAction(() => StateTracker.Get().NotifyFoundLocation(pair.Item2.ID, terminal.m_syncedInteractionSource));
                }
            }
        }
    }

    /// <summary>
    /// Adds empty locations represnting items that can be released by this subcommand
    /// </summary>
    /// <param name="data">The terminal data for processing callback</param>
    [Terminal.Callback]
    public void AddTerminalItems(Terminal.Data data)
    {
        for (int i = 1; i <= ItemsPerTerminal; i++)
        {
            data.AddLocation(
                TerminalExtractReleaseLocation.MakeTag(data, i), 
                data.LookupOrCreateRegion(data.TerminalName), 
                TerminalExtractReleaseLocation.MakeRandData()
            );
        }
    }

    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.AddInitialTerminalOutput))]
    public static class LG_ComputerTerminalCommandInterpreter__AddInitialTerminalOutput__Patch
    {
        public static void Postfix(LG_ComputerTerminalCommandInterpreter __instance)
        {
            // This command is often called on terminals before they are set up
            // If the terminal is not set up, we can't look up its data (and there's no output anyway)
            if (!__instance.m_terminal.m_isSetup) return;

            // Count how many items are available on this terminal
            var tDataResult = Terminal.Data.FromTerminal(__instance.m_terminal);
            if (tDataResult.Data == null)
            {
                if (!tDataResult.IsReactorTerminal)
                    FeatureLogger.Error($"Failed to lookup terminal data: {__instance.m_terminal.ItemKey}");
                return;
            }

            Terminal.Data data = tDataResult.Data;
            StateTracker stateTracker = StateTracker.Get();
            Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();

            int count = 0;
            for (int i = 0; i < ItemsPerTerminal; i++)
            {
                RandomizationTag tag = TerminalExtractReleaseLocation.MakeTag(data, i + 1);
                if (!gameData.TryLookupLocation(tag, out var loc))
                    FeatureLogger.Error($"Failed to lookup terminal extraction location: {gameData.LookupTagDef(tag).Name}");
                else if (!loc.ItemID.IsNull && !stateTracker.HasLocation(loc.ID)) ++count;
            }

            // Add our new item to the output queue
            if (count == 1)
                __instance.AddOutput("There is 1 available item on this terminal", false);
            else
                __instance.AddOutput($"There are {count} available items on this terminal", false);

            // Now simply move it back 5 lines to line it up below the logs message
            var queue = __instance.m_outputQueue;
            int currentIndex = (queue._head + queue.Count - 1) % queue._array.Count;
            int targetIndex;
            for (int i = 0; i < 5; i++)
            {
                targetIndex = currentIndex - 1;
                if (targetIndex < 0)
                    targetIndex = queue._array.Count - 1;
                var oldValue = queue._array[targetIndex];
                queue._array[targetIndex] = queue._array[currentIndex];
                queue._array[currentIndex] = oldValue;
                currentIndex = targetIndex;
            }
        }
    }

}
