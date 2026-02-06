
using System;
using System.Collections.Generic;
using System.Linq;
using GameData;
using ReTFO.Archipelago.ModdedInstanceData;

namespace ReTFO.Archipelago.ModdedInstanceData2.Callbacks;

public static class ObjectiveHandlers
{
    // Helper for getting descriptive, user-friendly names for each objective
    public static string FriendlyObjectiveName(ProcessObjective.Data data)
    {
        string name = data.Objective.Type switch
        {
            eWardenObjectiveType.HSU_FindTakeSample      => "Collect 1 HSU Sample",
            eWardenObjectiveType.Reactor_Startup         => "Startup 1 Reactor",
            eWardenObjectiveType.Reactor_Shutdown        => "Shutdown 1 Reactor",
            eWardenObjectiveType.GatherSmallItems        => $"Collect {data.Objective.Gather_RequiredCount}x\"{ItemDataBlock.GetBlock(data.Objective.Gather_ItemId).publicName}\"",
            eWardenObjectiveType.ClearAPath              => "Clear a Path",
            eWardenObjectiveType.SpecialTerminalCommand  => $"Run Command \"{data.Objective.SpecialTerminalCommand}\" 1 Time",
            eWardenObjectiveType.RetrieveBigItems        => $"Retrieve {data.Objective.Retrieve_Items.Count} Big Items",
            eWardenObjectiveType.PowerCellDistribution   => $"Distribute {data.Objective.PowerCellsToDistribute} Power Cells",
            eWardenObjectiveType.TerminalUplink          => $"Complete {data.Objective.Uplink_NumberOfTerminals} Normal Uplinks",
            eWardenObjectiveType.CentralGeneratorCluster => $"Power {data.Objective.CentralPowerGenClustser_NumberOfGenerators} Gens in Generator Cluster",
            eWardenObjectiveType.ActivateSmallHSU        => $"{(data.Objective.ActivateHSU_BringItemInElevator ? "" : "Find and ")}Process \"{ItemDataBlock.GetBlock(data.Objective.ActivateHSU_ItemFromStart).publicName}\" into \"{ItemDataBlock.GetBlock(data.Objective.ActivateHSU_ItemAfterActivation).publicName}\"",
            eWardenObjectiveType.Survival                => $"Survive {data.Objective.Survival_TimeToSurvive} Seconds and Reach Extract",
            eWardenObjectiveType.GatherTerminal          => $"Run Command \"{data.Objective.GatherTerminal_Command}\" {data.Objective.GatherTerminal_RequiredCount} Times",
            eWardenObjectiveType.CorruptedTerminalUplink => $"Complete {data.Objective.Uplink_NumberOfTerminals} Dual Uplinks",
            eWardenObjectiveType.Empty                   => "Empty - Activate Completedata.Objective Event",
            eWardenObjectiveType.TimedTerminalSequence   => $"Complete {data.Objective.TimedTerminalSequence_NumberOfRounds} Timed Sequences",
            _ => throw new NotImplementedException($"Objective name not recognized: {(int)data.Objective.Type} ({Enum.GetName(data.Objective.Type)})")
        };
        return $"{data.LayerName} Objective {data.ObjectiveIndex + 1} ({name}))";
    }


    // Objective requiring collection of one HSU sample
    [ProcessObjective.Callback(eWardenObjectiveType.HSU_FindTakeSample)]
    public static ProcessObjective.Result HandleCollectHSUSample(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        // Just need to reach the HSU to complete the objective
        string itemName = $"{objectiveName} HSU";

        Path path = manager.AddPath(result.FirstRegion, result.LastRegion);
        path.required_item = itemName;
        path.required_item_count = 1;

        // Add HSU as pickup
        manager.AddLocation(new()
        {
            name = $"{itemName} (Location)",
            item = itemName,
            regions = data.PlacementsToZoneRegions(manager, data.ObjectiveData.ZonePlacementDatas[0]),
        });

        // If we're triggering events, we only need the first chain - it's impossible to activate any beyond that
        if (data.Objective.OnActivateOnSolveItem)
        {
            manager.ProcessEvent.Invoke(manager, new(
                data, data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).First(), 
                result.LastRegion, $"{objectiveName} HSU Scan Completed"
            ));
        }

