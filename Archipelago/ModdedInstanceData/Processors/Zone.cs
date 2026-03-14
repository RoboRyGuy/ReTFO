using GameData;
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

using DoublePlacementList = Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<GameData.ZonePlacementData>>;
using PlacementList = Il2CppSystem.Collections.Generic.List<GameData.ZonePlacementData>;
using RegionInfo = ReTFO.Archipelago.ModdedInstanceData.Processors.Game.Data.RegionInfo;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Zone
{
    // Interface class passed to processing giving access to necessary data
    public abstract class Data : Layer.Data
    {
        // Minimal interface implementation
        public abstract Layer.Data LayerData { get; }
        public abstract ExpeditionZoneData? Zone { get; }

        // Zone basics
        private int WithOverride(int alias, int over) => over == -1 ? alias : over;
        public virtual int ZoneAlias // Note: Both DimensionData and Zone may be null if referring to the snatcher dimension
           => Zone != null ? WithOverride(Layout!.ZoneAliasStart + ((int)Zone.LocalIndex), Zone.AliasOverride)
                           : WithOverride((Layout?.ZoneAliasStart ?? 0), DimensionData?.StaticAliasOverride ?? 0);
        public virtual string ZoneName => $"{LayerName} ZONE_{ZoneAlias}";
        public virtual string? CustomGeo
            => Zone == null ? DimensionData!.DimensionGeomorph : Zone.CustomGeomorph;

        public static Data FromZone(LG_Zone zone)
        {
            var layer = FromLayer(zone.m_layer);
            return layer.FindZoneByIndex(zone.LocalIndex);
        }

        // Get the LG_Zone associated with this zone, if loaded
        public LG_Zone? GetLG_Zone()
        {
            LG_Layer? layer = GetLG_Layer();
            if (layer == null) return null;
            int entry = layer.m_zonesByLocalIndex.FindEntry(Zone?.LocalIndex ?? eLocalZoneIndex.Zone_0);
            return entry == -1 ? null : layer.m_zonesByLocalIndex.entries[entry].value;
        }

        // Implementing Layer.Data
        public override Expedition.Data ExpeditionData => LayerData.ExpeditionData;
        public override LayerType LayerType => LayerData.LayerType;
    }

    // Minimal concrete implementation of Data
    protected class BaseData : Data
    {
        // Standard constructor
        public BaseData(Layer.Data layerData, ExpeditionZoneData? zone)
        {
            this.layerData = layerData;
            this.zone = zone;
            if (zone == null && layerData.LayerType.IsReality)
                FeatureLogger.Error($"Constructed zone data for null zone in reality. Layer: {LayerName}");
        }

        // Copy constructor
        public BaseData(BaseData source)
        {
            layerData = source.layerData;
            zone = source.zone;
        }

        // Concretes
        private readonly Layer.Data layerData;
        private readonly ExpeditionZoneData? zone;

        // Interface implementation
        public override Layer.Data LayerData => layerData;
        public override ExpeditionZoneData? Zone => zone;
    }

    // Attribute used to mark static functions which should autoregister to this processor
    [AttributeUsage(AttributeTargets.Method)]
    public class Callback : Game.IProcessor<Data>.Callback { }

    // Actual class wrapping an event processing instance
    public class Processor : Game.IProcessor<Data>
    {
        // Public constructor which automatically registers callbacks using helper
        public Processor()
            => RegisterStaticCallbacks();

        protected event Delegate? Event = null;

        public override void RegisterCallback(Delegate callback)
            => Event += callback;

        public override void UnregisterCallback(Delegate callback)
            => Event -= callback;

        public override void Process(Data data)
            => Event?.Invoke(data);

        public Processor SubscribedTo(Layer.Processor owner)
        {
            owner.RegisterCallback(OnProcessLayer);
            return this;
        }

        // Callback to initiate processing when processing a layer
        protected void OnProcessLayer(Layer.Data data)
        {
            if (data.Layout != null)
                foreach (var zone in data.Layout.Zones) Process(new BaseData(data, zone));
            else
                Process(new BaseData(data, null));
        }
    }

    extension(Game.Data gameData)
    {
        public Processor ZoneProcessor
            => (Processor)gameData.GetProcessor<Data>();
    }

    extension(Expedition.Data expeditionData)
    {
        // First zone in an expedition
        public Data StartingZone
            => expeditionData.MainLayer.FirstZone;

        // Region for the first zone
        public int StartingRegion
            => expeditionData.GetOrCreateRegion(expeditionData.StartingZone.ZoneName);

        // Find a zone using event data
        public Data FindZoneByEvent(WardenObjectiveEventData ev)
            => expeditionData.FindZoneExact(ev.DimensionIndex, ev.Layer, ev.LocalIndex);

        // Find a zone in an expedition using its full coordinates
        public Data FindZoneExact(eDimensionIndex dimension, LG_LayerType layer, eLocalZoneIndex zoneIndex)
            => expeditionData.FindZoneExact(new LayerType(dimension, layer), zoneIndex);

        // Find a zone in an expedition using its full coordinates
        public Data FindZoneExact(LayerType layerType, eLocalZoneIndex zoneIndex)
            => expeditionData.GetLayer(layerType).FindZoneByIndex(zoneIndex);
    }

    extension(Layer.Data layerData)
    {
        // The first zone in the layer, which will connect to previous layers or will be the elevator drop zone
        public Data FirstZone
        {
            get
            {
                if (layerData.Layout != null)
                {
                    if ((layerData.Layout.Zones?.Count ?? 0) > 0)
                        return new BaseData(layerData, layerData.Layout?.Zones![0]);
                    else
                        FeatureLogger.Error($"Layer has no zones: {layerData.LayerName}");
                }
                else if (!layerData.LayerType.IsDimension)
                    FeatureLogger.Error($"Failed to find layout data for layer: {layerData.LayerName}");
                return new BaseData(layerData, null);
            }
        }

        // All zones in a layer
        public IEnumerable<Data> AllZones
            => layerData.Layout == null ? Enumerable.Repeat(new BaseData(layerData, null), 1)
             : layerData.Layout.Zones.Select(zone => new BaseData(layerData, zone));

        // Find a zone given a zone placement
        public Data FindZoneByPlacement(ZonePlacementData placement)
        {
            // Two edge cases: 
            //  1) If an event occurs in reality and targets reality, we inherit the layer
            //  2) If an event occurs in the same dimension as our current layer, we can reuse the layer data
            if (placement.DimensionIndex == eDimensionIndex.Reality && layerData.LayerType.IsReality
                || placement.DimensionIndex == layerData.LayerType
            ) return layerData.FindZoneByIndex(placement.LocalIndex);
            return layerData.GetLayer(placement.DimensionIndex).FindZoneByIndex(placement.LocalIndex);
        }

        // Convert a list of placement lists into zone regions
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToZoneRegions(DoublePlacementList placements)
            => layerData.PlacementsToZoneRegions(placements.Iter());

        // Convert an enumerable of placement lists into zone regions
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToZoneRegions(IEnumerable<PlacementList> placements)
            => placements.Select(ps => layerData.PlacementsToZoneRegions(ps));

        // Convert a list of placements into zone regions
        public IEnumerable<RegionInfo> PlacementsToZoneRegions(PlacementList placements)
            => layerData.PlacementsToZoneRegions(placements.Iter());

        // Convert an enumerable of placements into zone regions
        public IEnumerable<RegionInfo> PlacementsToZoneRegions(IEnumerable<ZonePlacementData> placements)
            => placements.Select(p => layerData.PlacementToZoneRegion(p));

        // Convert a single placement into a zone region
        public RegionInfo PlacementToZoneRegion(ZonePlacementData placement)
        {
            var zone = layerData.FindZoneByPlacement(placement);
            return new()
            {
                Region = layerData.GetOrCreateRegion(zone.ZoneName),
                IsBad = (zone.Zone?.ProgressionPuzzleToEnter?.PuzzleType ?? eProgressionPuzzleType.None) != eProgressionPuzzleType.None
            };
        }

        // Find a zone by its local index (not its index in the zone array)
        public Data FindZoneByIndex(eLocalZoneIndex zoneIndex)
        {
            if (layerData.Layout == null)
            {
                if (zoneIndex != eLocalZoneIndex.Zone_0)
                    FeatureLogger.Warning($"Attempted to find {Enum.GetName(zoneIndex)} in dimension with no layout: {layerData.LayerName}");
                return new BaseData(layerData, null);
            }

            ExpeditionZoneData? zone = layerData.Layout.Zones.FirstOrDefault(z => z.LocalIndex == zoneIndex);
            if (zone == null)
            {
                FeatureLogger.Error($"Failed to find {Enum.GetName(zoneIndex)} in layer: {layerData.LayerName}");
                return layerData.FirstZone;
            }
            else
                return new BaseData(layerData, zone);
        }
    }

    extension(Objective.Data objectiveData)
    {
        // Make and unstuff zone region sets for an objective
        public IEnumerable<List<int>> ObjectiveToZoneRegionSets(int count)
            => objectiveData.UnstuffPlacements(objectiveData.PlacementsToZoneRegions(objectiveData.ObjectiveData.ZonePlacementDatas), count);
    }
}
