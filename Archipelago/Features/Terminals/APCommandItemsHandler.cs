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

[EnableFeatureByDefault, AutomatedFeature]
public class APCommandItemsHandler : ArchipelagoFeature
{
    public override string Name => "AP Item Commands";
    public override string Description => "Adds the ITEMS, CLAIM, and CLAIM_ALL subcommands to the AP command";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    const float ItemsDelay = 2.5f;
    const float ClaimDelay = 2f;
    const float ClaimAllDelay = 3f;

    private ItemsCommand? m_itemsCommand = null;
    private ClaimCommand? m_claimCommand = null;
    private ClaimAllCommand? m_claimAllCommand = null;
    private CACommand? m_caCommand = null;

    public override void OnEnable()
    {
        base.OnEnable();
        APCommandHandler.RegisterCommand(m_itemsCommand ??= new());
        APCommandHandler.RegisterCommand(m_claimCommand ??= new());
        APCommandHandler.RegisterCommand(m_claimAllCommand ??= new());
        APCommandHandler.RegisterCommand(m_caCommand ??= new());
    }

    public override void OnDisable()
    {
        base.OnDisable();
        APCommandHandler.UnregisterCommand(m_itemsCommand ??= new());
        APCommandHandler.UnregisterCommand(m_claimCommand ??= new());
        APCommandHandler.UnregisterCommand(m_claimAllCommand ??= new());
        APCommandHandler.UnregisterCommand(m_caCommand ??= new());
    }

    // Helper for invoking callbacks when claiming items from a terminal
    private class ClaimItemsHelper
    {
        public ClaimItemsHelper() => thisAction = new Il2CppAction(this.DoWork);
        public Il2CppSystem.Action thisAction;
        public LG_ComputerTerminal terminal = null!;
        public int currentIndex = 0;
        public List<Action> claimActions = new();

        public void DoWork()
        {
            while (currentIndex < claimActions.Count)
            {
                claimActions[currentIndex++].Invoke();
                if (terminal.m_command.m_outputQueue.Count > 0) break;
            }
            if (currentIndex >= claimActions.Count)
                terminal.m_command.OnEndOfQueue -= thisAction;
        }
    }

    /// <summary>
    /// Handles the ITEMS subcommand
    /// </summary>
    private class ItemsCommand : APCommandHandler.SubCommand
    {
        public ItemsCommand()
        {
            SubCommandName = "ITEMS";
        }

        public override string HelpText
            => "List the items currently available to YOU"
            + "\nNote: Available items will vary by expedition";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            terminal.m_command.AddOutput(TerminalLineType.SpinningWaitDone, "Fetching currently available items", ItemsDelay, onWaitDoneSound: TerminalSoundType.Positive);

            StateTracker stateTracker = StateTracker.Get();
            var items = stateTracker.ItemsInTerminalSystem;
            Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();

