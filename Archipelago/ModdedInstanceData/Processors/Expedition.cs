using GameData;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using System.Collections;

public static class Expedition
{
    private record class ScopeData
    {
        public ScopeData(RundownDataBlock rundown, eRundownTier expeditionTier, int expeditionIndex)
        {
            Rundown = rundown;
            ExpeditionTier = expeditionTier;
            ExpeditionIndex = expeditionIndex;
        }

        public RundownDataBlock Rundown { get; init; }
        public eRundownTier ExpeditionTier { get; init; }
        public int ExpeditionIndex { get; init; }
    }

    // Data instance passed to processing giving access to necessary data
    public class Data : Game.Data
    {
        /// <summary>
        /// The custom data stored in the region object for this data
        /// </summary>
        private readonly ScopeData ExpeditionScopeData;

        /// <summary>
        /// The region associated with this expedition
        /// </summary>
        public RegionID Region_Expedition { get; private init; }

        /// <summary>
        /// Construct a new Expedition.Data from a base gameData and expedition targetting info
        /// </summary>
        /// <param name="data">The Game.Data containing this expedition</param>
        /// <param name="rundown">The rundown this expedition appears in</param>
        /// <param name="expeditionTier">The tier of the expedition in the rundown</param>
        /// <param name="expeditionIndex">The index of the expedition in the rundown</param>
        public Data(Game.Data data, RundownDataBlock rundown, eRundownTier expeditionTier, int expeditionIndex)
            : base(data)
        {
            string name = $"{GetExpeditionFromRundown(rundown, expeditionTier, expeditionIndex).GetShortName(expeditionIndex)}";
            Region_Expedition = Regions.LookUpOrCreate(
                data, name,
                data => new("A region for a particular expedition", data.Region_Menu)
            );

            if (!data.Regions.GetDataAllowNull(Region_Expedition, out ExpeditionScopeData!))
                data.Regions.SetData(Region_Expedition, ExpeditionScopeData = new ScopeData(rundown, expeditionTier, expeditionIndex));
            else if (Rundown.Pointer != rundown.Pointer || ExpeditionTier != expeditionTier || ExpeditionIndex != expeditionIndex)
                FeatureLogger.Warning($"Two expeditions share the same short name: {name}");
        }

        /// <summary>
        /// Constructor for constructing from an existing region's data.
        /// This can be invoked if you're reasonably confident the ID is a valid expedition region.
        /// </summary>
        public Data(Game.Data data, RegionID region)
            : base(data)
        {
            Region_Expedition = region;
            ExpeditionScopeData = data.Regions.GetData<ScopeData>(region);
        }

