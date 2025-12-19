
using GameData;
using ReTFO.Archipelago;
using System.Diagnostics.CodeAnalysis;
using static ReTFO.Archipelago.ModdedInstanceData.WardenEvent;

namespace ReTFO.Archipelago.ModdedInstanceData;

// Class used to create / manage instance data
// TODO: Add instanced hooks so mods can add customized data for AP
public class Manager
{
    // Helper which enumerates through all expeditions in a rundown datablock
    public static IEnumerable<Tuple<ExpeditionInTierData, int>> UnpackRundown(RundownDataBlock rundown)
    {
        int i = 0;
        for (i = 0; i < rundown.TierA.Count; i++) yield return Tuple.Create(rundown.TierA[i], i);
        for (i = 0; i < rundown.TierB.Count; i++) yield return Tuple.Create(rundown.TierB[i], i);
        for (i = 0; i < rundown.TierC.Count; i++) yield return Tuple.Create(rundown.TierC[i], i);
        for (i = 0; i < rundown.TierD.Count; i++) yield return Tuple.Create(rundown.TierD[i], i);
        for (i = 0; i < rundown.TierE.Count; i++) yield return Tuple.Create(rundown.TierE[i], i);
    }

    // Generate modded instance data from this copy of the game
    public static ModdedInstanceData GenerateModdedInstanceData()
    {
        ModdedInstanceData result = new();

        var expeditions = RundownDataBlock.GetAllBlocks()
            .Where(r => r.internalEnabled)
            .SelectMany(UnpackRundown)
            .Where(pair => pair.Item1.Enabled);

        foreach (var pair in expeditions)
        {
            var exp = GenerateExpeditionData(pair.Item1, pair.Item2);
            if (exp != null) result.expeditions.Add(exp);
        }

        // TODO: Add gear names
        // TODO: Add filler data
        // TODO: Add trap data

        return result;
    }

    // Generate expedition data from the provided expedition
    public static ExpeditionData GenerateExpeditionData(ExpeditionInTierData expedition, int indexInTier)
    {
        ExpeditionData result = new();
        result.name = expedition.GetShortName(indexInTier);

        // Generate all level data
        result.main_level = InitLevelData(expedition.LevelLayoutData);
        if (result.main_level != null)
            ProcessLayerData(result.main_level, expedition.MainLayerData);

        if (expedition.SecondaryLayerEnabled)
        {
            result.secondary_build_from = new()
            {
                layer_index = (int)expedition.BuildSecondaryFrom.LayerType,
                zone_index  = (int)expedition.BuildSecondaryFrom.Zone,
            };
            result.secondary_level = InitLevelData(expedition.SecondaryLayout)!;
            ProcessLayerData(result.secondary_level, expedition.SecondaryLayerData);
        }

        if (expedition.ThirdLayerEnabled)
        {
            result.overload_build_from = new()
            {
                layer_index = (int)expedition.BuildThirdFrom.LayerType,
                zone_index  = (int)expedition.BuildThirdFrom.Zone,
            };
            result.overload_level = InitLevelData(expedition.ThirdLayout)!;
            ProcessLayerData(result.overload_level, expedition.ThirdLayerData);
        }

        // Generate dimension data as level data
        Dictionary<int, LevelData> dimension_data = new();
        foreach (var dim in expedition.DimensionDatas.Iter())
        {
            DimensionDataBlock db = DimensionDataBlock.GetBlock(dim.DimensionData);
            dimension_data[(int)dim.DimensionIndex] = GenerateDimensionData(db, (int)dim.DimensionIndex);
        }

        // Apply locks to sector entrances
        if (expedition.SecondaryLayerEnabled)
        {
            if (result.secondary_level == null) throw new NullReferenceException();
            IEnumerable<BulkheadDoorPlacementData> dcLocs = result.secondary_build_from.layer_index switch
            {
                0 => expedition.MainLayerData.BulkheadDoorControllerPlacements.Iter(),
                2 => expedition.ThirdLayerData.BulkheadDoorControllerPlacements.Iter(),
                _ => throw new InvalidDataException()
            };

            if (dcLocs.Any(p => (int)p.ZoneIndex == result.secondary_build_from.zone_index))
                result.secondary_level.zones[result.secondary_level.start_zone].lock_type = ZoneData.eLockType.BulkheadKey;
            else
                result.secondary_level.zones[result.secondary_level.start_zone].lock_type = ZoneData.eLockType.Locked;
        }

        if (expedition.ThirdLayerEnabled)
        {
            if (result.overload_level == null) throw new NullReferenceException();
            IEnumerable<BulkheadDoorPlacementData> dcLocs = result.overload_build_from.layer_index switch
            {
                0 => expedition.MainLayerData.BulkheadDoorControllerPlacements.Iter(),
                1 => expedition.SecondaryLayerData.BulkheadDoorControllerPlacements.Iter(),
                _ => throw new InvalidDataException()
            };

            if (dcLocs.Any(p => (int)p.ZoneIndex == result.secondary_build_from.zone_index))
                result.overload_level.zones[result.overload_level.start_zone].lock_type = ZoneData.eLockType.BulkheadKey;
            else
                result.overload_level.zones[result.overload_level.start_zone].lock_type = ZoneData.eLockType.Locked;
        }

        return result;
    }

