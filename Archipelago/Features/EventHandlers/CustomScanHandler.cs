using GameData;
using LevelGeneration;
using ReTFO.Archipelago.Features.Terminals;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
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
        /// Events triggering custom scans based on world event objects
        /// </summary>
        public ItemID Item_EventScans
            => ItemID.From(gameData, "Event Scan Items", data => new("Scans triggered by starting a custom event", data.Item_Scans));
    }

    extension (Expedition.Data data)
    {
        /// <summary>
        /// A particular event-triggered scan
        /// </summary>
        public ItemID Item_EventScan_Instance(string worldEventObjectFilter)
            => ItemID.From(data,
                $"{data.ExpeditionName} Event Scan \"{worldEventObjectFilter}\"",
                data => new("A particular event-triggered scan", data.Item_Scans),
                new CustomScanHandler.StartCustomScanItem(data.Region_Expedition, worldEventObjectFilter)
            );
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

    /// <summary>
    /// Class representing an item used to start custom event scans
    /// </summary>
    public class StartCustomScanItem : TerminalItem
    {
        public StartCustomScanItem(RegionID region, string worldEventObjectFilter)
            : base(new ItemData() { IsProgression = true })
        {
            Expedition = region;
            WorldEventObjectFilter = worldEventObjectFilter;
        }

        /// <summary>
        /// The expedition this scan appears in
        /// </summary>
        public RegionID Expedition { get; private init; }

        /// <summary>
        /// The world event object filter used to initiate this scan
        /// </summary>
        public string WorldEventObjectFilter { get; private init; }

        public override RegionID TargetRegion => Expedition;

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
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

            ItemID item = data.Item_EventScan_Instance(e.WorldEventObjectFilter!);
            EventHelper.CreateEventLocation(data, e, count, item);
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
            ItemID item = data.Item_EventScan_Instance(scan.WorldEventObjectFilter!);

            uint count = 0;
            while (!eventWrapper.IsDone)
            {
                ++count;
                Zone.Data scanZone = data; // It may be worth searching for the scan, if I can find a good method
                string scanName = $"{scanZone.ZoneName} Custom Scan ({scan.WorldEventObjectFilter}) (Completion #{count})";
                RegionID scanRegion = data.Regions.Create(scanName, new("A custom scan's region", data.Region_Expedition));

                data.AddPath(new Path()
                {
                    StartingRegion = data.Region_Zone,
                    EndingRegion = scanRegion,
                    Reqs = new(Path.eType.Item, item, count),
                });

                eventWrapper.Process(scanRegion);
            }
        }
    }

    [ArchivePatch(typeof(WorldEventManager), nameof(WorldEventManager.OnLevelGenDone))]
    public static class WorldEventManager__OnLevelGenDone__Patch
    {
        public static void Postfix(WorldEventManager __instance)
        {
            Expedition.Data data = Expedition.Data.GetFromCurrentExpedition();
    
            var layouts = Enumerable.Empty<uint>().Append(data.Expedition.LevelLayoutData);
            if (data.HasSecondary)
                layouts = layouts.Append(data.Expedition.SecondaryLayout);
            if (data.HasOverload)
                layouts = layouts.Append(data.Expedition.ThirdLayout);
            layouts = layouts.Concat(
                data.Expedition.DimensionDatas
                  .Select(d => DimensionDataBlock.GetBlock(d.DimensionData).DimensionData.LevelLayoutData)
                  .Where(id => id != 0)
            );
    
            var scanDatas = layouts
                .Select(LevelLayoutDataBlock.GetBlock)
                .SelectMany(l => l.Zones.Iter())
                .SelectMany(z => z.WorldEventChainedPuzzleDatas.Iter());
    
            foreach (var scan in scanDatas)
            {
                int entry = __instance.m_uniqueWorldEventObjectMap.FindEntry(scan.WorldEventObjectFilter);
                if (entry == -1)
                {
                    FeatureLogger.Warning($"Failed to register scouting data for scan: {scan.WorldEventObjectFilter}");
                    continue;
                }
                LG_WorldEventObject obj = __instance.m_uniqueWorldEventObjectMap.entries[entry].value;
                EventHelper.GetWorldEventObjectDetailsProcessor(obj).DetailedInfoProcessors.Add((data) =>
                {
                    APCommandHandler.InsertLocationDataToDetailedInfo(
                        StateTracker.Get(),
                        data,
                        "SCAN ITEMS",
                        EventHelper.ExtractLocations(scan.EventsOnScanDone.Iter())
                    );
                    return data;
                });
            }
        }
    }
}
