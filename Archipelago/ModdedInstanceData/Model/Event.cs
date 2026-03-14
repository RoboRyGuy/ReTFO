
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Event
{
    // Interface class passed to processing giving access to necessary data
    public abstract class Data : Layer.Data, IList<WardenObjectiveEventData>
    {
        // Minimal interface implementation
        public abstract Layer.Data LayerData { get; }
        public abstract int EventRegion { get; }
        public abstract string EventName { get; }
        protected abstract Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData>? RawEvents { get; }
        public abstract int EventStart { get; protected set; }
        public abstract int EventCount { get; protected set; }

        // Granting access to events via an enumerator
        public IEnumerable<WardenObjectiveEventData> Events
            => Enumerable.Range(EventStart, EventCount).Select(i => RawEvents![i]);

        // Implementing Layer.Data
        public override Expedition.Data ExpeditionData => LayerData.ExpeditionData;
        public override LayerType LayerType => LayerData.LayerType;

        // Implementing IList<WardenObjectiveEventData> - Used to give access to the event set currently being processed
        public int Count => EventCount;
        public bool IsReadOnly => false;
        public WardenObjectiveEventData this[int index]
        {
            get
            {
                if (index < 0 || index >= EventCount)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return RawEvents![EventStart + index];
            }
            set
            {
                if (index < 0 || index >= EventCount)
                    throw new ArgumentOutOfRangeException(nameof(index));
                RawEvents![EventStart + index] = value;
            }
        }

        public int IndexOf(WardenObjectiveEventData item)
            => Enumerable.Range(EventStart, EventCount).FirstOrDefault(i => RawEvents![i] == item, -1);

        public void Insert(int index, WardenObjectiveEventData item)
        {
            if (index < 0 || index > EventCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            RawEvents!.Insert(EventStart + index, item);
            ++EventCount;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= EventCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            RawEvents!.RemoveAt(EventStart + index);
            --EventCount;
        }

        public void Add(WardenObjectiveEventData item)
        {
            RawEvents!.Insert(EventStart + EventCount, item);
            ++EventCount;
        }

        public void Clear()
        {
            RawEvents!.RemoveRange(EventStart, EventCount);
            EventCount = 0;
        }

        public bool Contains(WardenObjectiveEventData item)
            => Events.Any(i => i == item);

        public void CopyTo(WardenObjectiveEventData[] array, int arrayIndex)
        {
            for (int i = 0; i < EventCount; i++)
                array[arrayIndex + i] = RawEvents![EventStart + i];
        }

        public bool Remove(WardenObjectiveEventData item)
        {
            int index = IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }

        public IEnumerator<WardenObjectiveEventData> GetEnumerator()
            => Events.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    // Minimal concrete implementation of Data
    protected class BaseData : Data
    {
        // Standard constructor
        public BaseData(Layer.Data layerData, int eventRegion, string eventName, Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> rawEvents, int eventStart, int eventCount)
        {
            this.layerData = layerData;
            this.eventRegion = eventRegion;
            this.eventName = eventName;
            this.rawEvents = rawEvents;
            this.eventStart = eventStart;
            this.eventCount = eventCount;
        }

        // Quick constructor for processing full lists
        public BaseData(Layer.Data layerData, int eventRegion, string eventName, Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> rawEvents)
        {
            this.layerData = layerData;
            this.eventRegion = eventRegion;
            this.eventName = eventName;
            this.rawEvents = rawEvents;
            this.eventStart = 0;
            this.eventCount = rawEvents.Count;
        }

        // Copy constructor
        public BaseData(BaseData source)
        {
            layerData = source.layerData;
            rawEvents = source.rawEvents;
            eventStart = source.eventStart;
            eventCount = source.eventCount;
            eventRegion = source.eventRegion;
            eventName = source.eventName;
        }

        // Concretes
        private readonly Layer.Data layerData;
        private readonly Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> rawEvents;
        private int eventStart;
        private int eventCount;
        private int eventRegion;
        private string eventName;

        // Interface implementation
        public override Layer.Data LayerData => layerData;
        protected override Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> RawEvents => rawEvents;
        public override int EventStart { get => eventStart; protected set => eventStart = value; }
        public override int EventCount { get => eventCount; protected set => eventCount = value; }
        public override int EventRegion => eventRegion;
        public override string EventName => eventName;
    }

    // Wraps a list of events and breaks processing down with respect to event breaks
    public class Wrapper
    {
        // Standard constructor
        public Wrapper(Layer.Data layerData, Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> events)
        {
            this.layerData = layerData;
            this.events = events;
            eventStart = -1;
            eventCount = 0;
            Step();
        }

        // Copy constructor
        public Wrapper(Wrapper source)
        {
            layerData = source.layerData;
            events = source.events;
            eventStart = source.eventStart;
            eventCount = source.eventCount;
        }

        // Concretes
        private readonly Layer.Data layerData;
        private readonly Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> events;
        private int eventStart;
        private int eventCount;

        // True if all event sets have been processed. By design, an empty list is considered to have 1 event set with no events
        public bool IsDone => eventStart > events.Count;

        // Sets up the next event set, discarding the current one
        public void Step(bool errorIfHasEvents = false)
        {
            if (errorIfHasEvents && eventCount > 0)
                FeatureLogger.Error($"Performed step for event processing with \"errorIfHasEvents\"");

            eventStart += eventCount + 1;
            for (eventCount = 0; (eventStart + eventCount) < events.Count; eventCount++)
            {
                if (events[eventStart + eventCount].Type == eWardenObjectiveEventType.EventBreak)
                    break;
            }
        }

        // Process the current set of events; create a new set if necessary
        public void Process(int eventRegion, string eventSource, bool extendIfNecessary = false)
        {
            if (IsDone)
            {
                if (!extendIfNecessary) return;
                events.Add(new WardenObjectiveEventData() { Type = eWardenObjectiveEventType.EventBreak });
            }
            Data data = new BaseData(layerData, eventRegion, eventSource, events, eventStart, eventCount);
            layerData.EventProcessor.Process(data);
            eventStart = data.EventStart;
            eventCount = data.EventCount;
            Step();
        }

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
    }

    extension(Game.Data gameData)
    {
        public Processor EventProcessor
            => (Processor)gameData.GetProcessor<Data>();
    }

    extension(Layer.Data layerData)
    {
        // Wrap any list of events for processing with respct to event breaks
        public Wrapper WrapEvents(Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> events)
            => new Wrapper(layerData, events);

        // Quickly process a full list of events
        public Data ProcessEvents(int eventRegion, string eventSource, Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> events)
        {
            Data data = new BaseData(layerData, eventRegion, eventSource, events);
            layerData.EventProcessor.Process(data);
            return data;
        }

        // Process a custom list of events
        public Data ProcessEvents(int eventRegion, string eventSource, Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData> events, int eventStart, int eventCount)
        {
            Data data = new BaseData(layerData, eventRegion, eventSource, events, eventStart, eventCount);
            layerData.EventProcessor.Process(data);
            return data;
        }
    }

    extension(Objective.Data objectiveData)
    {
        // Wrap EventsOnActivate
        public Wrapper WrapOnActivateEvents()
            => objectiveData.WrapEvents(objectiveData.Objective.EventsOnActivate ??= new(1));

        // Wrap EventsOnActivate; if OnActivateOnSolve is false, make it true and clear the event list
        public Wrapper MakeOrWrapOnSolveEvents()
        {
            if (!objectiveData.Objective.OnActivateOnSolveItem)
            {
                objectiveData.Objective.OnActivateOnSolveItem = true;
                objectiveData.Objective.EventsOnActivate?.Clear();
            }
            return objectiveData.WrapOnActivateEvents();
        }
    }
}