        return result;
    }

    // Objective requiring a single reactor be started up
    [ProcessObjective.Callback(eWardenObjectiveType.Reactor_Startup)]
    public static ProcessObjective.Result HandleReactorStartupObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            !data.Objective.DoNotSolveObjectiveOnReactorComplete
        );

        // We will add a pre start reactor region so we can check if the reactor is reachable
        string reactorName = $"{objectiveName} Reactor";
        int inReactorRegion = manager.GetOrCreateRegion($"{objectiveName} Reach Reactor");
        Path path = manager.AddPath(result.FirstRegion, inReactorRegion);
        path.required_item = reactorName;
        path.required_item_count = 1;

        // The startup can be initiated from any reachable reactor in the list (for some reason)
        int count = 0;
        foreach (var placement in data.ObjectiveData.ZonePlacementDatas.SelectMany(l => l.Iter()))
        {
            manager.AddLocation(new()
            {
                name = $"{reactorName} (Location #{count++})",
                item = reactorName,
                regions = new(1) { manager.GetOrCreateRegion(data.FindZoneByPlacement(placement)) },
            });
        }


        // For each wave, there will be a "survive wave" and an "input code" region
        int lastRegion = inReactorRegion;
        count = 0;
        foreach (var wave in data.Objective.ReactorWaves)
        {
            ++count;
            int surviveRegion = manager.GetOrCreateRegion($"{objectiveName} Surive Wave {count}");
            int inputCodeRegion = manager.GetOrCreateRegion($"{objectiveName} Input Code {count}");

            manager.ProcessEvent.Invoke(manager, new(data, wave.Events.Iter(), surviveRegion, $"{objectiveName} Surive Wave {count}"));
            manager.AddPath(lastRegion, surviveRegion); // This path is always allowed

            // Verification, which may require finding a code
            path = manager.AddPath(surviveRegion, inputCodeRegion);
            if (wave.VerifyInOtherZone)
            {
                string codeName = $"{objectiveName} Wave {count} Code";
                path.required_item = codeName;
                path.required_item_count = 1;

                manager.AddLocation(new()
                {
                    name = $"{codeName} (Location)",
                    item = codeName,
                    regions = new(1) { manager.GetOrCreateRegion(data.FindZoneByIndex(wave.ZoneForVerification)) }
                });
            }

            lastRegion = inputCodeRegion;
        }

        // Finally, we connect this to the "Completed" region
        manager.AddPath(lastRegion, result.LastRegion);
        if (data.Objective.OnActivateOnSolveItem)
        {
            manager.ProcessEvent.Invoke(manager, new(
                data, data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).First(),
                result.LastRegion, $"{objectiveName} Startup Complete"
            ));
        }
        return result;
    }

    // Objective requiring a single reactor be shut down
    [ProcessObjective.Callback(eWardenObjectiveType.Reactor_Shutdown)]
    public static ProcessObjective.Result HandleReactorShutdownObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            !data.Objective.DoNotSolveObjectiveOnReactorComplete
        );

        // If we can reach the reactor we can shut it down
        string reactorName = $"{objectiveName} Reactor";
        Path path = manager.AddPath(result.FirstRegion, result.LastRegion);
        path.required_item = reactorName;
        path.required_item_count = 1;

        // The shutdown can be initiated from any reachable reactor in the list (for some reason)
        int count = 0;
        foreach (var placement in data.ObjectiveData.ZonePlacementDatas.SelectMany(l => l.Iter()))
        {
            manager.AddLocation(new()
            {
                name = $"{reactorName} (Location #{count++})",
                item = reactorName,
                regions = new(1) { manager.GetOrCreateRegion(data.FindZoneByPlacement(placement)) },
            });
        }

        // When the shutdown is complete, trigger events
        if (data.Objective.OnActivateOnSolveItem)
        {
            manager.ProcessEvent.Invoke(manager, new(
                data, data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).First(),
                result.LastRegion, $"{objectiveName} Shutdown Complete"
            ));
        }
        return result;
    }

    // Objective requiring picking up a certain number of small items
    [ProcessObjective.Callback(eWardenObjectiveType.GatherSmallItems)]
    public static ProcessObjective.Result HandleGatherSmallItemsObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        string itemName = ItemDataBlock.GetBlock(data.Objective.Gather_ItemId).publicName;
        string spawnSpotName = $"{objectiveName} Spawn Spot";

        // First thing we need to know is how many items can be "missing"
        // For example, in R1B1, there are 18 total IDs, 7 spawn zones, and up to 3 per zone
        // Therefore, there are (7*3)-18 = 3 "missing" IDs (7*3=21 spawn spots, 3 of which will be empty)
        int numSpawnZones = data.ObjectiveData.ZonePlacementDatas[0].Count;
        int numSpawnSpots = numSpawnZones * data.Objective.Gather_MaxPerZone;
        int numMissing = numSpawnSpots - data.Objective.Gather_SpawnCount;

        // We track progression not by how many pickups can be found, but instead by how many spawn spots can be found
        // The first numMissing spawn spots are assumed empty (because that is worst case), and therefore trigger no events
        var eventLists = data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).ToList();
        int lastRegion = result.FirstRegion;
        Path path;
        for (int i = 1; i <= numSpawnSpots; i++)
        {
            int newRegion = manager.GetOrCreateRegion($"{objectiveName} Checked {i} Spawn Spots");
            path = manager.AddPath(lastRegion, newRegion);
            path.required_item = spawnSpotName;
            path.required_item_count = (uint)i;

            int itemNum = i - numMissing;
            if (data.Objective.OnActivateOnSolveItem && (itemNum > 0) && (itemNum <= eventLists.Count))
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, eventLists[i - 1].AsEnumerable(),
                    newRegion, $"{objectiveName} Collect \"{itemName}\" #{itemNum}"
                ));
            }
            lastRegion = newRegion;
        }

        // We connect the objective complete region to the region representing the min spots we must check to reach the target
        manager.AddPath($"{objectiveName} Checked {numMissing + data.Objective.Gather_RequiredCount} Spawn Spots", result.LastRegion);

        // Finally, we place the spawn spots into the world as pickups
        int count = 0;
        foreach (var placement in data.ObjectiveData.ZonePlacementDatas[0])
        {
            List<int> regions = new(1) { manager.GetOrCreateRegion(data.FindZoneByPlacement(placement).ZoneName) };
            for (int i = 0; i < data.Objective.Gather_MaxPerZone; i++)
            {
                ++count;
                manager.AddLocation(new()
                {
                    name = $"{spawnSpotName} #{count}",
                    item = spawnSpotName,
                    regions = regions,
                });
            }
        }

        return result;
    }

    // Objective requiring a player to enter the extraction zone. Assumes (requires?) forward extraction
    [ProcessObjective.Callback(eWardenObjectiveType.ClearAPath)]
    public static ProcessObjective.Result HandleClearAPathObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        // This objective is immediately completed upon reaching extraction
        // Logically, we could omit the check, but for consistency's sake we will include it
        Path path = manager.AddPath(result.FirstRegion, result.LastRegion);
        path.required_item = data.ExtractionReachableName;
        path.required_item_count = 1;

        // No OnActivate events for this objective

        return result;
    }

    // Objective requiring a single command be entered into a specific terminal
    [ProcessObjective.Callback(eWardenObjectiveType.SpecialTerminalCommand)]
    public static ProcessObjective.Result HandleSpecialTerminalCommandObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        // We simply need to reach the terminal
        string itemName = $"{objectiveName} Special Terminal";
        Path path = manager.AddPath(result.FirstRegion, result.LastRegion);
        path.required_item = itemName;
        path.required_item_count = 1;

        manager.AddLocation(new()
        {
            name = $"{itemName} (Location)",
            item = itemName,
            regions = data.PlacementsToZoneRegions(manager, data.ObjectiveData.ZonePlacementDatas[0]),
        });

        // Events triggered upon running the command
        manager.ProcessEvent.Invoke(manager, new(
            data, data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).First(),
            result.LastRegion, $"{objectiveName} Command Inputted"
        ));

        return result;
    }

    // Objective requiring the retrieval of one or more big pickups, which may be of multiple (varying) item types
    [ProcessObjective.Callback(eWardenObjectiveType.RetrieveBigItems)]
    public static ProcessObjective.Result HandleRetrieveBigItemsObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        /* Similar to small items, we create one region per item we need to pickup
         * Each region will contain the events relevant to picking up that number of pickups
         * Placements are looped, so if we only have one list of zones it's reused; if two, it alternates; etc
         */
        string itemName = $"{objectiveName} Big Pickup";
        List<List<int>> regionSets = data.ObjectiveData.ZonePlacementDatas.Select(ps => data.PlacementsToZoneRegions(manager, ps)).ToList();
        List<IEnumerable<WardenObjectiveEventData>> eventSets = data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).ToList();

        int lastRegion = result.FirstRegion;
        int count = 0;
        foreach (var id in data.Objective.Retrieve_Items)
        {
            ++count;
            string idName = ItemDataBlock.GetBlock(id).publicName;
            int newRegion = manager.GetOrCreateRegion($"{objectiveName} Pickup Big Item #{count}");

            if (data.Objective.OnActivateOnSolveItem && (count <= eventSets.Count))
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, eventSets[count - 1],
                    newRegion, $"{objectiveName} Pickup Big Item #{count}"
                ));
            }

            Path path = manager.AddPath(lastRegion, newRegion);
            path.required_item = itemName;
            path.required_item_count = (uint)count;

            // Since this pickup is going to extraction, we don't allow its use in other logic and instead name it uniquely per this objective
            // IE if the pickup is a cell, this prevents the logic from using that cell to power a gen
            manager.AddLocation(new()
            {
                name = $"{objectiveName} Big Pickup #{count} ({idName})",
                item = itemName,
                regions = regionSets[(count - 1) % regionSets.Count],
            });

            lastRegion = newRegion;
        }

        // Connect final region to all pickups being grabbed
        manager.AddPath(lastRegion, result.LastRegion);
        return result;
    }

    // Objective requiring power cells be taken from the elevator zone and to various generators throughout the layer
    [ProcessObjective.Callback(eWardenObjectiveType.PowerCellDistribution)]
    public static ProcessObjective.Result HandlePowerCellDistributionObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        // TODO: This objective has somewhat complicated cell implications
        // Foreach gen needed, create two regions: One checks for access to cells, the other to gens
        string itemName = $"{objectiveName} Gen Location";
        List<List<int>> regionSets = data.ObjectiveData.ZonePlacementDatas.Select(ps => data.PlacementsToZoneRegions(manager, ps)).ToList();
        List<IEnumerable<WardenObjectiveEventData>> eventSets = data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).ToList();
        int last = result.FirstRegion;
        Path path;
        for (int i = 1; i <= data.Objective.PowerCellsToDistribute; i++)
        {
            int cellRegion = manager.GetOrCreateRegion($"{objectiveName} Collected {i} PowerCells");
            path = manager.AddPath(last, cellRegion);
            path.required_item = data.CellName;
            path.required_item_count = (uint)i;

            int genRegion = manager.GetOrCreateRegion($"{objectiveName} Powered Generator #{i}");
            path = manager.AddPath(cellRegion, genRegion);
            path.required_item = itemName;
            path.required_item_count = (uint)i;

            manager.AddLocation(new()
            {
                name = $"{objectiveName} Gen #{i} (Location)",
                item = itemName,
                regions = regionSets[(i- 1) % regionSets.Count]
            });

            if (data.Objective.OnActivateOnSolveItem && (i<= eventSets.Count))
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, eventSets[i- 1],
                    genRegion, $"{objectiveName} Powered Generator #{i}"
                ));
            }

            last = genRegion;
        }

        // Place starting cells in elevator zone - Only for main layer (and possible only for first objective?)
        if (data.LayerType.IsMainLayer) // && data.ObjectiveIndex == 0)
        {
            List<int> regions = new(1) { manager.GetOrCreateRegion(data.GetFirstZone().ZoneName) };
            for (int i = 1; i <= data.Objective.PowerCellsToDistribute; i++)
            {
                manager.AddLocation(new()
                {
                    name = $"{objectiveName} Starting Cell #{i} (Location)",
                    item = data.CellName,
                    regions = regions
                });
            }
        }

        // Connect final region to all pickups being grabbed
        manager.AddPath(last, result.LastRegion);
        return result;
    }

    // Objective requiring one or more standard uplinks to be completed
    [ProcessObjective.Callback(eWardenObjectiveType.TerminalUplink)]
    public static ProcessObjective.Result HandleTerminalUplinkObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        // Very similar to big pickups. However, terminal pickups will be inside terminal regions. We will require all terminals in all possible zones
        List<List<int>> regionSets = data.ObjectiveData.ZonePlacementDatas.Select(ps => data.PlacementsToTerminalRegions(manager, ps)).ToList();
        string itemName = $"{objectiveName} Terminal";
        List<IEnumerable<WardenObjectiveEventData>> eventSets = data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).ToList();
        int last = result.FirstRegion;
        for (int i = 1; i <= data.Objective.Uplink_NumberOfTerminals; i++)
        {
            int newRegion = manager.GetOrCreateRegion($"{objectiveName} Uplink #{i} Completed");
            Path path = manager.AddPath(last, newRegion);
            path.required_item = itemName;
            path.required_item_count = (uint)i;

            if (data.Objective.OnActivateOnSolveItem && (i <= eventSets.Count))
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, eventSets[i - 1],
                    newRegion, $"{objectiveName} Uplink #{i} Completed"
                ));
            }

            manager.AddLocation(new()
            {
                name = $"{objectiveName} Terminal #{i}",
                item = itemName,
                regions = regionSets[(i - 1) % regionSets.Count],
            });

            last = newRegion;
        }

        manager.AddPath(last, result.LastRegion);
        return result;
    }

    // Objective requiring one or more cells be found in the map and used to power a central generator cluster
    [ProcessObjective.Callback(eWardenObjectiveType.CentralGeneratorCluster)]
    public static ProcessObjective.Result HandleCentralGenGlusterObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        // Central gen requires a) us to place cells in the map, b) us to find the central gen, and c) events when each cell is inserted
        // a) Placing cells in the map
        List<List<int>> regionSets = data.ObjectiveData.ZonePlacementDatas.Select(ps => data.PlacementsToZoneRegions(manager, ps)).ToList();
        for (int i = 1; i < data.Objective.CentralPowerGenClustser_NumberOfPowerCells; i++)
        {
            manager.AddLocation(new()
            {
                name = $"{objectiveName} Power Cell #{1} (Location)",
                item = data.CellName,
                regions = regionSets[(i - 1) % regionSets.Count],
            });
        }

        // b) Finding the central gen cluster
        string itemName = $"{objectiveName} Gen Cluster";
        eLocalZoneIndex? genIndex = null;
        LevelLayoutDataBlock layout = data.Layout ?? throw new NotImplementedException("Empty layout on layer!");
        foreach (var zone in layout.Zones)
        {
            if (zone.GeneratorClustersInZone > 0)
            {
                genIndex = zone.LocalIndex;
                break;
            }
        }
        if (genIndex == null)
        {
            Plugin.Get().Log.LogWarning($"Failed to find gen cluster for objective {objectiveName} - Falling back to default of Zone_0");
            genIndex = eLocalZoneIndex.Zone_0;
        }
        ProcessZone.Data genZone = data.FindZoneByIndex(genIndex.Value);
        manager.AddLocation(new()
        {
            name = $"{itemName} (Location)",
            item = itemName,
            regions = new(1) { manager.GetOrCreateRegion(genZone.ZoneName) },
        });

        // c) Regions and events based on available cell counts
        List<IEnumerable<WardenObjectiveEventData>> eventSets = data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).ToList();
        int last = result.FirstRegion;
        for (int i = 1; i < data.Objective.CentralPowerGenClustser_NumberOfGenerators; i++)
        {
            int newRegion = manager.GetOrCreateRegion($"{objectiveName} Powered Gen #{i}");
            Path path = manager.AddPath(last, newRegion);
            path.required_item = data.CellName;
            path.required_item_count = (uint)i;

            if (data.Objective.OnActivateOnSolveItem && (i <= eventSets.Count))
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, eventSets[i - 1],
                    newRegion, $"{objectiveName} Powered Gen #{i}"
                ));
            }

            last = newRegion;
        }

        manager.AddPath(last, result.LastRegion);
        return result;
    }

    // Objective requiring an item be brought to be "processed" and then brought to extraction
    [ProcessObjective.Callback(eWardenObjectiveType.ActivateSmallHSU)]
    public static ProcessObjective.Result HandleActivateSmallHSUObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            data.Objective.ActivateHSU_ObjectiveCompleteAfterInsertion
        );

        // Two-step objective: Find the item, then get to the processor
        string itemName = data.BigPickupName(ItemDataBlock.GetBlock(data.Objective.ActivateHSU_ItemFromStart));
        string processorName = $"{objectiveName} Processor";
        int collectItemRegion = manager.GetOrCreateRegion($"{objectiveName} Collect Item");

        // Fun fact: You can actually process any item as long as the item type is correct, and it will count
        Path path = manager.AddPath(result.FirstRegion, collectItemRegion);
        path.required_item = itemName;
        path.required_item_count = 1;

        path = manager.AddPath(collectItemRegion, result.LastRegion);
        path.required_item = processorName;
        path.required_item_count = 1;

        // Add the processor to the world
        manager.AddLocation(new()
        {
            name = $"{processorName} (Location)",
            item = processorName,
            regions = data.ObjectiveData.ZonePlacementDatas.SelectMany(ps => data.PlacementsToZoneRegions(manager, ps)).ToList(),
        });

        // Add the item to the elevator zone, if necessary
        if (data.Objective.ActivateHSU_BringItemInElevator)
        {
            manager.AddLocation(new()
            {
                name = $"{objectiveName} - Item in Elevator",
                item = itemName,
                regions = new(1) { manager.GetOrCreateRegion(new ProcessLayer.Data(data, LayerType.Main).GetFirstZone().ZoneName) },
            });
        }

        // Events triggered by initiating processing on the small HSU - always triggered
        manager.ProcessEvent.Invoke(manager, new(data, data.Objective.EventsOnActivate.Iter(), result.LastRegion, $"{objectiveName} HSU Processed"));
        return result;
    }

    // Objective requiring prisoners survive a certain amount of time and reach extract
    [ProcessObjective.Callback(eWardenObjectiveType.Survival)]
    public static ProcessObjective.Result HandleSurvivalObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        // So, for simplicity, we're just going to make the objective immediately solved and all events immediately trigger
        manager.AddPath(result.FirstRegion, result.LastRegion);
        manager.ProcessEvent.Invoke(manager, new(
            data, data.Objective.EventsOnActivate.Iter(),
            result.FirstRegion, $"{objectiveName} Events"
        ));
        return result;
    }

    // Objective requiring prisoners enter commands on a variety of terminals throughought the complex
    // Like a blend of GatherSmallItems and SpecialTerminalCommand
    [ProcessObjective.Callback(eWardenObjectiveType.GatherTerminal)]
    public static ProcessObjective.Result HandleGatherTerminalObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            data.Objective.GatherTerminal_RequiredCount <= data.Objective.GatherTerminal_SpawnCount
        );

        string itemName = $"{objectiveName} Terminal";
        List<List<int>> regionSets = data.ObjectiveData.ZonePlacementDatas.Select(ps => data.PlacementsToTerminalRegions(manager, ps)).ToList();
        List<IEnumerable<WardenObjectiveEventData>> eventSets = data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).ToList();
        int last = result.FirstRegion;
        for (int i = 1; i <= data.Objective.GatherTerminal_SpawnCount; i++)
        {
            int newRegion = manager.GetOrCreateRegion($"{objectiveName} Terminal Command #{i} Executed");
            Path path = manager.AddPath(last, newRegion);
            path.required_item = itemName;
            path.required_item_count = (uint)i;

            manager.AddLocation(new()
            {
                name = $"{objectiveName} Terminal #{i} Spawn Location",
                item = itemName,
                regions = regionSets[(i - 1) % regionSets.Count],
            });

            if (data.Objective.OnActivateOnSolveItem && (i <= eventSets.Count))
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, eventSets[i - 1],
                    newRegion, $"{objectiveName} Terminal Command #{i} Executed"
                ));
            }

            last = newRegion;
        }

        // The required number of terminals differs from the spawn count. We can hook it here
        manager.AddPath(
            $"{objectiveName} Terminal Command #{data.Objective.GatherTerminal_RequiredCount} Executed",
            result.LastRegion
        );
        return result;
    }

    // Objective similar to a standard uplink, but requiring codes for the uplink be relayed from a second terminal
    [ProcessObjective.Callback(eWardenObjectiveType.CorruptedTerminalUplink)]
    public static ProcessObjective.Result HandleCorruptedUplinkObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        // Both terminals in a pair are always in the same zone (unless spawning hijinks ensue, but we're ignoring those)
        string itemName = $"{objectiveName} Terminal Pair";
        List<List<int>> regionSets = data.ObjectiveData.ZonePlacementDatas.Select(ps => data.PlacementsToTerminalRegions(manager, ps).Distinct().ToList()).ToList();
        List<IEnumerable<WardenObjectiveEventData>> eventSets = data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).ToList();
        int last = result.FirstRegion;
        for (int i = 1; i <= data.Objective.GatherTerminal_SpawnCount; i++)
        {
            int newRegion = manager.GetOrCreateRegion($"{objectiveName} Dual Uplink #{i} Completed");
            Path path = manager.AddPath(last, newRegion);
            path.required_item = itemName;
            path.required_item_count = (uint)i;

            manager.AddLocation(new()
            {
                name = $"{objectiveName} Terminal Pair #{i} Spawn Location",
                item = itemName,
                regions = regionSets[(i - 1) % regionSets.Count],
            });

            if (data.Objective.OnActivateOnSolveItem && (i <= eventSets.Count))
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, eventSets[i - 1],
                    newRegion, $"{objectiveName} Dual Uplink #{i} Completed"
                ));
            }

            last = newRegion;
        }

        manager.AddPath(last, result.LastRegion);
        return result;
    }

    // Objective that cannot be completed; instead, a ForceCompleteObjective event (or win event) must be triggerred
    [ProcessObjective.Callback(eWardenObjectiveType.Empty)]
    public static ProcessObjective.Result HandleEmptyObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            false
        );

        Path path = manager.AddPath(result.FirstRegion, result.LastRegion);
        path.required_item = data.NotAnItem;
        path.required_item_count = 1;

        return result;
    }

    // Objective requiring the completion of one or more timed terminal sequences
    [ProcessObjective.Callback(eWardenObjectiveType.TimedTerminalSequence)]
    public static ProcessObjective.Result HandleTimedSequenceObjective(Manager manager, ProcessObjective.Data data)
    {
        string objectiveName = FriendlyObjectiveName(data);
        ProcessObjective.Result result = new(
            manager.GetOrCreateRegion($"{objectiveName} Start"),
            manager.GetOrCreateRegion($"{objectiveName} Completed"),
            true
        );

        // Technically, you could get lucky. However, I'm going to state you need access to all terminals to do more than just start the sequence
        List<List<int>> regionSets = data.ObjectiveData.ZonePlacementDatas.Select(ps => data.PlacementsToTerminalRegions(manager, ps)).ToList();
        int mainTermRegion = manager.GetOrCreateRegion($"{objectiveName} Reached Main Terminal");
        Path path = manager.AddPath(result.FirstRegion, mainTermRegion);
        path.required_item = $"{objectiveName} Main Terminal";
        path.required_item_count = 1;

        manager.AddLocation(new()
        {
            name = $"{objectiveName} Main Terminal (Location)",
            item = $"{objectiveName} Main Terminal",
            regions = regionSets[0]
        });

        if (data.Objective.TimedTerminalSequence_EventsOnSequenceStart.Count > 0)
        {
            manager.ProcessEvent.Invoke(manager, new(
                data, data.Objective.TimedTerminalSequence_EventsOnSequenceStart[0].Iter(),
                mainTermRegion, $"{objectiveName} Start Round #1"
            ));
        }

        if (data.Objective.TimedTerminalSequence_EventsOnSequenceFail.Count > 0)
        {
            manager.ProcessEvent.Invoke(manager, new(
                data, data.Objective.TimedTerminalSequence_EventsOnSequenceFail[0].Iter(),
                mainTermRegion, $"{objectiveName} Fail Round #1"
            ));
        }

        // Adding in the secondary terminals as pickups for the next step
        for (int i = 1; i < regionSets.Count; i++)
        {
            manager.AddLocation(new()
            {
                name = $"{objectiveName} Verication Terminal #{i}",
                item = $"{objectiveName} Verification Terminal",
                regions = regionSets[i]
            });
        }

        // For each round of verification, we'll add success for the previous round and start/fail for the next
        int last = mainTermRegion;
        for (int i = 1; i <= data.Objective.TimedTerminalSequence_NumberOfRounds; i++)
        {
            int newRegion = manager.GetOrCreateRegion($"{objectiveName} Complete Round {i}");
            path = manager.AddPath(last, newRegion);
            path.required_item = $"{objectiveName} Verification Terminal";

            // NOTE: This may be wrong. I could not verify if the rounds are guaranteed to use specific terminal placements
            // The alternative is to require the full amount each time
            path.required_item_count = (uint)(i > (regionSets.Count - 1) ? (regionSets.Count - 1) : i);

            if (data.Objective.TimedTerminalSequence_EventsOnSequenceDone.Count >= i)
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, data.Objective.TimedTerminalSequence_EventsOnSequenceStart[i - 1].Iter(),
                    newRegion, $"{objectiveName} Complete Round #{i}"
                ));
            }

            if (data.Objective.TimedTerminalSequence_EventsOnSequenceStart.Count > i)
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, data.Objective.TimedTerminalSequence_EventsOnSequenceStart[i].Iter(),
                    newRegion, $"{objectiveName} Start Round #{i + 1}"
                ));
            }

            if (data.Objective.TimedTerminalSequence_EventsOnSequenceFail.Count > i)
            {
                manager.ProcessEvent.Invoke(manager, new(
                    data, data.Objective.TimedTerminalSequence_EventsOnSequenceFail[i].Iter(),
                    newRegion, $"{objectiveName} Fail Round #{i + 1}"
                ));
            }

            last = newRegion;
        }

        // The above loop overshoots the required number of steps, so we can guarntee all Done events are handled
        // We just need to connect to the final region and place OnActivate events in it
        path = manager.AddPath(last, result.LastRegion);
        if (data.Objective.OnActivateOnSolveItem)
        {
            List<IEnumerable<WardenObjectiveEventData>> eventSets = data.Objective.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak).ToList();
            if (eventSets.Count == 0) return result;
            manager.ProcessEvent.Invoke(manager, new(
                data, eventSets[0],
                result.FirstRegion, $"{objectiveName} Sequence Completed"
            ));
        }
        return result;

    }

}
