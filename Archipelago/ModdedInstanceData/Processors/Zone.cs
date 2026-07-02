using GameData;
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using DoublePlacementList = Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<GameData.ZonePlacementData>>;
using PlacementList = Il2CppSystem.Collections.Generic.List<GameData.ZonePlacementData>;
using RegionInfo = ReTFO.Archipelago.ModdedInstanceData.Processors.Game.Data.RegionInfo;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Zone
{
    private record class ScopeData
    {
        public ScopeData(ExpeditionZoneData? zone) => Zone = zone;
        public ExpeditionZoneData? Zone { get; init; }
    }

    // Interface class passed to processing giving access to necessary data
    public class Data : Layer.Data
    {
        /// <summary>
        /// The custom data stored in the region object for this data
        /// </summary>
        private readonly ScopeData ZoneScopeData;

        /// <summary>
        /// The region associated with this zone
        /// </summary>
        public RegionID Region_Zone { get; private init; }

        /// <summary>
        /// Create a new zone data
        /// </summary>
        /// <param name="data">The layer containing this zone</param>
        /// <param name="zone">The zone build data</param>
        public Data(Layer.Data data, ExpeditionZoneData? zone)
            : base(data)
        {
            string name = $"{LayerName} ZONE_{GetAlias(this, zone)}";
            Region_Zone = Regions.LookUpOrCreate(
                data, name,
                data => new("A region for a particular zone in a layer", data.Region_Layer)
            );
            if (!Regions.LookUpValue(Region_Zone).GetDataAllowNull(out ZoneScopeData!))
                Regions.SetData(Region_Zone, ZoneScopeData = new(zone));
        }

        /// <summary>
        /// Constructor for constructing from an existing region's data.
        /// This can be invoked if you're reasonably confident the ID is a valid zone region.
        /// </summary>
        public Data(Game.Data data, RegionID region)
            : base(data, data.Regions.LookUpDefinition(region).Parent)
        {
            Region_Zone = region;
            ZoneScopeData = Regions.GetData<ScopeData>(Region_Zone);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Data(Zone.Data other)
            : base(other)
        {
            Region_Zone = other.Region_Zone;
            ZoneScopeData = other.ZoneScopeData;
        }

        /// <summary>
        /// Data for the zone refered to by this data.
        /// This may be null if the zone is the only zone in a dimension.
        /// </summary>
        public ExpeditionZoneData? Zone => ZoneScopeData.Zone;

        /// <summary>
        /// A name uniquely identifying this zone in a user-friendly way
        /// </summary>
        /// <remarks>
        /// The generated name assumes all zones in a layer have a unique alias.
        /// If this is not the case, this name will not be unique and many problemss will arise.
        /// </remarks>
        public string ZoneName => Regions.LookUpName(Region_Zone);

        /// <summary>
        /// Helper for calculating aliases
        /// </summary>
        private static int WithOverride(int alias, int over) => over == -1 ? alias : over;

        /// <summary>
        /// Get the alias for a particular zone in the provided layer
        /// </summary>
        private static int GetAlias(Layer.Data layer, ExpeditionZoneData? zone)
           => zone != null ? WithOverride(layer.Layout!.ZoneAliasStart + ((int)zone.LocalIndex), zone.AliasOverride)
                           : WithOverride((layer.Layout?.ZoneAliasStart ?? 0), layer.DimensionData?.StaticAliasOverride ?? 0);

        /// <summary>
        /// Get the alias for this zone
        /// </summary>
        public int ZoneAlias => GetAlias(this, Zone);
        public string? CustomGeo
            => Zone == null ? DimensionData!.DimensionGeomorph : Zone.CustomGeomorph;

        /// <summary>
        /// Create zone data from the in-level zone component.
        /// Throws on fail, since loaded zones should always have registered data.
        /// </summary>
        public static Zone.Data GetFromZone(LG_Zone zone)
        {
            Layer.Data layer = Layer.Data.GetFromLayer(zone.Layer);
            return layer.FindZoneByIndex(zone.LocalIndex);
        }

        /// <summary>
        /// Assuming the correct expedition is loaded in, get the LG_Zone component
        ///  corresponding to this data's zone
        /// </summary>
        /// <returns></returns>
        public LG_Zone? GetLG_Zone()
        {
            LG_Layer layer = GetLG_Layer();
            int entry = layer.m_zonesByLocalIndex.FindEntry(Zone?.LocalIndex ?? eLocalZoneIndex.Zone_0);
            if (entry < 0)
                throw new NullReferenceException("Could not fetch LG_Zone: No such zone exists!");
            return layer.m_zonesByLocalIndex.entries[entry].value;
        }
    }

    // Attribute used to mark static functions which should autoregister to this processor
    [AttributeUsage(AttributeTargets.Method)]
    public class Callback : MidManager.Processor<Data>.Callback { }

    // Actual class wrapping an event processing instance
    public class Processor : MidManager.Processor<Data>
    {
        protected event Delegate? Event = null;

        public override void RegisterCallback(Delegate callback)
            => Event += callback;

        public override void UnregisterCallback(Delegate callback)
            => Event -= callback;

        public override void Process(Data data)
            => Event?.Invoke(data);

        public Processor SubscribedTo(MidManager.Processor<Layer.Data> owner)
        {
            owner.RegisterCallback(OnProcessLayer);
            return this;
        }

        // Callback to initiate processing when processing a layer
        protected void OnProcessLayer(Layer.Data data)
        {
            if (data.Layout != null)
                foreach (var zone in data.Layout.Zones) Process(new Data(data, zone));
            else
                Process(new Data(data, null));
        }
    }

    extension(Game.Data gameData)
    {
        public Processor ZoneProcessor
            => (Processor)gameData.Manager.GetProcessor<Data>();
    }

    extension(Expedition.Data expeditionData)
    {
        /// <summary>
        /// First zone in an expedition
        /// </summary>
        public Data StartingZone
            => expeditionData.MainLayer.FirstZone;

        /// <summary>
        /// Find a zone using event data
        /// </summary>
        /// <param name="ev">Event data targetting a zone-based event</param>
        /// <returns>The relevant zone data</returns>
        public Data FindZoneByEvent(WardenObjectiveEventData ev)
            => expeditionData.FindZoneExact(ev.DimensionIndex, ev.Layer, ev.LocalIndex);

        /// <summary>
        /// Find a zone in an expedition using its full coordinates
        /// </summary>
        /// <param name="dimension">The dimension containing the zone</param>
        /// <param name="layer">The layer of the zone in that dimension (ignored for non-reality dimensions)</param>
        /// <param name="zoneIndex">The local index of the zone</param>
        /// <returns>The found zone data</returns>
        public Data FindZoneExact(eDimensionIndex dimension, LG_LayerType layer, eLocalZoneIndex zoneIndex)
            => expeditionData.FindZoneExact(new LayerType(dimension, layer), zoneIndex);

        /// <summary>
        /// Find a zone in an expedition using its full coordinates
        /// </summary>
        /// <param name="layerType">The LayerType of the zone</param>
        /// <param name="zoneIndex">The local index of the zone</param>
        /// <returns></returns>
        public Data FindZoneExact(LayerType layerType, eLocalZoneIndex zoneIndex)
            => expeditionData.GetLayer(layerType).FindZoneByIndex(zoneIndex);
    }

    extension(Layer.Data layerData)
    {
        /// <summary>
        /// The first zone in the layer, which will connect to previous layers or will be the elevator drop zone
        /// </summary>
        public Data FirstZone
        {
            get
            {
                if (layerData.Layout != null)
                {
                    if ((layerData.Layout.Zones?.Count ?? 0) > 0)
                        return new Data(layerData, layerData.Layout?.Zones![0]);
                    else
                        FeatureLogger.Error($"Layer has no zones: {layerData.LayerName}");
                }
                else if (!layerData.LayerType.IsDimension)
                    FeatureLogger.Error($"Failed to find layout data for layer: {layerData.LayerName}");
                return new Data(layerData, null);
            }
        }

        /// <summary>
        /// Enumerates all zones in a layer
        /// </summary>
        public IEnumerable<Data> AllZones
            => layerData.Layout == null ? Enumerable.Repeat(new Data(layerData, null), 1)
             : layerData.Layout.Zones.Select(zone => new Data(layerData, zone));

        /// <summary>
        /// Find a zone given a zone placement
        /// </summary>
        /// <param name="placement">The zone placement relative to this layer</param>
        /// <returns>The found zone data</returns>
        public Data FindZoneByPlacement(ZonePlacementData placement)
        {
            // Two edge cases: 
            //  1) If this layer is reality and the placement targets reality, we inherit the layer
            //  2) If this placement targets the same layer as our current layer, we can reuse the layer data
            if (placement.DimensionIndex == eDimensionIndex.Reality && layerData.LayerType.IsReality
                || placement.DimensionIndex == layerData.LayerType
            ) return layerData.FindZoneByIndex(placement.LocalIndex);
            return layerData.GetLayer(placement.DimensionIndex).FindZoneByIndex(placement.LocalIndex);
        }

        /// <summary>
        /// Convert a list of placement lists into zone regions
        /// </summary>
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToZoneRegions(DoublePlacementList placements)
            => layerData.PlacementsToZoneRegions(placements.Iter());

        /// <summary>
        /// Convert an enumerable of placement lists into zone regions
        /// </summary>
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToZoneRegions(IEnumerable<PlacementList> placements)
            => placements.Select(ps => layerData.PlacementsToZoneRegions(ps));

        /// <summary>
        /// Convert a list of placements into zone regions
        /// </summary>
        public IEnumerable<RegionInfo> PlacementsToZoneRegions(PlacementList placements)
            => layerData.PlacementsToZoneRegions(placements.Iter());

        /// <summary>
        /// Convert an enumerable of placements into zone regions
        /// </summary>
        public IEnumerable<RegionInfo> PlacementsToZoneRegions(IEnumerable<ZonePlacementData> placements)
            => placements.Select(p => layerData.PlacementToZoneRegion(p));

        /// <summary>
        /// Convert a single placement into a zone region
        /// </summary>
        public RegionInfo PlacementToZoneRegion(ZonePlacementData placement)
        {
            var zone = layerData.FindZoneByPlacement(placement);
            return new()
            {
                Region = zone.Region_Zone,
                IsBad = (zone.Zone?.ProgressionPuzzleToEnter?.PuzzleType ?? eProgressionPuzzleType.None) != eProgressionPuzzleType.None
            };
        }

        /// <summary>
        /// Find a zone in this layer by its local index property (not its index in the zone array)
        /// </summary>
        public Data FindZoneByIndex(eLocalZoneIndex zoneIndex)
        {
            if (layerData.Layout == null)
            {
                if (zoneIndex != eLocalZoneIndex.Zone_0)
                    FeatureLogger.Warning($"Attempted to find {Enum.GetName(zoneIndex)} in dimension with no layout: {layerData.LayerName}");
                return new Data(layerData, null);
            }

            ExpeditionZoneData? zone = layerData.Layout.Zones.FirstOrDefault(z => z.LocalIndex == zoneIndex);
            if (zone == null)
            {
                FeatureLogger.Error($"Failed to find {Enum.GetName(zoneIndex)} in layer: {layerData.LayerName}");
                return layerData.FirstZone;
            }
            else
                return new Data(layerData, zone);
        }
    }

    extension(Objective.Data objectiveData)
    {
        /// <summary>
        /// Make and unstuff zone region sets for an objective
        /// </summary>
        public IEnumerable<List<RegionID>> ObjectiveToZoneRegionSets(int count)
            => objectiveData.UnstuffPlacements(objectiveData.PlacementsToZoneRegions(objectiveData.ObjectiveData.ZonePlacementDatas), count);
    }
}
