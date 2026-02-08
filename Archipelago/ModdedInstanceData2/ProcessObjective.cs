
using GameData;
using ReTFO.Archipelago.ModdedInstanceData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Security.AccessControl;

namespace ReTFO.Archipelago.ModdedInstanceData2;

/* Manager for processing objectives. Works a bit differently from the other processers
 *  beceause objectives are strictly ordered and build on each other sequentially
 */
public class ProcessObjective
{
    // Data passed to handler
    public class Data : ProcessLayer.Data
    {
        // Standard constructor
        public Data(ProcessLayer.Data layer, WardenObjectiveLayerData objectiveData, int objectiveIndex) : base(layer)
        {
            ObjectiveData = objectiveData;
            Objective = WardenObjectiveDataBlock.GetBlock(ObjectiveData.DataBlockId);
            ObjectiveIndex = objectiveIndex;
        }

        // Copy constructor
        public Data(Data source) : base(source)
        {
            ObjectiveData = source.ObjectiveData;
            Objective = source.Objective;
            ObjectiveIndex = source.ObjectiveIndex;
        }

        public WardenObjectiveLayerData ObjectiveData { get; init; }
        public WardenObjectiveDataBlock Objective { get; init; }
        public int ObjectiveIndex { get; init; }
    }

    // Data returned from handler
    public class Result
    {
        public Result(int firstRegion, int lastRegion, bool canCompleteObjective)
        {
            FirstRegion = firstRegion;
            LastRegion = lastRegion;
            CanCompleteObjective = canCompleteObjective;
        }

        // First region in the objective; immediately accessible on objective start
        public int FirstRegion { get; init; }

        // Last region in objective; accessible only when objective is fully complete
        public int LastRegion { get; init; }

        // If an ObjectiveComplete item should be placed at the end of this objective
        public bool CanCompleteObjective { get; init; }
    }

    // Constructs the processor and registers all callbacks with attributes
    public ProcessObjective() 
    {
        BindingFlags bf = BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;
        var methods = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(bf))
            .Where(m => m.CustomAttributes.Any(a => a.AttributeType == typeof(Callback)))
        ;

        Tuple<MethodInfo, IEnumerable<int>> Unpack(MethodInfo m)
            => Tuple.Create(m, Attribute.GetCustomAttributes(m).Cast<Callback>().SelectMany(c => c.Types));