    // Generate level data for the given level layout
    public static LevelData? InitLevelData(uint layoutId)
    {
        LevelLayoutDataBlock level = LevelLayoutDataBlock.GetBlock(layoutId);
        if (level == null) return null;
        LevelData result = new();

        // Set up basics for all zones. Sort by local index, and mark entrance
        int zoneCount = level.Zones.Select(z => (int)z.LocalIndex).Max() + 1;
        result.zones.EnsureCapacity(zoneCount);
        result.zones.AddRange(Enumerable.Repeat<ZoneData>(null!, zoneCount));
        for (int i = 0; i < level.Zones.Count; i++)
        {
            ExpeditionZoneData zone = level.Zones[i];
            if ((int)zone.LocalIndex < 0 || (int)zone.LocalIndex >= result.zones.Count)
                throw new ArgumentOutOfRangeException($"Local index {(int)zone.LocalIndex} is outside of expected range!");
            if (result.zones[(int)zone.LocalIndex] != null) 
                throw new NullReferenceException("Duplicate local index in zone list!");
            result.zones[(int)zone.LocalIndex] = new()
            {
                originalZone = zone,
                alias = zone.AliasOverride != -1 ? zone.AliasOverride : level.ZoneAliasStart + (int)zone.LocalIndex,
                terminal_count = zone.TerminalPlacements?.Count ?? 0,
                lock_type = (ZoneData.eLockType)zone.ProgressionPuzzleToEnter.PuzzleType,
            };
        }
        result.start_zone = (int)level.Zones[0].LocalIndex;

        // Ensure there are no null zones
        for (int i = 0; i < result.zones.Count; i++)
        {
            if (result.zones[i] == null) result.zones[i] = new()
            {
                originalZone = null,
                alias = -1,
                terminal_count = 0,
                lock_type = ZoneData.eLockType.Locked,
                entrance_index = 0,
            };
        }

        // With our zones sorted and defined, now add keys, events, big pickups, and cross-zone data
        foreach (var zone in result.zones)
        {
            // Skip non-zones (which will have no info to process)
            if (zone.originalZone == null) continue;

            // Entrance
            ZoneData? entryZone;
            if (result.start_zone != (int)zone.originalZone.LocalIndex)
            {
                entryZone = result.zones[(int)zone.originalZone.BuildFromLocalIndex];
                zone.entrance_index = (int)entryZone.originalZone!.LocalIndex;
            }
            else
            {
                entryZone = null;
                zone.entrance_index = 0;
            }

            // Lock handling
            if (zone.lock_type == ZoneData.eLockType.SimpleKey)
            {
                if (zone.originalZone.ProgressionPuzzleToEnter.ZonePlacementData.Count <= 0)
                    throw new InvalidOperationException("Simple key zone has no positions for its key to spawn in!");
                result.keys.Add(new()
                {
                    zone_alias = zone.alias,
                    positions = zone.originalZone.ProgressionPuzzleToEnter.ZonePlacementData.Select(ZonePosition.Make).ToList()
                });
            }
            else if (zone.lock_type == ZoneData.eLockType.GenAndCell && zone.originalZone.ProgressionPuzzleToEnter.ZonePlacementData.Count > 0)
            {
                result.pickups.Add(new()
                {
                    item_type = -1,
                    positions = zone.originalZone.ProgressionPuzzleToEnter.ZonePlacementData.Select(ZonePosition.Make).ToList()
                });
            }

            // Big pickup distributions in the zone. We only care about cells during this pass -> we'll figure out objective pickups during the objective pickups section
            BigPickupDistributionDataBlock? dist = BigPickupDistributionDataBlock.GetBlock(zone.originalZone.BigPickupDistributionInZone);
            foreach (var item in dist?.SpawnData.Iter() ?? Enumerable.Empty<BigPickupSpawnData>())
            {
                ItemDataBlock itemDB = ItemDataBlock.GetBlock(item.ItemID);
                if (itemDB.terminalItemShortName.Contains("CELL", StringComparison.OrdinalIgnoreCase))
                {
                    result.pickups.Add(new()
                    {
                        item_type = -1,
                        positions = new() { new ZonePosition(zone.originalZone.LocalIndex, 0) }
                    });
                }
            }

            // Events that simply occur "in the zone", without any more specific source
            ZonalEventSource zoneSource = new() { zone_local_index = (int)zone.originalZone.LocalIndex };
            ProcessEvents(result, zoneSource, WardenEvent.Source.eType.DoorUnlock,    zone.originalZone.EventsOnUnlockDoor.Iter());
            ProcessEvents(result, zoneSource, WardenEvent.Source.eType.DoorScanStart, zone.originalZone.EventsOnDoorScanStart.Iter());
            ProcessEvents(result, zoneSource, WardenEvent.Source.eType.DoorScanEnd,   zone.originalZone.EventsOnDoorScanDone.Iter());
            ProcessEvents(result, zoneSource, WardenEvent.Source.eType.DoorOpen,      zone.originalZone.EventsOnOpenDoor.Iter());
            ProcessEvents(result, zoneSource, WardenEvent.Source.eType.BossDeath,     zone.originalZone.EventsOnBossDeath.Iter());
            ProcessEvents(result, zoneSource, WardenEvent.Source.eType.PortalWarp,    zone.originalZone.EventsOnPortalWarp.Iter());

            // Terminal command events. LINQ refused to let me zip this prettily, so here is the simple version
            for (int i = 0; i < (zone.originalZone.TerminalPlacements?.Count ?? 0); i++)
            {
                var terminal = zone.originalZone.TerminalPlacements![i];
                foreach (var command in terminal.UniqueCommands.Iter())
                {
                    var commandSource = new TerminalCommandEventSource()
                    {
                        zone_local_index = (int)zone.originalZone.LocalIndex,
                        terminal_local_index = i,
                        command_name = command.Command,
                    };
                    ProcessEvents(result, commandSource, WardenEvent.Source.eType.TerminalCommand, command.CommandEvents.Iter());
                }
            }

            // Trigger events - Since each trigger must be named, these much be processed individually
            foreach (var ev in zone.originalZone.EventsOnTrigger.Iter())
            {
                if (TryMakeEventAction(ev, out var type, out var action))
                {
                    var triggerSource = new ZoneTriggerEventSource()
                    {
                        zone_local_index = (int)zone.originalZone.LocalIndex,
                        trigger_name = ev.WorldEventTriggerObjectFilter,
                    };
                    result.events.Add(new()
                    {
                        source_type = WardenEvent.Source.eType.ZoneTrigger,
                        source = triggerSource,
                        action_type = type,
                        action = action,
                    });
                }
            }

            // Scan events
            foreach (var scan in zone.originalZone.WorldEventChainedPuzzleDatas.Iter())
            {
                InZoneScanEventSource source = new()
                {
                    zone_local_index = (int)zone.originalZone.LocalIndex,
                    scan_name = scan.WorldEventObjectFilter,
                };
                ProcessEvents(result, source, WardenEvent.Source.eType.InZoneScan, scan.EventsOnScanDone.Iter());
            }

            // Event(s) occuring in the entry zone
            if (entryZone != null)
            {
                var approachSource = new ApproachDoorEventSource()
                {
                    zone_local_index = (int)entryZone.originalZone!.LocalIndex,
                    target_local_index = (int)zone.originalZone.LocalIndex,
                };
                ProcessEvents(result, approachSource, WardenEvent.Source.eType.DoorApproach, zone.originalZone.EventsOnApproachDoor.Iter());
            }
            else
            {
                var approachSource = new WardenEvent.Source();
                ProcessEvents(result, approachSource, WardenEvent.Source.eType.LevelApproach, zone.originalZone.EventsOnApproachDoor.Iter());
            }

            // Event(s) occuring in the terminal deactivation zone
            if (zone.originalZone.TurnOffAlarmOnTerminal)
            {
                var deactivateSource = new DeactivateAlarmEventSource()
                {
                    zone_local_index = (int)zone.originalZone.TerminalPuzzleZone.LocalIndex,
                    terminal_local_index = zone.originalZone.TerminalPuzzleZone.TerminalIndex,
                    command_name = "DEACTIVATE_ALARMS",
                    alarm_zone_index = (int)zone.originalZone.LocalIndex,
                };
                ProcessEvents(result, deactivateSource, WardenEvent.Source.eType.TerminalDeactivateAlarm, zone.originalZone.EventsOnTerminalDeactivateAlarm.Iter());
            }
        }

        return result;
    }

