using Clonesoft.Json;
using GameData;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class WinEventHandler : ArchipelagoFeature
{
    public override string Name => "Win Event Handler";
    public override string Description
        => "Handles the Win event type.\n"
        + "Win events immediately complete the main sector and end the expedition, skipping extract.\n"
        + "The WinOnDeath event is treated as a win event, with the assumption you will die very soon.";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// Get an instant win item
    /// </summary>
    /// <param name="data">the expedition the item is for</param>
    /// <param name="onDeath">
    /// If true, the instant win is "on death".
    /// Note that this is lazily-implemented; if an expedition has both onDeath and non onDeath, the
    ///  first to be registered will be the one used for all items.
    /// </param>
    /// <returns></returns>
    public static Item GetInstantWinItem(Expedition.Data data, bool onDeath = false)
        => data.GetItem(new InstantWinItem(data, onDeath));

    /// <summary>
    /// Item represnting the instant win (and win on death) events
    /// Note that because these are treated the same, only one can exist per expedition. 
    /// If an expedition has both types, this might sometimes trigger the wrong one when randomized
    /// </summary>
    private class InstantWinItem : Item
    {
        public InstantWinItem(Expedition.Data data, bool onDeath)
            : base($"{data.ExpeditionName} Instant Win")
        {
            ExpeditionData = data;
            OnDeath = onDeath;
        }

        [JsonIgnore]
        public Expedition.Data ExpeditionData { get; set; }

        [JsonIgnore]
        public bool OnDeath { get; set; }

        private static readonly RandomizationData s_randData = new()
        {
            IsProgression = true,
            Categories = new() { "All", "Objective Completions" }
        };
        public override RandomizationData RandData => s_randData;

        public override void OnItemObtained(StateTracker stateTracker, long sourceLocationId, PlayerAgent? player)
        {
            if (Expedition.Data.FromCurrentExpedition() == ExpeditionData)
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (data == ExpeditionData)
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Retrieving your win event", 2f);
            };

            yield return () =>
            {
                if (OnDeath)
                {
                    WorldEventManager.DoExcecuteEvent(new WardenObjectiveEventData()
                    {
                        Type = eWardenObjectiveEventType.WinOnDeath,
                        Delay = 0f,
                    });
                    terminal.AddLine("<#F00>ERROR: Prisoner death required. Proceed to self-terminate immediately.</color>");
                }
                else
                {
                    WorldEventManager.DoExcecuteEvent(new WardenObjectiveEventData()
                    {
                        Type = eWardenObjectiveEventType.ForceInstantWin,
                        Delay = 0f,
                    });
                    terminal.AddLine("Congrats, you win!");
                }
            };
        }
    }

    // Add win event items and set up win event locations
    [Event.Callback]
    public static void ProcessWinEvents(Event.Data data)
    {
        int count = 0;
        foreach (var e in data)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ForceInstantWin && e.Type != eWardenObjectiveEventType.WinOnDeath)
                continue;
            ++count;

            EventHelper.ConvertToCheckLocationEvent(
                data, e, count, 
                GetInstantWinItem(data, e.Type == eWardenObjectiveEventType.WinOnDeath)
            );
        }
    }

}
