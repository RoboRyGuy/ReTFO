using System.Linq;

using GameData;

namespace ReTFO.Archipelago.ModdedInstanceData2.Callbacks;

// Adds paths between zones
public static class AddEntrances
{
    // Add entrances between zones on the same layer - ie, the main entrance
    [ProcessZone.Callback]
    public static void AddZoneEntrances(Manager manager, ProcessZone.Data data)
    {
        // Check if this zone generates a normal doorway
        var layout = data.GetLayout();
        if (layout == null || data.Zone == null) return; // Dimension with only one zone
        if (data.Zone.Pointer == layout.Zones[0].Pointer) return; // First zone in layer - handled by AddLayerEntrances

        // Create path
        Path path = manager.AddPath(
            data.FindZoneByIndex(data.Zone.BuildFromLocalIndex).ZoneName,
            data.ZoneName
        );

        // Handle locked doors
        LayerData? layerData = data.GetLayerData();
        if (layerData?.ZonesWithBulkheadEntrance.Contains(data.Zone.LocalIndex) ?? false)
        {   // This zone is locked by a bulkhead door
            path.required_item = data.BulkheadKeyName;
            path.required_item_count = 1;
        }
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Keycard_SecurityBox)
        {   // Typical colored key
            path.required_item = data.ColoredKeyName;
            path.required_item_count = 1;
            manager.AddLocation(new Location()
            {
                name = $"{data.ColoredKeyName} (Spawn Location)",
                item = data.ColoredKeyName,
                regions = data.Zone.ProgressionPuzzleToEnter.ZonePlacementData
                    .Select(p => manager.GetOrCreateRegion(data.FindZoneByPlacement(p).ZoneName)).ToList()
            });
        }
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.PowerGenerator_And_PowerCell)
        {   // Must power a specific generator with a cell
            path.required_item = data.CellName;
            path.required_item_count = 1;
            manager.AddLocation(new Location()
            {
                name = $"{data.CellName} (Spawn Location)",
                item = data.CellName,
                regions = data.Zone.ProgressionPuzzleToEnter.ZonePlacementData
                    .Select(p => manager.GetOrCreateRegion(data.FindZoneByPlacement(p).ZoneName)).ToList()
            });
        }
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Locked_No_Key)
        {   // Can only be unlocked by an event force unlocking it
            path.required_item = data.NotAnItem;
            path.required_item_count = 0xFF;
        }
        path.alternate_item = data.UnlockZoneName;
    }

    // Add entrances to the first zones in secondary and overload
    [ProcessLayer.Callback]
    public static void AddLayerEntrances(Manager manager, ProcessLayer.Data data)
    {
        BuildLayerFromData? buildFromData = data.GetBuildFromData();
        if (buildFromData == null) return;

        ProcessLayer.Data sourceLayer = new(data, buildFromData.LayerType);
        ProcessZone.Data targetZone = data.GetFirstZone();
        Path path = manager.AddPath(
            sourceLayer.FindZoneByIndex(buildFromData.Zone).ZoneName,
            targetZone.ZoneName
        );

        LayerData sourceData = sourceLayer.GetLayerData()!;
        if (sourceData.BulkheadDoorControllerPlacements.FirstOrDefault(p => p.ZoneIndex == buildFromData.Zone) != null)
        {   // If there is a bulkhead DC in the zone this layer connects to, we can unlock this zone with a key
            path.required_item = data.BulkheadKeyName;
            path.required_item_count = 1;
        }
        else
        {   // Can only unlock via an event
            path.required_item = data.NotAnItem;
            path.required_item_count = 0xFF;
        }
        path.alternate_item = targetZone.UnlockZoneName;
    }

    // Warps between dimensions when triggered by an event
    [ProcessEvent.Callback]
    public static void AddEventWarps(Manager manager, ProcessEvent.Data data)
    {
        foreach (var e in data.Events)
        {
            // Filter out unwanted events - We don't care about flashes since they're transient
            if (e.Type != eWardenObjectiveEventType.DimensionWarpTeam)
                continue;

            ProcessZone.Data targetZone = data.FindZoneByEvent(e);

            // Warps are simply paths, accessible as long as the event which triggers them is also accessible
            manager.AddPath(
                data.SourceRegion,
                targetZone.ZoneName
            );
        }
    }

}
