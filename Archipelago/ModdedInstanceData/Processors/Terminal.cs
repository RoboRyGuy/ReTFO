
using GameData;
using LevelGeneration;
using ReTFO.Archipelago.Features.Terminals;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using DoublePlacementList = Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<GameData.ZonePlacementData>>;
using PlacementList = Il2CppSystem.Collections.Generic.List<GameData.ZonePlacementData>;
using RegionInfo = ReTFO.Archipelago.ModdedInstanceData.Processors.Game.Data.RegionInfo;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Terminal
{
    private record class ScopeData
    {
        public ScopeData(int terminalIndex) => TerminalIndex = terminalIndex;
        public int TerminalIndex { get; init; }
    }

    /// <summary>
    /// Data class passed to processing
    /// </summary>
    public class Data : Zone.Data
    {
        /// <summary>
        /// The custom data stored in the region object for this data
        /// </summary>
        private readonly ScopeData TerminalScopeData;

        /// <summary>
        /// The region associated with this terminal
        /// </summary>
        public RegionID Region_Terminal { get; private init; }

        /// <summary>
        /// Create a new terminal data
        /// </summary>
        /// <param name="data">The zone containing the terminal</param>
        /// <param name="terminalIndex">The index of the terminal; a negative index is for specific terminal placements</param>
        public Data(Zone.Data data, int terminalIndex)
            : base(data)
        {
            string name
                = (terminalIndex >= 0) ? $"{ZoneName} Terminal #{terminalIndex + 1}"
                : $"{ZoneName} Terminal ({data.Zone!.SpecificTerminalSpawnDatas[-(terminalIndex + 1)].WorldEventObjectFilter})";
            Region_Terminal = Regions.LookUpOrCreate(
                data, name,
                data => new("A region for a particular terminal in a zone", data.Region_Zone)
            );
            if (!Regions.GetDataAllowNull(Region_Terminal, out TerminalScopeData!))
                Regions.SetData(Region_Terminal, TerminalScopeData = new(terminalIndex));
        }

        /// <summary>
        /// Constructor for constructing from an existing region's data.
        /// This can be invoked if you're reasonably confident the ID is a valid terminal region.
        /// </summary>
        public Data(Game.Data data, RegionID region)
            : base(data, data.Regions.LookUpDefinition(region).Parent)
        {
            Region_Terminal = region;
            TerminalScopeData = Regions.GetData<ScopeData>(Region_Terminal);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Data(Terminal.Data other)
            : base(other)
        {
            Region_Terminal = other.Region_Terminal;
            TerminalScopeData = other.TerminalScopeData;
        }

        /// <summary>
        /// Index of this terminal in the zone. 
        /// A negative index is for specific terminal placements. (-1 => specific terminal 0, etc)
        /// </summary>
        public int TerminalIndex => TerminalScopeData.TerminalIndex;

        /// <summary>
        /// The unique name for this terminal
        /// </summary>
        public string TerminalName => Regions.LookUpName(Region_Terminal);

        /// <summary>
        /// True if this is a standard terminal
        /// </summary>
        public bool IsStandardTerminal => TerminalIndex >= 0;

        /// <summary>
        /// True if this is a specific terminal (placed on a specific align)
        /// </summary>
        public bool IsSpecificTerminal => TerminalIndex < 0;

        /// <summary>
        /// Standard terminal data for this terminal. Throws if this is not a standard terminal
        /// </summary>
        public TerminalPlacementData StandardTerminalData
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

        /// <summary>
        /// Specific terminal data for this terminal. Throws if this is not a specific terminal 
        /// </summary>
        public SpecificTerminalSpawnData SpecificTerminalData
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

        /// <summary>
        /// Starting state data for this terminal
        /// </summary>
        public virtual TerminalStartStateData TerminalStartingStateData
           => IsStandardTerminal ? StandardTerminalData.StartingStateData
            : IsSpecificTerminal ? SpecificTerminalData.StartingStateData
            : throw new NotImplementedException();

        /// <summary>
        /// Unique command list for this terminal
        /// </summary>
        public virtual Il2CppSystem.Collections.Generic.List<CustomTerminalCommand> TerminalUniqueCommands
           => IsStandardTerminal ? StandardTerminalData.UniqueCommands
            : IsSpecificTerminal ? SpecificTerminalData.UniqueCommands
            : throw new NotImplementedException();

        /// <summary>
        /// Local logs included in this terminal
        /// </summary>
        public virtual Il2CppSystem.Collections.Generic.List<TerminalLogFileData> TerminalLocalLogs
           => IsStandardTerminal ? StandardTerminalData.LocalLogFiles
            : IsSpecificTerminal ? SpecificTerminalData.LocalLogFiles
            : throw new NotImplementedException();

        /// <summary>
        /// Get terminal data for a spawned terminal
        /// </summary>
        /// <param name="terminal">The terminal to get data for</param>
        /// <returns>
        /// The IdentifyTerminalResult for this terminal, which either has data
        ///  or states the terminal is a reactor. Reactor terminals have no data.
        /// </returns>
        public static IdentifyingLogHandler.IdentifyTerminalResult FromTerminal(LG_ComputerTerminal terminal)
            => IdentifyingLogHandler.RetrieveDataFromLog(terminal);

        /// <summary>
        /// Attempt to get the relevant spawned terminal instance, assuming we're in the level
        /// </summary>
        public LG_ComputerTerminal? GetLG_Terminal()
        {
            LG_Zone? zone = GetLG_Zone();
            if (zone == null) return null;
            IntPtr test = TerminalStartingStateData.Pointer;
            return zone.TerminalsSpawnedInZone.FirstOrDefault(t => t.StartStateData.Pointer == test);
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

        // Helper so this can be created inline and also be registered to a zone processor
        public Processor SubscribedTo(MidManager.Processor<Zone.Data> owner)
        {
            owner.RegisterCallback(OnProcessZone);
            return this;
        }

        // Callback to initiate terminal processing when processing a zone
        protected void OnProcessZone(Zone.Data data)
        {
            foreach (var index in data.TerminalIndicies)
                Process(new Data(data, index));
        }
    }

    extension(Layer.Data layerData)
    {
        /// <summary>
        /// Find a terminal given a terminal placement
        /// </summary>
        /// <param name="placement"></param>
        /// <returns></returns>
        public Data FindTerminalByPlacement(TerminalZoneSelectionData placement)
            => new Data(layerData.FindZoneByIndex(placement.LocalIndex), placement.TerminalIndex);

        /// <summary>
        /// Convert a list of terminal selection lists into terminal regions
        /// </summary>
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToTerminalRegions(Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<TerminalZoneSelectionData>> placements)
            => placements.Select(ps => ps.Select(p => layerData.FindZoneByIndex(p.LocalIndex).GetTerminal(p.TerminalIndex))
                .Select(t => new RegionInfo() { Region = t.Region_Terminal, IsBad = t.TerminalStartingStateData.PasswordProtected }));

        /// <summary>
        /// Convert a list of placement lists into terminal regions
        /// </summary>
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToTerminalRegions(DoublePlacementList placements)
            => layerData.PlacementsToTerminalRegions(placements.Iter());

        /// <summary>
        /// Convert an enumerable of placement lists into terminal regions
        /// </summary>
        public IEnumerable<IEnumerable<RegionInfo>> PlacementsToTerminalRegions(IEnumerable<PlacementList> placements)
            => placements.Select(ps => layerData.PlacementsToTerminalRegions(ps));

        /// <summary>
        /// Convert a list of placements into terminal regions
        /// </summary>
        public IEnumerable<RegionInfo> PlacementsToTerminalRegions(PlacementList placements)
            => layerData.PlacementsToTerminalRegions(placements.Iter());

        /// <summary>
        /// Convert an enumerable of placements into terminal regions
        /// </summary>
        public IEnumerable<RegionInfo> PlacementsToTerminalRegions(IEnumerable<ZonePlacementData> placements)
            => placements.SelectMany(p => layerData.PlacementToTerminalRegions(p));

        /// <summary>
        /// Convert a single placement into a list of terminal regions
        /// </summary>
        public IEnumerable<RegionInfo> PlacementToTerminalRegions(ZonePlacementData placement)
        {
            return layerData.FindZoneByPlacement(placement).TerminalDatas.Select(t => new RegionInfo()
            {
                Region = t.Region_Terminal,
                IsBad = t.TerminalStartingStateData.PasswordProtected,
            });
        }
    }

    extension(Game.Data gameData)
    {
        public Processor TerminalProcessor
            => (Processor)gameData.Manager.GetProcessor<Data>();
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
            => zoneData.TerminalIndicies.Select(i => new Data(zoneData, i));

        // Get a specific terminal data for this zone
        public Data GetTerminal(int index)
        {
            if (index < (-zoneData.SpecificTerminalCount) || index >= zoneData.StandardTerminalCount)
            {
                FeatureLogger.Error($"Terminal index {index} does not exist in zone: {zoneData.ZoneName}");
                index = 0;
            }
            return new Data(zoneData, index);
        }
    }

    extension(Objective.Data objectiveData)
    {
        // Make and unstuff terminal region sets for an objective
        public IEnumerable<List<RegionID>> ObjectiveToTerminalRegionSets(int count)
            => objectiveData.UnstuffPlacements(objectiveData.PlacementsToTerminalRegions(objectiveData.ObjectiveData.ZonePlacementDatas), count);
    }
}
