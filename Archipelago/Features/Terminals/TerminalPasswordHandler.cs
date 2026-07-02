using AIGraph;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

/*
 Terminal log file format (TextDataBlock):
  (full)     1431221909
  (fragment) 2260297836
 */

public static class TerminalPasswordHandler_Tags
{ 
    extension (Game.Data gameData)
    {
        public LocationID Location_TerminalPasswords
            => LocationID.From(gameData, "Terminal Password Part Locations", data => new("Locations checked by opening logs containing terminal password parts", data.Location_TerminalLogs));

        public ItemID Item_TerminalPasswords
            => ItemID.From(gameData, "Terminal Password Part Item", data => new("A part of a terminal password", data.Item_Codes));
    }

    extension (Terminal.Data data)
    {
        public LocationID Location_TerminalPasswords_ByTerminal
            => LocationID.From(data, $"{data.TerminalName} Password Locations", data => new("Password part locations for a specific terminal", data.Location_TerminalPasswords));

        public ItemID Item_TerminalPasswords_ByTerminal
            => ItemID.From(data, $"{data.TerminalName} Password Parts", data => new("Password parts for a specific terminal", data.Item_TerminalPasswords));

        public LocationID Location_TerminalPasswords_Instance(int count)
            => LocationID.From(data, $"{data.TerminalName} Password Location #{count}", data => new("A particular password part's location", data.Location_TerminalPasswords_ByTerminal));

        public ItemID Item_TerminalPasswords_Instance(int count)
            => ItemID.From(
                data, 
                $"{data.TerminalName} Password Part #{count}", 
                data => new("A particular password part", data.Item_TerminalPasswords_ByTerminal),
                new TerminalPasswordHandler.TerminalPasswordPartItem(data.Region_Terminal, count)
            );
    }
}


// Handles terminal password and the related utilities
[EnableFeatureByDefault, AutomatedFeature, InjectToIl2Cpp]
public class TerminalPasswordHandler : ArchipelagoFeature
{
    public override string Name => "Terminal Password Handler";
    public override string Description 
        => "Adds passwords to terminals\n"
        + "Marks terminal password logs so that the correct location is checked when they're opened";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Password part item
    public class TerminalPasswordPartItem : TerminalItem
    {
        public TerminalPasswordPartItem(RegionID terminal, int count)
            : base(new ItemData() { IsProgression = true })
        {
            TerminalRegion = terminal;
            PartNumber = count;
        }

        // The terminal this password part is associated with
        public RegionID TerminalRegion { get; private init; }

        // 1-indexed part number
        public int PartNumber { get; private init; }

        public override RegionID TargetRegion => TerminalRegion;

        public override void OnEnteredExpedition(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
        {
            base.OnEnteredExpedition(stateTracker, sourceLocationId, player, itemId);

            Terminal.Data data = new(stateTracker.GameData, TerminalRegion);
            LG_ComputerTerminal? terminal = data.GetLG_Terminal();
            if (terminal == null)
                FeatureLogger.Error("Failed to identify spawned terminal while giving password part!");
            else
                ProgressionObjective_TerminalPassword.NotifyFoundCode(terminal, PartNumber);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            // Note that there's no need to split this into two actions, since the output will already be delayed
            Terminal.Data data = new(stateTracker.GameData, TerminalRegion);
            LG_ComputerTerminal? passwordTerminal = data.GetLG_Terminal();
            if (passwordTerminal == null)
                FeatureLogger.Error("Failed to identify spawned terminal while giving password part!");

            yield return () =>
            {
                stateTracker.AddItemToTerminal(itemId);
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving password", 2f);
                if (passwordTerminal == null)
                    terminal.AddLine("<#F00>Failed to find password terminal instance! No password to grant.</color>");
                else
                {
                    string password = ProgressionObjective_TerminalPassword.MakePasswordHint(passwordTerminal);
                    terminal.AddLine(string.Format(ArchipelagoFeatureHelper.GetFeature<ArchipelagoFeature>().Localization.Get(1431221909), password));
                }
            };
        }
    }

    // Add password parts to the expedition
    [Terminal.Callback]
    public void AddPasswordPartItems(Terminal.Data data)
    {
        if (data.TerminalStartingStateData.PasswordProtected)
        {
            if (data.TerminalStartingStateData.TerminalZoneSelectionDatas.Count == 0)
            {
                FeatureLogger.Warning($"Terminal has no placement positions for password parts: {data.TerminalName}");

                // This item is used at runtime to help display the password, even though no parts actually exist
                ItemID passwordItem = data.Item_TerminalPasswords_Instance(1);
                return;
            }

            var regionSets = data.UnstuffPlacements(
                data.PlacementsToTerminalRegions(data.TerminalStartingStateData.TerminalZoneSelectionDatas),
                data.TerminalStartingStateData.PasswordPartCount
            );

            int count = 0;
            foreach (var set in regionSets)
            {
                ++count;
                data.Locations.CreateValue(
                    data.Location_TerminalPasswords_Instance(count),
                    set!,
                    new LocationData(),
                    data.Item_TerminalPasswords_Instance(count)
                );
            }
        }
    }

