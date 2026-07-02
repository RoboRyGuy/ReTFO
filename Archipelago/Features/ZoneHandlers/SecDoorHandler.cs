
using GameData;
using Player;
using ReTFO.Archipelago.Features.EventHandlers;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ZoneHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class SecDoorHandler_Tags
{
    extension (Zone.Data data)
    {
        public RegionID Region_OnApproachEvents
            => RegionID.From(data, $"{data.ZoneName} OnApproach", data => new("Region entered by looking at a sec door's interact", data.Region_Zone));
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

    // Add entrances between zones on the same layer
    [Zone.Callback]
    public void AddZoneEntrances(Zone.Data data)
    {
        // Check if this zone generates a normal doorway
        if (data.Layout == null || data.Zone == null) return; // Dimension with only one zone
        if (data.Zone.Pointer == data.Layout.Zones[0].Pointer) return; // First zone in layer - handled by AddLayerEntrances

        // Create path
        Zone.Data entryZone;
        if (data.Zone.BuildFromLocalIndex == data.Zone.LocalIndex)
            entryZone = data.FirstZone; // Yes, this happens. Presumably an oversight in R8C1's secondary layout data
        else
            entryZone = data.FindZoneByIndex(data.Zone.BuildFromLocalIndex)!;
        RegionID entryRegion = entryZone.Region_Zone;

        // Handle locked doors
        Path.RequiredItem pathReq = new(Path.RequiredItem.eType.None, new());
        LayerData? layerData = data.LayerDatas;
        if (layerData?.ZonesWithBulkheadEntrance.Contains(data.Zone.LocalIndex) ?? false)
            // This zone is locked by a bulkhead door
            pathReq = new(Path.RequiredItem.eType.ItemConsumed, data.Item_BulkheadKey_Instance);
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Keycard_SecurityBox)
            // Typical colored key
            pathReq = new(Path.RequiredItem.eType.Item, data.Item_ColoredKey_Instance);
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.PowerGenerator_And_PowerCell)
            // Must power a specific generator with a cell
            pathReq = new(Path.RequiredItem.eType.ItemConsumed, data.Item_BigPickup_Cell);
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Locked_No_Key)
            // Can only be unlocked by an event force unlocking it
            pathReq = new(Path.RequiredItem.eType.Blocked, new());
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType != eProgressionPuzzleType.None)
            FeatureLogger.Error($"Unknown progression puzzle type {data.Zone.ProgressionPuzzleToEnter.PuzzleType}! Zone: {data.ZoneName}");

        data.AddPath(new Path()
        {
            Name = $"{data.ZoneName} Main Entry",
            StartingRegion = entryRegion,
            EndingRegion = data.Region_Zone,
            ReqItem = pathReq,
            ReqCount = 1u,
            AlternateItem = new(Path.RequiredItem.eType.Category, data.Item_DoorUnlockEvent_ByZone),
        });

        // Finally, handle OnApproach events, which will live in the entry zone
        if (data.Zone.EventsOnApproachDoor.Any())
        {
            RegionID eventRegion = data.Region_OnApproachEvents;
            
            data.AddPath(new Path() {
                StartingRegion = entryRegion, 
                EndingRegion = eventRegion
            });
            data.ProcessEvents(eventRegion, data.Zone.EventsOnApproachDoor);
        }
    }

    // Add entrances to the first zones in secondary and overload
    [Layer.Callback]
    public void AddLayerEntrances(Layer.Data data)
    {
        if (data.BuildFromData == null) return; // This limits processing to secondary and overload layers

        Zone.Data targetZone = data.FirstZone;
        Layer.Data sourceLayer = data.GetLayer(data.BuildFromData.LayerType);
        Zone.Data? entryZone = sourceLayer.FindZoneByIndex(data.BuildFromData.Zone);

        RegionID entryRegion = entryZone.Region_Zone;

        Path.RequiredItem pathReqs;
        if (sourceLayer.LayerDatas!.BulkheadDoorControllerPlacements.FirstOrDefault(p => p.ZoneIndex == data.BuildFromData.Zone) != null)
            // If there is a bulkhead DC in the zone this layer connects to, we can unlock this zone with a key
            pathReqs = new(Path.RequiredItem.eType.ItemConsumed, data.Item_BulkheadKey_Instance);
        else
            // Can only unlock via an event
            pathReqs = new(Path.RequiredItem.eType.Blocked, new());

        data.AddPath(new Path()
        {
            Name = $"{data.LayerName} Layer Entry",
            StartingRegion = entryRegion,
            EndingRegion = targetZone.Region_Zone,
            ReqItem = pathReqs,
            ReqCount = 1u,
            AlternateItem = new(Path.RequiredItem.eType.Category, targetZone.Item_DoorUnlockEvent_ByZone),
        });

        // Finally, handle OnApproach events, which will live in the entry zone
        if (targetZone.Zone!.EventsOnApproachDoor.Any())
        {
            RegionID eventRegion = targetZone.Region_OnApproachEvents;
            data.AddPath(new Path()
            {
                StartingRegion = entryRegion,
                EndingRegion = eventRegion
            });
            data.ProcessEvents(eventRegion, targetZone.Zone.EventsOnApproachDoor);
        }
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
}
