using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class TerminalLogHelper_Tags
{
    extension (Game.Data gameData)
    {
        public LocationID Location_TerminalLogs
            => LocationID.From(gameData, "Terminal Log Locations", data => new("Locations checked by reading specific terminal logs", data.Location_All));

        public ItemID Item_TerminalLogs
            => ItemID.From(gameData, "Terminal Log Items", data => new("Items typically obtained by reading specific terminal logs", data.Item_All));
    }
}

// Utility for associating logs with locations so that those locations get checked when the log is read
[InjectToIl2Cpp, AutomatedFeature, EnableFeatureByDefault]
public class TerminalLogHelper : ArchipelagoFeature
{
    public override string Name => "Terminal Log Helper";
    public override string Description 
        => "Helper for other handlers\n" 
        + "Associates terminal logs with locations, so when they're checked that location is checked";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public abstract class TerminalLogLocation : Location
    {
        public TerminalLogLocation(RegionList regions, LocationData randData, ItemID itemId) : base(regions, randData, itemId) { }

        /// <summary>
        /// Called when the log is read, but the location is not randomized. Provided to help implement vanilla-like behaviour.
        /// </summary>
        /// <param name="stateTracker">The current state tracker</param>
        /// <param name="terminal">The terminal the log was read on</param>
        public abstract void OnNotRandomized(StateTracker stateTracker, LG_ComputerTerminal terminal);
    }

    // Component placed on terminals to mark them as containing logs we care about
    [InjectToIl2Cpp]
    private class ContainsLocationLogComp : MonoBehaviour
    {
        public SortedList<string, LocationID> StoredLocations = new();
    }

    /// <summary>
    /// Associate a log with a location
    /// </summary>
    /// <param name="terminal">The terminal hosting the log</param>
    /// <param name="logName">The log's name; the same name used to read the log</param>
    /// <param name="locationId">ID of the location to associate with the log</param>
    /// <param name="overwriteLog">If true, replace the log's text with a simple "you found item x" message if it's randomized</param>
    public static void AssociateLog(LG_ComputerTerminal terminal, string logName, LocationID locationId, bool overwriteLog = true)
    {
        logName = logName.ToUpper();
        StateTracker stateTracker = StateTracker.Get();
        Game.Data gameData = stateTracker.GameData;
        ContainsLocationLogComp comp = terminal.GetComponent<ContainsLocationLogComp>()
            ?? terminal.gameObject.AddComponent<ContainsLocationLogComp>();
        
        if (comp.StoredLocations.TryGetValue(logName, out var oldLocation))
        {
            int locLength = Math.Max(oldLocation.ID.ToString().Length, locationId.ID.ToString().Length);
            string formatString = new string('0', locLength);
            FeatureLogger.Error(
                $"Overwriting location stored in log!\n"
                + $"  Old Location: [{oldLocation.ID.ToString(formatString)}] {gameData.Locations.LookUpName(oldLocation)}"
                + $"  New Location: [{locationId.ID.ToString(formatString)}] {gameData.Locations.LookUpName(locationId)}"
            );
        }
        comp.StoredLocations[logName] = locationId;

        int entry = terminal.m_localLogs.FindEntry(logName);
        if (entry == -1)
        {
            FeatureLogger.Warning("Failed to look up and overwrite local log while associating log: " + logName);
            return;
        }

        Location loc = gameData.Locations.LookUpValueChecked(locationId);
        if (!(loc.RandData.IsTreatedAsRandom && overwriteLog)) return;

        var log = terminal.m_localLogs.entries[entry].value;
        log.FileContent = new()
        {
            UntranslatedText =
                "Congratulations! By viewing this log, you have obtained the following item(s):"
                + $"\n  {loc.ScoutedItemName ?? (loc.ItemID.IsNull ? "None" : gameData.Items.LookUpName(loc.ItemID))}",
            OldId = 0,
            Id = 0,
        };
    }

    // When reading a log, check for associated locations
    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.ReadLog))]
    public static class LG_ComputerTerminalCommandInterpreter__ReadLog__Patch
    {
        public static void Postfix(LG_ComputerTerminalCommandInterpreter __instance, string param1, string param2)
        {
            ContainsLocationLogComp? comp = __instance.m_terminal.GetComponent<ContainsLocationLogComp>();
            if (comp == null) return;
            if (comp.StoredLocations.TryGetValue(param1.ToUpper(), out LocationID location))
            {
                StateTracker stateTracker = StateTracker.Get();
                Location loc = stateTracker.NotifyFoundLocation(location, __instance.m_terminal.m_syncedInteractionSource);
                if (!loc.RandData.IsTreatedAsRandom)
                {
                    if (loc is TerminalLogLocation log)
                        log.OnNotRandomized(stateTracker, __instance.m_terminal);
                }
            }
        }
    }

}
