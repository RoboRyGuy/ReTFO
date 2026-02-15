using GameData;
using System;
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData2;

public class ProcessEvent
{
    // Data passed to ProcessEvent.Event
    public class Data : ProcessLayer.Data
    {
        // Standard constructor
        public Data(ProcessLayer.Data layer, List<WardenObjectiveEventData> events, int sourceRegion, string sourceName) : base(layer)
        {
            Events = events;
            SourceRegion = sourceRegion;
            SourceName = sourceName;
        }

        // Copy constructor
        public Data(Data source) : base(source)
        {
            Events = source.Events;
            SourceRegion = source.SourceRegion;
            SourceName = source.SourceName;
        }

        // Actual event data to process, as a set of events triggered all at once
        public List<WardenObjectiveEventData> Events { get; init; }
        
        // Region this event is found in
        public int SourceRegion { get; init; }
        
        // Descriptive and unique source name; not necessarily the source region's name
        public string SourceName { get; init; }

    }

    public ProcessEvent() { Manager.RegisterStaticCallbacks<Callback, Delegate>(d => Event += d); }

    // Delegate type for the event
    public delegate void Delegate(Manager manager, ProcessEvent.Data data);

    // Event for this instance
    public event Delegate? Event = null;

    // Allow anyone to invoke this event
    public void Invoke(Manager manager, ProcessEvent.Data data) => Event?.Invoke(manager, data);

    // Attribute used to mark static functions which should autoregister to this event
    [AttributeUsage(AttributeTargets.Method)] public class Callback : Attribute { }
}