        /// <summary>
        /// Private constructor which bypasses data lookup
        /// </summary>
        private Data(Game.Data data, RegionID region, ScopeData scope)
            : base(data)
        {
            Region_Expedition = region;
            ExpeditionScopeData = scope;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Data(Expedition.Data other)
            : base(other as Game.Data)
        {
            Region_Expedition = other.Region_Expedition;
            ExpeditionScopeData = other.ExpeditionScopeData;
        }

        /// <summary>
        /// The rundown data block containing this expedition
        /// </summary>
        public RundownDataBlock Rundown => ExpeditionScopeData.Rundown;

        /// <summary>
        /// The tier of the expedition in the rundown
        /// </summary>
        public eRundownTier ExpeditionTier => ExpeditionScopeData.ExpeditionTier;

        /// <summary>
        /// The index of the rundown in the tier
        /// </summary>
        public int ExpeditionIndex => ExpeditionScopeData.ExpeditionIndex;

        /// <summary>
        /// Helper class for comparing two expeditions to see if they refer to the same expedition
        /// </summary>
        public class Comparer : EqualityComparer<Expedition.Data>
        {
            public override bool Equals(Expedition.Data? x, Expedition.Data? y)
                => x?.Region_Expedition.Equals(y?.Region_Expedition) ?? y == null;

            public override int GetHashCode([DisallowNull] Expedition.Data obj)
                => (obj.Rundown.Pointer, obj.ExpeditionTier, obj.ExpeditionIndex).GetHashCode();
        }

        /// <summary>
        /// Helper which gets an expedition from a rundown using its tier and index
        /// </summary>
        public static ExpeditionInTierData GetExpeditionFromRundown(RundownDataBlock rundown, eRundownTier tier, int index)
            => tier switch
            {
                eRundownTier.TierA => rundown.TierA[index],
                eRundownTier.TierB => rundown.TierB[index],
                eRundownTier.TierC => rundown.TierC[index],
                eRundownTier.TierD => rundown.TierD[index],
                eRundownTier.TierE => rundown.TierE[index],
                _ => throw new NotSupportedException($"Unrecognized expedition tier: {tier}")
            };

        /// <summary>
        /// Shortcut to retrieve the ExpeditionInTierData for this expedition
        /// </summary>
        public virtual ExpeditionInTierData Expedition => GetExpeditionFromRundown(Rundown, ExpeditionTier, ExpeditionIndex);

        /// <summary>
        /// The name of this expedition
        /// </summary>
        public string ExpeditionName => Regions.LookUpName(Region_Expedition);

        /// <summary>
        /// Checks if a region contains expedition data which can be used to construct an expedition
        /// </summary>
        public static bool IsRegion(Game.Data data, RegionID region)
            => data.Regions.TryGetData<ScopeData>(region, out _);

        /// <summary>
        /// Attempts to construct an Expedition.Data from the provided region.
        /// </summary>
        public static bool TryFromRegion(Game.Data data, RegionID region, [MaybeNullWhen(false)] out Expedition.Data expedition)
        {
            if (data.Regions.TryGetData<ScopeData>(region, out var scope))
            {
                expedition = new(data, region, scope);
                return true;
            }
            else
            {
                expedition = null;
                return false;
            }
        }

        /// <summary>
        /// Get expedition data from the currently-selected expedition. Throws on fail.
        /// </summary>
        /// <returns>The expedition data</returns>
        public static Expedition.Data GetFromCurrentExpedition()
            => Plugin.Get().MidManager.GetProcessedGameData().GetCurrentExpeditionData();

        /// <summary>
        /// Try to get expedition data from the currently-selected expedition.
        /// </summary>
        /// <returns>The expedition data</returns>
        public static bool TryGetFromCurrentExpedition([MaybeNullWhen(false)] out Expedition.Data result)
            => Plugin.Get().MidManager.GetProcessedGameData().TryGetCurrentExpeditionData(out result);

        /// <summary>
        /// Get an expedition data from a specific expedition. Throws on fail
        /// </summary>
        /// <returns>The expedition data</returns>
        public static Expedition.Data GetFromExpedition(ExpeditionInTierData expedition)
            => Plugin.Get().MidManager.GetProcessedGameData().GetExpeditionData(expedition);

        /// <summary>
        /// Try to get expedition data from an existing expedition.
        /// Fails if the expedition is not already registered.
        /// </summary>
        /// <param name="expedition">The expedition to get data for.</param>
        /// <returns>The expedition data</returns>
        public static bool TryGetFromExpedition(ExpeditionInTierData expedition, [MaybeNullWhen(false)] out Expedition.Data result)
           => Plugin.Get().MidManager.GetProcessedGameData().TryGetExpeditionData(expedition, out result);
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

        // Helper so this can be created inline and also be registered to an expedition processor
        public Processor SubscribedTo(MidManager.Processor<Game.Data> owner)
        {
            owner.RegisterCallback(OnProcessGame);
            return this;
        }

        // Callback to initiate processing when processing an expedition
        protected void OnProcessGame(Game.Data data)
        {
            IEnumerable<(RundownDataBlock Rundown, eRundownTier ExpeditionTier, int ExpeditionIndex)> UnpackExpeditions(RundownDataBlock rundown)
            {
                int i;
                for (i = 0; i < rundown.TierA.Count; i++) yield return (Rundown: rundown, ExpeditionTier: eRundownTier.TierA, ExpeditionIndex: i);
                for (i = 0; i < rundown.TierB.Count; i++) yield return (Rundown: rundown, ExpeditionTier: eRundownTier.TierB, ExpeditionIndex: i);
                for (i = 0; i < rundown.TierC.Count; i++) yield return (Rundown: rundown, ExpeditionTier: eRundownTier.TierC, ExpeditionIndex: i);
                for (i = 0; i < rundown.TierD.Count; i++) yield return (Rundown: rundown, ExpeditionTier: eRundownTier.TierD, ExpeditionIndex: i);
                for (i = 0; i < rundown.TierE.Count; i++) yield return (Rundown: rundown, ExpeditionTier: eRundownTier.TierE, ExpeditionIndex: i);
            }
            foreach (var expeditionData in RundownDataBlock.GetAllBlocks().SelectMany(UnpackExpeditions))
            {
                // Filter out invalid expeditions (ie test expeditions, filler expeditions, etc)
                if (!expeditionData.Rundown.internalEnabled) continue;
                ExpeditionInTierData expedition = Expedition.Data.GetExpeditionFromRundown(expeditionData.Rundown, expeditionData.ExpeditionTier, expeditionData.ExpeditionIndex);
                if (!expedition.Enabled) continue;
                if ((LevelLayoutDataBlock.GetBlock(expedition.LevelLayoutData)?.Zones?.Count ?? 0) == 0) continue;

                // List of expeditions which are technically valid but impossible to win
                // Trying to make this filter really precise so there isn't problems with modded expeditions having the same name
                SortedList<string, (uint, string, string)> invalidExpeditions = new()
                {
                    { "A3", (27u, "Geomorph Tester", "Geo Test - VS") },
                    { "A5", (27u, "Geomorph Tester", "Geo Test - LF") },
                };
                string shortName = expedition.GetShortName(expeditionData.ExpeditionIndex);
                if (invalidExpeditions.TryGetValue(shortName, out var tuple)
                    && expeditionData.Rundown.persistentID == tuple.Item1
                    && expeditionData.Rundown.name == tuple.Item2
                    && expedition.Descriptive.PublicName == tuple.Item3
                ) continue;

                // Overwrite the naming mode of this expedition to simplify lookups
                // Basically, now we don't need to know the expedition index in order to look it up - and the name remains the same :)
                expedition.Descriptive.Prefix = shortName;
                expedition.Descriptive.SkipExpNumberInName = true;

                // Processing!
                data.ExpeditionProcessor.Process(new Expedition.Data(data, expeditionData.Rundown, expeditionData.ExpeditionTier, expeditionData.ExpeditionIndex));
            }
        }
    }

