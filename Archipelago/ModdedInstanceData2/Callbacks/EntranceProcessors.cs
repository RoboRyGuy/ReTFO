using System.Linq;

using GameData;
using LevelGeneration;

namespace ReTFO.Archipelago.ModdedInstanceData2.Callbacks;

// Adds paths between zones
public static class EntranceProcessors
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
        ProcessZone.Data entryZone;
        if (data.Zone.BuildFromLocalIndex == data.Zone.LocalIndex)
            entryZone = data.GetFirstZone(); // Yes, this happens. Presumably an oversight in R8C1's secondary layout data
        else
            entryZone = data.FindZoneByIndex(data.Zone.BuildFromLocalIndex)!;
        int entryRegion = manager.GetOrCreateRegion(entryZone.ZoneName);
        Path path = manager.AddPath(
            entryRegion,
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
            manager.AddLocation(new Location(
                $"{data.ColoredKeyName} (Spawn Location)",
                data.ColoredKeyName,
                data.Zone.ProgressionPuzzleToEnter.ZonePlacementData
                    .Select(p => manager.GetOrCreateRegion(data.FindZoneByPlacement(p).ZoneName)).ToList(),
                true
            ));
        }
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.PowerGenerator_And_PowerCell)
        {   // Must power a specific generator with a cell
            path.required_item = data.CellName;
            path.required_item_count = 1;
            manager.AddLocation(new Location(
                $"{data.CellName} (Spawn Location)",
                data.CellName,
                data.Zone.ProgressionPuzzleToEnter.ZonePlacementData
                    .Select(p => manager.GetOrCreateRegion(data.FindZoneByPlacement(p).ZoneName)).ToList(),
                true
            ));
        }
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Locked_No_Key)
        {   // Can only be unlocked by an event force unlocking it
            path.required_item = data.NotAnItem;
            path.required_item_count = 0xFF;
        }
        path.alternate_item = data.UnlockZoneName;

        // Finally, handle on approach events, since they actually live in the entry zone
        int count = 0;
        foreach (var eventChain in data.Zone.EventsOnApproachDoor.EventSplit())
            manager.ProcessEvent.Invoke(manager, new ProcessEvent.Data(data, eventChain, entryRegion, $"{data.ZoneName} OnApproachZone ({++count})"));
    }

    // Add entrances to the first zones in secondary and overload
    [ProcessLayer.Callback]
    public static void AddLayerEntrances(Manager manager, ProcessLayer.Data data)
    {
        BuildLayerFromData? buildFromData = data.GetBuildFromData();
        if (buildFromData == null) return; // As a side effect, this limits processing to secondary and overload layers

        ProcessLayer.Data sourceLayer = new(data, buildFromData.LayerType);
        ProcessZone.Data targetZone = data.GetFirstZone();
        ProcessZone.Data entryZone = sourceLayer.FindZoneByIndex(buildFromData.Zone);
        int entryRegion = manager.GetOrCreateRegion(entryZone.ZoneName);
        Path path = manager.AddPath(
            entryRegion,
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

        // Finally, handle on approach events, since they actually live in the entry zone
        int count = 0;
        foreach (var eventChain in targetZone.Zone!.EventsOnApproachDoor.EventSplit())
            manager.ProcessEvent.Invoke(manager, new ProcessEvent.Data(data, eventChain, entryRegion, $"{targetZone.ZoneName} OnApproachLayer ({++count})"));
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

    // Warps between dimensions triggered by the portal room
    [ProcessZone.Callback]
    public static void AddZoneWarps(Manager manager, ProcessZone.Data data)
    {
        if (!(data.Zone?.CustomGeomorph?.Contains("_portal_", System.StringComparison.OrdinalIgnoreCase) ?? false)) return;

        ProcessZone.Data targetZone = data.FindZoneExact(LayerType.Dimension_1, eLocalZoneIndex.Zone_0); // Target of all vanilla portals
        ComplexResourceSetDataBlock? complex = ComplexResourceSetDataBlock.GetBlock(data.Expedition.Expedition.ComplexResourceData);
        UnityEngine.GameObject? go = complex?.GetCustomGeomorph(data.Zone.CustomGeomorph);
        LG_DimensionPortal? portal = go?.GetComponentInChildren<LG_DimensionPortal>();
        if (portal != null) targetZone = data.FindZoneExact(portal.m_targetDimension, portal.m_targetZone);

        Path path = manager.AddPath(
            manager.GetOrCreateRegion(data.ZoneName),
            targetZone.ZoneName
        );
        path.required_item = data.BigPickupName(ItemDataBlock.GetAllBlocks().First(i => i.terminalItemShortName == "MATTER_WAVE_PROJECTOR"));
        path.required_item_count = 1; // Of note, this does consume the MWP, so we may need multiple in some modded levels
    }

}
