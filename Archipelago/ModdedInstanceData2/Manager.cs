
using GameData;
using LevelGeneration;
using ReTFO.Archipelago.ModdedInstanceData;
using System.Reflection;
using UnityEngine;
using UnityEngine.Bindings;

namespace ReTFO.Archipelago.ModdedInstanceData2;

public class Manager
{
    // Called after processing an expedition
    public event Action<Manager, ProcessExpeditionData>? OnProcessExpedition;

    // Called after processing a layer
    public event Action<Manager, ProcessLayerData>? OnProcessLayer;

    // Called after processing a zone
    public event Action<Manager, ProcessZoneData>? OnProcessZone;

    // Called after processing a terminal
    public event Action<Manager, ProcessTerminalData>? OnProcessTerminal;

    // Called when processing an event source
    public event Action<Manager, ProcessEventSourceData>? OnProcessEventSource;


    protected List<Region>? regions = null;
    public List<Region> Regions => regions ??= new();

    protected List<Location>? locations = null;
    public List<Location> Locations => locations ??= new();

    protected List<Path>? paths = null;
    public List<Path> Paths => paths ??= new();

    protected Dictionary<string, int> RegionMap = new();   // Reverse index

    // Creates and adds a region. Returns its index
    public int AddRegion(string name)
    {
        if (RegionMap.ContainsKey(name))
        {
            Plugin.Get().Log.LogWarning($"Region \"{name}\" already exists!");
            return RegionMap[name];
        }

        int index = Regions.Count;
        RegionMap[name] = index;
        Regions.Add(new Region(name));
        return index;
    }

    // Gets a region by name. Throws if it doesn't exist
    public int GetRegion(string name)
    {
        if (RegionMap.TryGetValue(name, out int index))
            return index;
        else
            throw new KeyNotFoundException($"Region \"{name}\" does not exist!");
    }

    // Gets a region by name. Creates it if it doesn't exist
    public int GetOrCreateRegion(string name)
    {
        if (RegionMap.TryGetValue(name, out int index))
            return index;
        else
            return AddRegion(name);
    }

    // Adds a location. Returns its index
    public int AddLocation(Location location)
    {
        Locations.Add(location);
        return Locations.Count - 1;
    }

    // Add a path object directly
    public int AddPath(Path path)
    {
        Paths.Add(path);
        return Paths.Count - 1;
    }

    // Convenience method for adding a path by region names
    public Path AddPath(string startingRegion, string endingRegion)
    {
        return AddPath(RegionMap[startingRegion], RegionMap[endingRegion]);
    }

    // Convenience method for adding a path by index and region name
    public Path AddPath(int startingRegion, string endingRegion)
    {
        return AddPath(startingRegion, RegionMap[endingRegion]);
    }

    // Convenience method for adding a path by region name and index
    public Path AddPath(string startingRegion, int endingRegion)
    {
        return AddPath(RegionMap[startingRegion], endingRegion);
    }

    // Convenience method for adding a path by index only
    public Path AddPath(int startingRegion, int endingRegion)
    {
        Path path = new()
        {
            starting_region = startingRegion,
            ending_region = endingRegion
        };
        AddPath(path);
        return path;
    }

    // Create a new instance of this manager
    internal Manager()
    {
        BindingFlags bf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var method in GetType().GetMethods(bf).Where(m => m.GetCustomAttributes(typeof(ExpeditionProcessor), false).Length > 0))
        {
            var del = Delegate.CreateDelegate(typeof(Action<Manager, ProcessExpeditionData>), method);
            OnProcessExpedition += (Action<Manager, ProcessExpeditionData>)del;
        }

        foreach (var method in GetType().GetMethods(bf).Where(m => m.GetCustomAttributes(typeof(LayerProcessor), false).Length > 0))
        {
            var del = Delegate.CreateDelegate(typeof(Action<Manager, ProcessLayerData>), method);
            OnProcessLayer += (Action<Manager, ProcessLayerData>)del;
        }

        foreach (var method in GetType().GetMethods(bf).Where(m => m.GetCustomAttributes(typeof(ZoneProcessor), false).Length > 0))
        {
            var del = Delegate.CreateDelegate(typeof(Action<Manager, ProcessZoneData>), method);
            OnProcessZone += (Action<Manager, ProcessZoneData>)del;
        }

        foreach (var method in GetType().GetMethods(bf).Where(m => m.GetCustomAttributes(typeof(TerminalProcessor), false).Length > 0))
        {
            var del = Delegate.CreateDelegate(typeof(Action<Manager, ProcessTerminalData>), method);
            OnProcessTerminal += (Action<Manager, ProcessTerminalData>)del;
        }

