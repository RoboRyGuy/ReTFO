using System;
using GameData;

namespace ReTFO.Archipelago.ModdedInstanceData2;

public class ProcessZone
{
    // Data passed to ProcessZone.Event
    public class Data : ProcessLayer.Data
    {
        // Standard constructor
        public Data(ProcessLayer.Data layer, ExpeditionZoneData? zone) : base(layer)
        {
            Zone = zone;
        }

        // Copy constructor
        public Data(Data source) : base(source)
        {
            Zone = source.Zone;
        }

        public ExpeditionZoneData? Zone { get; init; }

        public int ZoneAlias => GetZoneAlias();
        public int GetZoneAlias()
        {
            int WithOverride(int value, int over) => over == -1 ? value : over;

            if (Zone == null) return WithOverride(0, DimensionData!.StaticAliasOverride);
            else return WithOverride(CalcZoneAlias(Zone.LocalIndex), Zone.AliasOverride);
        }

        public int TerminalCount => GetTerminalCount();
        public int GetTerminalCount()
        {
            if (Zone != null) 
                if (!Zone.ForbidTerminalsInZone) return Zone.TerminalPlacements?.Count ?? 0;
            else if (DimensionData != null) 
                    if (!DimensionData.ForbidTerminalsInDimension) return DimensionData.StaticTerminalPlacements?.Count ?? 0;
            return 0;
        }

        public string ZoneName => GetZoneName();
        public string GetZoneName() => $"{LayerName} ZONE_{ZoneAlias}";
        public string ColoredKeyName => $"{ZoneName} Colored Key";
        public string UnlockZoneName => $"{ZoneName} Unlock Event";
    }

    public ProcessZone() { Manager.RegisterStaticCallbacks<Callback, Delegate>(d => Event += d); }

    // Delegate type for the event
    public delegate void Delegate(Manager manager, ProcessZone.Data data);

    // Event for this instance
    public event Delegate? Event = null;

    // Allow anyone to invoke this event
    public void Invoke(Manager manager, ProcessZone.Data data) => Event?.Invoke(manager, data);

    // Attribute used to mark static functions which should autoregister to this event
    [AttributeUsage(AttributeTargets.Method)] public class Callback : Attribute { }

    // Invoke the event when processing a layer
    internal void OnProcessLayer(Manager manager, ProcessLayer.Data data)
    {
        var layout = data.Layout;
        if (layout == null) Event?.Invoke(manager, new Data(data, null));
        else foreach (var zone in layout.Zones) Event?.Invoke(manager, new(data, zone));
    }

    public ProcessZone RegisteredTo(ProcessLayer owner)
    {
        owner.Event += OnProcessLayer;
        return this;
    }
}