    // Identify terminal logs on generation and associate them with the password locations
    [ArchivePatch(typeof(LG_TerminalPasswordLinkerJob), nameof(LG_TerminalPasswordLinkerJob.Build))]
    public static class LG_TerminalPasswordLinkerJob__Build__Patch
    {
        public static void Postfix(LG_TerminalPasswordLinkerJob __instance)
        {
            var result = Terminal.Data.FromTerminal(__instance.m_lockedTerminal);
            Terminal.Data? data = result.Data;
            if (data == null)
            {
                if (result.IsReactorTerminal)
                    FeatureLogger.Error("A reactor terminal is password locked; Archipelago can't currently support this!");
                else
                    FeatureLogger.Error("Null terminal data for password link job!");
                return;
            }

            for (int i = 0; i < __instance.m_terminalsWithPasswordParts.Count; i++)
            {
                var terminal = __instance.m_terminalsWithPasswordParts[i];
                LocationID id = data.Location_TerminalPasswords_Instance(i + 1);

                var logNames = terminal.m_localLogs.entries.Where(e => e?.value?.FileName?.StartsWith($"KEY", StringComparison.OrdinalIgnoreCase) ?? false).ToList();
                if (logNames.Count == 0)
                    FeatureLogger.Error($"Failed to find any logs for password part location: {data.Locations.LookUpName(id)}");
                else
                    TerminalLogHelper.AssociateLog(terminal, logNames[0].value.FileName, id);

                if (logNames.Count > 1)
                    FeatureLogger.Warning($"Found multiple possible logs for password part location, using first: {data.Locations.LookUpName(id)}");
            }
        }
    }