    // Similar to GenerateLevelData, but takes into account the special cases of dimension building
    public static LevelData GenerateDimensionData(DimensionDataBlock db, int dimension = 0)
    {
        var result = InitLevelData(db.DimensionData.LevelLayoutData);
        if (result != null) return result;

        // No level? Then it's just a single custom geomorph. We'll treat it like a single zone
        LevelData level = new() { zones = new(1) };
        level.zones.Add(new()
        {
            alias = 0,
            entrance_index = 0,
            terminal_count = db.DimensionData.StaticTerminalPlacements?.Count ?? 0,
            lock_type = 0,
        });
        level.start_zone = 0;

        for (int i = 0; i < (db.DimensionData.StaticTerminalPlacements?.Count ?? 0); i++)
        {
            var terminal = db.DimensionData.StaticTerminalPlacements![i];
            foreach (var command in terminal.UniqueCommands)
            {
                TerminalCommandEventSource source = new()
                {
                    zone_local_index = 0,
                    terminal_local_index = i,
                    command_name = command.Command,
                };
                foreach (var ev in command.CommandEvents)
                {
                    if (TryMakeEventAction(ev, out var type, out var action))
                    {
                        level.events.Add(new()
                        {
                            source_type = WardenEvent.Source.eType.TerminalCommand,
                            source = source,
                            action_type = type,
                            action = action,
                        });
                    }
                }
            }
        }

        return level;
    }

    // Adds bulkheads, objectives, and other misc things to intialized level data
    public static void ProcessLayerData(LevelData level, LayerData layerData) {

        // Any bulkhead door with a bulkhead DC to access it is implicitly locked via bulkhead key
        foreach (var placement in layerData.BulkheadDoorControllerPlacements.Iter())
        {
            var DCindex = (int)placement.ZoneIndex;
            foreach (var zoneIndex in layerData.ZonesWithBulkheadEntrance.Iter())
            {
                if (zoneIndex <= 0 || (int)zoneIndex >= level.zones.Count) continue; // Seems the actual level maker also just discards them?
                ZoneData zone = level.zones[(int)zoneIndex];
                if (zone.entrance_index == DCindex)
                {
                    if (zone.lock_type != ZoneData.eLockType.None
                        && zone.lock_type != ZoneData.eLockType.BulkheadKey
                        && zone.lock_type != ZoneData.eLockType.Locked
                    ) throw new InvalidDataException("Expected bulkhead door to have None lock type prior to bulkhead door processing");
                    level.zones[(int)zoneIndex].lock_type = ZoneData.eLockType.BulkheadKey;
                }
            }
        }

        // If there is no bulkhead DC then the door is simply "locked" expecting an event
        foreach (var zoneIndex in layerData.ZonesWithBulkheadEntrance.Iter())
        {
            if (zoneIndex <= 0 || (int)zoneIndex >= level.zones.Count) continue; // Seems the actual level maker also just discards them?
            ZoneData zone = level.zones[(int)zoneIndex];
            if (zone.lock_type != ZoneData.eLockType.BulkheadKey)
            {
                if (zone.lock_type != ZoneData.eLockType.None
                    && zone.lock_type != ZoneData.eLockType.Locked
                ) throw new InvalidDataException("Expected bulkhead door to have None lock type prior to bulkhead door processing");
                zone.lock_type = ZoneData.eLockType.Locked;
            }
        }

        // Simply add the bulkhead keys
        foreach (var placement in layerData.BulkheadKeyPlacements.Iter())
        {
            level.keys.Add(new()
            {
                zone_alias = -1,
                positions = placement.Select(ZonePosition.Make).ToList(),
            });
        }

        // Processing objectives
        level.objectives.EnsureCapacity(1 + layerData.ChainedObjectiveData.Count);
        WardenObjectiveDataBlock? obj = null;

        var objectives = Enumerable.Repeat(layerData.ObjectiveData, 1).Concat(layerData.ChainedObjectiveData.Iter());
        int objectiveCount = -1;
        foreach (var objective in objectives)
        {
            obj = WardenObjectiveDataBlock.GetBlock(objective.DataBlockId);
            objectiveCount += 1;
            switch (obj.Type)
            {
                /* Retrieve HSU DNA sample
                 *  Solve  -> Starting the HSU scan
                 *  Solved -> Completing the HSU scan
                 * Exactly 1 HSU scan per objective
                 */
                case eWardenObjectiveType.HSU_FindTakeSample:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.CollectDNASample,
                        sub_objective_count = 1,
                    });

                    level.pickups.Add(new()
                    {
                        item_type = objectiveCount,
                        positions = objective.ZonePlacementDatas[0].Select(ZonePosition.Make).ToList(),
                    });

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        MultiZonalEventSource hsuSource = new()
                        {
                            zones = objective.ZonePlacementDatas[0].Select(ZonePosition.Make).ToList()
                        };
                        ProcessEvents(level, hsuSource, WardenEvent.Source.eType.CompleteHSUScan, events.FirstOrDefault() ?? Enumerable.Empty<WardenObjectiveEventData>());
                    }

