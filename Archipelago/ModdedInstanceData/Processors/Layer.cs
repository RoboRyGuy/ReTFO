
using GameData;
using LevelGeneration;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class Layer
{
    // Interface class passed to processing giving access to necessary data
    public abstract class Data : Expedition.Data
    {
        // Minimal implementation
        public abstract Expedition.Data ExpeditionData { get; }
        public abstract LayerType LayerType { get; }

        // Names
        public virtual string LayerName => $"{ExpeditionName} ({LayerType.GetName()})";
        public virtual string ObjectiveStartRegionName => $"{ExpeditionName} Elevator Landed";
        public virtual int ObjectiveStartRegion => GetOrCreateRegion(ObjectiveStartRegionName);

        // Other useful things to have //

        // Layer data for this layer (objectives, etc). null if layer has no layer data (ie a dimension)
        public virtual LayerData? LayerDatas
           => LayerType.IsMainLayer ? Expedition.MainLayerData
            : LayerType.IsSecondaryLayer ? Expedition.SecondaryLayerData
            : LayerType.IsOverloadLayer ? Expedition.ThirdLayerData
            : null;

        // Build from data for this layer, if secondary or overload; null otherwise
        public virtual BuildLayerFromData? BuildFromData
           => LayerType.IsSecondaryLayer ? Expedition.BuildSecondaryFrom
            : LayerType.IsOverloadLayer ? Expedition.BuildThirdFrom
            : null;

        // Dimenion entry in the expedition; contains the index, dimension datablock id, and the bool "Enabled". Null if not a dimension
        public virtual DimensionInExpeditionData? DimensionEntry
           => LayerType.IsReality ? null
            : Expedition.DimensionDatas.FirstOrDefault(d => d.DimensionIndex == LayerType);

        // Dimension datablock entry; this skips the datablock itself and gives you the dimension data it wraps. Null if not a dimension
        public virtual DimensionData? DimensionData
           => DimensionDataBlock.GetBlock(DimensionEntry?.DimensionData ?? 0)?.DimensionData;

        // Level layout datablock ID for this layer; defaults to 0 if no layout is found (dimension with a single special zone, usually)
        public virtual uint LayoutID
           => LayerType.IsMainLayer ? Expedition.LevelLayoutData
            : LayerType.IsSecondaryLayer ? Expedition.SecondaryLayout
            : LayerType.IsOverloadLayer ? Expedition.ThirdLayout
            : DimensionData?.LevelLayoutData ?? 0u;

        // Level layout datablock for this layer; can be null if this is a dimension with one special zone in it
        public virtual LevelLayoutDataBlock? Layout
           => LevelLayoutDataBlock.GetBlock(LayoutID);

        // Get data for a loaded layer
        public static Data FromLayer(LG_Layer layer)
        {
            var expedition = FromCurrentExpedition(); // Expedition.Data.FromCurrentExpedition()
            LayerType type = new(layer.m_dimension.DimensionIndex, layer.m_type);
            return new BaseData(expedition, type);
        }

        // Get the actual layer object if loaded
        public LG_Layer? GetLG_Layer()
        {
            if (!ExpeditionData.IsCurrentExepdition())
                return null;
            if (!Dimension.GetDimension(LayerType, out var dim))
                return null;
            return dim.GetLayer(LayerType);
        }

        // Implementing Expedition.Data
        public override Game.Data GameData => ExpeditionData.GameData;
        public override RundownDataBlock Rundown => ExpeditionData.Rundown;
        public override eRundownTier ExpeditionTier => ExpeditionData.ExpeditionTier;
        public override int ExpeditionIndex => ExpeditionData.ExpeditionIndex;
    }

    // Minimal concrete implementation of Data
    protected class BaseData : Data
    {
        // Standard constructor
        public BaseData(Expedition.Data expeditionData, LayerType layerType)
        {
            if (expeditionData is Data layerData)
                this.expeditionData = layerData.ExpeditionData;
            else
                this.expeditionData = expeditionData;
            this.layerType = layerType;
        }

        // Copy constructor
        public BaseData(Data source)
        {
            expeditionData = source.ExpeditionData;
            layerType = source.LayerType;
        }

        // Concretes
        private readonly Expedition.Data expeditionData;
        private readonly LayerType layerType;

        // Interface implementation
        public override Expedition.Data ExpeditionData => expeditionData;
        public override LayerType LayerType => layerType;
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

        // Helper so this can be created inline and also be registered to an expedition processor
        public Processor SubscribedTo(Expedition.Processor owner)
        {
            owner.RegisterCallback(OnProcessExpedition);
            return this;
        }

        // Callback to initiate processing when processing an expedition
        protected void OnProcessExpedition(Expedition.Data data)
        {
            foreach (var layer in data.AllLayers) Process(layer);
        }
    }

    extension(Game.Data gameData)
    {
        public Processor LayerProcessor
            => (Processor)gameData.GetProcessor<Data>();
    }

    extension(Expedition.Data expeditionData)
    {
        public bool HasMain => true;
        public bool HasSecondary => expeditionData.Expedition.SecondaryLayerEnabled;
        public bool HasOverload => expeditionData.Expedition.ThirdLayerEnabled;

        public Data MainLayer
            => new BaseData(expeditionData, LayerType.Main);

        public IEnumerable<Data> RealLayers
        {
            get
            {
                yield return new BaseData(expeditionData, LayerType.Main);
                if (expeditionData.HasSecondary) yield return new BaseData(expeditionData, LayerType.Secondary);
                if (expeditionData.HasOverload) yield return new BaseData(expeditionData, LayerType.Overload);
            }
        }

        public IEnumerable<Data> DimensionLayers
        {
            get
            {
                foreach (var entry in expeditionData.Expedition.DimensionDatas)
                    yield return new BaseData(expeditionData, entry.DimensionIndex);
            }
        }

        public IEnumerable<Data> AllLayers
            => expeditionData.RealLayers.Concat(expeditionData.DimensionLayers);

        public Data GetLayer(LayerType layerType)
            => new BaseData(expeditionData, layerType);
    }
}
