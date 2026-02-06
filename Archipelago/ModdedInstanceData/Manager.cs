
using GameData;
using ReTFO.Archipelago;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

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
        ExpeditionData exData = new();
        exData.name = expedition.GetShortName(indexInTier);

        // Generate all level data
        exData.main_level = InitLevelData(expedition.LevelLayoutData, 0, $"{exData.name} (Main)");
        if (exData.main_level != null)
            ProcessLayerData(exData.main_level, expedition.MainLayerData);

        if (expedition.SecondaryLayerEnabled)
        {
            exData.secondary_build_from = new()
            {
                layer_index = (int)expedition.BuildSecondaryFrom.LayerType,
                zone_index  = (int)expedition.BuildSecondaryFrom.Zone,
            };
            exData.secondary_level = InitLevelData(expedition.SecondaryLayout, 0, $"{exData.name} (Secondary)")!;
            ProcessLayerData(exData.secondary_level, expedition.SecondaryLayerData);
        }

        if (expedition.ThirdLayerEnabled)
        {
            exData.overload_build_from = new()
            {
                layer_index = (int)expedition.BuildThirdFrom.LayerType,
                zone_index  = (int)expedition.BuildThirdFrom.Zone,
            };
            exData.overload_level = InitLevelData(expedition.ThirdLayout, 0, $"{exData.name} (Overload)")!;
            ProcessLayerData(exData.overload_level, expedition.ThirdLayerData);
        }

        // Generate dimension data as level data
        Dictionary<int, LevelData> dimension_data = new();
        foreach (var dim in expedition.DimensionDatas.Iter())
        {
            DimensionDataBlock db = DimensionDataBlock.GetBlock(dim.DimensionData);
            dimension_data[(int)dim.DimensionIndex] = GenerateDimensionData(db, (int)dim.DimensionIndex, $"{exData.name} (Dimension #{(int)dim.DimensionIndex})");
        }

        // Apply locks to sector entrances
        if (expedition.SecondaryLayerEnabled)
        {
            if (exData.secondary_level == null) throw new NullReferenceException();
            IEnumerable<BulkheadDoorPlacementData> dcLocs = exData.secondary_build_from.layer_index switch
            {
                0 => expedition.MainLayerData.BulkheadDoorControllerPlacements.Iter(),
                2 => expedition.ThirdLayerData.BulkheadDoorControllerPlacements.Iter(),
                _ => throw new InvalidDataException()
            };

            if (dcLocs.Any(p => (int)p.ZoneIndex == exData.secondary_build_from.zone_index))
                exData.secondary_level.zones[exData.secondary_level.start_zone].lock_type = ZoneData.eLockType.BulkheadKey;
            else
                exData.secondary_level.zones[exData.secondary_level.start_zone].lock_type = ZoneData.eLockType.Locked;
        }

        if (expedition.ThirdLayerEnabled)
        {
            if (exData.overload_level == null) throw new NullReferenceException();
            IEnumerable<BulkheadDoorPlacementData> dcLocs = exData.overload_build_from.layer_index switch
            {
                0 => expedition.MainLayerData.BulkheadDoorControllerPlacements.Iter(),
                1 => expedition.SecondaryLayerData.BulkheadDoorControllerPlacements.Iter(),
                _ => throw new InvalidDataException()
            };

            if (dcLocs.Any(p => (int)p.ZoneIndex == exData.secondary_build_from.zone_index))
                exData.overload_level.zones[exData.overload_level.start_zone].lock_type = ZoneData.eLockType.BulkheadKey;
            else
                exData.overload_level.zones[exData.overload_level.start_zone].lock_type = ZoneData.eLockType.Locked;
        }

        // Add elevator drop and exit scan events
        WardenObjectiveDataBlock firstObjective = WardenObjectiveDataBlock.GetBlock(expedition.MainLayerData.ObjectiveData.DataBlockId);
        ProcessEvents(exData.events_on_elevator_land, firstObjective.EventsOnElevatorLand.Iter());

        if (expedition.MainLayerData.ChainedObjectiveData.Count == 0)
        {
            if (firstObjective.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.WhenExitScanMakesProgress)
                ProcessEvents(exData.events_on_progress_exit_scan, firstObjective.EventsOnGotoWin.Iter());
        }
        else
        {
            WardenObjectiveDataBlock lastObjective = WardenObjectiveDataBlock.GetBlock(
                expedition.MainLayerData.ChainedObjectiveData[expedition.MainLayerData.ChainedObjectiveData.Count - 1].DataBlockId
            );
            if (lastObjective.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.WhenExitScanMakesProgress)
                ProcessEvents(exData.events_on_progress_exit_scan, lastObjective.EventsOnGotoWin.Iter());
        }

        return exData;
    }

    // Generate level data for the given level layout
    public static LevelData? InitLevelData(uint layoutId, int dimensionIndex, string debugExpeditionName)
    {
        LevelLayoutDataBlock level = LevelLayoutDataBlock.GetBlock(layoutId);
        if (level == null) return null;
        LevelData lData = new();

        // Set up basics for all zones. Sort by local index, and mark entrance
        int zoneCount = level.Zones.Select(z => (int)z.LocalIndex).Max() + 1;
        lData.zones.EnsureCapacity(zoneCount);
        lData.zones.AddRange(Enumerable.Repeat<ZoneData>(null!, zoneCount));
        for (int i = 0; i < level.Zones.Count; i++)
        {
            ExpeditionZoneData zone = level.Zones[i];
            if ((int)zone.LocalIndex < 0 || (int)zone.LocalIndex >= lData.zones.Count)
                throw new ArgumentOutOfRangeException($"Local index {(int)zone.LocalIndex} is outside of expected range!");
            if (lData.zones[(int)zone.LocalIndex] != null) 
                throw new NullReferenceException("Duplicate local index in zone list!");

            int terminalIndex = 0;
            int alias = zone.AliasOverride != -1 ? zone.AliasOverride : level.ZoneAliasStart + (int)zone.LocalIndex;
            lData.zones[(int)zone.LocalIndex] = new()
            {
                originalZone = zone,
                alias = alias,
                lock_type = (ZoneData.eLockType)zone.ProgressionPuzzleToEnter.PuzzleType,
                terminals = zone.TerminalPlacements.Select(t => GenerateTerminalData(t, lData, dimensionIndex, alias, terminalIndex++)).ToList(),
            };
        }
        lData.start_zone = (int)level.Zones[0].LocalIndex;

        // Ensure there are no null zones
        for (int i = 0; i < lData.zones.Count; i++)
        {
            if (lData.zones[i] == null) lData.zones[i] = new()
            {
                originalZone = null,
                alias = -1,
                terminals = new(0),
                lock_type = ZoneData.eLockType.Locked,
                entrance_index = 0,
            };
        }

        // With our zones sorted and defined, now add keys, events, big pickups, and cross-zone data
        foreach (var zData in lData.zones)
        {
            // Skip non-zones (which will have no info to process)
            if (zData.originalZone == null) continue;

            // Entrance
            ZoneData? entryZData;
            if (lData.start_zone != (int)zData.originalZone.LocalIndex)
            {
                entryZData = lData.zones[(int)zData.originalZone.BuildFromLocalIndex];
                zData.entrance_index = (int)entryZData.originalZone!.LocalIndex;
            }
            else
            {
                entryZData = null;
                zData.entrance_index = 0;
            }

            // Lock handling
            if (zData.lock_type == ZoneData.eLockType.SimpleKey)
            {
                if (zData.originalZone.ProgressionPuzzleToEnter.ZonePlacementData.Count <= 0)
                    throw new InvalidOperationException("Simple key zone has no positions for its key to spawn in!");
                lData.keys.Add(new()
                {
                    type = KeyData.eType.SimpleKey,
                    zone_alias = zData.alias,
                    positions = zData.originalZone.ProgressionPuzzleToEnter.ZonePlacementData.Select(ZonePosition.Make).ToList()
                });
            }
            else if (zData.lock_type == ZoneData.eLockType.GenAndCell && zData.originalZone.ProgressionPuzzleToEnter.ZonePlacementData.Count > 0)
            {
                lData.keys.Add(new()
                {
                    type = KeyData.eType.Cell,
                    positions = zData.originalZone.ProgressionPuzzleToEnter.ZonePlacementData.Select(ZonePosition.Make).ToList()
                });
            }

            // Terminals in the zone
            for (int i = 0; i < (zData.originalZone.TerminalPlacements?.Count ?? 0); i++)
            {
                var terminal = zData.originalZone.TerminalPlacements![i];
                TerminalData tData = new()
                {
                    logs = terminal.LocalLogFiles.Select(l => l.FileName).ToList(),
                    commands = new(terminal.UniqueCommands.Count),
                };
                zData.terminals.Add(tData);
                
                // Password and password parts
                if (terminal.StartingStateData.PasswordProtected)
                {
                    tData.password_count = terminal.StartingStateData.PasswordPartCount;
                    lData.keys.AddRange(
                        Enumerable.Range(0, terminal.StartingStateData.PasswordPartCount)
                            .Select(j => j % terminal.StartingStateData.TerminalZoneSelectionDatas.Count)
                            .Select(j => terminal.StartingStateData.TerminalZoneSelectionDatas[j])
                            .Select(ps => new KeyData()
                            {
                                type = KeyData.eType.PasswordPart,
                                zone_alias = zData.alias,
                                terminal_index = i,
                                positions = ps.Select(p => new ZonePosition(p.LocalIndex, (eDimensionIndex)dimensionIndex)).ToList()
                            })
                    );
                }

                foreach (var command in terminal.UniqueCommands)
                {
                    CommandData cData = new(command.Command);
                    tData.commands.Add(cData);
                    ProcessEvents(cData.events, command.CommandEvents.Iter());
                }
            }

            // Big pickup distributions in the zone. Cells are separated out as keys
            BigPickupDistributionDataBlock? dist = BigPickupDistributionDataBlock.GetBlock(zData.originalZone.BigPickupDistributionInZone);
            foreach (var item in dist?.SpawnData.Iter() ?? Enumerable.Empty<BigPickupSpawnData>())
            {
                ItemDataBlock itemDB = ItemDataBlock.GetBlock(item.ItemID);
                if (itemDB.terminalItemShortName.Contains("CELL", StringComparison.OrdinalIgnoreCase))
                {
                    lData.keys.Add(new()
                    {
                        type = KeyData.eType.Cell,
                        positions = new(1) { new ZonePosition(zData.originalZone.LocalIndex, (eDimensionIndex)dimensionIndex) }
                    });
                }
                else
                {
                    zData.big_pickups.Add(new() { item_type = item.ItemID, });
                }
            }

            // Zone events
            ProcessEvents(zData.events_on_unlock_door,     zData.originalZone.EventsOnUnlockDoor.Iter());
            ProcessEvents(zData.events_on_door_scan_start, zData.originalZone.EventsOnDoorScanStart.Iter());
            ProcessEvents(zData.events_on_door_scan_done,  zData.originalZone.EventsOnDoorScanDone.Iter());
            ProcessEvents(zData.events_on_open_door,       zData.originalZone.EventsOnOpenDoor.Iter());
            ProcessEvents(zData.events_on_boss_death,      zData.originalZone.EventsOnBossDeath.Iter());
            ProcessEvents(zData.events_on_portal_warp,     zData.originalZone.EventsOnPortalWarp.Iter());

            // Trigger events
            foreach (var ev in zData.originalZone.EventsOnTrigger.Iter())
            {
                if (TryMakeEventAction(ev, out var type, out var action))
                    zData.events_on_trigger.Add(new(type, action, ev.WorldEventTriggerObjectFilter));
            }

            // Scan events - we can wrap these the same as trigger events, AP will account for the scan needing to be started
            foreach (var scan in zData.originalZone.WorldEventChainedPuzzleDatas.Iter())
            {
                foreach (var ev in scan.EventsOnScanDone)
                {
                    if (TryMakeEventAction(ev, out var type, out var action))
                        zData.events_on_trigger.Add(new(type, action, scan.WorldEventObjectFilter));
                }
            }

            if (entryZData != null) // Event(s) occuring in the entry zone
            {
                foreach (var ev in zData.originalZone.EventsOnApproachDoor)
                {
                    if (TryMakeEventAction(ev, out var type, out var data))
                        entryZData.events_on_approach_zone.Add(new(type, data, zData.alias));
                }
            }
            else // Events occuring when approaching the level
            {
                foreach (var ev in zData.originalZone.EventsOnApproachDoor)
                {
                    if (TryMakeEventAction(ev, out var type, out var data))
                        lData.events_on_approach_level.Add(new(type, data, zData.alias));
                }
            }
        }

        // One extra loop because we need to touch existing terminal data
        foreach (var zData in lData.zones)
        {
            if (zData.originalZone?.TurnOffAlarmOnTerminal ?? false)
            {
                ZoneData?     deactivateZoneData = lData.zones[(int)zData.originalZone.TerminalPuzzleZone.LocalIndex];
                TerminalData? deactivateTerminal = deactivateZoneData?.terminals[zData.originalZone.TerminalPuzzleZone.TerminalIndex % deactivateZoneData.terminals.Count];
                if (deactivateTerminal == null)
                {
                    Plugin.Get().Log.LogError($"ZONE_{zData.alias} in expedition \"{debugExpeditionName}\" has deactivate puzzle, but could not find a terminal for the puzzle");
                    continue;
                }
                CommandData cData = new("DEACTIVATE_ALARM");
                deactivateTerminal.commands.Add(cData);
                ProcessEvents(cData.events, zData.originalZone.EventsOnTerminalDeactivateAlarm.Iter());
            }
        }

        return lData;
    }

    // Similar to GenerateLevelData, but takes into account the special cases of dimension building
    public static LevelData GenerateDimensionData(DimensionDataBlock db, int dimensionIndex, string debugExpeditionName)
    {
        var result = InitLevelData(db.DimensionData.LevelLayoutData, dimensionIndex, debugExpeditionName);
        if (result != null) return result;

        // No level? Then it's just a single custom geomorph. We'll treat it like a single zone
        LevelData lData = new() { zones = new(1) };
        int terminalIndex = 0;
        lData.zones.Add(new()
        {
            alias = 0,
            entrance_index = 0,
            terminals = db.DimensionData.StaticTerminalPlacements.Select(t => GenerateTerminalData(t, lData, dimensionIndex, 0, terminalIndex++)).ToList(),
            lock_type = 0,
        });
        lData.start_zone = 0;

        return lData;
    }

    // Small helper to help consolidate terminal generation code
    public static TerminalData GenerateTerminalData(TerminalPlacementData terminal, LevelData lData, int dimensionIndex, int alias, int terminalIndex)
    {
        TerminalData tData = new()
        {
            logs = terminal.LocalLogFiles.Select(l => l.FileName).ToList(),
            commands = new(terminal.UniqueCommands.Count),
        };

        // Password and password parts
        if (terminal.StartingStateData.PasswordProtected)
        {
            tData.password_count = terminal.StartingStateData.PasswordPartCount;
            lData.keys.AddRange(
                Enumerable.Range(0, terminal.StartingStateData.PasswordPartCount)
                    .Select(j => j % terminal.StartingStateData.TerminalZoneSelectionDatas.Count)
                    .Select(j => terminal.StartingStateData.TerminalZoneSelectionDatas[j])
                    .Select(ps => new KeyData()
                    {
                        type = KeyData.eType.PasswordPart,
                        zone_alias = alias,
                        terminal_index = terminalIndex,
                        positions = ps.Select(p => new ZonePosition(p.LocalIndex, (eDimensionIndex)dimensionIndex)).ToList()
                    })
            );
        }

        foreach (var command in terminal.UniqueCommands)
        {
            CommandData cData = new(command.Command);
            tData.commands.Add(cData);
            ProcessEvents(cData.events, command.CommandEvents.Iter());
        }

        return tData;
    }

    // Adds bulkheads, objectives, and other misc things to intialized level data
    public static void ProcessLayerData(LevelData lData, LayerData layerData) {

        // Any bulkhead door with a bulkhead DC to access it is implicitly locked via bulkhead key
        foreach (var placement in layerData.BulkheadDoorControllerPlacements.Iter())
        {
            var DCindex = (int)placement.ZoneIndex;
            foreach (var zoneIndex in layerData.ZonesWithBulkheadEntrance.Select(i => (int)i))
            {
                if (zoneIndex < 0 || zoneIndex >= lData.zones.Count) continue; // Seems the actual level maker also just discards them?
                if (zoneIndex == lData.start_zone) continue; // We don't want to touch the entry zone

                ZoneData zData = lData.zones[zoneIndex];
                if (zData.entrance_index == DCindex)
                {
                    if (zData.lock_type != ZoneData.eLockType.None
                        && zData.lock_type != ZoneData.eLockType.BulkheadKey
                        && zData.lock_type != ZoneData.eLockType.Locked
                    ) throw new InvalidDataException("Expected bulkhead door to have None lock type prior to bulkhead door processing");
                    lData.zones[zoneIndex].lock_type = ZoneData.eLockType.BulkheadKey;
                }
            }
        }

        // If there is no bulkhead DC then the door is simply "locked" expecting an event
        foreach (var zoneIndex in layerData.ZonesWithBulkheadEntrance.Select(i => (int)i))
        {
            if (zoneIndex <= 0 || zoneIndex >= lData.zones.Count) continue; // Seems the actual level maker also just discards them?
            if (zoneIndex == lData.start_zone) continue; // We don't want to touch the entry zone

            ZoneData zData = lData.zones[zoneIndex];
            if (zData.lock_type != ZoneData.eLockType.BulkheadKey)
            {
                if (zData.lock_type != ZoneData.eLockType.None
                    && zData.lock_type != ZoneData.eLockType.Locked
                ) throw new InvalidDataException("Expected bulkhead door to have None lock type prior to bulkhead door processing");
                zData.lock_type = ZoneData.eLockType.Locked;
            }
        }

        // Simply add the bulkhead keys
        foreach (var placement in layerData.BulkheadKeyPlacements.Iter())
        {
            lData.keys.Add(new()
            {
                type = KeyData.eType.Cell,
                positions = placement.Select(ZonePosition.Make).ToList(),
            });
        }

        // Processing objectives
        lData.objectives.EnsureCapacity(1 + layerData.ChainedObjectiveData.Count);
        WardenObjectiveDataBlock? obj = null;
        ObjectiveData oData; // Shared by multiple catch statements, so defined here for simplicity

        var objectives = Enumerable.Empty<WardenObjectiveLayerData>().Append(layerData.ObjectiveData).Concat(layerData.ChainedObjectiveData.Iter());
        foreach (var objective in objectives)
        {
            obj = WardenObjectiveDataBlock.GetBlock(objective.DataBlockId);
            switch (obj.Type)
            {
                /* Retrieve HSU DNA sample
                 *  Solve  -> Starting the HSU scan
                 *  Solved -> Completing the HSU scan
                 * Exactly 1 HSU scan per objective
                 */
                case eWardenObjectiveType.HSU_FindTakeSample:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.CollectDNASample,
                        sub_objective_count = 1,
                        positions = new(1) { objective.ZonePlacementDatas[0].Select(ZonePosition.Make).ToList() }
                    };
                    lData.objectives.Add(oData);

                    if (obj.OnActivateOnSolveItem) 
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

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

                    ReactorStartupObjectiveData rsData = new()
                    {
                        objective_type = obj.DoNotSolveObjectiveOnReactorComplete ? ObjectiveData.eType.ReactorStartup_Empty : ObjectiveData.eType.ReactorStartup,
                        sub_objective_count = 1,
                        positions = new(1) { objective.ZonePlacementDatas.SelectMany(ps => ps.Select(ZonePosition.Make)).ToList() }, // Flatten all placements into one list
                        wave_count = obj.ReactorWaves.Count,
                        events_on_finish_wave = new(obj.ReactorWaves.Count)
                    };
                    lData.objectives.Add(oData = rsData);

                    // Reactor wave events
                    foreach (var wave in obj.ReactorWaves)
                    {
                        List<WardenEvent> list = new();
                        ProcessEvents(list, wave.Events.Iter());
                        rsData.events_on_finish_wave.Add(list);
                    }

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, rsData);

                    break;

                /* Navigate to the reactor, complete the scan, then extract
                 *  Solve  -> Completing the shutdown scan
                 *  Solved -> The shutdown sequence is complete, unless DoNotSolveObjectiveOnReactorComplete is set
                 * While ReactorShutdown objectives typically define ReactorWaves, they are not used
                 * I'm assuming this this is also multizonal similar to ReactorStartup - ie, you can use any reactor defined in the objective
                 */
                case eWardenObjectiveType.Reactor_Shutdown:

                    oData = new()
                    {
                        objective_type = obj.DoNotSolveObjectiveOnReactorComplete ? ObjectiveData.eType.ReactorShutdown_Empty : ObjectiveData.eType.ReactorShutdown,
                        sub_objective_count = 1,
                        positions = new(1) { objective.ZonePlacementDatas.SelectMany(ps => ps.Select(ZonePosition.Make)).ToList() },
                    };
                    lData.objectives.Add(oData);

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

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

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.GatherSmallItems,
                        sub_objective_count = reqZoneCount,

                        // It seems pickup zones can only be in the first list. We're breaking that list into multiple lists of 1 zone
                        positions = objective.ZonePlacementDatas[0].Select(p => new List<ZonePosition>(1) { ZonePosition.Make(p) }).ToList()
                    };

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;

                /* Enter the forward extraction zone (not opening, explictly entering the zone triggers GotoWin)
                 *  Solve  -> None (Cannot trigger OnActivate either)
                 *  Solved -> Enter the forward extraction zone
                 */
                case eWardenObjectiveType.ClearAPath:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.ClearAPath,
                        sub_objective_count = 1,
                        positions = new(0),
                    };
                    lData.objectives.Add(oData);

                    break;

                /* Enter a specific terminal command on a specific terminal. The name of the command does not matter
                 *  Activate -> Entering the command and completing the resulting scan
                 *  Solved   -> the command is entered and the resulting scan is completed
                 * Because it has an Activate, "Solve" doesn't matter to us - rather, "OnActivateOnSolveItem" has no effect
                 *  - Note: "OnActivateOnSolveItem" seems to delay OnActivate to after GotoWin. Not sure if that ever matters
                 * Only the first placement group is respected, though it can randomize between zones in that one group
                 */
                case eWardenObjectiveType.SpecialTerminalCommand:

                    oData = new()
                    { 
                        objective_type = ObjectiveData.eType.SpecialTerminalCommand,
                        sub_objective_count = 1,

                        // Sometimes no position is given, in which case it's in the first possible zone
                        positions = new(1) { objective.ZonePlacementDatas.Count == 0 ? new List<ZonePosition>() { new(0, 0) } : objective.ZonePlacementDatas[0].Select(ZonePosition.Make).ToList(), }
                    };
                    lData.objectives.Add(oData);

                    ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;

                /* Retrieve one or more "big" pickups and move them to the extraction scan
                 *  Solve  -> Pickup a required big pickup
                 *  Solved -> All required big pickups have been picked up at least once
                 * The scan spawns when the first item is picked up, but cannot make progress until all pickups are in it
                 */
                case eWardenObjectiveType.RetrieveBigItems:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.RetrieveBigItems,
                        sub_objective_count = obj.Retrieve_Items.Count,
                        positions = MakePositions(objective.ZonePlacementDatas, obj.Retrieve_Items.Count)
                    };
                    lData.objectives.Add(oData);

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;

                /* Power up generators throughout the level. Generally, you start with all the powercells you need (but not necessarily)
                 *  Solve  -> Power up a generator
                 *  Solved -> All required generators have been powered by a cell
                 */
                case eWardenObjectiveType.PowerCellDistribution:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.PowerCellDistribution,
                        sub_objective_count = obj.PowerCellsToDistribute,
                        positions = MakePositions(objective.ZonePlacementDatas, obj.PowerCellsToDistribute),
                    };
                    lData.objectives.Add(oData);

                    // Add starting cells
                    lData.keys.AddRange(
                        Enumerable.Repeat(new KeyData() { type = KeyData.eType.Cell, positions = new(1) { new ZonePosition(0, 0), } }, obj.PowerCellsToDistribute)
                    );

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;

                /* Complete all required terminal uplinks
                 *  Solve  -> Complete an uplink
                 *  Solved -> Complete all uplinks
                 */
                case eWardenObjectiveType.TerminalUplink:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.TerminalUplink,
                        sub_objective_count = obj.Uplink_NumberOfTerminals,
                        positions = MakePositions(objective.ZonePlacementDatas, obj.Uplink_NumberOfTerminals),
                    };
                    lData.objectives.Add(oData);

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;

                /* Fill the required number of generators in the cluster with cells, typically gathered from throughout the level
                 *  Solve  -> Placing a cell in a gen (in the cluster)
                 *  Solved -> Complete scan after all cells are inserted. 
                 *            If there is no scan, it is (presumably) solved as soon as all required cells are inserted
                 */
                case eWardenObjectiveType.CentralGeneratorCluster:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.CentralGeneratorCluster,
                        sub_objective_count = obj.CentralPowerGenClustser_NumberOfGenerators,
                        positions = new(1),
                    };
                    lData.objectives.Add(oData);

                    // Find and add the gen cluster. We're assuming all gen clusters in the layer are tied to this objective
                    // TODO: Technically, there could be multiple, distinct gen clusters, perhaps paired to multiple objectives? This does not happen in vanilla
                    foreach (var zData in lData.zones)
                    {
                        List<ZonePosition> positions = new(1);
                        if ((zData.originalZone?.GeneratorClustersInZone ?? 0) > 0)
                            positions.Add(new(zData.originalZone!.LocalIndex, 0));
                        oData.positions.Add(positions);
                    }

                    // Add objective-placed cells
                    lData.keys.AddRange(
                        Enumerable.Range(0, obj.CentralPowerGenClustser_NumberOfPowerCells)
                            .Select(i => objective.ZonePlacementDatas[i % objective.ZonePlacementDatas.Count])
                            .Select(p => new KeyData() { type = KeyData.eType.Cell, positions = p.Select(ZonePosition.Make).ToList() })
                    );

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;

                /* Also known as "Process Item". Start with or find a large pickup, carry it to processing machine, and optionally carry it out
                 *  Solve  -> When the item finishes processing
                 *  Solved -> When the item finishes processing, barring "ActivateHSU_ObjectiveCompleteAfterInsertion" is false
                 * ActivateSmallHSU has its own events list, ActivateHSU_Events, which generally fires in place of OnActivateOnSolveItem
                 *  -> OnActivateOnSolveItem still works, however
                 * Also, sometimes (oftentimes?) ActivateHSU_ObjectiveCompleteAfterInsertion is ignored. Not sure what the pattern is
                 */
                case eWardenObjectiveType.ActivateSmallHSU:

                    oData = new()
                    {
                        objective_type = obj.ActivateHSU_ObjectiveCompleteAfterInsertion ? ObjectiveData.eType.ProcessItem : ObjectiveData.eType.ProcessItem_Empty,
                        sub_objective_count = 1,
                        positions = new(1),
                    };
                    lData.objectives.Add(oData);

                    // Processing machine
                    oData.positions.Add(objective.ZonePlacementDatas[0].Select(ZonePosition.Make).ToList());

                    // If we start with the big pickup, add it to the first zone. Else, hope it's in the level somewhere
                    if (obj.ActivateHSU_BringItemInElevator)
                        lData.zones[lData.start_zone].big_pickups.Add(new() { item_type = obj.ActivateHSU_ItemFromStart });

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;

                /* Warden protocol. Survive until the time expires through various events
                 *  Activate -> After the inital grace period expires. Usually events in this list set up fog changes and doors unlocking
                 *  Solved   -> After the survival timer expires.
                 * Similar to SpecialTerminalCommand, OnActivateOnSolveItem has no use here
                 */
                case eWardenObjectiveType.Survival:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.SurviveWardenProtocol,
                        sub_objective_count = 1,
                        positions = new(0)
                    };
                    lData.objectives.Add(oData);

                    ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;

                /* Perform a special terminal command on multiple terminals. A blend of GatherSmallItems and SpecialTerminalCommand
                 *  Solve  -> When the command on the terminal finishes executing. Presumably, requires any relevant scans to be completed
                 *  Solved -> The required number of terminals have had the command run on them
                 * Unlike SpecialTerminalCommand, OnActivateOnSolve is required for the commands to use OnActivate events
                 */
                case eWardenObjectiveType.GatherTerminal:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.GatherTerminals,
                        sub_objective_count = obj.GatherTerminal_RequiredCount,
                        positions = MakePositions(objective.ZonePlacementDatas, obj.GatherTerminal_SpawnCount),
                    };
                    lData.objectives.Add(oData);


                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;

                /* Same as uplink, but codes are sent to a second terminal
                 *  Solve  -> Complete an uplink
                 *  Solved -> Complete all uplinks
                 */
                case eWardenObjectiveType.CorruptedTerminalUplink:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.CorruptedTerminalUplink,
                        sub_objective_count = obj.Uplink_NumberOfTerminals,

                        // Corrupted uplinks always have both termianls in the same zone (or so it seems)
                        positions = MakePositions(objective.ZonePlacementDatas, obj.Uplink_NumberOfTerminals),
                    };
                    lData.objectives.Add(oData);

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, oData);

                    break;
                
                /* An empty objective with no win condition; requires a ForceCompleteObjective or similar event
                 *  Solve  -> None (impossible)
                 *  Solved -> None (impossible)
                 */
                case eWardenObjectiveType.Empty:

                    oData = new()
                    {
                        objective_type = ObjectiveData.eType.Empty,
                        sub_objective_count = 1,
                    };
                    lData.objectives.Add(oData);

                    break;

                /* Hitting init, verify, and confirm on two alternating terminals in quick succession. Typically multiple rounds
                 *  Solve  -> When all timed sequences are completed
                 *  Solved -> When all timed sequences are completed
                 * Timed Sequences have their own sets of events on start, succeed, and fail
                 */
                case eWardenObjectiveType.TimedTerminalSequence:

                    TimedSequenceObjectiveData tsData = new()
                    {
                        objective_type = ObjectiveData.eType.TimedTerminalSequence,
                        sub_objective_count = 1,
                        num_rounds = obj.TimedTerminalSequence_NumberOfRounds,
                        events_on_start_round   = new(Enumerable.Repeat(new List<WardenEvent>(0), obj.TimedTerminalSequence_NumberOfRounds)),
                        events_on_succeed_round = new(Enumerable.Repeat(new List<WardenEvent>(0), obj.TimedTerminalSequence_NumberOfRounds)),
                        events_on_fail_round    = new(Enumerable.Repeat(new List<WardenEvent>(0), obj.TimedTerminalSequence_NumberOfRounds)),

                        // Zone placement data is weird here. I believe the first set is the main terminal, and the following are where each verify terminal is
                        // Further, I believe the order of the verify terminals is randomized; it can choose any of the groups for each verify terminal,
                        //  potentially with the ability to reuse placement groups
                        // We'll process this with the assumption AP will simply require the first zone to start it and all zones from all lists to complete it
                        positions = MakePositions(objective.ZonePlacementDatas, obj.TimedTerminalSequence_NumberOfRounds + 1)
                    };
                    lData.objectives.Add(oData = tsData);

                    for (int i = 0; i < obj.TimedTerminalSequence_NumberOfRounds; i++)
                    {
                        if (obj.TimedTerminalSequence_EventsOnSequenceStart.Count > i)
                            ProcessEvents(tsData.events_on_start_round[i], obj.TimedTerminalSequence_EventsOnSequenceStart[i].Iter());
                        if (obj.TimedTerminalSequence_EventsOnSequenceDone.Count > i)
                            ProcessEvents(tsData.events_on_succeed_round[i], obj.TimedTerminalSequence_EventsOnSequenceDone[i].Iter());
                        if (obj.TimedTerminalSequence_EventsOnSequenceFail.Count > i)
                            ProcessEvents(tsData.events_on_fail_round[i], obj.TimedTerminalSequence_EventsOnSequenceFail[i].Iter());
                    }

                    if (obj.OnActivateOnSolveItem)
                        ProcessOnActivateEvents(obj.EventsOnActivate, tsData);
                    
                    break;

                default:
                    Plugin.Get().Log.LogError($"Unrecognized objective type: {obj.Type}");
                    continue;
            }

            // Only add events triggered immediately after completion; on scan events will be associated with the level instead
            if (obj.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.OnObjectiveCompleted)
                ProcessEvents(oData.events_on_goto_win, obj.EventsOnGotoWin.Iter());
        }
    }

    // Helper to make zone positions from placement lists and a number of needed position lists
    public static List<List<ZonePosition>> MakePositions(Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<ZonePlacementData>> placements, int count)
    {
        return Enumerable.Range(0, count)                         // For each placement needed
            .Select(i => placements[i % placements.Count])        // Limit to available placement lists
            .Select(ps => ps.Select(ZonePosition.Make).ToList())  // And convert to a zone placment list
            .ToList();
    }

    // Given an event source and list of events, populate event data
    public static void ProcessEvents(IList<WardenEvent> eventList, IEnumerable<WardenObjectiveEventData> events)
    {
        foreach (var ev in events)
        {
            if (TryMakeEventAction(ev, out var type, out var data))
                eventList.Add(new(type, data));
        }
    }

    // Helper which processes objective OnActivate events into lists for objective data
    public static void ProcessOnActivateEvents(Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> events, ObjectiveData oData)
    {
        var eventChains = events.Split(e => e.Type == eWardenObjectiveEventType.EventBreak);
        foreach (var subEventList in eventChains)
        {
            List<WardenEvent> list = new();
            ProcessEvents(list, subEventList);
            oData.events_on_activate.Add(list);
        }
    }

    // Look at an event and add it to level data as needed. Returns false if there is no applicable action
    public static bool TryMakeEventAction(WardenObjectiveEventData ev, out WardenEvent.eType type, [NotNullWhen(true)] out WardenEvent.Action? data)
    {
        type = 0;
        data = null;

        switch (ev.Type)
        {
            case eWardenObjectiveEventType.UnlockSecurityDoor:
            case eWardenObjectiveEventType.OpenSecurityDoor:
                data = new SpecificZoneEventAction()
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
                data = new ObjectiveEventAction()
                {
                    objective_layer = (int)ev.Layer
                };
                break;

            case eWardenObjectiveEventType.DimensionWarpTeam:
                data = new WarpEventAction()
                {
                    target_dimension_index = (int)ev.DimensionIndex,
                    target_zone_local_index = (int)ev.LocalIndex,
                };
                break;

            case eWardenObjectiveEventType.ActivateChainedPuzzle:
                data = new StartScanEventAction()
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
            eWardenObjectiveEventType.UnlockSecurityDoor       => WardenEvent.eType.UnlockZoneDoor,
            eWardenObjectiveEventType.OpenSecurityDoor         => WardenEvent.eType.OpenZoneDoor,
            eWardenObjectiveEventType.StepProgressionObjective => WardenEvent.eType.StepObjectiveProgression,
            eWardenObjectiveEventType.ForceCompleteObjective   => WardenEvent.eType.ForceCompleteObjective,
            eWardenObjectiveEventType.ForceInstantWin          => WardenEvent.eType.ForceInstantWin,
            eWardenObjectiveEventType.WinOnDeath               => WardenEvent.eType.ActivateWinOnDeath,
            eWardenObjectiveEventType.DimensionWarpTeam        => WardenEvent.eType.DimensionWarp,
            eWardenObjectiveEventType.ActivateChainedPuzzle    => WardenEvent.eType.StartScan,

            _ => throw new InvalidOperationException("Unexpected invalid event type in TryMakeEventAction!"), // Make the compiler happy
        };
        return true;
    }


}
