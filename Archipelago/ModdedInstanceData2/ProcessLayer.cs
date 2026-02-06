using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AIGraph;
using GameData;

namespace ReTFO.Archipelago.ModdedInstanceData2;

public class ProcessLayer
{
    // Data passed to ProcessLayer.Event
    public class Data : ProcessExpedition.Data
    {
        // Standard constructor
        public Data(ProcessExpedition.Data expedition, LayerType layerType) : base(expedition)
        {
            LayerType = layerType;
        }

        // Copy constructor
        public Data(Data source) : base(source)
        {
            LayerType = source.LayerType;
        }

        public LayerType LayerType { get; init; }

        public string LayerName => GetLayerName();
        public string GetLayerName() => $"{ExpeditionName} ({LayerType.GetName()})";

        public LayerData? LayerData => GetLayerData();
        public LayerData? GetLayerData()
        {
            return LayerType.value switch
            {
                0 => Expedition.MainLayerData,
                -1 => Expedition.SecondaryLayerData,
                -2 => Expedition.ThirdLayerData,
                _ => null
            };
        }

        public BuildLayerFromData? BuildFromData => GetBuildFromData();
        public BuildLayerFromData? GetBuildFromData()
        {
            return LayerType.value switch
            {
                -1 => Expedition.BuildSecondaryFrom,
                -2 => Expedition.BuildThirdFrom,
                _ => null
            };
        }

        public DimensionData? DimensionData => GetDimensionData();
        public DimensionData? GetDimensionData()
        {
            if (LayerType.IsReality) return null; // Save a list iteration, I suppose
            return DimensionDataBlock.GetBlock(Expedition.DimensionDatas.FirstOrDefault(d => d.DimensionIndex == LayerType)?.DimensionData ?? 0)?.DimensionData;
        }

        public LevelLayoutDataBlock? Layout => GetLayout();
        public LevelLayoutDataBlock? GetLayout()
        {
            uint id;
            if (LayerType.IsMainLayer) id = Expedition.LevelLayoutData;
            else if (LayerType.IsSecondaryLayer) id = Expedition.SecondaryLayout;
            else if (LayerType.IsOverloadLayer) id = Expedition.ThirdLayout;
            else id = DimensionData?.LevelLayoutData ?? 0;
            return LevelLayoutDataBlock.GetBlock(id);
        }

        public int LayerAliasStart => Layout?.ZoneAliasStart ?? 0;
        public int CalcZoneAlias(eLocalZoneIndex index = eLocalZoneIndex.Zone_0) => LayerAliasStart + (int)index;

        public ProcessZone.Data GetFirstZone() => new(this, Layout?.Zones[0]);
        public ProcessZone.Data FindZoneByIndex(eLocalZoneIndex index) => new(this, Layout?.Zones.FirstOrDefault(z => z.LocalIndex == index));
        public ProcessZone.Data FindZoneByPlacement(ZonePlacementData placement)
        {
            if (((LayerType)placement.DimensionIndex).IsReality != LayerType.IsReality)
            {
                ProcessLayer.Data layer = new(this, placement.DimensionIndex); // Reality is assumed to be Main layer
                return layer.FindZoneByIndex(placement.LocalIndex);
            }
            else return FindZoneByIndex(placement.LocalIndex);
        }

        // Helper specifically for converting a list of placements into a list of region ints
        public List<int> PlacementsToZoneRegions(Manager manager, Il2CppSystem.Collections.Generic.List<ZonePlacementData> placements)
            => PlacementsToZoneRegions(manager, placements.Iter());

        // Helper specifically for converting a list of placements into a list of region ints
        public List<int> PlacementsToZoneRegions(Manager manager, IEnumerable<ZonePlacementData> placements)
            => placements.Select(p => manager.GetOrCreateRegion(FindZoneByPlacement(p).ZoneName)).ToList();

        // Helper specifically for converting a list of placements into a list of terminal region ints
        public List<int> PlacementsToTerminalRegions(Manager manager, Il2CppSystem.Collections.Generic.List<ZonePlacementData> placements)
            => PlacementsToTerminalRegions(manager, placements.Iter());

        // Helper specifically for converting a list of placements into a list of terminal region ints
        public List<int> PlacementsToTerminalRegions(Manager manager, IEnumerable<ZonePlacementData> placements)
            => placements.Select(FindZoneByPlacement).SelectMany(
                z => Enumerable.Range(1, z.Zone?.TerminalPlacements.Count ?? z.DimensionData?.StaticTerminalPlacements.Count ?? 0)
                               .Select(i => new ProcessTerminal.Data(z, i).TerminalName)
               ).Select(n => manager.GetOrCreateRegion(n)).ToList();

        public string ObjectiveStartRegionName => $"{LayerName} Objective Start";
        public string CompleteObjectiveName => $"{LayerName} Complete Objective";
        public string InstantWinEventName => $"{LayerName} Instant Win";
        public string ObjeciveGotoWinRegionName => $"{LayerName} Goto Win";
        public string ObjectiveRewardRegionName => $"{LayerName} Objective Complete and Extracted";

    }

    public ProcessLayer() { Manager.RegisterStaticCallbacks<Callback, Delegate>(d => Event += d); }

    // Delegate type for the event
    public delegate void Delegate(Manager manager, ProcessLayer.Data data);

    // Event for this instance
    public event Delegate? Event = null;

    // Allow anyone to invoke this event
    public void Invoke(Manager manager, ProcessLayer.Data data) => Event?.Invoke(manager, data);

    // Attribute used to mark static functions which should autoregister to this event
    [AttributeUsage(AttributeTargets.Method)] public class Callback : Attribute { }

    // Invoke the event when processing an expedition
    internal void OnProcessExpedition(Manager manager, ProcessExpedition.Data data)
    {
        Event?.Invoke(manager, new Data(data, LayerType.Main));
        if (data.Expedition.SecondaryLayerEnabled) Event?.Invoke(manager, new Data(data, LayerType.Secondary));
        if (data.Expedition.ThirdLayerEnabled)     Event?.Invoke(manager, new Data(data, LayerType.Overload));
        foreach (var dimData in data.Expedition.DimensionDatas)
            Event?.Invoke(manager, new Data(data, dimData.DimensionIndex));
    }

    public ProcessLayer RegisteredTo(ProcessExpedition owner)
    {
        owner.Event += OnProcessExpedition;
        return this;
    }
}