    // Retry discovering the terminal when the password is entered
    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.TryUnlockingTerminal))]
    public static class LG_ComputerTerminalCommandInterpreter__TryUnlockingTerminal__Patch
    {
        public static void Postfix(LG_ComputerTerminalCommandInterpreter __instance)
        {
            LG_ComputerTerminal terminal = __instance.m_terminal;
            if (terminal.IsPasswordProtected) return;

            var result = Terminal.Data.FromTerminal(terminal);
            Terminal.Data? terminalData = result.Data;
            if (terminalData == null)
            {
                if (!result.IsReactorTerminal)
                    FeatureLogger.Error("Unlocked unknown terminal!");
                return;
            }

            StateTracker.Get().NotifyFoundRegion(
                terminalData.TerminalName,
                terminal.m_syncedInteractionSource
            );

            ProgressionObjective_TerminalPassword.Update(terminal);
        }
    }

    // Set up our custom progression objective and add item to terminal
    [ArchivePatch(typeof(LG_ComputerTerminal), nameof(LG_ComputerTerminal.OnFactoryBuildDone))]
    public static class LG_ComputerTerminal__OnFactoryBuildDone__Patch
    {
        public static void Postfix(LG_ComputerTerminal __instance)
        {
            // Note: This is often called on destroyed terminals, hence why we check for it
            // Of note, it's usually called on terminals which were part of the previous expedition
            if (!__instance || !__instance.IsPasswordProtected) return;
            ProgressionObjective_TerminalPassword.Setup(__instance);
        }
    }

    // Update progression objective state when a recall is initiated
    [ArchivePatch(typeof(LG_ComputerTerminal), nameof(LG_ComputerTerminal.OnPostRecall))]
    public static class LG_ComputerTerminal__OnPostRecall__Patch
    {
        public static void Postfix(LG_ComputerTerminal __instance)
        {
            if (!__instance.IsPasswordProtected) return;

            // If it's not randomized, the password changes. So we can update that
            var result = Terminal.Data.FromTerminal(__instance);
            if (result.Data == null)
            {
                FeatureLogger.Error("Failed to update terminal UI post reload; failed to find terminal data!");
                return;
            }

            for (int i = 1; i <= __instance.m_passwordLinkerJob.m_passwordParts; i++)
            {
                LocationID id = result.Data.Location_TerminalPasswords_Instance(i);
                Location loc = result.Data.Locations.LookUpValueChecked(id);
                if (!loc.RandData.IsTreatedAsRandom)
                    ProgressionObjective_TerminalPassword.ResetCode(__instance, i);
            }

            ProgressionObjective_TerminalPassword.Update(__instance);
        }
    }

    /// <summary>
    /// Custom progression objective object used to display codes during reactor startup sequence
    /// </summary>
    public class ProgressionObjective_TerminalPassword : CustomObjectiveHandler.ObjectiveItem
    {
        /// <summary>
        /// Make the string key used to find the progression objective
        /// </summary>
        public static string MakeKey(LG_ComputerTerminal terminal)
            => terminal.Pointer.ToString();

        /// <summary>
        /// Set up a progression objective for a particular terminal
        /// </summary>
        public static void Setup(LG_ComputerTerminal terminal)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_TerminalPassword>(MakeKey(terminal)).SetupWithData(terminal);

        /// <summary>
        /// Update the text for a particular terminal progression objective
        /// </summary>
        public static void Update(LG_ComputerTerminal terminal)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_TerminalPassword>(MakeKey(terminal)).UpdateInternal(terminal);

        /// <summary>
        /// Notify that a code part has been found for the specified terminal
        /// </summary>
        public static void NotifyFoundCode(LG_ComputerTerminal terminal, int count)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_TerminalPassword>(MakeKey(terminal)).NotifyFoundCodeInternal(terminal, count);

        /// <summary>
        /// Reset password progress (typically because the password resets during checkpoints)
        /// </summary>
        public static void ResetCode(LG_ComputerTerminal terminal, int count)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_TerminalPassword>(MakeKey(terminal)).ResetCodeInternal(terminal, count);

        /// <summary>
        /// Make a password hint for the provided terminal based on current found hints
        /// </summary>
        public static string MakePasswordHint(LG_ComputerTerminal terminal)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_TerminalPassword>(MakeKey(terminal)).MakePasswordHintInternal(terminal);

        private BitArray? m_foundParts = null;

        public override void Setup()
        {
            IsActive = false;
            Scope = CustomObjectiveHandler.eObjectiveScope.Zone;
            ObjectiveTag = "PASSWORD";
            HeaderText = "TERMINAL PASSWORD OBJECTIVE";
            SubText = "Retrieving data...";
            base.Setup();
        }

        /// <summary>
        /// Set up this progression objective to use the provided data
        /// </summary>
        public void SetupWithData(LG_ComputerTerminal terminal)
        {
            m_foundParts = new(terminal.m_passwordLinkerJob.m_passwordParts);
            HeaderText = $"{terminal.m_terminalItem.TerminalItemKey} Password";
            ScopeTarget = new(
                terminal.SpawnNode.m_zone.DimensionIndex,
                terminal.SpawnNode.LayerType,
                terminal.SpawnNode.m_zone.LocalIndex
            );
            UpdateInternal(terminal);
        }

        /// <summary>
        /// Update this progression status using the provided data and terminal.
        /// </summary>
        public void UpdateInternal(LG_ComputerTerminal terminal)
        {
            IsActive = terminal.IsPasswordProtected;
            SubText = $"Current known password: {MakePasswordHintInternal(terminal)}";
            Refresh();
        }

        /// <summary>
        /// Notify that a code part has been found
        /// </summary>
        /// <param name="terminal">The terminal for this progression objective</param>
        /// <param name="count">The 1-indexed count of the code part</param>
        public void NotifyFoundCodeInternal(LG_ComputerTerminal terminal, int count)
        {
            if (m_foundParts == null)
                throw new NullReferenceException("Terminal UI was not properly set up");
            else if (count < 1 || count > m_foundParts.Count)
                throw new ArgumentOutOfRangeException("Terminal UI notified of a code which was out of bounds");

            m_foundParts[count - 1] = true;
            UpdateInternal(terminal);
        }

        /// <summary>
        /// Reset a particular code part's progress
        /// </summary>
        public void ResetCodeInternal(LG_ComputerTerminal terminal, int count)
        {
            if (m_foundParts == null)
                throw new NullReferenceException("Terminal UI was not properly set up");
            else if (count < 1 || count > m_foundParts.Count)
                throw new ArgumentOutOfRangeException("Terminal UI notified of a code which was out of bounds");

            m_foundParts[count - 1] = false;
            UpdateInternal(terminal);
        }


        /// <summary>
        /// Create the formatted text used to display this terminal's password
        /// </summary>
        /// <param name="terminal">The terminal text is being formatted for</param>
        /// <returns></returns>
        public string MakePasswordHintInternal(LG_ComputerTerminal terminal)
        {
            string password = terminal.m_password;

            int partCount = terminal.m_passwordLinkerJob.m_passwordParts;
            if (partCount == 0) return password; // It's free!

            int perPartCount = password.Length / partCount;
            int remainingCount = password.Length % partCount;

            if (m_foundParts == null || m_foundParts.Length < partCount)
                throw new InvalidOperationException("Attempted to generate password hint before being set up!");

            int start(int i) => perPartCount * i + Math.Min(remainingCount, i);     // Starting position of 0-indexed part i
            int len(int i) => i < remainingCount ? perPartCount + 1 : perPartCount; // Len of 0-indexed part i
            string hide(int i) => new string('–', len(i));                          // Get hidden portion of password for part i
            string reveal(int i) => password.Substring(start(i), len(i));           // Get actual portion of password for part i

            IEnumerable<string> passwordParts = Enumerable.Range(1, partCount)
                .Select(i => m_foundParts[i - 1])
                .Select((o, i) => o ? reveal(i) : hide(i));

            return string.Join("", passwordParts);
        }

        public override void CheckScope(AIG_CourseNode? localNode = null)
        {
            base.CheckScope(localNode);
            if (IsInScope && Scope == CustomObjectiveHandler.eObjectiveScope.Zone)
            {
                Scope = CustomObjectiveHandler.eObjectiveScope.Layer;
                IsActive = true;
            }
        }
    }

}
