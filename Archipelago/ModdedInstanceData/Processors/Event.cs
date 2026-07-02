using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using EventList = Il2CppSystem.Collections.Generic.List<GameData.WardenObjectiveEventData>;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Event
{
    private record class ScopeData
    {
        public ScopeData(EventList rawEvents, int eventStart, int eventCount)
        {
            RawEvents = rawEvents;
            EventStart = eventStart;
            EventCount = eventCount;
        }

        public EventList RawEvents { get; init; }
        public int EventStart { get; set; }
        public int EventCount { get; set; }
    }

    // Interface class passed to processing giving access to necessary data
    public class Data : Layer.Data, IList<WardenObjectiveEventData>
    {
        /// <summary>
        /// The custom data stored in the region object for this data
        /// </summary>
        private readonly ScopeData EventScopeData;

        /// <summary>
        /// The region associated with this event
        /// </summary>
        public RegionID Region_Event { get; private init; }

        /// <summary>
        /// Construct a new data with the given parameters
        /// </summary>
        public Data(Layer.Data data, RegionID region, EventList rawEvents, int eventStart = 0, int eventCount = -1) 
            : base(data)
        {
            Region_Event = region;
            if (Regions.LookUpValue(region).RegionData != null)
                throw new NotSupportedException("Cannot create new event data wrapping a region which contains data already");
            Regions.SetData(region, EventScopeData = new ScopeData(rawEvents, eventStart, eventCount == -1 ? rawEvents.Count : eventCount));
        }

        /// <summary>
        /// Constructor for constructing from an existing region's data.
        /// This can be invoked if you're reasonably confident the ID is a valid event region.
        /// </summary>
        public Data(Game.Data data, RegionID region)
            : base(data, data.Regions.LookUpDefinition(region).Parent)
        {
            Region_Event = region;
            EventScopeData = Regions.GetData<ScopeData>(region);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Data(Event.Data other)
            : base(other)
        {
            Region_Event = other.Region_Event;
            EventScopeData = other.EventScopeData;
        }

        /// <summary>
        /// Raw list of events
        /// </summary>
        protected EventList RawEvents => EventScopeData.RawEvents;

        /// <summary>
        /// The actual Il2Cpp type of the events in the event list; it's allowed for the list to refer to only derived types
        /// </summary>
        public Il2CppSystem.Type EventType 
        { 
            get => m_eventType ??= RawEvents.GetIl2CppType().GetGenericArguments()[0]; 
            init => m_eventType = value; 
        }
        private Il2CppSystem.Type? m_eventType = null;

        /// <summary>
        /// Index of the first event in the list
        /// </summary>
        public int EventStart { get => EventScopeData.EventStart; protected set => EventScopeData.EventStart = value; }

        /// <summary>
        /// How many events are being processed, starting with EventStart
        /// </summary>
        public int EventCount { get => EventScopeData.EventCount; protected set => EventScopeData.EventCount = value; }

        /// <summary>
        /// Gets the name of the event
        /// </summary>
        public string EventName => Regions.LookUpName(Region_Event);

        /// <summary>
        /// Grants access to events via an enumerator
        /// </summary>
        public IEnumerable<WardenObjectiveEventData> Events
            => Enumerable.Range(EventStart, EventCount).Select(i => RawEvents![i]);

        // Implementing IList<WardenObjectiveEventData> - Used to give access to the event set currently being processed
        #region IList<WardenObjectiveEventData>
        private void CheckEntry(WardenObjectiveEventData data)
        {
            if (!EventType.IsAssignableFrom(data.GetIl2CppType()))
                throw new ArgumentException(
                    $"Cannot perform add or modify operation using object of type {data.GetIl2CppType().FullName} on a list of {EventType.FullName}"
                    + "\nConsider using the Event.Data.EventType property to ensure your event data is correctly typed."
                );
        }

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
                CheckEntry(value);
                if (index < 0 || index >= EventCount)
                    throw new ArgumentOutOfRangeException(nameof(index));
                RawEvents![EventStart + index] = value;
            }
        }

        public int IndexOf(WardenObjectiveEventData item)
            => Enumerable.Range(EventStart, EventCount).FirstOrDefault(i => RawEvents![i] == item, -1);

        public void Insert(int index, WardenObjectiveEventData item)
        {
            CheckEntry(item);
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
            CheckEntry(item);
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
        #endregion
    }

    /// <summary>
    /// Wrapper around a list of events for repeated processing using event breaks.
    /// Used to break event lists into separate event processing instances, 
    /// </summary>
    public class Wrapper
    {
        /// <summary>
        /// Standard constructor
        /// </summary>
        public Wrapper(Layer.Data layerData, EventList events)
        {
            this.layerData = layerData;
            this.events = events;
            eventStart = -1;
            eventCount = 0;
            Step();
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Wrapper(Wrapper source)
        {
            layerData = source.layerData;
            events = source.events;
            eventStart = source.eventStart;
            eventCount = source.eventCount;
        }

        private readonly Layer.Data layerData;
        private readonly EventList events;
        private int eventStart;
        private int eventCount;

        /// <summary>
        /// True if all event sets have been processed. By design, an empty list is considered to have 1 event set with no events
        /// </summary>
        public bool IsDone => eventStart > events.Count;

        /// <summary>
        /// Sets up the next event set, discarding the current one
        /// </summary>
        /// <param name="errorIfHasEvents">If true, log an error if skipping processing of any events</param>
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

        /// <summary>
        /// Process the current set of events; optionally create a new set if necessary
        /// </summary>
        /// <param name="eventRegion">Region the events occur in</param>
        /// <param name="extendIfNecessary">If true and at the end of the event list, extend the list using an event break</param>
        public void Process(RegionID eventRegion, bool extendIfNecessary = false)
        {
            if (IsDone)
            {
                if (!extendIfNecessary) return;
                events.Add(new WardenObjectiveEventData() { Type = eWardenObjectiveEventType.EventBreak });
            }
            Data data = new Data(layerData, eventRegion, events, eventStart, eventCount);
            layerData.EventProcessor.Process(data);
            eventStart = data.EventStart;
            eventCount = data.EventCount;
            Step();
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
    }

    extension(Game.Data gameData)
    {
        public Processor EventProcessor
            => (Processor)gameData.Manager.GetProcessor<Data>();
    }

    extension(Layer.Data layerData)
    {
        /// <summary>
        /// Wrap any list of events for processing with respct to event breaks
        /// </summary>
        public Wrapper WrapEvents(EventList events)
            => new Wrapper(layerData, events);

        /// <summary>
        /// Quickly process a full list of events. Return the processed data
        /// </summary>
        public Data ProcessEvents(RegionID eventRegion, EventList events)
        {
            Data data = new Data(layerData, eventRegion, events);
            layerData.EventProcessor.Process(data);
            return data;
        }

        /// <summary>
        /// Process a custom list of events
        /// </summary>
        public Data ProcessEvents(RegionID eventRegion, EventList events, int eventStart, int eventCount)
        {
            Data data = new Data(layerData, eventRegion, events, eventStart, eventCount);
            layerData.EventProcessor.Process(data);
            return data;
        }
    }

    extension(Objective.Data objectiveData)
    {
        /// <summary>
        /// Wrap EventsOnActivate
        /// </summary>
        public Wrapper WrapOnActivateEvents()
            => objectiveData.WrapEvents(objectiveData.Objective.EventsOnActivate ??= new(1));

        /// <summary>
        /// Wrap EventsOnActivate; if OnActivateOnSolve is false, make it true and clear the event list.
        /// </summary>
        /// <remarks>
        /// This is to be used on objectives that use OnActivateOnSolveItem. This will ensure EventsOnActivate is
        ///  run for those events; the existing list is cleared first to ensure no side effects occur.
        /// Not all objectives respect OnActivateOnSolve; using this on those events could cause problems.
        /// </remarks>
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
