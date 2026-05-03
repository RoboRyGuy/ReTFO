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
        public TagResolver Tag_TerminalLogLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Terminal Log Locations", "Locations checked by reading specific terminal logs", gd.Tag_AllLocations));

        public TagResolver Tag_TerminalLogItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Terminal Log Items", "Items typically obtained by reading specific terminal logs", gd.Tag_AllItems));
    }
}

// Utility for associating logs with locations so that those locations get checked when the log is read
[InjectToIl2Cpp, EnableFeatureByDefault]
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

    // Component placed on terminals to mark them as containing logs we care about
    [InjectToIl2Cpp]
    private class ContainsLocationLogComp : MonoBehaviour
    {
        public SortedList<string, LocationID> StoredLocations = new();
    }

    // Associate a log with a location
    public static void AssociateLog(LG_ComputerTerminal terminal, string logName, LocationID locationId)
    {
        logName = logName.ToUpper();
        Game.Data gameData = Plugin.Get().MidManager.GetProcessedGameData();
        ContainsLocationLogComp comp = terminal.GetComponent<ContainsLocationLogComp>()
            ?? terminal.gameObject.AddComponent<ContainsLocationLogComp>();
        
        if (comp.StoredLocations.TryGetValue(logName, out var oldLocation))
        {
            int locLength = Math.Max(oldLocation.AsId.ToString().Length, locationId.AsId.ToString().Length);
            string formatString = new string('0', locLength);
            FeatureLogger.Error(
                $"Overwriting location stored in log!\n"
                + $"  Old Location: [{oldLocation.AsId.ToString(formatString)}] {gameData.LookupTagDef(gameData.LookupLocation(oldLocation).NameTag).Name}"
                + $"  New Location: [{locationId.AsId.ToString(formatString)}] {gameData.LookupTagDef(gameData.LookupLocation(locationId).NameTag).Name}"
            );
        }
        comp.StoredLocations[logName] = locationId;

        int entry = terminal.m_localLogs.FindEntry(logName);
        if (entry == -1)
        {
            FeatureLogger.Warning("Failed to look up and overwrite local log while associating log: " + logName);
            return;
        }

        Location loc = gameData.LookupLocation(locationId);
        if (!StateTracker.Get().TestRandomization(loc).IsTreatedAsRandom) return;

        var log = terminal.m_localLogs.entries[entry].value;
        log.FileContent = new()
        {
            UntranslatedText =
                "Congratulations! By viewing this log, you have obtained the following item(s):"
                + $"\n  {loc.ScoutedItem?.ItemDisplayName ?? (loc.ItemID.IsNull ? "None" : gameData.LookupTagDef(gameData.LookupItem(loc.ItemID).NameTag).Name)}",
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
                StateTracker.Get().NotifyFoundLocation(location, __instance.m_terminal.m_syncedInteractionSource);
        }
    }

}
