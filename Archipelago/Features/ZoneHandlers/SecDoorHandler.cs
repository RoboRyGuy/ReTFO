
using GameData;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.Features.EventHandlers;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.Features.Terminals;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ZoneHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Linq;
using EventList = Il2CppSystem.Collections.Generic.List<GameData.WardenObjectiveEventData>;

public static class SecDoorHandler_Tags
{
    extension (Zone.Data data)
    {
        public RegionID Region_OnApproachEvents
            => RegionID.From(data, $"{data.ZoneName} Door Approached", data => new("Region entered by looking at a sec door's interact", data.Region_Zone));

        public RegionID Region_OnUnlockDoorEvents
            => RegionID.From(data, $"{data.ZoneName} Door Unlocked", data => new("Region entered by unlocking a particular zone door", data.Region_Zone));

        public RegionID Region_OnDoorScanStartEvents
            => RegionID.From(data, $"{data.ZoneName} Door Scan Started", data => new("Region entered by starting a scan to unlock a particular zone door", data.Region_Zone));

        public RegionID Region_OnDoorScanDoneEvents
            => RegionID.From(data, $"{data.ZoneName} Door Scan Completed", data => new("Region entered by completing a scan to unlock a particular zone door", data.Region_Zone));

        public RegionID Region_OnDoorOpenedEvents
            => RegionID.From(data, $"{data.ZoneName} Door Opened", data => new("Region entered by opening a particular zone door", data.Region_Zone));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class SecDoorHandler : ArchipelagoFeature
{
    public override string Name => "Sec Door Handler";
    public override string Description
        => "Adds sec doors as parths and applies locks to them";
    public override FeatureGroup Group => FeatureGroups.ZoneHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// Connect the first zone of an expedition to its virtual expedition region
    /// </summary>
    [Expedition.Callback]
    public void AddExpeditionEntrace(Expedition.Data data)
    {
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Expedition,
            EndingRegion = data.MainLayer.FirstZone.Region_Zone,
        });
    }

    /// <summary>
    /// Helper struct for below, since delegate* cannot be used in generics (ie in tuple types)
    /// </summary>
    private unsafe struct RegionEventPair
    {
        public RegionEventPair(delegate*<Zone.Data, RegionID> regionFactory, EventList? eventList)
        {
            RegionFactory = regionFactory;
            EventList = eventList;
        }

        public readonly delegate*<Zone.Data, RegionID> RegionFactory;
        public readonly EventList? EventList;
    }

    // Add entrances between zones on the same layer
    [Zone.Callback]
    public unsafe void AddZoneEntrances(Zone.Data data)
    {
        // Check if this zone generates a normal doorway
        if (data.Layout == null || data.Zone == null) return; // Dimension with only one zone
        bool isFirstZone = data.Zone.Pointer == data.Layout.Zones[0].Pointer;
        if ((data.LayerType.IsMainLayer || data.LayerType.IsDimension) && isFirstZone) return; // First zone in each dimension has no door :(

        // Starting region
        Layer.Data sourceLayer;
        Zone.Data entryZone;
        if (isFirstZone)
        {
            sourceLayer = data.GetLayer(data.BuildFromData!.LayerType);
            entryZone = sourceLayer.FindZoneByIndex(data.BuildFromData.Zone);
        }
        else
        {
            sourceLayer = data;
            if (data.Zone.BuildFromLocalIndex == data.Zone.LocalIndex)
                entryZone = sourceLayer.FirstZone; // Rare edge case. Presumably an oversight in R8C1's secondary layout data
            else
                entryZone = sourceLayer.FindZoneByIndex(data.Zone.BuildFromLocalIndex)!;
        }

        RegionID lastRegion = entryZone.Region_Zone;

        // Event region for when the door is approached
        if (data.Zone.EventsOnApproachDoor?.Any() ?? false)
        {
            RegionID eventRegion = data.Region_OnApproachEvents;
            data.ProcessEvents(eventRegion, data.Zone.EventsOnApproachDoor);
            data.AddPath(new Path()
            {
                StartingRegion = lastRegion,
                EndingRegion = eventRegion,
            });
            lastRegion = eventRegion;
        }

        // Calculate door unlock requirements
        Path.PathReq req = new();
        LayerData? layerData = data.LayerDatas;
        if (isFirstZone)
        {   // This is the first zone of a secondary or overload layer - ie, a forced bulkhead door
            
            // If there is a bulkhead DC in the zone this layer connects to, we can unlock this zone with a key
            if (sourceLayer.LayerDatas!.BulkheadDoorControllerPlacements.FirstOrDefault(p => p.ZoneIndex == data.BuildFromData!.Zone) != null)
                req = new(Path.eType.ItemConsumed, data.Item_BulkheadKey_Instance, 1u);
            
            // Can only unlock via an event
            else
                req = new(Path.eType.Category, data.Item_DoorUnlockEvent_ByZone, 1u);
        }
        else
        {   // This is a typical sec door
            
            // This zone is locked by a bulkhead door
            if (layerData?.ZonesWithBulkheadEntrance.Contains(data.Zone.LocalIndex) ?? false)
                req = new(Path.eType.ItemConsumed, data.Item_BulkheadKey_Instance, 1u);

            // Typical colored key
            else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Keycard_SecurityBox)
                req = new(Path.eType.Item, data.Item_ColoredKey_Instance, 1u);

            // Must power a specific generator with a cell
            else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.PowerGenerator_And_PowerCell)
                req = new(Path.eType.ItemConsumed, data.Item_BigPickup_Cell, 1u);
            
