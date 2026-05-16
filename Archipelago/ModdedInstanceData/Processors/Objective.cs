
using GameData;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class Objective
{
    // Interface class passed to processing giving access to necessary data
    public class Data : Layer.Data
    {
        /// <summary>
        /// Index of the objective in its layer; same as ChainIndex internally, with 0
        ///  being the first objective, 1 is the first in the "ChainedObjectives" list, etc
        /// </summary>
        public int ObjectiveIndex { get; private init; }

        /// <summary>
        /// Create a new data for the provided objective in the provided layer
        /// </summary>
        public Data(Layer.Data data, int objectiveIndex)
            : base(data)
        {
            ObjectiveIndex = objectiveIndex;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Data(Objective.Data other)
            : base(other as Layer.Data)
        {
            ObjectiveIndex = other.ObjectiveIndex;
        }

        /// <summary>
        /// Layer objective data for this objective.
        /// This is placements, WardenObjectiveDataBlock ID, etc
        /// </summary>
        public WardenObjectiveLayerData ObjectiveData
            => ObjectiveIndex == 0
            ? LayerDatas!.ObjectiveData
            : LayerDatas!.ChainedObjectiveData[ObjectiveIndex - 1];

        /// <summary>
        /// Actual objective datablock being processed
        /// </summary>
        public virtual WardenObjectiveDataBlock Objective
            => WardenObjectiveDataBlock.GetBlock(ObjectiveData.DataBlockId)
            ?? throw new NullReferenceException($"Failed to find objective datablock for layer: {LayerName}");

        /// <summary>
        /// Helper to construct a unique, user-friendly name for this objective.
        /// </summary>
        /// <param name="objectiveTypeSummary">A brief summary of what the objective is</param>
        public string ObjectiveName(string? objectiveTypeSummary = null)
            => $"{LayerName} Objective #{ObjectiveIndex + 1}{(objectiveTypeSummary == null ? "" : $" ({objectiveTypeSummary})")}";

        /// <summary>
        /// Get the warden objective instance assuming we're loaded into the correct level AND that this objective type
        ///  implements a warden objective behavior
        /// </summary>
        public IWardenObjective GetWardenObjective()
            => WardenObjectiveManager.GetWardenObjectiveBehaviour(LayerType, ObjectiveIndex);
    }

    // Attribute used to mark static functions which should autoregister to this processor
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

        public Processor SubscribedTo(MidManager.Processor<Layer.Data> owner)
        {
            owner.RegisterCallback(OnProcessLayer);
            return this;
        }

        // Callback to initiate processing when processing a layer
        protected void OnProcessLayer(Layer.Data data)
        {
            if (data.LayerDatas == null) return;
            foreach (Data oData in data.GetObjectiveDatas()) 
                Process(oData);
        }
    }

    extension(Game.Data gameData)
    {
        public Processor ObjectiveProcessor
            => (Processor)gameData.Manager.GetProcessor<Data>();
    }

    extension(Layer.Data layerData)
    {
        /// <summary>
        /// Get all layer objectives for a layer. Empty if layer has no layer data
        /// </summary>
        public IEnumerable<Data> GetObjectiveDatas()
        {
            if (layerData.LayerDatas == null) return Enumerable.Empty<Data>();
            else return Enumerable.Range(0, 1 + (layerData.LayerDatas.ChainedObjectiveData?.Count ?? 0))
                .Select(i => new Data(layerData, i));
        }
    }
}