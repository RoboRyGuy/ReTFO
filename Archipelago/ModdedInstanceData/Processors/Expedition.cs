
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Expedition
{
    // Interface class passed to processing giving access to necessary data
    public abstract class Data : Game.Data
    {
        // Minimal interface implementation
        public abstract Game.Data GameData { get; }
        public abstract RundownDataBlock Rundown { get; }
        public abstract eRundownTier ExpeditionTier { get; }
        public abstract int ExpeditionIndex { get; }

        // The actual expedition being processed
        public virtual ExpeditionInTierData Expedition => ExpeditionTier switch
        {
            eRundownTier.TierA => Rundown.TierA[ExpeditionIndex],
            eRundownTier.TierB => Rundown.TierB[ExpeditionIndex],
            eRundownTier.TierC => Rundown.TierC[ExpeditionIndex],
            eRundownTier.TierD => Rundown.TierD[ExpeditionIndex],
            eRundownTier.TierE => Rundown.TierE[ExpeditionIndex],
            _ => throw new NotImplementedException()
        };

        // Names of things in this expedition
        public virtual string ExpeditionName => Expedition.GetShortName(ExpeditionIndex);

        // Get expedition data for the currently loaded expedition. Throws if not in an expedition
        public static Data FromCurrentExpedition()
            => Data.FromExpedition(RundownManager.ActiveExpedition);

        // Get expedition data for any given expedition
        public static Data FromExpedition(ExpeditionInTierData expedition)
        {
            Plugin plugin = Plugin.Get();
            string? expeditionName = expedition.Descriptive?.Prefix;
            var data = expeditionName != null ? plugin.MidManager.LookupExpedition(expeditionName) : null;
            if (data == null)
            {
                string error = $"Failed to retrieve expedition: {expeditionName}";
                FeatureLogger.Error(error);
                throw new Exception(error);
            }
            return data;
        }

        // Check if this expedition data is for the expedition currently selected
        public bool IsCurrentExepdition()
        {
            if (this is Layer.Data layerData)
                return Data.FromCurrentExpedition() == layerData.ExpeditionData;
            else
                return Data.FromCurrentExpedition() == this;
        }

        // Custom comparisons for expedition.data
        public override bool Equals(object? obj)
        {
            if (obj is not Data other) return false;
            return true
                && (GameData.Equals(other.GameData))
                && (Rundown.Pointer.Equals(other.Rundown.Pointer))
                && (ExpeditionTier.Equals(other.ExpeditionTier))
                && (ExpeditionIndex.Equals(other.ExpeditionIndex))
            ;
        }
        public static bool operator ==(Data? data, Data? other) => data is null ? other is null : data.Equals(other);
        public static bool operator !=(Data? data, Data? other) => data is null ? other is null : !data.Equals(other);
        public override int GetHashCode()
        {
            return Tuple.Create(
                GameData,
                Rundown.Pointer,
                ExpeditionTier,
                ExpeditionIndex
            ).GetHashCode();
        }

        // Implementing Game.Data
        public override Dictionary<Type, Game.IProcessor> Processors => GameData.Processors;
        public override List<Region> RegionList => GameData.RegionList;
        public override Dictionary<string, int> RegionLookup => GameData.RegionLookup;
        public override List<Location> LocationList => GameData.LocationList;
        public override Dictionary<string, Location> LocationLookup => GameData.LocationLookup;
        public override List<Item> ItemList => GameData.ItemList;
        public override Dictionary<string, Item> ItemLookup => GameData.ItemLookup;
        public override List<long> FloatingItemIds => GameData.FloatingItemIds;
    }

    // Minimal concrete implementation of Data
    protected class BaseData : Data
    {
        // Standard constructor
        public BaseData(Game.Data gameData, RundownDataBlock rundown, eRundownTier expeditionTier, int expeditionIndex)
        {
            // A lot of our expedition checks compare items by reference, so this helps avoid unwated implicit casts
            if (gameData is Data expeditionData)
                this.gameData = expeditionData.GameData;
            else
                this.gameData = gameData;
            this.rundown = rundown;
            this.expeditionTier = expeditionTier;
            this.expeditionIndex = expeditionIndex;
        }

        // Copy constructor
        public BaseData(Data source)
        {
            gameData = source.GameData;
            rundown = source.Rundown;
            expeditionTier = source.ExpeditionTier;
            expeditionIndex = source.ExpeditionIndex;
        }

        // Concretes
        private readonly Game.Data gameData;
        private readonly RundownDataBlock rundown;
        private readonly eRundownTier expeditionTier;
        private readonly int expeditionIndex;

        // Interface implementation
        public override Game.Data GameData => gameData;
        public override RundownDataBlock Rundown => rundown;
        public override eRundownTier ExpeditionTier => expeditionTier;
        public override int ExpeditionIndex => expeditionIndex;
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

        // Helper so this can be created inline and also be registered to an expedition processor
        public Processor SubscribedTo(Game.Processor owner)
        {
            owner.RegisterCallback(OnProcessGame);
            return this;
        }

        // Callback to initiate processing when processing an expedition
        protected void OnProcessGame(Game.Data data)
        {
            Plugin plugin = Plugin.Get();

            IEnumerable<Expedition.Data> UnpackExpeditions(RundownDataBlock rundown)
            {
                int i;
                for (i = 0; i < rundown.TierA.Count; i++) yield return Expedition.MakeData(data, rundown, eRundownTier.TierA, i);
                for (i = 0; i < rundown.TierB.Count; i++) yield return Expedition.MakeData(data, rundown, eRundownTier.TierB, i);
                for (i = 0; i < rundown.TierC.Count; i++) yield return Expedition.MakeData(data, rundown, eRundownTier.TierC, i);
                for (i = 0; i < rundown.TierD.Count; i++) yield return Expedition.MakeData(data, rundown, eRundownTier.TierD, i);
                for (i = 0; i < rundown.TierE.Count; i++) yield return Expedition.MakeData(data, rundown, eRundownTier.TierE, i);
            }
            var expeditionsToProcess = RundownDataBlock.GetAllBlocks().SelectMany(UnpackExpeditions);

            using var enumerator = expeditionsToProcess.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var expeditionData = enumerator.Current;

                // Filter out invalid expeditions (ie test expeditions, filler expeditions, etc)
                if (!expeditionData.Rundown.internalEnabled) continue;
                if (!expeditionData.Expedition.Enabled) continue;
                if ((LevelLayoutDataBlock.GetBlock(expeditionData.Expedition.LevelLayoutData)?.Zones?.Count ?? 0) == 0) continue;

                // List of expeditions which are technically valid but impossible to win
                // Trying to make this filter really precise so there isn't problems with modded expeditions having the same name
                SortedList<string, Tuple<uint, string, string>> invalidExpeditions = new()
                {
                    { "A3", Tuple.Create(27u, "Geomorph Tester", "Geo Test - VS") },
                    { "A5", Tuple.Create(27u, "Geomorph Tester", "Geo Test - LF") },
                };
                if (invalidExpeditions.TryGetValue(expeditionData.ExpeditionName, out var tuple)
                    && expeditionData.Rundown.persistentID == tuple.Item1
                    && expeditionData.Rundown.name == tuple.Item2
                    && expeditionData.Expedition.Descriptive.PublicName == tuple.Item3
                ) continue;

                // Try and register the expeiditon - make sure the namespace is clear
                if (!plugin.MidManager.TryRegisterExpedition(expeditionData.ExpeditionName, expeditionData))
                {
                    FeatureLogger.Error($"Skipping processing for expedition due to duplicate naming: {expeditionData.ExpeditionName}");
                    continue;
                }

                // Processing!
                data.ExpeditionProcessor.Process(expeditionData);
            }
        }
    }

    // Allow the creation of processing data
    public static Data MakeData(Game.Data gameData, RundownDataBlock rundown, eRundownTier expeditionTier, int expeditionIndex)
        => new BaseData(gameData, rundown, expeditionTier, expeditionIndex);

    extension(Game.Data gameData)
    {
        public Processor ExpeditionProcessor
            => (Processor)gameData.GetProcessor<Data>();
    }
}
