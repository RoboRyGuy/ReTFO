using Clonesoft.Json;
using GameData;
using HarmonyLib;
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

/*
 * TODO:
 Terminal log file format:




 */

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

    // Password part item
    private class TerminalPasswordPartItem : Item
    {
        public TerminalPasswordPartItem(Terminal.Data data, int index)
            : base($"{data.TerminalName} Password Part #{index}", $"{data.TerminalName} Password Part", eRandomizationType.Progression, new List<string>() { "All", "Logs", "Passwords" })
        {
            TerminalData = data;
            PartNumber = index;
        }

        // The terminal this password part is associated with
        [JsonIgnore]
        public Terminal.Data TerminalData { get; set; }

        // 1-indexed part number
        [JsonIgnore]
        public int PartNumber { get; set; }

        protected string GetPassword(StateTracker stateTracker, out bool isFirst)
        {
            isFirst = true;
            bool hasDimension = Dimension.GetDimension(TerminalData.LayerType, out var dim);
            if (!hasDimension) throw new NotImplementedException();
            LG_Zone zone = dim.GetLayer(TerminalData.LayerType).m_zonesByLocalIndex[TerminalData.Zone?.LocalIndex ?? eLocalZoneIndex.Zone_0];
            LG_ComputerTerminal terminal = zone.TerminalsSpawnedInZone.First(t => Terminal.Data.FromTerminal(t) == TerminalData);
            string password = terminal.m_password;

            if (TerminalData.TerminalStartingStateData.PasswordPartCount == 0)
                return password;

            bool isObtained(int i) => stateTracker.CollectedItemCounts.GetValueOrDefault(GetTerminalPasswordPartItem(TerminalData, i), 0) > 0;
            for (int i = 1; i < PartNumber; i++) if (isObtained(i)) { isFirst = false; break; }


            int partCount = TerminalData.TerminalStartingStateData.PasswordPartCount;
            int perPartCount = password.Length / partCount;
            int remainingCount = password.Length % partCount;

            int start(int i) => perPartCount * i + Math.Min(remainingCount, i);       // Starting position of 0-indexed part i
            int len(int i) => i < remainingCount ? perPartCount + 1 : perPartCount; // Len of 0-indexed part i
            string hide(int i) => new string('-', len(i));                              // Get hidden portion of password for part i
            string reveal(int i) => password.Substring(start(i), len(i));                 // Get actual portion of password for part i

            IEnumerable<string> passwordParts = Enumerable.Range(1, partCount)
                .Select(isObtained)
                .Select((o, i) => o ? reveal(i) : hide(i));

            return string.Join("", passwordParts);
        }

        public override void OnItemObtained(StateTracker stateTracker)
        {
            if (Expedition.Data.FromCurrentExpedition() != TerminalData.ExpeditionData)
                return;

            string password = GetPassword(stateTracker, out _);

            PlayerChatManager.WantToSentTextMessage(
                Player.PlayerManager.GetLocalPlayerAgent(),
                $"{TerminalData.TerminalName} Password:"
            );
            PlayerChatManager.WantToSentTextMessage(
                Player.PlayerManager.GetLocalPlayerAgent(),
                password
            );
            stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (data != TerminalData.ExpeditionData)
                return;

            string password = GetPassword(stateTracker, out bool isFirst);
            if (!isFirst) return;

            PlayerChatManager.WantToSentTextMessage(
                Player.PlayerManager.GetLocalPlayerAgent(),
                $"{TerminalData.TerminalName} Password:"
            );
            PlayerChatManager.WantToSentTextMessage(
                Player.PlayerManager.GetLocalPlayerAgent(),
                password
            );
            stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            // Note that there's no need to split this into two actions, since the output will already be delayed
            string password = GetPassword(stateTracker, out _);
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, "Retrieving password", 2f);
                terminal.AddLine($" Terminal name: {TerminalData.TerminalName}", false);
                terminal.AddLine($" Password:      {password}");
            };
        }
    }

    // Add password parts to the expedition
    [Terminal.Callback]
    public static void AddPasswordPartItems(Terminal.Data data)
    {
        if (data.TerminalStartingStateData.PasswordProtected)
        {
            if (data.TerminalStartingStateData.TerminalZoneSelectionDatas.Count == 0)
            {
                FeatureLogger.Warning($"Terminal has no placement positions for password parts: {data.TerminalName}");
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
                Item passwordItem = GetTerminalPasswordPartItem(data, count);
                data.AddLocation(
                    GetTerminalPasswordPartLocationName(data, count),
                    set!,
                    eRandomizationType.Progression,
                    false,
                    passwordItem
                );
            }
        }
    }

    // Get a password part, 1-indexed. Use Categories[0] to count total parts obtained
    public static Item GetTerminalPasswordPartItem(Terminal.Data data, int count = 1)
        => data.GetItem(new TerminalPasswordPartItem(data, count));

    private static string GetTerminalPasswordPartLocationName(Terminal.Data data, int count)
        => $"{data.TerminalName} Password Part #{count} Location";

    // Identify terminal logs on generation and store references in a helper component
    [ArchivePatch(typeof(LG_TerminalPasswordLinkerJob), nameof(LG_TerminalPasswordLinkerJob.Build))]
    public static class LG_TerminalPasswordLinkerJob__Build__Patch
    {
        public static void Postfix(LG_TerminalPasswordLinkerJob __instance)
        {
            if (__instance.m_passwordLogsIDs.Count != __instance.m_terminalsWithPasswordParts.Count)
                throw new NotImplementedException(); // Unexpected!

            Terminal.Data? data = Terminal.Data.FromTerminal(__instance.m_lockedTerminal);
            if (data == null)
            {
                FeatureLogger.Error("Null terminal data for password link job! Is this a reactor terminal?");
                return;
            }

            for (int i = 0; i < __instance.m_passwordLogsIDs.Count; i++)
            {
                var terminal = __instance.m_terminalsWithPasswordParts[i];
                var locationName = GetTerminalPasswordPartLocationName(data, i + 1);

                var logNames = terminal.m_localLogs.entries.Where(e => e?.value?.FileName?.StartsWith($"KEY", StringComparison.OrdinalIgnoreCase) ?? false).ToList();
                if (logNames.Count == 0)
                    FeatureLogger.Error($"Failed to find any logs for password part location: {locationName}");
                else
                    TerminalLogHelper.AssociateLog(terminal, logNames[0].value.FileName, data.LookupLocation(locationName).ID);

                if (logNames.Count > 1)
                    FeatureLogger.Warning($"Found multiple possible logs for password part location: {locationName}");

            }
        }
    }

}
