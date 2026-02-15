using System;
using System.Collections.Generic;
using GameData;
using LevelGeneration;
using UnityEngine;

namespace ReTFO.Archipelago.ModdedInstanceData2;

public class ProcessExpedition
{
    // Data passed to ProcessExpedition.Event
    public class Data
    {
        // Standard constructor
        public Data(RundownDataBlock rundown, eRundownTier tier, int indexInTier)
        {
            Rundown = rundown;
            Tier = tier;
            IndexInTier = indexInTier;
        }

        // Copy constructor
        public Data(Data source)
        {
            Rundown = source.Rundown;
            Tier = source.Tier;
            IndexInTier = source.IndexInTier;
        }

        public RundownDataBlock Rundown { get; init; }
        public eRundownTier Tier { get; init; }
        public int IndexInTier { get; init; }

        public ExpeditionInTierData Expedition => GetExpedition();
        public ExpeditionInTierData GetExpedition()
            => Tier switch
            {
                eRundownTier.TierA => Rundown.TierA[IndexInTier],
                eRundownTier.TierB => Rundown.TierB[IndexInTier],
                eRundownTier.TierC => Rundown.TierC[IndexInTier],
                eRundownTier.TierD => Rundown.TierD[IndexInTier],
                eRundownTier.TierE => Rundown.TierE[IndexInTier],
                _ => throw new NotImplementedException()
            };

        public string ExpeditionName => GetExpeditionName();
        public string GetExpeditionName() => Expedition.GetShortName(IndexInTier);

        public ProcessZone.Data FindZoneExact(eDimensionIndex dimension, LG_LayerType layer, eLocalZoneIndex index) 
            => FindZoneExact(new LayerType(dimension, layer), index);
        public ProcessZone.Data FindZoneExact(LayerType layer, eLocalZoneIndex index) => new ProcessLayer.Data(this, layer).FindZoneByIndex(index);
        public ProcessZone.Data FindZoneByEvent(WardenObjectiveEventData ev) => FindZoneExact(ev.DimensionIndex, ev.Layer, ev.LocalIndex);

        public string NotAnItem => $"{ExpeditionName} NotAnItem"; // Impossible to obtain item prevents traversal, etc
        public string BulkheadKeyName => $"{ExpeditionName} Bulkhead Key";
        public string BigPickupName(ItemDataBlock item) => $"{ExpeditionName} {item.terminalItemShortName}";
        public string CellName => $"{ExpeditionName} CELL";
        public string CustomScanName(string worldEventObjectFilter) => $"{ExpeditionName} Start Scan {worldEventObjectFilter}";
        public string ExtractionRegionName => $"{ExpeditionName} Extraction";
        public string ExtractionReachableName => $"{ExpeditionName} Extraction Reachable";

        protected Dictionary<string, ProcessZone.Data>? worldEventObjectLocations = null;
        public ProcessZone.Data FindWorldEventObjectZone(string objectFilter)
        {
            if (worldEventObjectLocations == null)
            {
                worldEventObjectLocations = new();

                void CheckGeo(string geo, LayerType layer, ExpeditionZoneData? zone)
                {
                    GameObject go = AssetShards.AssetShardManager.GetLoadedAsset<GameObject>(geo);
                    foreach (var comp in go.GetComponentsInChildren<LG_WorldEventObject>())
                    {
                        ProcessZone.Data data = new(new ProcessLayer.Data(this, layer), zone);
                        worldEventObjectLocations.Add(comp.gameObject.name, data);
                    }
                }

                void CheckLayout(uint id, LayerType layer)
                {
                    LevelLayoutDataBlock layout = LevelLayoutDataBlock.GetBlock(id);
                    foreach (var zone in layout.Zones)
                    {
                        if ((zone.CustomGeomorph?.Length ?? 0) > 0)
                            CheckGeo(zone.CustomGeomorph!, layer, zone);
                    }
                }

                CheckLayout(Expedition.LevelLayoutData, LayerType.Main);
                if (Expedition.SecondaryLayerEnabled) CheckLayout(Expedition.SecondaryLayout, LayerType.Secondary);
                if (Expedition.ThirdLayerEnabled) CheckLayout(Expedition.ThirdLayout, LayerType.Overload);
                foreach (var dim in Expedition.DimensionDatas)
                {
                    DimensionDataBlock dd = DimensionDataBlock.GetBlock(dim.DimensionData);
                    if (dd.DimensionData.LevelLayoutData != 0) CheckLayout(dd.DimensionData.LevelLayoutData, dim.DimensionIndex);
                    else CheckGeo(dd.DimensionData.DimensionGeomorph, dim.DimensionIndex, null);
                }
            }
            return worldEventObjectLocations[objectFilter];
        }
    }

    public ProcessExpedition() { Manager.RegisterStaticCallbacks<Callback, Delegate>(d => Event += d); }

    // Delegate type for the event
    public delegate void Delegate(Manager manager, ProcessExpedition.Data data);

    // Event for this instance
    public event Delegate? Event = null;

    // Allow anyone to invoke this event
    public void Invoke(Manager manager, ProcessExpedition.Data data) => Event?.Invoke(manager, data);

    // Attribute used to mark static functions which should autoregister to this event
    [AttributeUsage(AttributeTargets.Method)] public class Callback : Attribute { }

    // Invoke the event when processing managed instance data
    internal void OnProcessExpedition(Manager manager, ProcessExpedition.Data data)
    {
        Event?.Invoke(manager, data);
    }
}
