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
    public override string Description => "Adds the ITEMS, CLAIM, CA, and CLAIM_CODE subcommands to the AP command";
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
    private CACommand? m_caCommand = null;
    private ClaimCode? m_claimCodeCommand = null;

    public override void OnEnable()
    {
        base.OnEnable();
        APCommandHandler.RegisterCommand(m_itemsCommand ??= new());
        APCommandHandler.RegisterCommand(m_claimCommand ??= new());
        APCommandHandler.RegisterCommand(m_caCommand ??= new());
        APCommandHandler.RegisterCommand(m_claimCodeCommand ??= new());
    }

    public override void OnDisable()
    {
        base.OnDisable();
        APCommandHandler.UnregisterCommand(m_itemsCommand ??= new());
        APCommandHandler.UnregisterCommand(m_claimCommand ??= new());
        APCommandHandler.UnregisterCommand(m_caCommand ??= new());
        APCommandHandler.UnregisterCommand(m_claimCodeCommand ??= new());
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
            => "List the items currently available to YOU. You may optionally provide a single text filter, which is matched by name."
            + "\nNote: Available items will vary by expedition";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            terminal.m_command.AddOutput(TerminalLineType.SpinningWaitDone, $"Fetching currently available items{((param2?.Length == 0) ? "" : $" matching filter \"{param2}\"")}", ItemsDelay, onWaitDoneSound: TerminalSoundType.Positive);

            StateTracker stateTracker = StateTracker.Get();
            Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();

            IEnumerable<(ItemID, string)> items;
            if ((param2?.Length ?? 0) == 0)
                items = stateTracker.ItemsInTerminalSystem;
            else
                items = stateTracker.ItemsInTerminalSystem.Where(pair => gameData.Items.LookUpName(pair.Item1).Contains(param2!, StringComparison.OrdinalIgnoreCase));


            if (!items.Any())
            {
                terminal.AddLine(string.Empty);
                terminal.AddLine("   -- NO ITEMS FOUND --");
            }
            else
            {
                terminal.AddLine(string.Empty);
                terminal.AddLine(" <u>ITEM CODE</u>     <u>ITEM NAME</u>", false);
                foreach (var item in items)
                    terminal.AddLine($"{item.Item2}   {gameData.Items.LookUpName(item.Item1)}", false);
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
            => "Retrieve all of YOUR items. Can optionally include a filter"
            + "\nExample: `AP CLAIM KEY` will claim all items with \"KEY\" in their name";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            StateTracker stateTracker = StateTracker.Get();
            Game.Data gameData = stateTracker.MidManager.GetProcessedGameData();
            bool predicate((ItemID, string) pair) 
                => param2 == null ? true : gameData.Items.LookUpName(pair.Item1).Contains(param2, StringComparison.OrdinalIgnoreCase);
            var pairs = stateTracker.ItemsInTerminalSystem.Where(predicate).ToList();
            string firstMessage = (param2 == null || param2.Trim().Length == 0)
                ? "Preparing all items to be claimed"
                : $"Collecting items to be claimed using filter \"{param2.ToUpper()}\"";

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
                // Isolate actions and remove items
                void postMessage()
                    => terminal.m_command.AddOutput(TerminalLineType.SpinningWaitDone, firstMessage, ClaimAllDelay, onWaitDoneSound: TerminalSoundType.Positive);

                List<Action> claimActions = pairs.SelectMany(p => gameData.Items.LookUpValueChecked(p.Item1).OnRetrieveFromTerminalSystem(stateTracker, terminal, p.Item1)).ToList();
                stateTracker.ItemsInTerminalSystem.RemoveAll(predicate);

                // Add actions to queue if it exists
                foreach (var d in terminal.m_command.OnEndOfQueue?.GetInvocationList() ?? Enumerable.Empty<Il2CppSystem.Delegate>())
                {
                    Il2CppAction? action = d.Target.TryCast<Il2CppAction>();
                    if (action == null) continue;
                    if (action.WrappedAction?.Target is ClaimItemsHelper existingHelper)
                    {
                        existingHelper.claimActions.AddRange(claimActions.Prepend(postMessage));
                        return;
                    }
                }

                // No queue exists, so we'll create a new one
                postMessage();
                var helper = new ClaimItemsHelper()
                {
                    terminal = terminal,
                    currentIndex = 0,
                    claimActions = claimActions,
                };
                terminal.m_command.OnEndOfQueue += helper.thisAction;
            }
        }
    }

    /// <summary>
    /// Handles the CA subcommand
    /// </summary>
    private class CACommand : ClaimCommand
    {
        public CACommand()
        {
            SubCommandName = "CA";
        }

        public override string HelpText
            => "Alias for CLAIM";
    }

    /// <summary>
    /// Handles the CLAIM_CODE subcommand
    /// </summary>
    private class ClaimCode : APCommandHandler.SubCommand
    {
        public ClaimCode()
        {
            SubCommandName = "CLAIM_CODE";
        }

        public override string HelpText
            => "Claim one of YOUR currently-held items using its code.\nUseful when you want to retrieve exactly one item when items share names.";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            StateTracker stateTracker = StateTracker.Get();
            var pair = stateTracker.ItemsInTerminalSystem.FirstOrDefault(pair => string.Compare(pair.Item2, param2, StringComparison.OrdinalIgnoreCase) == 0, (new ItemID(), string.Empty));

            if (pair.Item1.IsNull)
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
                    claimActions = stateTracker.MidManager.GetProcessedGameData().Items.LookUpValueChecked(pair.Item1).OnRetrieveFromTerminalSystem(stateTracker, terminal, pair.Item1).ToList(),
                };
                terminal.m_command.OnEndOfQueue += helper.thisAction;
                stateTracker.ItemsInTerminalSystem.Remove(pair);
            }
        }
    }

    /// <summary>
    /// Prevent the command interpreter from unsubscribing our helper from OnEndOfQueue.
    /// This is a rather inefficient way to handle it, but it's the only one I've found that works.
    /// </summary>
    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.UpdateTerminalScreen))]
    private static class DontUnscubscribeMePatch
    {
        public static void Prefix(LG_ComputerTerminalCommandInterpreter __instance, ref ClaimItemsHelper? __state)
        {
            // Search the callback to find our claim items helper
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

        public static void Postfix(LG_ComputerTerminalCommandInterpreter __instance, ClaimItemsHelper? __state)
        {
            // Check if we even need to restore the state
            if (__state == null || __state.currentIndex >= __state.claimActions.Count) return;
            if (__instance.OnEndOfQueue != null) return;

            // Restore our helper
            __instance.OnEndOfQueue += __state.thisAction;
        }
    }
}
