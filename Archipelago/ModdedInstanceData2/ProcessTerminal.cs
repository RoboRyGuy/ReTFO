using GameData;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

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

        // Consider using UniqueCommands, StartStateData, or Logs instead, to skip handling specific vs standard terminal data
        public TerminalPlacementData StandardTerminalData => GetStandardTerminalData();
        public TerminalPlacementData GetStandardTerminalData()
        {
            if (TerminalIndex < 0) throw new NotImplementedException($"No standard terminal data; should use specific terminal data instead");
            return Zone?.TerminalPlacements[TerminalIndex]
                ?? DimensionData?.StaticTerminalPlacements[TerminalIndex]
                ?? throw new NullReferenceException($"\"{ZoneName}\" does not have terminal data");
        }

        // Consider using UniqueCommands, StartStateData, or Logs instead, to skip handling specific vs standard terminal data
        public SpecificTerminalSpawnData SpecificTerminalData => GetSpecificTerminalData();
        public SpecificTerminalSpawnData GetSpecificTerminalData()
        {
            if (TerminalIndex >= 0) throw new NotImplementedException($"No specific terminal data; should use standard terminal data instead");
            return Zone?.SpecificTerminalSpawnDatas[-1 - TerminalIndex]
                ?? throw new NullReferenceException($"\"{ZoneName}\" does not have specific terminal data");
        }

        public string TerminalName => GetTerminalName();
        public string GetTerminalName()
        {
            if (TerminalIndex >= 0) return $"{ZoneName} Terminal #{TerminalIndex + 1}";
            else if ((SpecificTerminalData.WorldEventObjectFilter?.Length ?? 0) > 0 ) 
                return $"{ZoneName} Specific Terminal #{-TerminalIndex} ({SpecificTerminalData.WorldEventObjectFilter})";
            else return $"{ZoneName} Specific Terminal #{-TerminalIndex}";
        }

        public TerminalStartStateData StartingStateData => GetStartStateData();
        public TerminalStartStateData GetStartStateData()
            => TerminalIndex < 0 ? SpecificTerminalData.StartingStateData : StandardTerminalData.StartingStateData;

        public IEnumerable<CustomTerminalCommand> UniqueCommands => GetUniqueCommands();
        public IEnumerable<CustomTerminalCommand> GetUniqueCommands()
            => TerminalIndex < 0 ? SpecificTerminalData.UniqueCommands.Iter() : StandardTerminalData.UniqueCommands.Iter();

        public IEnumerable<TerminalLogFileData> Logs => GetLogs();
        public IEnumerable<TerminalLogFileData> GetLogs()
            => TerminalIndex < 0 ? SpecificTerminalData.LocalLogFiles.Iter() : StandardTerminalData.LocalLogFiles.Iter();
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
        for (int i = 0; i < data.TerminalCount; i++) 
            Event?.Invoke(manager, new(data, i));

        for (int i = 0; i < (data.Zone?.SpecificTerminalSpawnDatas?.Count ?? 0); i++)
            Event?.Invoke(manager, new(data, -1 - i));
    }

    public ProcessTerminal RegisteredTo(ProcessZone owner)
    {
        owner.Event += OnProcessZone;
        return this;
    }
}