            // Can only be unlocked by an event force unlocking it
            else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Locked_No_Key)
                req = new(Path.eType.Category, data.Item_DoorUnlockEvent_ByZone, 1u);
            
            // Unknown; error!
            else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType != eProgressionPuzzleType.None)
                FeatureLogger.Error($"Unknown progression puzzle type {data.Zone.ProgressionPuzzleToEnter.PuzzleType}! Zone: {data.ZoneName}");
        }

        // For each event region, try and create it
        RegionEventPair[] pairs = [
            new(&SecDoorHandler_Tags.get_Region_OnUnlockDoorEvents,    data.Zone.EventsOnUnlockDoor),
            new(&SecDoorHandler_Tags.get_Region_OnDoorScanStartEvents, data.Zone.EventsOnDoorScanStart),
            new(&SecDoorHandler_Tags.get_Region_OnDoorScanDoneEvents,  data.Zone.EventsOnDoorScanDone),
            new(&SecDoorHandler_Tags.get_Region_OnDoorOpenedEvents,    data.Zone.EventsOnOpenDoor),
        ];

        foreach (var pair in pairs)
        {
            // Only create the region if we have to
            if (!(pair.EventList?.Any() ?? false)) continue;

            // Process the events
            RegionID eventRegion = pair.RegionFactory(data);
            data.ProcessEvents(eventRegion, pair.EventList);

            // Chain it to the last region
            data.AddPath(new Path()
            {
                Name = req.Type != Path.eType.None ? $"Unlocked {data.ZoneName}" : null,
                StartingRegion = lastRegion,
                EndingRegion = eventRegion,
                Reqs = new(req),
            });

            // Check the reqs before cycling them out. Conveniently, only the unlock event is a category, so we can avoid duplicating it
            if (req.Type != Path.eType.None && req.Type != Path.eType.Category)
            {
                data.AddPath(new Path()
                {
                    StartingRegion = lastRegion,
                    EndingRegion = eventRegion,
                    Reqs = new(Path.eType.Category, data.Item_DoorUnlockEvent_ByZone, 1u),
                });
            }

            req = new();
            lastRegion = eventRegion;
        }

        // We can now chain it to the next zone. Still need to check for the extra path, though..
        data.AddPath(new()
        {
            StartingRegion = lastRegion,
            EndingRegion = data.Region_Zone,
            Reqs = new(req),
        });

        if (req.Type != Path.eType.None && req.Type != Path.eType.Category)
        {
            data.AddPath(new Path()
            {
                StartingRegion = lastRegion,
                EndingRegion = data.Region_Zone,
                Reqs = new(Path.eType.Category, data.Item_DoorUnlockEvent_ByZone, 1u),
            });
        }
    }

    // Identifies and reports when players enter a new zone
    [ArchivePatch(typeof(PlayerAgent), nameof(PlayerAgent.SetCourseNode))]
    public static class PlayerAgent__SetCourseNode__Patch
    {
        public static void Postfix(PlayerAgent __instance)
        {
            Zone.Data zoneData = Zone.Data.GetFromZone(__instance.CourseNode.m_zone);
            StateTracker.Get().NotifyFoundRegion(zoneData.Region_Zone, __instance);
        }
    }

    [ArchivePatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor._Setup_b__60_0))]
    public static class LG_SecurityDoor___Setup_b__60_0__Patch
    {
        public static void Postfix(LG_SecurityDoor __instance, Il2CppSystem.Collections.Generic.List<string> __result)
        {
            (string, EventList)[] events = [
                ("ON APPROACH", __instance.LinkedToZoneData.EventsOnApproachDoor),
                ("ON UNLOCK", __instance.LinkedToZoneData.EventsOnUnlockDoor),
                ("ON SCAN STARTED", __instance.LinkedToZoneData.EventsOnDoorScanStart),
                ("ON SCAN COMPLETED", __instance.LinkedToZoneData.EventsOnDoorScanDone),
                ("ON DOOR OPENED", __instance.LinkedToZoneData.EventsOnOpenDoor),
            ];

            StateTracker st = StateTracker.Get();
            foreach (var pair in events)
            {
                var locs = EventHelper.ExtractLocations(pair.Item2.Iter());
                if (locs.Any())
                    APCommandHandler.InsertLocationDataInDetailedInfo(st, __result, pair.Item1, locs);
            }
        }
    }
}