    extension(Game.Data gameData)
    {
        /// <summary>
        /// The expedition processor stored in this game data
        /// </summary>
        public Processor ExpeditionProcessor
            => (Processor)gameData.Manager.GetProcessor<Data>();

        /// <summary>
        /// Returns true if the provided region ID is the current expedition's region
        /// </summary>
        public bool IsCurrentExpedition(RegionID region)
        {
            if (!gameData.TryGetCurrentExpeditionID(out RegionID id))
                return false;
            return region.Equals(id);
        }

        /// <summary>
        /// Returns true if the provided region ID is or is the child of the current expedition
        /// </summary>
        public bool IsInCurrentExpedition(RegionID region)
        {
            if (!gameData.TryGetCurrentExpeditionID(out RegionID id))
                return false;
            return gameData.Regions.IsChild(region, id);
        }

        /// <summary>
        /// Get the current expedition's data
        /// </summary>
        public Expedition.Data GetCurrentExpeditionData()
            => gameData.GetExpeditionData(RundownManager.ActiveExpedition ?? throw new NullReferenceException("Cannot get current expedition; no expedition is active!"));

        /// <summary>
        /// Try to get the current expedition's data
        /// </summary>
        public bool TryGetCurrentExpeditionData([MaybeNullWhen(false)] out Expedition.Data result)
        {
            if (RundownManager.ActiveExpedition == null)
            {
                result = null;
                return false;
            }
            return gameData.TryGetExpeditionData(RundownManager.ActiveExpedition, out result);
        }