        foreach (var method in GetType().GetMethods(bf).Where(m => m.GetCustomAttributes(typeof(EventSourceProcessor), false).Length > 0))
        {
            var del = Delegate.CreateDelegate(typeof(Action<Manager, ProcessEventSourceData>), method);
            OnProcessEventSource += (Action<Manager, ProcessEventSourceData>)del;
        }
    }

    // Create modded instance data
    public ModdedInstanceData CreateData()
    {
        IEnumerable<ProcessExpeditionData> UnpackExpeditions(RundownDataBlock rundown)
        {
            int i;
            for (i = 0; i < rundown.TierA.Count; i++) yield return new ProcessExpeditionData(rundown, rundown.TierA[i], eRundownTier.TierA, i);
            for (i = 0; i < rundown.TierB.Count; i++) yield return new ProcessExpeditionData(rundown, rundown.TierB[i], eRundownTier.TierB, i);
            for (i = 0; i < rundown.TierC.Count; i++) yield return new ProcessExpeditionData(rundown, rundown.TierC[i], eRundownTier.TierC, i);
            for (i = 0; i < rundown.TierD.Count; i++) yield return new ProcessExpeditionData(rundown, rundown.TierD[i], eRundownTier.TierD, i);
            for (i = 0; i < rundown.TierE.Count; i++) yield return new ProcessExpeditionData(rundown, rundown.TierE[i], eRundownTier.TierE, i);
        }

        ModdedInstanceData result = new();

        foreach (var data in RundownDataBlock.GetAllBlocks().SelectMany(UnpackExpeditions))
        {
            regions = new();
            locations = new();
            paths = new();

            OnProcessExpedition?.Invoke(this, data);

            Expedition exp = new()
            {
                name = data.GetName(),
                regions = Regions,
                locations = Locations,
                paths = Paths,
                start_region = GetOrCreateRegion(new ProcessLayerData(data, LG_LayerType.MainLayer, data.Expedition.MainLayerData).GetFirstZoneName())
            };
            result.expeditions.Add(exp);
        }

        return result;
    }

    [AttributeUsage(AttributeTargets.Method)] class ExpeditionProcessor : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] class LayerProcessor: Attribute { }
    [AttributeUsage(AttributeTargets.Method)] class ZoneProcessor : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] class TerminalProcessor: Attribute { }
    [AttributeUsage(AttributeTargets.Method)] class EventSourceProcessor: Attribute { }

    // Layers in an expedition
    [ExpeditionProcessor]
    internal protected static void ProcessExpeditionLayers(Manager manager, ProcessExpeditionData expeditionData)
    {
        ProcessLayerData mainData = new(expeditionData, LayerType.Main, expeditionData.Expedition.MainLayerData);
        manager.OnProcessExpedition?.Invoke(manager, mainData);

        ProcessLayerData secondaryData = new(expeditionData, LayerType.Secondary, expeditionData.Expedition.SecondaryLayerData, expeditionData.Expedition.BuildSecondaryFrom);
        manager.OnProcessExpedition?.Invoke(manager, secondaryData);

        ProcessLayerData overloadData = new(expeditionData, LayerType.Overload, expeditionData.Expedition.ThirdLayerData, expeditionData.Expedition.BuildThirdFrom);
        manager.OnProcessExpedition?.Invoke(manager, overloadData);

        foreach (var dim in expeditionData.Expedition.DimensionDatas)
        {
            ProcessLayerData data = new(expeditionData, dim.DimensionIndex);
            manager.OnProcessExpedition?.Invoke(manager, data);
        }
    }

    // Process zones in a layer
    [LayerProcessor]
    internal protected static void ProcessLayerZones(Manager manager, ProcessLayerData layer)
    {
        LevelLayoutDataBlock? layout = layer.GetLayout();
        if (layout != null)
        {
            foreach (var zone in layout.Zones)
            {
                ProcessZoneData data = new(layer, zone);
                manager.OnProcessZone?.Invoke(manager, data);
            }
        }
        else
        {
            ProcessZoneData data = new(layer, null);
            manager.OnProcessZone?.Invoke(manager, data);
        }
    }

    // Process terminals in a zone
    [ZoneProcessor]
    internal protected static void ProcessZoneTerminals(Manager manager, ProcessZoneData zoneData)
    {
        Il2CppSystem.Collections.Generic.List<TerminalPlacementData> terms;
        if (zoneData.Zone == null)
        {
            DimensionData? dim = zoneData.Layer.GetDimensionData();
            if (dim == null) return;
            if (dim.ForbidTerminalsInDimension) return;
            terms = dim.StaticTerminalPlacements;
        }
        else
        {
            if (zoneData.Zone.ForbidTerminalsInZone) return;
            if (zoneData.Layer.GetDimensionData()?.ForbidTerminalsInDimension ?? false) return;
            terms = zoneData.Zone.TerminalPlacements;
        }

        for (int i = 0; i < terms.Count; i++)
        {
            ProcessTerminalData data = new(zoneData, terms[i], i);
            manager.OnProcessTerminal?.Invoke(manager, data);
        }
    }

    // Connect layers together
    [LayerProcessor]
    internal protected static void AddLayerEntrances(Manager manager, ProcessLayerData layer)
    {
        BuildLayerFromData? buildFromData = layer.GetBuildFromData();
        if (buildFromData == null) return;

        ProcessLayerData sourceLayer = new(layer.ExpeditionData, buildFromData.LayerType);
        Path path = manager.AddPath(
            manager.GetOrCreateRegion(sourceLayer.GetName(buildFromData.Zone)),
            manager.GetOrCreateRegion(layer.GetFirstZoneName())
        );

        LayerData sourceData = sourceLayer.GetLayerData()!;
        if (sourceData.BulkheadDoorControllerPlacements.FirstOrDefault(p => p.ZoneIndex == buildFromData.Zone) != null)
        {   // If there is a bulkhead DC in the zone this layer connects to, we can unlock this zone with a key
            path.required_item = $"{layer.ExpeditionData.GetName()} Bulkhead Key";
            path.required_item_count = 1;
        }
        else
        {   // Can only unlock via an event
            path.required_item = $"NotAnItem";
            path.required_item_count = 0xFF;
        }
        path.alternate_item = $"{layer.GetFirstZoneName()} Unlock Event";
    }

    // Add standard paths between zones
    [ZoneProcessor]
    internal protected static void AddZoneEntances(Manager manager, ProcessZoneData zoneData)
    {
        var layout = zoneData.Layer.GetLayout();
        if (layout == null || zoneData.Zone == null) return; // Dimension with only one zone
        if (zoneData.Zone == layout.Zones[0]) return; // First zone in layout

        Path path = manager.AddPath(
            manager.GetOrCreateRegion(zoneData.Layer.GetName(zoneData.Zone.BuildFromLocalIndex)),
            manager.GetOrCreateRegion(zoneData.GetName())
        );


        // Handle locked doors
        LayerData? layerData = zoneData.Layer.GetLayerData();
        if (layerData?.ZonesWithBulkheadEntrance.Contains(zoneData.Zone.LocalIndex) ?? false)
        {   // This zone is locked by a bulkhead door
            path.required_item = $"{zoneData.ExpeditionData.GetName()} Bulkhead Key";
            path.required_item_count = 1;
        }
        else if (zoneData.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Keycard_SecurityBox)
        {
            path.required_item = $"{zoneData.GetName()} Colored Key";
            path.required_item_count = 1;
            manager.AddLocation(new Location()
            {
                name = $"{zoneData.GetName()} Colored Key Spawn Location",
                item = $"{zoneData.GetName()} Colored Key",
                regions = zoneData.Zone.ProgressionPuzzleToEnter.ZonePlacementData.Select(p => manager.GetOrCreateRegion(zoneData.Layer.GetName(p))).ToList()
            });
        }
        else if (zoneData.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.PowerGenerator_And_PowerCell)
        {
            path.required_item = $"{zoneData.ExpeditionData.GetName()} CELL";
            path.required_item_count = 1;
            manager.AddLocation(new Location()
            {
                name = $"{zoneData.GetName()} CELL Spawn Location",
                item = $"{zoneData.GetName()} CELL",
                regions = zoneData.Zone.ProgressionPuzzleToEnter.ZonePlacementData.Select(p => manager.GetOrCreateRegion(zoneData.Layer.GetName(p))).ToList()
            });
        }
        else if (zoneData.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Locked_No_Key)
        {
            path.required_item = $"NotAnItem";
            path.required_item_count = 0xFF;
        }
        path.alternate_item = $"{zoneData.GetName()} Unlock Event";
    }

    // Add an extraction region to the level by finding the extraction zone and creating a virtual path from it to the extraction region
    [LayerProcessor]
    internal protected static void AddExtraction(Manager manager, ProcessLayerData layer)
    {
        // Extraction is only processed for the main layer
        if (layer.LayerType != LayerType.Main) return;

        int extractionRegion = manager.GetOrCreateRegion($"{layer.ExpeditionData.GetName()} Extraction");
        if (layer.Expedition.MainLayerData.ObjectiveData.WinCondition == eWardenObjectiveWinCondition.GoToExitGeo)
        {   // We'll have to find the extraction zone via complex data
            // Basically, if it's listed in the custom exits section of the complex data, it's an exit
            ComplexResourceSetDataBlock complex = ComplexResourceSetDataBlock.GetBlock(layer.Expedition.Expedition.ComplexResourceData);
            ExpeditionZoneData? zone = null;
            foreach (var z in layer.GetLayout()!.Zones)
            {
                if (z.CustomGeomorph.Length == 0 || z.CustomGeomorph == "")
                    continue;

                if (complex.CustomGeomorphs_Exit_1x1.FirstOrDefault(c => c.Prefab == z.CustomGeomorph) != null)
                {
                    zone = z;
                    break;
                }
            }

            if (zone == null) Plugin.Get().Log.LogError($"Could not find forward exit for layer: {layer.GetName()}");
            else
            {
                manager.AddPath(new ProcessZoneData(layer, zone).GetName(), extractionRegion);
                return;
            }
        }   
        
        // Extraction in first zone
        manager.AddPath(layer.GetFirstZoneName(), extractionRegion);
    }

    // Add big pickups in zones
    [ZoneProcessor]
    internal protected static void AddZoneBigPickups(Manager manager, ProcessZoneData zoneData)
    {
        int zone = manager.GetOrCreateRegion(zoneData.GetName());

        uint id = zoneData.Zone?.BigPickupDistributionInZone ?? zoneData.Layer.GetDimensionData()?.StaticBigPickupDistributionInZone ?? 0u;
        if (id == 0) return;

        BigPickupDistributionDataBlock pickups = BigPickupDistributionDataBlock.GetBlock(id);
        foreach (var spawn in pickups.SpawnData)
        {
            ItemDataBlock item = ItemDataBlock.GetBlock(spawn.ItemID);
            int counter = 0;
            float count = spawn.Weight * pickups.SpawnsPerZone;
            for (int i = 0; i < count; i++)
            {
                manager.AddLocation(new Location
                {
                    name = $"{zoneData.GetName()} Big Pickup {++counter} ({item.terminalItemShortName})",
                    item = $"{zoneData.ExpeditionData.GetName()} {item.terminalItemShortName}",
                    regions = new(1) { zone }
                });
            }
        }
    }

    // Add bulkhead key spawns
    [LayerProcessor]
    internal protected static void AddBulkheadKeys(Manager manager, ProcessLayerData layer)
    {
        LayerData? layerData = layer.GetLayerData();
        if (layerData == null) return;

        for (int i = 0; i < layerData.BulkheadKeyPlacements.Count; i++)
        {
            manager.AddLocation(new Location()
            {
                name = $"{layer.ExpeditionData.GetName()} ({layer.GetName()}) Bulkhead Key Spawn Location {i}",
                item = $"{layer.ExpeditionData.GetName()} Bulkhead Key",
                regions = layerData.BulkheadKeyPlacements[i].Select(p => manager.GetOrCreateRegion(layer.GetName(p))).ToList()
            });
        }
    }

    // Process Zone Events
    [ZoneProcessor]
    internal protected static void AddZoneEvents(Manager manager, ProcessZoneData zoneData)
    {
        ProcessEventSourceData data;
        string name = zoneData.GetName();
        int region = manager.GetOrCreateRegion(name);

        if (zoneData.Zone != null)
        {
            Tuple<string, IEnumerable<WardenObjectiveEventData>>[] pairs =
            {
                Tuple.Create( $"{name} OnApproachZone",            zoneData.Zone.EventsOnApproachDoor.Iter() ),
                Tuple.Create( $"{name} OnBossDeath",               zoneData.Zone.EventsOnBossDeath.Iter() ),
                Tuple.Create( $"{name} OnDoorScanDone",            zoneData.Zone.EventsOnDoorScanDone.Iter() ),
                Tuple.Create( $"{name} OnDoorScanStart",           zoneData.Zone.EventsOnDoorScanStart.Iter() ),
                Tuple.Create( $"{name} OnOpenDoor",                zoneData.Zone.EventsOnOpenDoor.Iter() ),
                Tuple.Create( $"{name} OnPortalWarp",              zoneData.Zone.EventsOnPortalWarp.Iter() ),
                Tuple.Create( $"{name} OnTerminalDeactivateAlarm", zoneData.Zone.EventsOnTerminalDeactivateAlarm.Iter() ),
                Tuple.Create( $"{name} OnUnlockDoor",              zoneData.Zone.EventsOnUnlockDoor.Iter() ),
            };
            foreach (var pair in pairs)
                manager.OnProcessEventSource?.Invoke(manager, new ProcessEventSourceData(zoneData.Layer, pair.Item1, region, pair.Item2));

            // Trigger events need to be sorted to the object which triggers it
            var triggers = zoneData.Zone.EventsOnTrigger.Select(e => e.WorldEventTriggerObjectFilter).Distinct();
            foreach (var trigger in triggers)
            {
                data = new(zoneData.Layer, $"{name} OnTrigger ({trigger})", region, zoneData.Zone.EventsOnTrigger.Where(e => e.WorldEventTriggerObjectFilter == trigger));
                manager.OnProcessEventSource?.Invoke(manager, data);
            }

            // In-zone scans, which are event-triggered scans
            foreach (var scan in zoneData.Zone.WorldEventChainedPuzzleDatas)
            {
                int scanRegion = manager.GetOrCreateRegion($"{zoneData.GetName()} Custom Scan ({scan.WorldEventObjectFilter})");
                manager.AddPath(new Path()
                {
                    starting_region = region,
                    ending_region = scanRegion,
                    required_item = $"Start Scan {scan.WorldEventObjectFilter}",
                    required_item_count = 1,
                    alternate_item = null
                });

                data = new(zoneData.Layer, $"{name} OnCompleteScan ({scan.WorldEventObjectFilter})", scanRegion, scan.EventsOnScanDone.Iter());
                manager.OnProcessEventSource?.Invoke(manager, data);
            }
        }
        else if (zoneData.Layer.GetDimensionData() is not null and DimensionData dim)
        {
            data = new(zoneData.Layer, name, region, dim.EventsOnBossDeath.Iter());
            manager.OnProcessEventSource?.Invoke(manager, data);
        }
    }

    [EventSourceProcessor]
    internal protected static void ProcessUnlockEvents(Manager manager, ProcessEventSourceData eventData)
    {
        int count = 0;
        foreach (var e in eventData.Events)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.UnlockSecurityDoor && e.Type != eWardenObjectiveEventType.OpenSecurityDoor)
                continue;
            count += 1;

            LayerType targetLayer;
            if (e.DimensionIndex == eDimensionIndex.Reality)
                targetLayer = e.Layer;
            else
                targetLayer = e.DimensionIndex;
            string targetZone = new ProcessLayerData(eventData.Layer.ExpeditionData, targetLayer).GetName(e.LocalIndex);

            manager.AddLocation(new Location()
            {
                name = $"{eventData.SourceName} Unlock Event {count} (for {targetZone})",
                item = $"{targetZone} Unlock Event",
                regions = new(1) { eventData.SourceRegion }
            });
        }
    }

    [EventSourceProcessor]
    internal protected static void ProcessWarpEvents(Manager manager, ProcessEventSourceData eventData)
    {
        foreach (var e in eventData.Events)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.DimensionWarpTeam)
                continue;

            LayerType targetLayer;
            if (e.DimensionIndex == eDimensionIndex.Reality)
                targetLayer = e.Layer;
            else
                targetLayer = e.DimensionIndex;
            string targetZone = new ProcessLayerData(eventData.Layer.ExpeditionData, targetLayer).GetName(e.LocalIndex);

            // Warps are simply paths, accessible as long as the event which triggers them is also accessible
            Path path = manager.AddPath(
                eventData.SourceRegion,
                targetZone
            );
        }
    }

    [EventSourceProcessor]
    internal protected static void ProcessCustomScanEvents(Manager manager, ProcessEventSourceData eventData)
    {
        int count = 0;
        foreach (var e in eventData.Events)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ActivateChainedPuzzle)
                continue;
            count += 1;

            manager.AddLocation(new Location()
            {
                name = $"{eventData.SourceName} Start Scan {count} (for {e.WorldEventObjectFilter})",
                item = $"Start Scan {e.WorldEventObjectFilter}",
                regions = new(1) { eventData.SourceRegion }
            });
        }
    }

    [EventSourceProcessor]
    internal protected static void ProcessWinEvents(Manager manager, ProcessEventSourceData eventData)
    {
        int count = 0;
        foreach (var e in eventData.Events)
        { 
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ForceInstantWin && e.Type != eWardenObjectiveEventType.WinOnDeath)
                continue;
            count += 1;

            ProcessLayerData layer = new(eventData.Layer, LayerType.Main);

            manager.AddLocation(new Location()
            {
                name = $"{eventData.SourceName} Instant Win Event {count}",
                item = $"{layer.GetName()} Win",
                regions = new(1) { eventData.SourceRegion }
            });
        }
    }



}
