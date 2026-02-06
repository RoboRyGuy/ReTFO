using GameData;
using System;

namespace ReTFO.Archipelago.ModdedInstanceData2;

public class ProcessTerminal
{
    // Data passed to ProcessTerminal.Event
    public class Data : ProcessZone.Data
    {
        // Standard constructor
        public Data(ProcessZone.Data layer, int terminalIndex) : base(layer)
        {
            TerminalIndex = terminalIndex;
        }

        // Copy constructor
        public Data(Data source) : base(source)
        {
            TerminalIndex = source.TerminalIndex;
        }

        public int TerminalIndex { get; init; }

        public string TerminalName => GetTerminalName();
        public string GetTerminalName() => $"{ZoneName} Terminal #{TerminalIndex + 1}";
    }

    public ProcessTerminal() { Manager.RegisterStaticCallbacks<Callback, Delegate>(d => Event += d); }

    // Delegate type for the event
    public delegate void Delegate(Manager manager, ProcessTerminal.Data data);

    // Event for this instance
    public event Delegate? Event = null;

    // Allow anyone to invoke this event
    public void Invoke(Manager manager, ProcessTerminal.Data data) => Event?.Invoke(manager, data);

    // Attribute used to mark static functions which should autoregister to this event
    [AttributeUsage(AttributeTargets.Method)] public class Callback : Attribute { }

    // Invoke the event when processing a zone
    internal void OnProcessZone(Manager manager, ProcessZone.Data data)
    {
        Il2CppSystem.Collections.Generic.List<TerminalPlacementData>? list;
        if (data.Zone == null) list = data.DimensionData?.StaticTerminalPlacements;
        else list = data.Zone.TerminalPlacements;

        if (list != null) 
            for (int i = 0; i < list.Count; i++) Event?.Invoke(manager, new(data, i));
    }

    public ProcessTerminal RegisteredTo(ProcessZone owner)
    {
        owner.Event += OnProcessZone;
        return this;
    }
}
