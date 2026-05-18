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

public static class WinEventHandler_Tags
{
    extension (Game.Data gameData)
    {
        public TagResolver Tag_WinEventItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Win Event Items", "Event items which cause the player to immediately clear the main sector and extract (optionally triggered on death)", gd.Tag_EventItems));
    }

    extension (Expedition.Data data)
    {
        public TagResolver Tag_WinEventItemForExpedition
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Win Event Item", "A win event item category for a particular expedition", gd.Tag_WinEventItems));
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
    /// Get an instant win item
    /// </summary>
    /// <param name="data">the expedition the item is for</param>
    /// <param name="onDeath">If true, the instant win is "on death". Otherwise, it's instant.</param>
    /// <returns></returns>
    public static KeyedItem GetInstantWinItem(Expedition.Data data, bool onDeath)
    {
        if (data.TryLookupItem(InstantWinItem.MakeTag(data, onDeath), out var item))
            return item;

        Item newItem = new InstantWinItem(data, onDeath);
        return new(data.AddItem(newItem), newItem);
    }

    public static Path.RequiredItem GetInstantWinPathReqs(Expedition.Data data)
        => new(Path.RequiredItem.eType.Category, data.Tag_WinEventItemForExpedition);

    /// <summary>
    /// Item represnting the instant win (and win on death) events
    /// Note that because these are treated the same, only one can exist per expedition. 
    /// If an expedition has both types, this might sometimes trigger the wrong one when randomized
    /// </summary>
    private class InstantWinItem : Item
    {
        public InstantWinItem(Expedition.Data data, bool onDeath)
            : base(MakeTag(data, onDeath), MakeRandData())
        {
            ExpeditionData = data;
            OnDeath = onDeath;
        }

        public static TagResolver MakeTag(Expedition.Data data, bool onDeath)
            => new TagResolver(data, gd => gd.LookupOrCreateTag(onDeath ? $"{data.ExpeditionName} Win on Death" : $"{data.ExpeditionName} Instant Win", "An instant win event instance", data.Tag_WinEventItemForExpedition));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Expedition.Data ExpeditionData { get; set; }

        public bool OnDeath { get; set; }

        public override Path.RequiredItem PathReqs => GetInstantWinPathReqs(ExpeditionData);

        public override Expedition.Data? RequiredExpedition => ExpeditionData;

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (ExpeditionData.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ExpeditionData.IsSameExpedition(data))
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

            var item = GetInstantWinItem(data, e.Type == eWardenObjectiveEventType.WinOnDeath);
            FeatureLogger.Notice($"Adding instant win item: {data.LookupTagDef(item.NameTag).Name}");

            EventHelper.ConvertToCheckLocationEvent(
                data, e, count, item.ID
            );
        }
    }

}