        /// <summary>
        /// Find an existing instance of expedition data from the provided game data
        /// </summary>
        /// <param name="expedition">The expedition to try and create data for</param>
        /// <returns>The found expedtiion data</returns>
        public Expedition.Data GetExpeditionData(ExpeditionInTierData expedition)
        {
            if (!gameData.TryGetExpeditionData(expedition, out var result))
                throw new KeyNotFoundException($"Failed to find expedition data from game data. Expedition: {expedition.Descriptive.Prefix}");
            return result;
        }

        /// <summary>
        /// Attempts to find an existing instance of expedition data from the provided game data
        /// </summary>
        /// <param name="expedition">The expedition to try and create data for</param>
        /// <param name="result">The found expedition data, if any</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryGetExpeditionData(ExpeditionInTierData expedition, [MaybeNullWhen(false)] out Expedition.Data result)
        {
            if (gameData.TryGetExpeditionID(expedition, out RegionID id))
            {
                result = new(gameData, id);
                return true;
            }
            else
            {
                result = null;
                return false;
            }
        }

        /// <summary>
        /// Attempts to find the expedition ID for the active expedition
        /// </summary>
        public bool TryGetCurrentExpeditionID(out RegionID id)
        {
            if (RundownManager.ActiveExpedition == null)
            {
                id = default;
                return false;
            }
            return gameData.TryGetExpeditionID(RundownManager.ActiveExpedition, out id);
        }

        /// <summary>
        /// Attempts to find the region ID of a particular expedition
        /// </summary>
        public bool TryGetExpeditionID(ExpeditionInTierData expedition, out RegionID id)
            => gameData.Regions.TryLookUpID(expedition.Descriptive.Prefix, out id);

        /// <summary>
        /// Enumerable for all expeditions registered in this game data
        /// </summary>
        public IEnumerable<Expedition.Data> GetAllExpeditions() => new GetAllExpeditionsEnumerable(gameData);
    }

    /// <summary>
    /// Allows enumeration of a game data for its expedition regions
    /// </summary>
    private class GetAllExpeditionsEnumerable : IEnumerable<Expedition.Data>
    {
        readonly Game.Data m_data;

        public GetAllExpeditionsEnumerable(Game.Data data) => m_data = data;
        public IEnumerator<Data> GetEnumerator() => new GetAllExpeditionsEnumerator(m_data);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Actual enumerator for this enumerable
        /// </summary>
        private class GetAllExpeditionsEnumerator : IEnumerator<Expedition.Data>
        {
            public GetAllExpeditionsEnumerator(Game.Data data)
            {
                m_data = data;
                m_regions = null!;
                Reset();
            }

            readonly Game.Data m_data;
            IEnumerator<RegionID> m_regions;
            Expedition.Data? m_current = null;

            public Data Current => m_current ?? throw new NullReferenceException("GetAllExpeditionsEnumerator was not enumerated correctly!");
            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                while (m_regions.MoveNext())
                {
                    RegionID id = m_regions.Current;
                    if (Expedition.Data.TryFromRegion(m_data, id, out var exp))
                    {
                        m_current = exp;
                        return true;
                    }
                }
                m_current = null;
                return false;
            }

            public void Reset() => m_regions = m_data.Regions.GetAllIDs().GetEnumerator();
            public void Dispose() { }
        }
    }

}
