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
        public SortedList<string, long> StoredLocations = new();
    }

    // Associate a log with a location
    public static void AssociateLog(LG_ComputerTerminal terminal, string logName, long locationId)
    {
        ContainsLocationLogComp? comp = terminal.GetComponent<ContainsLocationLogComp>();
        if (comp == null)
            comp = terminal.gameObject.AddComponent<ContainsLocationLogComp>();
        if (comp.StoredLocations.TryGetValue(logName, out var oldLocation))
        {
            Game.Data gameData = Plugin.Get().MidManager.GetProcessedGameData();
            int locLength = Math.Max(oldLocation.ToString().Length, locationId.ToString().Length);
            string formatString = new string('0', locLength);
            FeatureLogger.Error(
                $"Overwriting location on pickup!\n"
                + $"  Old Location: [{oldLocation.ToString(formatString)}] {gameData.LookupLocation(oldLocation)}"
                + $"  New Location: [{locationId.ToString(formatString)}] {gameData.LookupLocation(locationId)}"
            );
        }
        comp.StoredLocations[logName.ToUpper()] = locationId;
    }

    // When reading a log, check for associated locations
    [ArchivePatch(typeof(LG_ComputerTerminalCommandInterpreter), nameof(LG_ComputerTerminalCommandInterpreter.ReadLog))]
    public static class LG_ComputerTerminalCommandInterpreter__ReadLog__Patch
    {
        public static void Postfix(LG_ComputerTerminalCommandInterpreter __instance, string param1, string param2)
        {
            ContainsLocationLogComp? comp = __instance.m_terminal.GetComponent<ContainsLocationLogComp>();
            if (comp == null) return;
            if (comp.StoredLocations.TryGetValue(param1.ToUpper(), out long location))
                StateTracker.Get().NotifyFoundLocation(location, __instance.m_terminal.m_syncedInteractionSource);
        }
    }

}
