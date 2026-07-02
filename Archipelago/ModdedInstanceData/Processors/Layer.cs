using GameData;
using LevelGeneration;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Layer
{
    private record class ScopeData
    {
        public ScopeData(LayerType layerType) => LayerType = layerType;
        public LayerType LayerType { get; init; }
    }

    /// <summary>
    /// Class passed to processing to provided relevant data
    /// </summary>
    public class Data : Expedition.Data
    {
        /// <summary>
        /// The custom data stored in the region object for this data
        /// </summary>
        private readonly ScopeData LayerScopeData;

        /// <summary>
        /// The region associated with this layer
        /// </summary>
        public RegionID Region_Layer { get; private init; }

        /// <summary>
        /// Construct a new Layer.Data
        /// </summary>
        /// <param name="data">The expedition data for the expedition containing this layer</param>
        /// <param name="layerType">The type of layer this data is for (in the expedition)</param>
        public Data(Expedition.Data data, LayerType layerType)
            : base(data)
        {
            string name = $"{data.ExpeditionName} ({layerType.GetName()})";
            Region_Layer = data.Regions.LookUpOrCreate(
                data, name,
                data => new("A region for a particular layer for an expedition", data.Region_Expedition)
            );
            if (!Regions.LookUpValue(Region_Layer).GetDataAllowNull(out LayerScopeData!))
                data.Regions.SetData(Region_Layer, LayerScopeData = new ScopeData(layerType));
        }

        /// <summary>
        /// Constructor for constructing from an existing region's data.
        /// This can be invoked if you're reasonably confident the ID is a valid layer region.
        /// </summary>
        public Data(Game.Data data, RegionID region)
            : base(data, data.Regions.LookUpDefinition(region).Parent)
        {
            Region_Layer = region;
            LayerScopeData = data.Regions.GetData<ScopeData>(region);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Data(Layer.Data other)
            : base(other)
        {
            Region_Layer = other.Region_Layer;
            LayerScopeData = other.LayerScopeData;
        }

        /// <summary>
        /// The layer type
        /// </summary>
        public LayerType LayerType => LayerScopeData.LayerType;

        /// <summary>
        /// Name uniquely identifying this layer in a user-friendly way
        /// </summary>
        public string LayerName => Regions.LookUpName(Region_Layer);

        // Other useful things to have //

        /// <summary>
        /// Layer data for this layer (objectives, etc). null if layer has no layer data (ie a dimension)
        /// </summary>
        public virtual LayerData? LayerDatas
           => LayerType.IsMainLayer ? Expedition.MainLayerData
            : LayerType.IsSecondaryLayer ? Expedition.SecondaryLayerData
            : LayerType.IsOverloadLayer ? Expedition.ThirdLayerData
            : null;

        /// <summary>
        /// Build from data for this layer, if secondary or overload; null otherwise
        /// </summary>
        public virtual BuildLayerFromData? BuildFromData
           => LayerType.IsSecondaryLayer ? Expedition.BuildSecondaryFrom
            : LayerType.IsOverloadLayer ? Expedition.BuildThirdFrom
            : null;

        /// <summary>
        /// Dimension entry in the expedition; contains the index, dimension datablock id, and the bool "Enabled". Null if not a dimension
        /// </summary>
        public virtual DimensionInExpeditionData? DimensionEntry
           => LayerType.IsReality ? null
            : Expedition.DimensionDatas.FirstOrDefault(d => d.DimensionIndex == LayerType);

        /// <summary>
        /// Dimension datablock entry; this skips the datablock itself and gives you the dimension data it wraps. Null if not a dimension
        /// </summary>
        public virtual DimensionData? DimensionData
           => DimensionDataBlock.GetBlock(DimensionEntry?.DimensionData ?? 0)?.DimensionData;

        /// <summary>
        /// Level layout datablock ID for this layer; defaults to 0 if no layout is found (dimension with a single special zone, usually)
        /// </summary>
        public virtual uint LayoutID
           => LayerType.IsMainLayer ? Expedition.LevelLayoutData
            : LayerType.IsSecondaryLayer ? Expedition.SecondaryLayout
            : LayerType.IsOverloadLayer ? Expedition.ThirdLayout
            : DimensionData?.LevelLayoutData ?? 0u;

        /// <summary>
        /// Level layout datablock for this layer; can be null if this is a dimension made from one custom geo
        /// </summary>
        public virtual LevelLayoutDataBlock? Layout
           => LevelLayoutDataBlock.GetBlock(LayoutID);

        /// <summary>
        /// Create layer data from the in-level layer component.
        /// Throws on fail, since loaded layer should always have registered data.
        /// </summary>
        public static Layer.Data GetFromLayer(LG_Layer layer)
        {
            Expedition.Data expedition = GetFromCurrentExpedition();
            return expedition.GetLayer(new LayerType(layer.m_dimension.DimensionIndex, layer.m_type));
        }

        /// <summary>
        /// Create layer data from the in-level layer component.
        /// If the in-level layer is not in reality, instead returns the main layer.
        /// Used in certain objective processing to find the objective's origin layer.
        /// </summary>
        public static Layer.Data GetFromLayerFlattened(LG_Layer layer)
        {
            if (layer.m_dimension.DimensionIndex != eDimensionIndex.Reality)
                return GetFromCurrentExpedition().MainLayer;
            else 
                return GetFromLayer(layer);
        }

        /// <summary>
        /// Assuming the correct layer is loaded in, get the LG_Layer component
        ///  corresponding to this data's layer
        /// </summary>
        public LG_Layer GetLG_Layer()
        {
            if (!TryGetFromCurrentExpedition(out Expedition.Data? result) || !result.Region_Expedition.Equals(Region_Expedition))
                throw new NullReferenceException("Cannot fetch LG_Layer; expedition is not loaded!");
            if (!Dimension.GetDimension(LayerType, out Dimension? dimension))
                throw new NullReferenceException("Failed to fetch layer's dimension from loaded expedition!");
            return dimension.GetLayer(LayerType);
        }
    }

    /// <summary>
    /// Attribute used to mark static functions which should autoregister to this processor.
    /// </summary>
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

        // Helper so this can be created inline and also be registered to an expedition processor
        public Processor SubscribedTo(MidManager.Processor<Expedition.Data> owner)
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
            => (Processor)gameData.Manager.GetProcessor<Data>();
    }

    extension(Expedition.Data expeditionData)
    {
        public bool HasMain => true;
        public bool HasSecondary => expeditionData.Expedition.SecondaryLayerEnabled;
        public bool HasOverload => expeditionData.Expedition.ThirdLayerEnabled;

        public Data MainLayer => new Data(expeditionData, LayerType.Main);

        public IEnumerable<Data> RealLayers
        {
            get
            {
                yield return new Data(expeditionData, LayerType.Main);
                if (expeditionData.HasSecondary) yield return new Data(expeditionData, LayerType.Secondary);
                if (expeditionData.HasOverload) yield return new Data(expeditionData, LayerType.Overload);
            }
        }

        public IEnumerable<Data> DimensionLayers
            => expeditionData.Expedition.DimensionDatas
                .Where(e => e.DimensionIndex != eDimensionIndex.ARENA_DIMENSION)
                .Select(e => new Data(expeditionData, e.DimensionIndex));

        public IEnumerable<Data> AllLayers
            => expeditionData.RealLayers.Concat(expeditionData.DimensionLayers);

        public Data GetLayer(LayerType layerType)
            => new Data(expeditionData, layerType);
    }
}
