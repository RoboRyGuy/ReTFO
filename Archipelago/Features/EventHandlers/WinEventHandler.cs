using GameData;
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class WinEventHandler_Tags
{
    extension (Game.Data gameData)
    {
        /// <summary>
        /// Parent tag for win event items
        /// </summary>
        public ItemID Item_WinEvent
            => ItemID.From(gameData, "Win Event Items", data => new("Event items which cause the player to immediately clear the main sector and extract (sometimes triggered on death)", data.Item_Event));
    }

    extension(Expedition.Data data)
    {
        /// <summary>
        /// Parent tag for win event items for a particular expedition
        /// </summary>
        public ItemID Item_WinEvent_ByExpedition
            => ItemID.From(
                data,
                $"{data.ExpeditionName} Win Event Items",
                data => new("Parent tag for win event items for a particular expedition", data.Item_WinEvent)
            );

        /// <summary>
        /// A particular type of win event item for an expedition
        /// </summary>
        /// <param name="onDeath">If the event only triggers when all team members die</param>
        public ItemID Item_WinEvent_Instance(bool onDeath)
            => ItemID.From(
                data,
                onDeath ? $"{data.ExpeditionName} Win-On-Death Event" : $"{data.ExpeditionName} Instance Win Event",
                data => new("A particular type of win event item for an expedition", data.Item_WinEvent_ByExpedition),
                new WinEventHandler.InstantWinItem(data.Region_Expedition, onDeath)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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
    /// Item represnting the instant win (and win on death) events
    /// </summary>
    public class InstantWinItem : TerminalItem
    {
        public InstantWinItem(RegionID expedition, bool onDeath)
            : base(MakeRandData())
        {
            ExpeditionRegion = expedition;
            OnDeath = onDeath;
        }

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        /// <summary>
        /// The expedition the win event is for
        /// </summary>
        public RegionID ExpeditionRegion { get; private init; }

        /// <summary>
        /// If true, the win event only triggers when the team wipes
        /// </summary>
        public bool OnDeath { get; private init; }

        public override RegionID TargetRegion => ExpeditionRegion;

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Retrieving your win event", 2f);
            };

            yield return () =>
            {
                if (OnDeath)
                {
                    WorldEventManager.ExecuteEvent(new WardenObjectiveEventData()
                    {
                        Type = eWardenObjectiveEventType.WinOnDeath,
                        Layer = LG_LayerType.MainLayer,
                        Delay = 0f,
                    });
                    terminal.AddLine("<#F00>ERROR: Prisoner death required. Proceed to self-terminate immediately.</color>");
                }
                else
                {
                    WorldEventManager.ExecuteEvent(new WardenObjectiveEventData()
                    {
                        Type = eWardenObjectiveEventType.ForceInstantWin,
                        Layer = LG_LayerType.MainLayer,
                        Delay = 0f,
                    });
                    terminal.AddLine("Congrats, you win!");
                }
            };
        }
    }

    // Add win event items and set up win event locations
    [Event.Callback]
    public void ProcessWinEvents(Event.Data data)
    {
        int count = 0;
        foreach (var e in data)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ForceInstantWin && e.Type != eWardenObjectiveEventType.WinOnDeath)
                continue;
            ++count;

            EventHelper.CreateEventLocation(data, e, count, data.Item_WinEvent_Instance(e.Type == eWardenObjectiveEventType.WinOnDeath));
        }
    }

}
