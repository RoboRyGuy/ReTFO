
using Clonesoft.Json;
using GameData;
using LevelGeneration;
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

[EnableFeatureByDefault]
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
            : base($"{data.ExpeditionName} Start Custom Scan ({worldEventObjectFilter})", eRandomizationType.Progression, new List<string> { "All", "Events", "Scans", "Custom Scans" })
        {
            Data = data;
            WorldEventObjectFilter = worldEventObjectFilter;
        }

        // The expedition this item was created for
        [JsonIgnore]
        public Expedition.Data Data { get; set; }

        // The item datablock this big pickup represents
        [JsonIgnore]
        public string WorldEventObjectFilter { get; set; }

        public override void OnItemObtained(StateTracker stateTracker)
        {
            if (Expedition.Data.FromCurrentExpedition() == Data)
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (data == Data)
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

    public static Item GetCustomScanStartItem(Expedition.Data data, string worldEventObjectFilter)
        => data.GetItem(new StartCustomScanItem(data, worldEventObjectFilter));

    public static string GetCustomScanEventName(Event.Data data, string worldEventObjectFilter, int count)
        => $"{data.EventName} - Start Custom Scan {count} (for {worldEventObjectFilter})";

    // Replace custom scan events with check location events for that scan location
    [Event.Callback]
    public static void ProcessCustomScanStartEvents(Event.Data data)
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

            string locationName = GetCustomScanEventName(data, e.WorldEventObjectFilter!, count);
            data.AddLocation(
                locationName,
                data.EventRegion,
                eRandomizationType.Progression,
                false,
                GetCustomScanStartItem(data, e.WorldEventObjectFilter!)
            );

            EventHelper.ConvertToCheckLocationEvent(e, data, locationName);
        }
    }

    // Add regions and process events for custom scans (by zone)
    [Zone.Callback]
    public static void AddCustomScans(Zone.Data data)
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
                int scanRegion = data.GetOrCreateRegion(scanName);

                Path path = data.AddPath(data.GetOrCreateRegion(scanZone.ZoneName), scanRegion);
                path.RequiredItem = item.Name;
                path.RequiredItemCount = count;
                path.AlternateItem = null;
                eventWrapper.Process(scanRegion, scanName);
            }
        }
    }

}
