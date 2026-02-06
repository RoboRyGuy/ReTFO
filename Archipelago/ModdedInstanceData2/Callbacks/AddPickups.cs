using GameData;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData2.Callbacks;

public static class AddPickups
{
    // Add big pickups in zones which spawn them via big pickup distributions
    [ProcessZone.Callback]
    public static void AddBigPickups(Manager manager, ProcessZone.Data data)
    {   
        uint id = data.Zone?.BigPickupDistributionInZone ?? data.DimensionData?.StaticBigPickupDistributionInZone ?? 0u;
        if (id == 0) return;
        BigPickupDistributionDataBlock pickups = BigPickupDistributionDataBlock.GetBlock(id);
        List<int> regions = new(1) { manager.GetOrCreateRegion(data.ZoneName) };

        // Big pickups are handled weirdly. Below is a guess as to how it's handled (including for non-1 weights)
        float usedWeight = 0f;
        int index = 0;
        int count = 0;
        while ((usedWeight + pickups.SpawnData[index].Weight) <= pickups.SpawnsPerZone)
        {
            ItemDataBlock item = ItemDataBlock.GetBlock(pickups.SpawnData[index].ItemID);
            manager.AddLocation(new()
            {
                name = $"{data.ZoneName} Big Pickup {++count} ({item.terminalItemShortName})",
                item = data.BigPickupName(item),
                regions = regions
            });
            usedWeight += pickups.SpawnData[index].Weight;
            index = (index + 1) % pickups.SpawnData.Count;
        }
    }

    // Add bulkhead keys from layer data
    [ProcessLayer.Callback]
    public static void AddBulkheadKeys(Manager manager, ProcessLayer.Data data)
    {
        LayerData? layerData = data.LayerData;
        if (layerData == null) return;

        for (int i = 0; i < layerData.BulkheadKeyPlacements.Count; i++)
        {
            manager.AddLocation(new Location()
            {
                name = $"{data.BulkheadKeyName} (Spawn Location {i + 1})",
                item = data.BulkheadKeyName,
                regions = layerData.BulkheadKeyPlacements[i].Select(p => manager.GetOrCreateRegion(data.FindZoneByPlacement(p).ZoneName)).ToList()
            });
        }
    }

    // Unlock events are like keys, right? You can totally pick them up. Hence, pickups
    [ProcessEvent.Callback]
    public static void ProcessUnlockEvents(Manager manager, ProcessEvent.Data data)
    {
        int count = 0;
        foreach (var e in data.Events)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.UnlockSecurityDoor && e.Type != eWardenObjectiveEventType.OpenSecurityDoor)
                continue;
            count += 1;

            ProcessZone.Data targetZone = data.FindZoneByEvent(e);
            manager.AddLocation(new Location()
            {
                name = $"{data.SourceName} - Unlock Event {count} (for {targetZone.ZoneName})",
                item = targetZone.UnlockZoneName,
                regions = new(1) { eventData.SourceRegion }
            });
        }
    }

    // Custom scans are started via an event item, so they're technically pickups
    [ProcessEvent.Callback]
    public static void ProcessCustomScanEvents(Manager manager, ProcessEvent.Data data)
    {
        int count = 0;
        foreach (var e in data.Events)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ActivateChainedPuzzle)
                continue;
            count += 1;

            manager.AddLocation(new Location()
            {
                name = $"{data.SourceName} - Start Scan {count} (for {e.WorldEventObjectFilter})",
                item = data.CustomScanName(e.WorldEventObjectFilter),
                regions = new(1) { eventData.SourceRegion }
            });
        }
    }

    // Objective completions (via ForceCompleteObjective) are treated as an item, both here and in objective processing
    [ProcessEvent.Callback]
    public static void ProcessForceCompleteEvents(Manager manager, ProcessEvent.Data data)
    {
        int count = 0;
        foreach (var e in data.Events)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ForceCompleteObjective)
                continue;
            count += 1;

            ProcessLayer.Data layer = new(data, e.Layer);
            manager.AddLocation(new Location()
            {
                name = $"{data.SourceName} - Force Complete Objective Event {count}",
                item = data.CompleteObjectiveName,
                regions = new(1) { eventData.SourceRegion }
            });
        }
    }

    // Win events (instant win, win on death) are also treated as a form of pickup
    [ProcessEvent.Callback]
    public static void ProcessWinEvents(Manager manager, ProcessEvent.Data data)
    {
        int count = 0;
        foreach (var e in data.Events)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ForceInstantWin && e.Type != eWardenObjectiveEventType.WinOnDeath)
                continue;
            count += 1;

            ProcessLayer.Data layer = new(data, e.Layer);
            manager.AddLocation(new Location()
            {
                name = $"{data.SourceName} - Instant Win Event {count}",
                item = data.InstantWinEventName,
                regions = new(1) { eventData.SourceRegion }
            });
        }
    }

    // Add extraction, which contains a single pickup indicating extraction is reachable
    [ProcessExpedition.Callback]
    public static void AddExtraction(Manager manager, ProcessExpedition.Data data)
    {
        ProcessLayer.Data layerData = new(data, LayerType.Main);
        int extractionRegion = manager.GetOrCreateRegion(data.ExtractionRegionName);
        manager.AddLocation(new Location()
        {
            name = $"{data.ExtractionReachableName} (Location)",
            item = data.ExtractionReachableName,
            regions = new(1) { extractionRegion },
        });

        // Now, we calc where extraction is and connect it to the map
        if (data.Expedition.MainLayerData.ObjectiveData.WinCondition == eWardenObjectiveWinCondition.GoToExitGeo)
        {   // We'll have to find the extraction zone via complex data
            // Basically, if it's listed in the custom exits section of the complex data, it's probably the exit
            ComplexResourceSetDataBlock complex = ComplexResourceSetDataBlock.GetBlock(data.Expedition.Expedition.ComplexResourceData);
            ExpeditionZoneData? zone = null;
            foreach (var z in layerData.Layout!.Zones)
            {
                if (z.CustomGeomorph.Length == 0 || z.CustomGeomorph == "")
                    continue;

                if (complex.CustomGeomorphs_Exit_1x1.Any(c => c.Prefab == z.CustomGeomorph))
                {
                    zone = z;
                    break;
                }
            }

            if (zone == null) 
                Plugin.Get().Log.LogError($"Could not find forward exit for expedition: {data.ExpeditionName}");
            else
            {
                var zoneData = new ProcessZone.Data(layerData, zone);
                manager.AddPath(zoneData.ZoneName, extractionRegion);
                return;
            }
        }

        // Extraction in first zone - also fallback in case we couldn't find forward extract
        manager.AddPath(layerData.GetFirstZone().ZoneName, extractionRegion);
    }

}