            if (items.Count == 0)
            {
                terminal.AddLine(string.Empty);
                terminal.AddLine("   -- NO ITEMS FOUND --");
            }
            else
            {
                terminal.AddLine(string.Empty);
                terminal.AddLine(" <u>ITEM CODE</u>     <u>ITEM NAME</u>", false);
                foreach (var item in items)
                    terminal.AddLine($"{item.Item2}   {gameData.LookupTagDef(gameData.LookupItem(item.Item1).NameTag).Name}", false);
                terminal.AddLine(string.Empty);
            }
        }
    }

    /// <summary>
    /// Handles the CLAIM subcommand
    /// </summary>
    private class ClaimCommand : APCommandHandler.SubCommand
    {
        public ClaimCommand()
        {
            SubCommandName = "CLAIM";
        }

        public override string HelpText
            => "Claim one of YOUR currently-held items using its code";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            StateTracker stateTracker = StateTracker.Get();
            var pair = stateTracker.ItemsInTerminalSystem.FirstOrDefault(pair => string.Compare(pair.Item2, param2, StringComparison.OrdinalIgnoreCase) == 0);

            if (pair == null)
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitNoDone, "Searching for item " + param2.ToUpper(), ClaimDelay, onWaitDoneSound: TerminalSoundType.Negative);
                terminal.m_command.AddOutput("<#F00>Incorrect item code!</color>");
            }
            else
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitDone, "Searching for item " + param2.ToUpper(), ClaimDelay, onWaitDoneSound: TerminalSoundType.Positive);

                var helper = new ClaimItemsHelper()
                {
                    terminal = terminal.m_command.m_terminal,
                    currentIndex = 0,
                    claimActions = stateTracker.MidManager.GetProcessedGameData().LookupItem(pair.Item1).OnRetrieveFromTerminalSystem(stateTracker, terminal).ToList(),
                };
                terminal.m_command.OnEndOfQueue += helper.thisAction;
                stateTracker.ItemsInTerminalSystem.Remove(pair);
            }
        }
    }

    /// <summary>
    /// Handles the CLAIM_ALL subcommand
    /// </summary>
    private class ClaimAllCommand : APCommandHandler.SubCommand
    {
        public ClaimAllCommand()
        {
            SubCommandName = "CLAIM_ALL";
        }

        public override string HelpText
            => "Retrieve all of YOUR items. Can optionally include a filter"
            + "\nExample: `AP CLAIM_ALL KEY` will claim all items with \"KEY\" in their name";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            StateTracker stateTracker = StateTracker.Get();
            Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();
            bool predicate(Tuple<ItemID, string> pair) => param2 == null ? true : gameData.LookupTagDef(gameData.LookupItem(pair.Item1).NameTag).Name.Contains(param2, StringComparison.OrdinalIgnoreCase);
            var pairs = stateTracker.ItemsInTerminalSystem.Where(predicate).ToList();
            string firstMessage = (param2 == null || param2.Trim().Length == 0)
                ? "Preparing all items to be claimed"
                : "Collecting items to be claimed using filter " + param2.ToUpper();

            if (!pairs.Any())
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitNoDone, firstMessage, ClaimAllDelay, onWaitDoneSound: TerminalSoundType.Negative);
                if (param2 == null || param2.Trim().Length == 0)
                    terminal.m_command.AddOutput("<#F00>No items available to be claimed!</color>");
                else
                    terminal.m_command.AddOutput("<#F00>No items matching filter!</color>");
            }
            else
            {
                terminal.m_command.AddOutput(TerminalLineType.SpinningWaitDone, firstMessage, ClaimAllDelay, onWaitDoneSound: TerminalSoundType.Positive);

                var helper = new ClaimItemsHelper()
                {
                    terminal = terminal,
                    currentIndex = 0,
                    claimActions = pairs.SelectMany(p => gameData.LookupItem(p.Item1).OnRetrieveFromTerminalSystem(stateTracker, terminal)).ToList(),
                };
                terminal.m_command.OnEndOfQueue += helper.thisAction;
                stateTracker.ItemsInTerminalSystem.RemoveAll(predicate);
            }
        }
    }

    /// <summary>
    /// Handles the CA subcommand
    /// </summary>
    private class CACommand : ClaimAllCommand
    {
        public CACommand()
        {
            SubCommandName = "CA";
        }

        public override string HelpText
            => "Alias for CLAIM_ALL";
    }


    // Prevent the command interpreter from unsubscribing our helper from OnEndOfQueue
    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.UpdateTerminalScreen))]
    private static class DontUnscubscribeMePatch
    {
        // For some reason, __state doesn't work here, so we're doing this manually
        private static ClaimItemsHelper? __state = null;

        public static void Prefix(LG_ComputerTerminalCommandInterpreter __instance)
        {
            __state = null;
            if (__instance.OnEndOfQueue == null) return;
            foreach (var d in __instance.OnEndOfQueue.GetInvocationList())
            {
                Il2CppAction? action = d.Target.TryCast<Il2CppAction>();
                if (action == null) continue;
                if (action.WrappedAction?.Target is ClaimItemsHelper helper)
                {
                    __state = helper;
                    return;
                }
            }
        }

        public static void Postfix(LG_ComputerTerminalCommandInterpreter __instance)
        {
            if (__state == null || __state.currentIndex >= __state.claimActions.Count) return;

            // Checks if it contains the helper already
            if (__instance.OnEndOfQueue?.GetInvocationList().Any(i => i.Target.Pointer == __state.thisAction.Pointer) ?? false)
                return;

            __instance.OnEndOfQueue += __state.thisAction;
            __state = null;
        }
    }
}
