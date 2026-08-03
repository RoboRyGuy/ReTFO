using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public LocationID Location_TerminalExtractions
            => LocationID.From(data, "Terminal Extraction Locations", data => new("Locations checked by performing the extract and release commands on terminals", data.Location_Empty));
    }

    extension (Terminal.Data data)
    {
        public LocationID Location_TerminalExtraction_Instance(int count)
            => LocationID.From(data, $"{data.TerminalName} Extraction Location #{count}", data => new("A particular terminal extraction location", data.Location_TerminalExtractions));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class APCommandExtractHandler : ArchipelagoFeature
{
    public override string Name => "AP Extract Command";
    public override string Description => "Adds the EXTRACT, RELEASE, and TRASH subcommands to the AP command";
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
    private TrashCommand? m_trashCommand = null;

    public override void OnEnable()
    {
        base.OnEnable();
        APCommandHandler.RegisterCommand(m_extractCommand ??= new());
        APCommandHandler.RegisterCommand(m_releaseCommand ??= new());
        APCommandHandler.RegisterCommand(m_trashCommand ??= new());
    }

    public override void OnDisable()
    {
        base.OnDisable();
        APCommandHandler.UnregisterCommand(m_extractCommand ??= new());
        APCommandHandler.UnregisterCommand(m_releaseCommand ??= new());
        APCommandHandler.UnregisterCommand(m_trashCommand ??= new());
    }

    // Make the item codes used for extract / release
    private static IEnumerable<(string, LocationID)> MakeItemCodes(StateTracker stateTracker, LG_ComputerTerminal terminal)
    {
        Terminal.Data? terminalData = Terminal.Data.FromTerminal(terminal).Data;
        if (terminalData == null)
            return Enumerable.Empty<(string, LocationID)>();

        // Creating a deterministic hash based on the terminal name and the root seed
        // This gives use a unique random seed and ensures the same codes are generated for all players
        SHA256 hash = SHA256.Create();
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, stateTracker.RootSeed);
        hash.TransformBlock(buffer.ToArray(), 0, 8, null, 0);
        byte[] bytes = Encoding.UTF8.GetBytes(terminalData.TerminalName);
        hash.TransformFinalBlock(bytes, 0, bytes.Length);
        bytes = hash.Hash!;
        int result = 0;
        const int chunkSize = sizeof(int);
        for (int i = 0; i < bytes.Length; i += chunkSize)
            result ^= BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i, chunkSize));

        Random random = new(result);
        char r()
        {
            int choice = (int)(36d * random.NextDouble());
            return (char)(choice >= 10 ? 'A' + (choice - 10) : '0' + choice);
        }

        Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();
        (string, LocationID)[] results = new (string, LocationID)[ItemsPerTerminal];
        for (int i = 0; i < ItemsPerTerminal; i++)
            results[i] = ($"{r()}{r()}{r()}{r()}-{r()}{r()}-{r()}{r()}{r()}{r()}", terminalData.Location_TerminalExtraction_Instance(i + 1));

        return results;
    }

    private static bool IsEmpty(StateTracker stateTracker, LocationID id)
        => IsEmpty(stateTracker, id, stateTracker.GameData.Locations.LookUpValueChecked(id));

    private static bool IsEmpty(StateTracker stateTracker, LocationID id, Location loc)
        => id.IsNull || loc.ItemID.IsNull || stateTracker.HasLocation(id, false);

    /// <summary>
    /// Handles the EXTRACT command
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
                Location location = gameData.Locations.LookUpValueChecked(pair.Item2);
                bool isEmpty = IsEmpty(stateTracker, pair.Item2, location);

                string itemName() => location.ScoutedItemName ?? gameData.Items.LookUpName(location.ItemID);
                string itemGame()   => location.ScoutedGameName ?? "DEBUG";
                string itemPlayer() => location.ScoutedPlayerName ?? StateTracker.Config.Username;
                
                terminal.AddLine($"\n |--------------| {(isEmpty ? ""                   : " Item: " + itemName())}", false);
                terminal.AddLine(  $" | {pair.Item1} | {(isEmpty ? "-- MODULE EMPTY --" : "World: " + itemGame())}", false);
                terminal.AddLine(  $" |--------------| {(isEmpty ? ""                   : "Owner: " + itemPlayer())}", false);
                printedSomething = true;
            }
            if (printedSomething)
            {
                stateTracker.ScoutLocations(codes.Select(pair => pair.Item2));
                terminal.AddLine($"\n   -- END OF LIST --", true);
            }    
            else
                terminal.AddLine($"\n   -- NOTHING TO EXTRACT --", true);
        }
    }

    /// <summary>
    /// Handles the RELEASE command
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
            var pair = MakeItemCodes(stateTracker, terminal).FirstOrDefault(p => string.Compare(p.Item1, param2, StringComparison.OrdinalIgnoreCase) == 0, (string.Empty, new LocationID()));
            Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();

            if (pair.Item2.IsNull)
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitNoDone, "Releasing item " + param2.ToUpper(), ReleaseDelay, TerminalSoundType.LineTypeDefault, TerminalSoundType.Negative);
                terminal.AddLine("<#F00>Incorrect item code!</color>");
            }
            else
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitDone, $"Releasing item {param2.ToUpper()}", ReleaseDelay, TerminalSoundType.LineTypeDefault, TerminalSoundType.Positive);
                Location loc = stateTracker.GameData.Locations.LookUpValueChecked(pair.Item2);

                if (loc.ItemID.IsNull)
                {
                    terminal.AddLine("No item to release.");
                }
                else
                {
                    terminal.AddLine("Item released successfully: " + loc.ScoutedItemName ?? gameData.Items.LookUpName(loc.ItemID));
                    terminal.m_command.OnEndOfQueue += new Il2CppAction(() => StateTracker.Get().NotifyFoundLocation(pair.Item2, terminal.m_syncedInteractionSource));
                }
            }
        }
    }

    /// <summary>
    /// Handles the TRASH command
    /// </summary>
    private class TrashCommand : APCommandHandler.SubCommand
    {
        public TrashCommand()
        {
            SubCommandName = "TRASH";
        }

        public override string HelpText
            => "Mark all items found using the EXTRACT command as trash."
            + "\nThis marks them as found in the menu, but does not release them to the multiworld.";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            StateTracker stateTracker = StateTracker.Get();
            var codes = MakeItemCodes(stateTracker, terminal)
                .Where(pair => !IsEmpty(stateTracker, pair.Item2))
                .Select(pair => pair.Item2)
                .ToList();
            stateTracker.MarkAsTrash(codes, terminal.m_syncedInteractionSource);
            terminal.m_command.AddOutput($"You have marked {codes.Count} item{(codes.Count == 1 ? "" : "s")} as <i><#F00>TRASH</i></color>");
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
            data.Locations.CreateValue(
                data.Location_TerminalExtraction_Instance(i),
                data.Region_Terminal,
                new LocationData() { IsEmpty = true },
                new ItemID()
            );
        }
    }

    /// <summary>
    /// Add the current extractable item count to the terminal's output
    /// </summary>
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
                LocationID id = data.Location_TerminalExtraction_Instance(i + 1);
                Location loc = data.Locations.LookUpValueChecked(id);
                if (!loc.ItemID.IsNull && !stateTracker.HasLocation(id)) ++count;
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

    /// <summary>
    /// Modify the results of a query of this terminal with the contained items
    /// </summary>
    [ArchivePatch(typeof(LG_ComputerTerminal), nameof(LG_ComputerTerminal._Setup_b__117_0))]
    public static class LG_ComputerTerminal___Setup_b__117_0__Patch
    {
        public static void Postfix(LG_ComputerTerminal __instance, Il2CppSystem.Collections.Generic.List<string> __result)
        {
            StateTracker st = StateTracker.Get();
            APCommandHandler.InsertLocationDataToDetailedInfo(
                st,
                __result,
                "AVAILABLE EXTRACTION(S)",
                MakeItemCodes(st, __instance)
                  .Where(pair => !IsEmpty(st, pair.Item2))
                  .Select(pair => pair.Item2)
            );
        }
    }

}
