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
    /// <summary>
    /// Class passed to processing to provided relevant data
    /// </summary>
    public class Data : Expedition.Data
    {
        /// <summary>
        /// The layer type
        /// </summary>
        public LayerType LayerType { get; private init; }

        /// <summary>
        /// Construct a new Layer.Data
        /// </summary>
        /// <param name="data">The expedition data for the expedition containing this layer</param>
        /// <param name="layerType">The type of layer this data is for (in the expedition)</param>
        public Data(Expedition.Data data, LayerType layerType)
            : base(data)
        {
            LayerType = layerType;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Data(Layer.Data other)
            : base(other as Expedition.Data)
        {
            LayerType = other.LayerType;
        }

        /// <summary>
        /// Name uniquely identifying this layer in a user-friendly way
        /// </summary>
        public string LayerName => $"{ExpeditionName} ({LayerType.GetName()})";

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
        /// Get data for a loaded layer
        /// </summary>
        /// <param name="layer">The in-level layer object, typically obtained from a course node</param>
        /// <returns>The relevant layer data</returns>
        public static Data FromLayer(LG_Layer layer)
        {
            var expedition = FromCurrentExpedition(); // Expedition.Data.FromCurrentExpedition()
            LayerType type = new(layer.m_dimension.DimensionIndex, layer.m_type);
            return new Data(expedition, type);
        }

        /// <summary>
        /// Get data for a loaded layer, but only for reality; uses MainLayer if looking at a dimension
        /// </summary>
        /// <param name="layer">The in-level layer object, typically from an objective item's "Origin Layer"</param>
        /// <returns>The relevant layer data</returns>
        /// <remarks>
        /// This is tyipcally used for objective items and similar; from what I can tell, only main objective items
        ///  can spawn in alternate dimensions, and I believe GTFO assumes the same.
        /// </remarks>
        public static Data FromLayerFlattened(LG_Layer layer)
            => layer.m_dimension.DimensionIndex != eDimensionIndex.Reality
                ? FromCurrentExpedition().MainLayer
                : FromLayer(layer);

        /// <summary>
        /// Get the actual layer object if in level
        /// </summary>
        /// <returns>The layer (in in the correct expedition), null otherwise</returns>
        public LG_Layer? GetLG_Layer()
        {
            if (!IsCurrentExepdition())
                return null;
            if (!Dimension.GetDimension(LayerType, out var dim))
                return null;
            return dim.GetLayer(LayerType);
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
