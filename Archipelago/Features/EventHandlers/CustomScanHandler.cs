using GameData;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class CustomScanHandler_Tags
{
    extension (Game.Data gameData)
    {
        /// <summary>
        /// Scans triggering custom scans based on world event objects
        /// </summary>
        public TagResolver Tag_EventScanItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Event Scan Items", "Scans triggered by starting a custom event", gd.Tag_ScanItems));
    }

}

[EnableFeatureByDefault, AutomatedFeature]
public class CustomScanHandler : ArchipelagoFeature
{
    public override string Name => "Custom Scans Handler";
    public override string Description
        => "Handles certain custom scans which are triggered by events"
        + "\nThese are typically zone scans, and are often started by a terminal command";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public class StartCustomScanItem : Item
    {
        public StartCustomScanItem(Expedition.Data data, string worldEventObjectFilter)
            : base(MakeTag(data, worldEventObjectFilter), MakeRandData())
        {
            Data = data;
            WorldEventObjectFilter = worldEventObjectFilter;
        }

        public static TagResolver MakeTag(Expedition.Data data, string worldEventObjectFilter)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Event Scan \"{worldEventObjectFilter}\"", "A particular event-triggered scan", gd.Tag_EventScanItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        /// <summary>
        /// The expedition this scan occurs in
        /// </summary>
        public Expedition.Data Data { get; set; }

        /// <summary>
        /// The world event object filter used to initiate this scan
        /// </summary>
        public string WorldEventObjectFilter { get; set; }

        public override Expedition.Data? RequiredExpedition => Data;

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (Data.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (Data.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Initiating Scan {WorldEventObjectFilter}", 2f);
                terminal.AddLine($"Scan will start in 10 seconds. Good luck :)");
            };

            yield return () =>
            {
                WorldEventManager.ExecuteEvent(new WardenObjectiveEventData()
                {
                    Type = eWardenObjectiveEventType.ActivateChainedPuzzle,
                    WorldEventObjectFilter = WorldEventObjectFilter,
                    Delay = 10f,
                });
            };
        }
    }

    public static KeyedItem GetCustomScanStartItem(Expedition.Data data, string worldEventObjectFilter)
    {
        if (data.TryLookupItem(StartCustomScanItem.MakeTag(data, worldEventObjectFilter), out var item))
            return item;

        Item newItem = new StartCustomScanItem(data, worldEventObjectFilter);
        return new KeyedItem(data.AddItem(newItem), newItem);
    }

    // Replace custom scan events with check location events for that scan location
    [Event.Callback]
    public void ProcessCustomScanStartEvents(Event.Data data)
    {
        int count = 0;
        foreach (var e in data)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ActivateChainedPuzzle)
                continue;
            ++count;

            /* While processing UniqueCommands for terminals, if there is an event where .ChainPuzzle != 0,
             *  it stops processing before that event is executed and immediately activates the chain puzzle.
             *  It then continues processing events after the puzzle is completed.
             * In later rundowns, the devs mark such events with the ActivateChainedPuzzle event type (probably for readability),
             *  but as they don't reference a world object they don't actually activate a world puzzle.
             * If we try to process those events, things get wonky (lots of charger alarms for some reason)
             */
            if ((e.WorldEventObjectFilter?.Length ?? 0) == 0)
                continue;

            KeyedItem item = GetCustomScanStartItem(data, e.WorldEventObjectFilter!);
            EventHelper.ConvertToCheckLocationEvent(data, e, count, item.ID);
        }
    }

    // Add regions and process events for custom scans (by zone)
    [Zone.Callback]
    public void AddCustomScans(Zone.Data data)
    {
        if (data.Zone == null) return;

        foreach (var scan in data.Zone.WorldEventChainedPuzzleDatas.Iter())
        {
            var eventWrapper = data.WrapEvents(scan.EventsOnScanDone ??= new(1));
            var item = GetCustomScanStartItem(data, scan.WorldEventObjectFilter);

            uint count = 0;
            while (!eventWrapper.IsDone)
            {
                ++count;
                Zone.Data scanZone = data; // It may be worth searching for the scan, if I can find a good method
                string scanName = $"{scanZone.ZoneName} Custom Scan ({scan.WorldEventObjectFilter}) (Completion #{count})";
                RegionID scanRegion = data.LookupOrCreateRegion(scanName);

                data.AddPath(new Path()
                {
                    StartingRegion = data.LookupOrCreateRegion(scanZone.ZoneName),
                    EndingRegion = scanRegion,
                    ReqItem = item.PathReqs,
                    ReqCount = count,
                    AlternateItem = new(),
                });

                eventWrapper.Process(scanRegion, scanName);
            }
        }
    }

}
