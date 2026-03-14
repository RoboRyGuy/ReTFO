
using GameData;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Objective
{
    // Interface class passed to processing giving access to necessary data
    public abstract class Data : Layer.Data
    {
        // Minimal implementation
        public abstract Layer.Data LayerData { get; }
        public abstract WardenObjectiveLayerData ObjectiveData { get; }
        public abstract int ObjectiveIndex { get; }

        // Actual objective datablock being processed
        public virtual WardenObjectiveDataBlock Objective
            => WardenObjectiveDataBlock.GetBlock(ObjectiveData.DataBlockId)
            ?? throw new NullReferenceException($"Failed to find objective datablock for layer: {LayerName}");

        // Names
        public string ObjectiveName(string? objectiveTypeSummary)
            => $"{LayerName} Objective #{ObjectiveIndex + 1}{(objectiveTypeSummary == null ? "" : $" ({objectiveTypeSummary})")}";

        // Implementing Layer.Data
        public override Expedition.Data ExpeditionData => LayerData.ExpeditionData;
        public override LayerType LayerType => LayerData.LayerType;
    }

    // Minimal concrete implementation of Data
    protected class BaseData : Data
    {
        // Standard constructor
        public BaseData(Layer.Data layerData, WardenObjectiveLayerData objectiveData, int objectiveIndex)
        {
            this.layerData = layerData;
            this.objectiveData = objectiveData;
            this.objectiveIndex = objectiveIndex;
        }

        // Copy constructor
        public BaseData(BaseData source)
        {
            layerData = source.layerData;
            objectiveData = source.objectiveData;
            objectiveIndex = source.objectiveIndex;
        }

        // Concretes
        private readonly Layer.Data layerData;
        private readonly WardenObjectiveLayerData objectiveData;
        private readonly int objectiveIndex;

        // Interface implementation
        public override Layer.Data LayerData => layerData;
        public override WardenObjectiveLayerData ObjectiveData => objectiveData;
        public override int ObjectiveIndex => objectiveIndex;
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
            if (data.LayerDatas == null) return;

            var objectivesDatas = data.LayerDatas.ChainedObjectiveData.Iter()
                .Prepend(data.LayerDatas.ObjectiveData)
                .Select((d, i) => new BaseData(data, d, i));

            foreach (Data objectiveData in objectivesDatas)
                Process(objectiveData);
        }


    }

    extension(Game.Data gameData)
    {
        public Processor ObjectiveProcessor
            => (Processor)gameData.GetProcessor<Data>();
    }

    extension(Layer.Data layerData)
    {
        // Get all layer objectives for a layer. Empty if layer has no layer data
        public IEnumerable<Data> getObjectiveDatas()
           => layerData.LayerDatas == null ? Enumerable.Empty<Data>()
            : layerData.LayerDatas.ChainedObjectiveData.Iter()
                 .Prepend(layerData.LayerDatas.ObjectiveData)
                 .Select((d, i) => new BaseData(layerData, d, i));
    }
}