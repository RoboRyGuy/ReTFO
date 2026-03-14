
using GameData;
using LevelGeneration;
using ReTFO.Archipelago.Features.Terminals;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

using DoublePlacementList = Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<GameData.ZonePlacementData>>;
using PlacementList = Il2CppSystem.Collections.Generic.List<GameData.ZonePlacementData>;
using RegionInfo = ReTFO.Archipelago.ModdedInstanceData.Processors.Game.Data.RegionInfo;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class Terminal
{
    // Interface class passed to processing giving access to necessary data
    public abstract class Data : Zone.Data
    {
        // Minimal interface implementation
        public abstract Zone.Data ZoneData { get; }
        public abstract int TerminalIndex { get; }

        // Names
        public virtual string TerminalName
           => IsStandardTerminal ? $"{ZoneName} Terminal #{TerminalIndex + 1}"
            : IsSpecificTerminal ? $"{ZoneName} Terminal ({SpecificTerminalData.WorldEventObjectFilter})"
            : throw new NotImplementedException();

        // Useful helpers
        public bool IsStandardTerminal => TerminalIndex >= 0;
        public bool IsSpecificTerminal => TerminalIndex < 0;

        // Standard terminal data for this terminal. Throws if this is not a standard terminal
        public virtual TerminalPlacementData StandardTerminalData
        {
            get
            {
                if (IsSpecificTerminal)
                    throw new ArgumentException($"Attempted to retrieve standard terminal data for specific terminal: {TerminalName}");
                Il2CppSystem.Collections.Generic.List<TerminalPlacementData>? placements
                    = Zone?.TerminalPlacements ?? DimensionData?.StaticTerminalPlacements;
                if (placements == null)
                    throw new NullReferenceException($"Failed to find standard terminal placement list in zone {ZoneName}");
                if (TerminalIndex < 0 || TerminalIndex >= placements.Count)
                    throw new ArgumentOutOfRangeException($"Terminal {TerminalIndex} does not exist in standard placement list for zone: {ZoneName}");
                return placements[TerminalIndex];
            }
        }

        // Specific terminal data for this terminal. Throws if this is not a specific terminal 
        public virtual SpecificTerminalSpawnData SpecificTerminalData
        {
            get
            {
                if (IsStandardTerminal)
                    throw new ArgumentException($"Attempted to retrieve specific terminal data for specific terminal: {TerminalName}");
                Il2CppSystem.Collections.Generic.List<SpecificTerminalSpawnData>? placements = Zone?.SpecificTerminalSpawnDatas;
                if (placements == null)
                    throw new NullReferenceException($"Failed to find specific terminal placement list in zone {ZoneName}");
                int actualIndex = -1 - TerminalIndex;
                if (actualIndex < 0 || actualIndex >= placements.Count)
                    throw new ArgumentOutOfRangeException($"Terminal {TerminalIndex} does not exist in specific placement list for zone: {ZoneName}");
                return placements[actualIndex];
            }
        }

        // Starting state data for this terminal
        public virtual TerminalStartStateData TerminalStartingStateData
           => IsStandardTerminal ? StandardTerminalData.StartingStateData
            : IsSpecificTerminal ? SpecificTerminalData.StartingStateData
            : throw new NotImplementedException();

        // Unique command list for this terminal
        public virtual Il2CppSystem.Collections.Generic.List<CustomTerminalCommand> TerminalUniqueCommands
           => IsStandardTerminal ? StandardTerminalData.UniqueCommands
            : IsSpecificTerminal ? SpecificTerminalData.UniqueCommands
            : throw new NotImplementedException();

        // Local logs included in this terminal
        public virtual Il2CppSystem.Collections.Generic.List<TerminalLogFileData> TerminalLocalLogs
           => IsStandardTerminal ? StandardTerminalData.LocalLogFiles
            : IsSpecificTerminal ? SpecificTerminalData.LocalLogFiles
            : throw new NotImplementedException();

        /// <summary>
        /// Get terminal data for a spawned terminal
        /// </summary>
        /// <param name="terminal">The terminal to get data for</param>
        /// <returns>
        /// The terminal data, or null if this terminal has no data.
        /// Some terminals are not spawned by GameData (for example, reactor terminals) and as such have no data.
        /// </returns>
        public static Terminal.Data? FromTerminal(LG_ComputerTerminal terminal)
            => IdentifyingLogHandler.RetrieveDataFromLog(terminal);

        // Implementing Zone.Data
        public override Layer.Data LayerData => ZoneData.LayerData;
        public override ExpeditionZoneData? Zone => ZoneData.Zone;
    }

    // Minimal concrete implementation of Data
    protected class BaseData : Data
    {
        // Standard constructor
        public BaseData(Zone.Data zoneData, int terminalIndex)
        {
            this.zoneData = zoneData;
            this.terminalIndex = terminalIndex;
        }

        // Copy constructor
        public BaseData(BaseData source)
        {
            zoneData = source.zoneData;
            terminalIndex = source.terminalIndex;
        }

        // Concretes
        private readonly Zone.Data zoneData;
        private readonly int terminalIndex;

        // Interface implementation
        public override Zone.Data ZoneData => zoneData;
        public override int TerminalIndex => terminalIndex;
    }

    // Attribute used to mark static functions which should autoregister to this processor
    [AttributeUsage(AttributeTargets.Method)]
    public class Callback : Game.IProcessor<Data>.Callback { }

    // Actual class wrapping an event processing instance
    public class Processor : Game.IProcessor<Data>
    {
        public Processor()
            => RegisterStaticCallbacks();

        protected event Delegate? Event = null;

        public override void RegisterCallback(Delegate callback)
            => Event += callback;

        public override void UnregisterCallback(Delegate callback)
            => Event -= callback;

        public override void Process(Data data)
            => Event?.Invoke(data);

        // Helper so this can be created inline and also be registered to a zone processor
        public Processor SubscribedTo(Zone.Processor owner)
        {
            owner.RegisterCallback(OnProcessZone);
            return this;
        }

        // Callback to initiate terminal processing when processing a zone
        protected void OnProcessZone(Zone.Data data)
        {
            foreach (var index in data.TerminalIndicies)
                Process(new BaseData(data, index));
        }
    }

    extension(Layer.Data layerData)
    {
        // Find a terminal given a terminal placement
        public Data FindTerminalByPlacement(TerminalZoneSelectionData placement)
            => new BaseData(layerData.FindZoneByIndex(placement.LocalIndex), placement.TerminalIndex);

        // Convert a list of terminal selection lists into terminal regions
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToTerminalRegions(Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<TerminalZoneSelectionData>> placements)
            => placements.Select(ps => ps.Select(p => layerData.FindZoneByIndex(p.LocalIndex).GetTerminal(p.TerminalIndex))
                .Select(t => new RegionInfo() { Region = layerData.GetOrCreateRegion(t.TerminalName), IsBad = t.TerminalStartingStateData.PasswordProtected }));

        // Convert a list of placement lists into terminal regions
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToTerminalRegions(DoublePlacementList placements)
            => layerData.PlacementsToTerminalRegions(placements.Iter());

        // Convert an enumerable of placement lists into terminal regions
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToTerminalRegions(IEnumerable<PlacementList> placements)
            => placements.Select(ps => layerData.PlacementsToTerminalRegions(ps));

        // Convert a list of placements into terminal regions
        public IEnumerable<RegionInfo> PlacementsToTerminalRegions(PlacementList placements)
            => layerData.PlacementsToTerminalRegions(placements.Iter());

        // Convert an enumerable of placements into terminal regions
        public IEnumerable<RegionInfo> PlacementsToTerminalRegions(IEnumerable<ZonePlacementData> placements)
            => placements.SelectMany(p => layerData.PlacementToTerminalRegions(p));

        // Convert a single placement into a list of terminal regions
        public IEnumerable<RegionInfo> PlacementToTerminalRegions(ZonePlacementData placement)
        {
            return layerData.FindZoneByPlacement(placement).TerminalDatas.Select(t => new RegionInfo()
            {
                Region = layerData.GetOrCreateRegion(t.TerminalName),
                IsBad = t.TerminalStartingStateData.PasswordProtected,
            });
        }
    }

    extension(Game.Data gameData)
    {
        public Processor TerminalProcessor
            => (Processor)gameData.GetProcessor<Data>();
    }

    extension(Zone.Data zoneData)
    {
        // Number of "standard" terminals in the zone
        public int StandardTerminalCount
           => zoneData.Zone != null ? (zoneData.Zone.ForbidTerminalsInZone ? 0 : zoneData.Zone.TerminalPlacements?.Count ?? 0)
            : zoneData.DimensionData != null ? (zoneData.DimensionData.ForbidTerminalsInDimension ? 0 : zoneData.DimensionData.StaticTerminalPlacements?.Count ?? 0)
            : throw new NotImplementedException();

        // Number of "specific" terminals in the zone
        public int SpecificTerminalCount
           => zoneData.Zone != null ? zoneData.Zone.SpecificTerminalSpawnDatas?.Count ?? 0 // ForbidTerminalsInZone does not apply to specific terminal placements
            : zoneData.LayerType.IsDimension ? 0
            : throw new NotImplementedException();

        // Get all terminal indicies for this zone
        public IEnumerable<int> TerminalIndicies
           => Enumerable.Range(-zoneData.SpecificTerminalCount, zoneData.SpecificTerminalCount + zoneData.StandardTerminalCount);

        // Get all terminal datas for this zone
        public IEnumerable<Data> TerminalDatas
            => zoneData.TerminalIndicies.Select(i => new BaseData(zoneData, i));

        // Get a specific terminal data for this zone
        public Data GetTerminal(int index)
        {
            if (index < (-zoneData.SpecificTerminalCount) || index >= zoneData.StandardTerminalCount)
            {
                FeatureLogger.Error($"Terminal index {index} does not exist in zone: {zoneData.ZoneName}");
                index = 0;
            }
            return new BaseData(zoneData, index);
        }
    }

    extension(Objective.Data objectiveData)
    {
        // Make and unstuff terminal region sets for an objective
        public IEnumerable<List<int>> ObjectiveToTerminalRegionSets(int count)
            => objectiveData.UnstuffPlacements(objectiveData.PlacementsToTerminalRegions(objectiveData.ObjectiveData.ZonePlacementDatas), count);
    }
}