        foreach (var pair in methods.Select(Unpack))
        {
            Delegate? del = System.Delegate.CreateDelegate(typeof(Delegate), pair.Item1) as Delegate;
            if (del == null)
            {
                Plugin.Get().Log.LogWarning($"Failed to register callback {pair.Item1.DeclaringType?.FullName}.{pair.Item1.Name} to ProcessObjective; failed to convert to delegate type");
                continue;
            }
            foreach (int index in pair.Item2)
            {
                if (Handlers.Count <= index) Handlers.AddRange(Enumerable.Repeat<Delegate?>(null, index - Handlers.Count + 1));
                if (Handlers[index] != null)
                {
                    Plugin.Get().Log.LogWarning($"Failed to register callback {pair.Item1.DeclaringType?.FullName}.{pair.Item1.Name} to ProcessObjective; event {index} is already handled!");
                    continue;
                }
                Handlers[index] = del;
            }
        }
    }

    // Delegate used for handling objectives
    public delegate ProcessObjective.Result Delegate(Manager manager, ProcessObjective.Data data);

    // List of handlers for the various objective types
    protected List<Delegate?> Handlers { get; set; } 
        = new(Enumerable.Repeat<Delegate?>(null, (int)eWardenObjectiveType.TimedTerminalSequence)); // Timed Sequence is just the last objective type

    /* Attribute used to mark static functions which should autoregister to this processor
     * Accepts one or more event types which the function will handle
     * Only one handler can be assigned per event type. Attempting more will fail somewhat non-deterministically
     */
    [AttributeUsage(AttributeTargets.Method)] 
    public class Callback : Attribute 
    {
        public Callback(eWardenObjectiveType type) { Types = Enumerable.Empty<int>().Append((int)type); }
        public Callback(params eWardenObjectiveType[] types) { Types = types.Cast<int>(); }
        public Callback(List<eWardenObjectiveType> types) { Types = types.Cast<int>(); }
        public Callback(int type) { Types = Enumerable.Empty<int>().Append(type); }
        public Callback(params int[] types) { Types = types.AsEnumerable(); }
        public Callback(List<int> types) { Types = types.AsEnumerable(); }

        public IEnumerable<int> Types { get; init; }
    }

    public ProcessObjective RegisteredTo(ProcessLayer owner)
    {
        owner.Event += OnProcessLayer;
        return this;
    }

    // Invoke handlers when processing a layer
    internal void OnProcessLayer(Manager manager, ProcessLayer.Data data)
    {
        // No layer data = no objectives for layer
        var layerData = data.LayerData;
        if (layerData == null) return;

        // Gather all objectives in one place, for convenience
        List<Data> objectiveDatas = Enumerable.Empty<WardenObjectiveLayerData>()
            .Append(layerData.ObjectiveData)
            .Concat(layerData.ChainedObjectiveData.Iter())
            .Select((d, i) => new Data(data, d, i))
            .ToList();

        // Helper which will be used to invoke processing inline during the for loop
        Result? Process(Data data)
        {
            if (data.Objective == null)
            {
                Plugin.Get().Log.LogWarning($"Failed to find objective {data.ObjectiveData.DataBlockId} for expedition layer {data.LayerName}");
                return null;
            }

            Delegate? handler = Handlers.ElementAtOrDefault((int)data.Objective.Type);
            if (handler == null)
            {
                Plugin.Get().Log.LogError($"Failed to find objective handler for type {(int)data.Objective.Type} ({Enum.GetName(typeof(eWardenObjectiveType), data.Objective.Type)})");
                return null;
            }

            return handler.Invoke(manager, data);
        }

        // Create the starting region and connect it to the first zone in the layer
        int startRegion = manager.GetOrCreateRegion(data.ObjectiveStartRegionName);
        Path path = manager.AddPath(data.GetFirstZone().ZoneName, startRegion);

        if (data.LayerType.IsMainLayer)
        {   // On elevator land events only trigger for first objective of the main layer
            manager.ProcessEvent.Invoke(manager, new(
                data, objectiveDatas[0].Objective.EventsOnElevatorLand.Iter(),
                startRegion, $"{data.LayerName} On Elevator Land"
            ));

            if (objectiveDatas[0].Objective.GenericItemFromStart != 0)
            {   // I believe GenericInElevator only works for the first objective of the main layer
                manager.AddLocation(new(
                    $"{data.LayerName} Generic Item in Elevator",
                    data.BigPickupName(ItemDataBlock.GetBlock(objectiveDatas[0].Objective.GenericItemFromStart)),
                    new(1) { manager.GetOrCreateRegion(data.GetFirstZone().ZoneName) },
                    true
                ));
            }
        }

        // Process each objective and chain it all together
        int last = startRegion;
        uint count = 0;
        foreach (var result in objectiveDatas.Select(Process).OfType<Result>())
        {   // Connect the objectives together. Lock each with CompleteObjective items based on count
            path = manager.AddPath(last, result.FirstRegion);
            path.required_item = data.CompleteObjectiveName;
            path.required_item_count = count++;
            path.alternate_item = data.InstantWinEventName;
            last = result.FirstRegion;

            // If the objective can be completed, we add a CompleteObjective item the end of it (for now, might change this later)
            if (result.CanCompleteObjective)
            {
                manager.AddLocation(new(
                    $"{data.LayerName} Completed Objective #{count}",
                    data.CompleteObjectiveName,
                    new(1) { result.LastRegion },
                    true
                ));
            }
        }

        // One zone is added for the gotowin events
        int next = manager.GetOrCreateRegion(data.ObjeciveGotoWinRegionName);
        path = manager.AddPath(last, next);
        path.required_item = data.CompleteObjectiveName;
        path.required_item_count = count++;
        path.alternate_item = data.InstantWinEventName;
        last = next;

        // A final zone is added with the reward for reaching extraction with the objective complete
        next = manager.GetOrCreateRegion(data.ObjectiveRewardRegionName);
        path = manager.AddPath(last, next);
        path.required_item = data.ExtractionReachableName;
        path.required_item_count = 1;

        manager.AddLocation(new(
            $"{data.LayerName} Any Sector Clear",
            manager.UnlockExpeditionName,
            new(1) { next },
            true
        ));

        if (data.LayerType.IsMainLayer)
        {
            manager.AddLocation(new(
                $"{data.LayerName} Main Sector Clear",
                manager.UnlockExpeditionMainOnlyName,
                new(1) { next },
                true
            ));
        }
    }

}
