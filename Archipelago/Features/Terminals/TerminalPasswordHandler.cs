using AIGraph;
using LevelGeneration;
using Player;
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

/*
 Terminal log file format (TextDataBlock):
  (full)     1431221909
  (fragment) 2260297836
 */

public static class TerminalPasswordHandler_Tags
{ 
    extension (Game.Data gameData)
    {
        public TagResolver Tag_TerminalPasswordLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Terminal Password Part Locations", "Locations checked by opening logs containing terminal password parts", gd.Tag_TerminalLogLocations));

        public TagResolver Tag_TerminalPasswordItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Terminal Password Part Item", "A part of a terminal password", gd.Tag_TerminalLogItems));
    }

    extension (Terminal.Data data)
    {
        public TagResolver Tag_TerminalPasswordLocations_ByTerminal
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.TerminalName} Password Locations", "Password part locations for a specific terminal", gd.Tag_TerminalPasswordLocations));

        public TagResolver Tag_TerminalPasswordItems_ByTerminal
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.TerminalName} Password Parts", "Password parts for a specific terminal", gd.Tag_TerminalPasswordItems));
    }
}


// Handles terminal password and the related utilities
[EnableFeatureByDefault, InjectToIl2Cpp]
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

    // Password part location
    private static class TerminalPasswordPartLocation
    {
        public static TagResolver MakeTag(Terminal.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.TerminalName} Password Part #{count} Location", $"Location containng a part of a terminal password", data.Tag_TerminalPasswordLocations_ByTerminal));

        public static LocationData MakeRandData() => new LocationData();
    }

    // Password part item
    private class TerminalPasswordPartItem : Item
    {
        public TerminalPasswordPartItem(Terminal.Data data, int count)
            : base(MakeTag(data, count), MakeRandData())
        {
            TerminalData = data;
            PartNumber = count;
        }

        public static TagResolver MakeTag(Terminal.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.TerminalName} Password Part #{count}", $"A part of a terminal password", data.Tag_TerminalPasswordItems_ByTerminal));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        // The terminal this password part is associated with
        public Terminal.Data TerminalData { get; set; }

        // 1-indexed part number
        public int PartNumber { get; set; }

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, TerminalData.Tag_TerminalPasswordItems_ByTerminal);

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            Expedition.Data expedition = Expedition.Data.FromCurrentExpedition();
            if (TerminalData.IsSameExpedition(expedition))
                OnStartExpeditionWithItem(stateTracker, expedition);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (!TerminalData.IsSameExpedition(data))
                return;

            LG_ComputerTerminal? terminal = TerminalData.GetLG_Terminal();
            if (terminal == null)
            {
                FeatureLogger.Error("Failed to identify spawned terminal while giving password part!");
                return;
            }

            ProgressionObjective_TerminalPassword.Update(TerminalData, terminal);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            // Note that there's no need to split this into two actions, since the output will already be delayed
            LG_ComputerTerminal? passwordTerminal = TerminalData.GetLG_Terminal();
            if (passwordTerminal == null)
                FeatureLogger.Error("Failed to identify spawned terminal while giving password part!");

            yield return () =>
            {
                stateTracker.AddItemToTerminal(this);
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving password", 2f);
                if (passwordTerminal == null)
                    terminal.AddLine("<#F00>Failed to find password terminal instance! No password to grant.</color>");
                else
                {
                    string password = ProgressionObjective_TerminalPassword.MakePasswordHint(TerminalData, passwordTerminal);
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
                KeyedItem passwordItem = GetTerminalPasswordPartItem(data, 1);
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
                KeyedItem passwordItem = GetTerminalPasswordPartItem(data, count);
                data.AddLocation(
                    TerminalPasswordPartLocation.MakeTag(data, count),
                    set!,
                    TerminalPasswordPartLocation.MakeRandData(),
                    passwordItem.ID
                );
            }
        }
    }

    // Get a password part, 1-indexed. Use Categories[0] to count total parts obtained
    public static KeyedItem GetTerminalPasswordPartItem(Terminal.Data data, int count = 1)
    {
        if (data.TryLookupItem(TerminalPasswordPartItem.MakeTag(data, count), out var item))
            return item;

        Item newItem = new TerminalPasswordPartItem(data, count);
        return new(data.AddItem(newItem), newItem);
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
                RandomizationTag tag = TerminalPasswordPartLocation.MakeTag(data, i + 1);
                if (!data.TryLookupLocation(tag, out var location))
                {
                    FeatureLogger.Error("Failed to lookup password part location during association!");
                    return;
                }

                var logNames = terminal.m_localLogs.entries.Where(e => e?.value?.FileName?.StartsWith($"KEY", StringComparison.OrdinalIgnoreCase) ?? false).ToList();
                if (logNames.Count == 0)
                    FeatureLogger.Error($"Failed to find any logs for password part location: {data.LookupTagDef(location.NameTag).Name}");
                else
                    TerminalLogHelper.AssociateLog(terminal, logNames[0].value.FileName, location.ID);

                if (logNames.Count > 1)
                    FeatureLogger.Warning($"Found multiple possible logs for password part location, using first: {data.LookupTagDef(location.NameTag).Name}");
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

            Plugin.Get().StateTracker.NotifyFoundRegion(
                terminalData.TerminalName,
                terminal.m_syncedInteractionSource
            );

            ProgressionObjective_TerminalPassword.Update(terminalData, terminal);
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
            var result = Terminal.Data.FromTerminal(__instance);
            Terminal.Data? data = result.Data;
            if (data == null)
            {
                if (result.IsReactorTerminal)
                    FeatureLogger.Error("A reactor terminal is password locked; Archipelago can't currently support this!");
                else
                    FeatureLogger.Error("Null terminal data for password link job!");
                return;
            }
            ProgressionObjective_TerminalPassword.Setup(data, __instance);
        }
    }

    // Update progression objective state when a recall is initiated
    [ArchivePatch(typeof(LG_ComputerTerminal), nameof(LG_ComputerTerminal.OnPostRecall))]
    public static class LG_ComputerTerminal__OnPostRecall__Patch
    {
        public static void Postfix(LG_ComputerTerminal __instance)
        {
            if (!__instance.IsPasswordProtected) return;

            var result = Terminal.Data.FromTerminal(__instance);
            Terminal.Data? data = result.Data;
            if (data == null)
            {
                if (!result.IsReactorTerminal)
                    FeatureLogger.Error("Failed to identify locked terminal post recall!");
                return;
            }

            ProgressionObjective_TerminalPassword.Update(data, __instance);
            StateTracker.Get().AddItemToTerminal(GetTerminalPasswordPartItem(data, 1).ID);
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
        public static string MakeKey(Terminal.Data data, LG_ComputerTerminal _)
            => data.TerminalName;

        /// <summary>
        /// Set up a progression objective for a particular terminal
        /// </summary>
        public static void Setup(Terminal.Data data, LG_ComputerTerminal terminal)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_TerminalPassword>(MakeKey(data, terminal)).SetupWithData(data, terminal);

        /// <summary>
        /// Update the text for a particular terminal progression objective
        /// </summary>
        public static void Update(Terminal.Data data, LG_ComputerTerminal terminal)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_TerminalPassword>(MakeKey(data, terminal)).UpdateInternal(data, terminal);

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
        public void SetupWithData(Terminal.Data data, LG_ComputerTerminal terminal)
        {
            HeaderText = data.TerminalName;
            ScopeTarget = new(
                terminal.SpawnNode.m_zone.DimensionIndex,
                terminal.SpawnNode.LayerType,
                terminal.SpawnNode.m_zone.LocalIndex
            );
            SubText = $"Current known password: {MakePasswordHint(data, terminal)}";
            Refresh();
        }

        /// <summary>
        /// Update this progression status using the provided data and terminal.
        /// </summary>
        /// <param name="data">The data for this terminal</param>
        /// <param name="terminal">The terminal to update for</param>
        public void UpdateInternal(Terminal.Data data, LG_ComputerTerminal terminal)
        {
            IsActive = terminal.IsPasswordProtected;
            SubText = $"Current known password: {MakePasswordHint(data, terminal)}";
            Refresh();
        }

        /// <summary>
        /// Create the formatted text used to display this terminal's password
        /// </summary>
        /// <param name="data">Terminal data for this terminal</param>
        /// <param name="terminal">The terminal text is being formatted for</param>
        /// <returns></returns>
        public static string MakePasswordHint(Terminal.Data data, LG_ComputerTerminal terminal)
        {
            StateTracker stateTracker = StateTracker.Get();
            string password = terminal.m_password;

            if (data.TerminalStartingStateData.TerminalZoneSelectionDatas.Count == 0)
                return password; // It's free!

            int partCount = data.TerminalStartingStateData.PasswordPartCount;
            int perPartCount = password.Length / partCount;
            int remainingCount = password.Length % partCount;

            bool isObtained(int i) => stateTracker.CollectedItemCounts.GetValueOrDefault(GetTerminalPasswordPartItem(data, i).ID, 0) > 0;
            int start(int i) => perPartCount * i + Math.Min(remainingCount, i);     // Starting position of 0-indexed part i
            int len(int i) => i < remainingCount ? perPartCount + 1 : perPartCount; // Len of 0-indexed part i
            string hide(int i) => new string('–', len(i));                          // Get hidden portion of password for part i
            string reveal(int i) => password.Substring(start(i), len(i));           // Get actual portion of password for part i

            IEnumerable<string> passwordParts = Enumerable.Range(1, partCount)
                .Select(isObtained)
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