                    break;

                /* Navigate to reactor, complete all startup waves, then extract
                 *  Solve  -> Completing the entire reactor. Slightly delayed
                 *  Solved -> The startup sequence is complete, unless DoNotSolveObjectiveOnReactorComplete is set
                 * Each reactor wave contains events which are executed when the wave survival timer is completed
                 *  -> This means the events for one wave are only contingent on access to the code for the previous wave and access to the previous wave
                 * Also of note, it seems any reactor from the placement can be used to achieve the objective; at most one is necessary,
                 *  but if you include more then any of them can be used
                 */
                case eWardenObjectiveType.Reactor_Startup:

                    level.objectives.Add(new ReactorStartupObjectiveData()
                    {
                        objective_type = obj.DoNotSolveObjectiveOnReactorComplete ? ObjectiveData.eType.ReactorStartup_Empty : ObjectiveData.eType.ReactorStartup,
                        sub_objective_count = 1,
                        wave_count = obj.ReactorWaves.Count,
                    });

                    // Place reactor itself as a pickup
                    level.pickups.Add(new()
                    {
                        item_type = objectiveCount,
                        positions = objective.ZonePlacementDatas.SelectMany(ps => ps.Select(ZonePosition.Make)).ToList(),
                    });

                    // Reactor wave events
                    for (int i = 0; i < obj.ReactorWaves.Count; i++)
                    {
                        ReactorWaveEventSource source = new()
                        {
                            objective_num = objectiveCount,
                            subobjective_num = 0, // We assume only one reactor because that's all the game seems to be able to process
                            wave_index = i,
                        };
                        ProcessEvents(level, source, WardenEvent.Source.eType.CompleteReactorWave, obj.ReactorWaves[i].Events.Iter());
                    }

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        MultiZonalEventSource reactorSource = new()
                        {
                            zones = objective.ZonePlacementDatas.SelectMany(ps => ps.Select(ZonePosition.Make)).ToList()
                        };
                        ProcessEvents(level, reactorSource, WardenEvent.Source.eType.CompleteReactorStartup, events.FirstOrDefault() ?? Enumerable.Empty<WardenObjectiveEventData>());
                    }

                    break;

                /* Navigate to the reactor, complete the scan, then extract
                 *  Solve  -> Completing the shutdown scan
                 *  Solved -> The shutdown sequence is complete, unless DoNotSolveObjectiveOnReactorComplete is set
                 * While ReactorShutdown objectives typically define ReactorWaves, they are not used
                 * I'm assuming this this is also multizonal similar to ReactorStartup - ie, you can use any reactor defined in the objective
                 */
                case eWardenObjectiveType.Reactor_Shutdown:

                    level.objectives.Add(new()
                    {
                        objective_type = obj.DoNotSolveObjectiveOnReactorComplete ? ObjectiveData.eType.ReactorShutdown_Empty : ObjectiveData.eType.ReactorShutdown,
                        sub_objective_count = 1,
                    });

                    // Place reactor itself as a pickup
                    level.pickups.Add(new()
                    {
                        item_type = objectiveCount,
                        positions = objective.ZonePlacementDatas.SelectMany(ps => ps.Select(ZonePosition.Make)).ToList(),
                    });

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        MultiZonalEventSource reactorSource = new()
                        {
                            zones = objective.ZonePlacementDatas.SelectMany(ps => ps.Select(ZonePosition.Make)).ToList()
                        };
                        ProcessEvents(level, reactorSource, WardenEvent.Source.eType.CompleteReactorShutdown, events.FirstOrDefault() ?? Enumerable.Empty<WardenObjectiveEventData>());
                    }
                    break;

                /* Collect items (small pickups) from lockers and such
                 *  Solve  -> Picking up an item
                 *  Solved -> The target quantity of small pickups is collected
                 */
                case eWardenObjectiveType.GatherSmallItems:

                    /* The number of zones we must traverse can be calculated by how many zones we are able to miss
                     * Example: R1B1 has 18 IDs in 7 zones, with at most 3 in each zone. 12 are required for success.
                     *  -> 18 - 12 = 6 IDs can be missed
                     *  -> This means 6 / 3 = 2 zones can be skipped (that is, 2 zones can hold AT MOST six IDs)
                     *  -> Therefore, the remaining five zones must be checked to guarantee enough IDs
                     * If there was a MinPerZone we could've used that to calculate an upper bound on the required zone count, 
                     *  but since there isn't the bound is considered inf
                     */
                    float missableCount = obj.Gather_SpawnCount - obj.Gather_RequiredCount;
                    int missableZoneCount = (int)MathF.Floor(missableCount / obj.Gather_MaxPerZone);
                    int reqZoneCount = objective.ZonePlacementDatas[0].Count - missableZoneCount;
                    level.objectives.Add(new GatherItemsObjectiveData()
                    {
                        objective_type = ObjectiveData.eType.GatherSmallItems,
                        sub_objective_count = obj.Gather_RequiredCount,
                        req_count = Math.Clamp(reqZoneCount, 0, objective.ZonePlacementDatas[0].Count),
                    });

                    // Seems pickup zones can only be defined in the first placement list
                    level.pickups.AddRange(objective.ZonePlacementDatas[0].Select<ZonePlacementData, PickupData>(p => new()
                    {
                        item_type = objectiveCount,
                        positions = new(1) { ZonePosition.Make(p) },
                    }));

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        int count = 0;
                        foreach (var subevents in events)
                        {
                            // Note that it's possible for there to be more subevent chains than small pickups (required or otherwise)
                            SubObjectiveEventSource source = new()
                            {
                                objective_num = objectiveCount,
                                subobjective_num = count++,
                            };
                            ProcessEvents(level, source, WardenEvent.Source.eType.PickupSmallItem, subevents);
                        }
                    }

                    break;

                /* Enter the forward extraction zone (not opening, explictly entering the zone triggers GotoWin)
                 *  Solve  -> None (Cannot trigger OnActivate either)
                 *  Solved -> Enter the forward extraction zone
                 */
                case eWardenObjectiveType.ClearAPath:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.ClearAPath,
                        sub_objective_count = 1
                    });

                    break;

                /* Enter a specific terminal command on a specific terminal. The name of the command does not matter
                 *  Activate -> Entering the command and completing the resulting scan
                 *  Solved   -> the command is entered and the resulting scan is completed
                 * Because it has an Activate, "Solve" doesn't matter to us - rather, "OnActivateOnSolveItem" has no effect
                 *  - Note: "OnActivateOnSolveItem" seems to delay OnActivate to after GotoWin. Not sure if that ever matters
                 * Only the first placement group is respected, though it can randomize between zones in that one group
                 */
                case eWardenObjectiveType.SpecialTerminalCommand:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.SpecialTerminalCommand,
                        sub_objective_count = 1,
                    });

                    // Add terminal command as pickup
                    List<ZonePosition> positions = objective.ZonePlacementDatas.Count == 0 ? new List<ZonePosition>() { new(0, 0) } : objective.ZonePlacementDatas[0].Select(ZonePosition.Make).ToList();
                    level.pickups.Add(new()
                    {
                        item_type = objectiveCount,
                        positions = positions,
                    });

                    var specialTerminalCommandSource = new MultiZonalEventSource()
                    {
                        zones = positions
                    };
                    ProcessEvents(level, specialTerminalCommandSource, WardenEvent.Source.eType.SpecialTerminalCommand, obj.EventsOnActivate.Iter());

                    break;

                /* Retrieve one or more "big" pickups and move them to the extraction scan
                 *  Solve  -> Pickup a required big pickup
                 *  Solved -> All required big pickups have been picked up at least once
                 * The scan spawns when the first item is picked up, but cannot make progress until all pickups are in it
                 */
                case eWardenObjectiveType.RetrieveBigItems:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.RetrieveBigItems,
                        sub_objective_count = obj.Retrieve_Items.Count,
                    });

                   level.pickups.AddRange(
                        Enumerable.Range(0, obj.Retrieve_Items.Count)
                            .Select(i => objective.ZonePlacementDatas[i % objective.ZonePlacementDatas.Count])
                            .Select(ps => new PickupData { item_type = objectiveCount, positions = ps.Select(ZonePosition.Make).ToList() })
                    );

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        int count = 0;
                        foreach (var subevents in events)
                        {
                            // Note that it's possible for there to be more subevent chains than big pickups
                            SubObjectiveEventSource source = new()
                            {
                                objective_num = objectiveCount,
                                subobjective_num = count++,
                            };
                            ProcessEvents(level, source, WardenEvent.Source.eType.RetrieveBigPickup, subevents);
                        }
                    }

                    break;

                /* Power up generators throughout the level. Generally, you start with all the powercells you need (but not necessarily)
                 *  Solve  -> Power up a generator
                 *  Solved -> All required generators have been powered by a cell
                 */
                case eWardenObjectiveType.PowerCellDistribution:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.PowerCellDistribution,
                        sub_objective_count = obj.PowerCellsToDistribute,
                    });

                    // Add starting cells
                    level.pickups.AddRange(
                        Enumerable.Repeat(new PickupData() { item_type = -1, positions = new(1) { new ZonePosition(0, 0) } }, obj.PowerCellsToDistribute)
                    );

                    // Add generators which will be powered
                    level.pickups.AddRange(objective.ZonePlacementDatas.Select(ps => new PickupData()
                    {
                        item_type = objectiveCount,
                        positions = ps.Select(ZonePosition.Make).ToList()
                    }));

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        int count = 0;
                        foreach (var subevents in events)
                        {
                            // Note that it's possible for there to be more subevent chains than gens to power
                            SubObjectiveEventSource source = new()
                            {
                                objective_num = objectiveCount,
                                subobjective_num = count++,
                            };
                            ProcessEvents(level, source, WardenEvent.Source.eType.PowerSoloGen, subevents);
                        }
                    }

                    break;

                /* Complete all required terminal uplinks
                 *  Solve  -> Complete an uplink
                 *  Solved -> Complete all uplinks
                 */
                case eWardenObjectiveType.TerminalUplink:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.TerminalUplink,
                        sub_objective_count = obj.Uplink_NumberOfTerminals,
                    });

                    // Add uplink terminals as pickups
                    level.pickups.AddRange(
                        Enumerable.Range(0, obj.Uplink_NumberOfTerminals)
                            .Select(i => objective.ZonePlacementDatas[i % objective.ZonePlacementDatas.Count])
                            .Select(p => new PickupData() { item_type = objectiveCount, positions = p.Select(ZonePosition.Make).ToList() })
                    );

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        int count = 0;
                        foreach (var subevents in events)
                        {
                            // Note that it's possible for there to be more subevent chains than gens to power
                            SubObjectiveEventSource source = new()
                            {
                                objective_num = objectiveCount,
                                subobjective_num = count++,
                            };
                            ProcessEvents(level, source, WardenEvent.Source.eType.CompleteStandardUplink, subevents);
                        }
                    }

                    break;

                /* Fill the required number of generators in the cluster with cells, typically gathered from throughout the level
                 *  Solve  -> Placing a cell in a gen (in the cluster)
                 *  Solved -> Complete scan after all cells are inserted. 
                 *            If there is no scan, it is (presumably) solved as soon as all required cells are inserted
                 */
                case eWardenObjectiveType.CentralGeneratorCluster:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.CentralGeneratorCluster,
                        sub_objective_count = obj.CentralPowerGenClustser_NumberOfGenerators,
                    });

                    // Add gen cluster as pickup
                    // TODO: Technically, there could be multiple gen clusters, perhaps paired to multiple objectives? This does not happen in vanilla
                    foreach (var zone in level.zones)
                    {
                        if ((zone.originalZone?.GeneratorClustersInZone ?? 0) > 0)
                        {
                            level.pickups.Add(new() { item_type = objectiveCount, positions = new List<ZonePosition>() { new(zone.originalZone!.LocalIndex, 0) } } );
                            break;
                        }
                    }

                    // Add objective-placed cells
                    level.pickups.AddRange(
                        Enumerable.Range(0, obj.CentralPowerGenClustser_NumberOfPowerCells)
                            .Select(i => objective.ZonePlacementDatas[i % objective.ZonePlacementDatas.Count])
                            .Select(p => new PickupData() { item_type = -1, positions = p.Select(ZonePosition.Make).ToList() })
                    );

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        int count = 0;
                        foreach (var subevents in events)
                        {
                            // Note that it's possible for there to be more subevent chains than gens to power
                            SubObjectiveEventSource source = new()
                            {
                                objective_num = objectiveCount,
                                subobjective_num = count++,
                            };
                            ProcessEvents(level, source, WardenEvent.Source.eType.PowerClusterGen, subevents);
                        }
                    }

                    break;

                /* Also known as "Process Item". Start with or find a large pickup, carry it to processing machine, and optionally carry it out
                 *  Solve  -> When the item finishes processing
                 *  Solved -> When the item finishes processing, barring "ActivateHSU_ObjectiveCompleteAfterInsertion" is false
                 * ActivateSmallHSU has its own events list, ActivateHSU_Events, which generally fires in place of OnActivateOnSolveItem
                 *  -> OnActivateOnSolveItem still works, however
                 * Also, sometimes (oftentimes?) ActivateHSU_ObjectiveCompleteAfterInsertion is ignored. Not sure what the pattern is
                 */
                case eWardenObjectiveType.ActivateSmallHSU:

                    level.objectives.Add(new()
                    {
                        objective_type = obj.ActivateHSU_ObjectiveCompleteAfterInsertion ? ObjectiveData.eType.ProcessItem : ObjectiveData.eType.ProcessItem_Empty,
                        sub_objective_count = 1,
                    });

                    // Processing machine
                    level.pickups.Add(new()
                    {
                        item_type = objectiveCount,
                        positions = objective.ZonePlacementDatas[0].Select(ZonePosition.Make).ToList()
                    });

                    // Big pickup (either in elevator or in level with us)
                    if (obj.ActivateHSU_BringItemInElevator || obj.GenericItemFromStart == obj.ActivateHSU_ItemFromStart)
                    {
                        level.pickups.Add(new()
                        {
                            item_type = objectiveCount,
                            positions = new(1) { new ZonePosition(0, 0) }
                        });
                    }
                    else
                    {
                        // TODO: Search for big pickups in other levels / dimensions, just in case
                        foreach (var zone in level.zones.Select(z => z.originalZone))
                        {
                            if (zone == null) continue;
                            BigPickupDistributionDataBlock dist = BigPickupDistributionDataBlock.GetBlock(zone.BigPickupDistributionInZone);
                            if (dist == null) continue;
                            if (dist.SpawnData.Select(d => d.ItemID).Contains(obj.ActivateHSU_ItemFromStart))
                            {
                                level.pickups.Add(new()
                                {
                                    item_type = objectiveCount,
                                    positions = new(1) { new ZonePosition(zone.LocalIndex, 0) }
                                });
                                break;
                            }
                        }
                    }

                    if (obj.OnActivateOnSolveItem)
                    {
                        // You should only ever be able to process 1 item. That said, might as well include the other events JIC
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        int count = 0;
                        foreach (var subevents in events)
                        {
                            SubObjectiveEventSource source = new()
                            {
                                objective_num = objectiveCount,
                                subobjective_num = count,
                            };
                            ProcessEvents(level, source, WardenEvent.Source.eType.ProcessItem, subevents);
                        }
                    }

                    break;

                /* Warden protocol. Survive until the time expires through various events
                 *  Activate -> After the inital grace period expires. Usually events in this list set up fog changes and doors unlocking
                 *  Solved   -> After the survival timer expires.
                 * Similar to SpecialTerminalCommand, OnActivateOnSolveItem has no use here
                 */
                case eWardenObjectiveType.Survival:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.SurviveWardenProtocol,
                        sub_objective_count = 1
                    });

                    // OnActivate events
                    ObjectiveEventSource survivalEventSource = new()
                    {
                        objective_num = 0,
                    };
                    ProcessEvents(level, survivalEventSource, WardenEvent.Source.eType.StartSurvival, obj.EventsOnActivate.Iter());

                    break;

                /* Perform a special terminal command on multiple terminals. A blend of GatherSmallItems and SpecialTerminalCommand
                 *  Solve  -> When the command on the terminal finishes executing. Presumably, requires any relevant scans to be completed
                 *  Solved -> The required number of terminals have had the command run on them
                 * Unlike SpecialTerminalCommand, OnActivateOnSolve is required for the commands to use OnActivate events
                 */
                case eWardenObjectiveType.GatherTerminal:

                    level.objectives.Add(new GatherItemsObjectiveData()
                    {
                        objective_type = ObjectiveData.eType.GatherTerminals,
                        sub_objective_count = obj.GatherTerminal_RequiredCount,
                        req_count = obj.GatherTerminal_RequiredCount,
                    });

                    // Add terminals as pickups
                    level.pickups.AddRange(
                        Enumerable.Range(0, obj.GatherTerminal_SpawnCount)
                            .Select(i => objective.ZonePlacementDatas[i % objective.ZonePlacementDatas.Count])
                            .Select(p => new PickupData() { item_type = -1, positions = p.Select(ZonePosition.Make).ToList() })
                    );

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        int count = 0;
                        foreach (var subevents in events)
                        {
                            // Note that it's possible for there to be more subevent chains than terminals (required or otherwise)
                            SubObjectiveEventSource source = new()
                            {
                                objective_num = objectiveCount,
                                subobjective_num = count++,
                            };
                            ProcessEvents(level, source, WardenEvent.Source.eType.SpecialTerminalCommand, subevents);
                        }
                    }

                    break;

                /* Same as uplink, but codes are sent to a second terminal
                 *  Solve  -> Complete an uplink
                 *  Solved -> Complete all uplinks
                 */
                case eWardenObjectiveType.CorruptedTerminalUplink:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.CorruptedTerminalUplink,
                        sub_objective_count = obj.Uplink_NumberOfTerminals,
                    });

                    // Corrupted uplinks are always both in the same zone (or so it seems)
                    level.pickups.AddRange(
                        Enumerable.Range(0, obj.Uplink_NumberOfTerminals)
                            .Select(i => objective.ZonePlacementDatas[i % objective.ZonePlacementDatas.Count])
                            .Select(p => new PickupData() { item_type = objectiveCount, positions = p.Select(ZonePosition.Make).ToList() })
                    );

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        int count = 0;
                        foreach (var subevents in events)
                        {
                            // Note that it's possible for there to be more subevent chains than uplinks (required or otherwise)
                            SubObjectiveEventSource source = new()
                            {
                                objective_num = objectiveCount,
                                subobjective_num = count++,
                            };
                            ProcessEvents(level, source, WardenEvent.Source.eType.CompleteCorruptedUplink, subevents);
                        }
                    }

                    break;
                
                /* An empty objective with no win condition; requires a ForceCompleteObjective or similar event
                 *  Solve  -> None (impossible)
                 *  Solved -> None (impossible)
                 */
                case eWardenObjectiveType.Empty:

                    level.objectives.Add(new()
                    {
                        objective_type = ObjectiveData.eType.Empty,
                        sub_objective_count = 1,
                    });

                    break;

                /* Hitting init, verify, and confirm on two alternating terminals in quick succession. Typically multiple rounds
                 *  Solve  -> When all timed sequences are completed
                 *  Solved -> When all timed sequences are completed
                 * Timed Sequences have their own sets of events on start, succeed, and fail
                 */
                case eWardenObjectiveType.TimedTerminalSequence:

                    level.objectives.Add(new TimedSequenceObjectiveData()
                    {
                        objective_type = ObjectiveData.eType.TimedTerminalSequence,
                        sub_objective_count = 1,
                        num_rounds = obj.TimedTerminalSequence_NumberOfRounds,
                    });

                    // Zone placement data is weird here. I believe the first set is the main terminal, and the following are where each verify terminal is
                    // Further, I believe the order of the verify terminals is randomized; it can choose any of the groups for each verify terminal,
                    //  potentially with the ability to reuse placement groups
                    // We'll process this with the assumption AP will simply require the first zone to start it and all zones from all lists to complete it
                    level.pickups.AddRange(
                        Enumerable.Range(0, obj.TimedTerminalSequence_NumberOfRounds + 1)
                            .Select(i => objective.ZonePlacementDatas[i % objective.ZonePlacementDatas.Count])
                            .Select(ps => new PickupData() { item_type = objectiveCount, positions = ps.Select(ZonePosition.Make).ToList() }
                        )
                    );

                    for (int i = 0; i < obj.TimedTerminalSequence_NumberOfRounds; i++)
                    {
                        var source = new SubObjectiveEventSource()
                        {
                            objective_num = objectiveCount,
                            subobjective_num = i,
                        };
                        if (obj.TimedTerminalSequence_EventsOnSequenceStart.Count > i)
                            ProcessEvents(level, source, WardenEvent.Source.eType.StartTimedSequenceRound, obj.TimedTerminalSequence_EventsOnSequenceStart[i].Iter());
                        if (obj.TimedTerminalSequence_EventsOnSequenceDone.Count > i)
                            ProcessEvents(level, source, WardenEvent.Source.eType.CompleteTimedSequenceRound, obj.TimedTerminalSequence_EventsOnSequenceDone[i].Iter());
                        if (obj.TimedTerminalSequence_EventsOnSequenceFail.Count > i)
                            ProcessEvents(level, source, WardenEvent.Source.eType.FailTimdSequenceRound, obj.TimedTerminalSequence_EventsOnSequenceFail[i].Iter());
                    }

                    if (obj.OnActivateOnSolveItem)
                    {
                        var events = obj.EventsOnActivate.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
                        SubObjectiveEventSource source = new()
                        {
                            objective_num = objectiveCount,
                            subobjective_num = 1,
                        };
                        ProcessEvents(level, source, WardenEvent.Source.eType.CompleteFullTimedSequence, events.FirstOrDefault() ?? Enumerable.Empty<WardenObjectiveEventData>());
                    }

                    break;
            }

            // If they're delayed until exit scan, we delay adding them with the intent they'll be overwritten by another event
            if (obj.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.OnObjectiveCompleted)
            {
                var gotoWinSource = new ObjectiveEventSource() { objective_num = objectiveCount };
                ProcessEvents(level, gotoWinSource, WardenEvent.Source.eType.ObjectiveComplete, obj.EventsOnGotoWin.Iter());
            }
        }

        // Only the last objective's events are triggered if they're delayed until the exit scan
        if (obj == null) throw new NullReferenceException("Did not expect objective to be null here");
        if (obj.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.WhenExitScanMakesProgress)
        {
            var startExitScanSource = new ObjectiveEventSource() { objective_num = objectiveCount };
            ProcessEvents(level, startExitScanSource, WardenEvent.Source.eType.StartExitScan, obj.EventsOnGotoWin.Iter());
        }

        // Only the first objective's events are triggered for OnElevatorLand
        obj = WardenObjectiveDataBlock.GetBlock(layerData.ObjectiveData.DataBlockId) ?? throw new NullReferenceException();
        var onElevatorLandSource = new WardenEvent.Source();
        ProcessEvents(level, onElevatorLandSource, WardenEvent.Source.eType.OnElevatorLand, obj.EventsOnElevatorLand.Iter());
    }

    // Given an event source and list of events, populate event data
    public static void ProcessEvents(LevelData level, WardenEvent.Source source, WardenEvent.Source.eType sourceType, IEnumerable<WardenObjectiveEventData> events)
    {
        foreach (var ev in events)
        {
            if (TryMakeEventAction(ev, out var type, out var action))
            {
                level.events.Add(new()
                {
                    source_type = sourceType,
                    source = source,
                    action_type = type,
                    action = action,
                });
            }
        }
    }

    // Look at an event and add it to level data as needed. Returns false if there is no applicable action
    public static bool TryMakeEventAction(WardenObjectiveEventData ev, out WardenEvent.Action.eType type, [NotNullWhen(true)] out WardenEvent.Action? action)
    {
        type = 0;
        action = null;

        switch (ev.Type)
        {
            case eWardenObjectiveEventType.UnlockSecurityDoor:
            case eWardenObjectiveEventType.OpenSecurityDoor:
                action = new SpecificZoneEventAction()
                {
                    target_zone_local_index = (int)ev.LocalIndex,
                    target_zone_layer = (int)ev.Layer,
                    target_zone_dimension = (int)ev.DimensionIndex,
                };
                break;

            case eWardenObjectiveEventType.StepProgressionObjective:
            case eWardenObjectiveEventType.ForceCompleteObjective:
            case eWardenObjectiveEventType.ForceInstantWin:
            case eWardenObjectiveEventType.WinOnDeath:
                action = new ObjectiveEventAction()
                {
                    objective_layer = (int)ev.Layer
                };
                break;

            case eWardenObjectiveEventType.DimensionWarpTeam:
                action = new WarpEventAction()
                {
                    target_dimension_index = (int)ev.DimensionIndex,
                    target_zone_local_index = (int)ev.LocalIndex,
                };
                break;

            case eWardenObjectiveEventType.ActivateChainedPuzzle:
                action = new StartScanEventAction()
                {
                    target_zone_local_index = (int)ev.LocalIndex,
                    target_zone_layer = (int)ev.Layer,
                    target_zone_dimension = (int)ev.DimensionIndex,
                    scan_name = ev.WorldEventObjectFilter
                };
                break;

            default:
                return false;
        }

        type = ev.Type switch 
        {
            eWardenObjectiveEventType.UnlockSecurityDoor       => WardenEvent.Action.eType.UnlockZoneDoor,
            eWardenObjectiveEventType.OpenSecurityDoor         => WardenEvent.Action.eType.OpenZoneDoor,
            eWardenObjectiveEventType.StepProgressionObjective => WardenEvent.Action.eType.StepObjectiveProgression,
            eWardenObjectiveEventType.ForceCompleteObjective   => WardenEvent.Action.eType.ForceCompleteObjective,
            eWardenObjectiveEventType.ForceInstantWin          => WardenEvent.Action.eType.ForceInstantWin,
            eWardenObjectiveEventType.WinOnDeath               => WardenEvent.Action.eType.ActivateWinOnDeath,
            eWardenObjectiveEventType.DimensionWarpTeam        => WardenEvent.Action.eType.DimensionWarp,
            eWardenObjectiveEventType.ActivateChainedPuzzle    => WardenEvent.Action.eType.StartScan,

            _ => throw new InvalidOperationException("Unexpected invalid event type in TryMakeEventAction!"), // Make the compiler happy
        };
        return true;
    }


}
