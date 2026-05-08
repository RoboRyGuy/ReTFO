
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using System.Diagnostics.CodeAnalysis;

public static class Expedition
{
    // Data instance passed to processing giving access to necessary data
    public class Data : Game.Data
    {
        /// <summary>
        /// The rundown data block containing this expedition
        /// </summary>
        public RundownDataBlock Rundown { get; private init;}

        /// <summary>
        /// The tier of the expedition in the rundown
        /// </summary>
        public eRundownTier ExpeditionTier { get; init;}

        /// <summary>
        /// The index of the rundown in the tier
        /// </summary>
        public int ExpeditionIndex { get; init;  }

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
            Rundown = rundown;
            ExpeditionTier = expeditionTier;
            ExpeditionIndex = expeditionIndex;
        }
        
        /// <summary>
        /// Copy constructor
        /// </summary>
        public Data(Expedition.Data other)
            : base(other as Game.Data)
        {
            Rundown = other.Rundown;
            ExpeditionTier = other.ExpeditionTier ;
            ExpeditionIndex = other.ExpeditionIndex;
        }

        /// <summary>
        /// Helper class for comparing two expeditions to see if they refer to the same expedition
        /// </summary>
        public class Comparer : EqualityComparer<Expedition.Data>
        {
            public override bool Equals(Expedition.Data? x, Expedition.Data? y)
                => x != null ? y?.IsSameExpedition(x) ?? false : y == null;

            public override int GetHashCode([DisallowNull] Expedition.Data obj)
                => obj.GetHashCode();
        }

        /// <summary>
        /// Shortcut to retrieve the ExpeditionInTierData for this expedition
        /// </summary>
        public virtual ExpeditionInTierData Expedition => ExpeditionTier switch
        {
            eRundownTier.TierA => Rundown.TierA[ExpeditionIndex],
            eRundownTier.TierB => Rundown.TierB[ExpeditionIndex],
            eRundownTier.TierC => Rundown.TierC[ExpeditionIndex],
            eRundownTier.TierD => Rundown.TierD[ExpeditionIndex],
            eRundownTier.TierE => Rundown.TierE[ExpeditionIndex],
            _ => throw new NotSupportedException($"Unrecognized expedition tier: {ExpeditionTier}")
        };

        /// <summary>
        /// The name of this expedition
        /// </summary>
        public string ExpeditionName => Expedition.GetShortName(ExpeditionIndex);

        /// <summary>
        /// Parent tag for unlock items for this expedition. Typically only parents the expedition unlock item.
        /// </summary>
        public TagResolver UnlockItemsTag 
            => new TagResolver(this, gd => gd.LookupOrCreateTag($"{ExpeditionName} Unlock Items", $"Floating items required to start / progress expedition {ExpeditionName}", gd.Tag_UnlockItems));

        /// <summary>
        /// Parent tag for clear items for this expedition. Typically is just the sector clears.
        /// </summary>
        public TagResolver GoalItemsTag
            => new TagResolver(this, gd => gd.LookupOrCreateTag($"{ExpeditionName} Goal Items", $"Items indicating a successful full clear of {ExpeditionName}", gd.Tag_GoalItems));

        /// <summary>
        /// Name of the objective start region for the expedition. This is used by all objectives for all layers
        /// </summary>
        public string ObjectiveStartRegionName => $"{ExpeditionName} Elevator Landed";

        /// <summary>
        /// RegionID for the objective start region for the expedition. This is used by all objectives for all layers
        /// </summary>
        public RegionID ObjectiveStartRegion => LookupOrCreateRegion(ObjectiveStartRegionName);

        /// <summary>
        /// Get expedition data for the currently loaded expedition. Throws if not in an expedition
        /// </summary>
        /// <returns></returns>
        public static Data FromCurrentExpedition()
            => Data.FromExpedition(RundownManager.ActiveExpedition);

        /// <summary>
        /// Get expedition data for any given expedition
        /// </summary>
        /// <param name="expedition">The expedition data to fetch data for</param>
        /// <returns>The expedition's data</returns>
        /// <exception cref="ArgumentException">The expedition data is not registered / found</exception>
        /// <remarks>
        /// This method assumes the requested expedition is successfully processed and registered in Game.Data.
        /// @TODO: Let this create Expedition.Data without processing? Identify expeditions which aren't registered?
        /// </remarks>
        public static Data FromExpedition(ExpeditionInTierData expedition)
        {
            Game.Data gameData = Plugin.Get().MidManager.GetProcessedGameData();
            string? expeditionName = expedition.Descriptive?.Prefix;
            if (expeditionName == null || !gameData.TryLookupExpedition(expeditionName, out Expedition.Data? data))
            {
                string error = $"Failed to retrieve expedition: {expeditionName}";
                FeatureLogger.Error(error);
                throw new ArgumentException(error);
            }
            return data;
        }

        /// <summary>
        /// Attempt to find registered expedition data for the requested expedition.
        /// Returns null if it fails, but otherwise logs no errors.
        public static Data? TryFromExpedition(ExpeditionInTierData expedition)
        {
            Game.Data gameData = Plugin.Get().MidManager.GetProcessedGameData();
            string? expeditionName = expedition.Descriptive?.Prefix;
            if (expeditionName == null || !gameData.TryLookupExpedition(expeditionName, out Expedition.Data? data))
                return null;
            else return data;
        }

        /// <summary>
        /// Returns true if this expedition is the same expedition as the other expedition
        /// </summary>
        /// <param name="other">The expedition to test against</param>
        /// <returns>True if they're the same, false otherwise</returns>
        public bool IsSameExpedition(Expedition.Data other)
            => Rundown.Pointer == other.Rundown.Pointer
            && ExpeditionTier == other.ExpeditionTier
            && ExpeditionIndex == other.ExpeditionIndex;

        /// <summary>
        /// Check if this expedition data is for the expedition currently selected
        /// </summary>
        /// <returns>True if this is the currently-selected expedition, false otherwise.</returns>
        public bool IsCurrentExepdition()
            => RundownManager.ActiveExpedition != null && Data.FromCurrentExpedition().IsSameExpedition(this);

        /// <summary>
        /// Check if this expedition is both the selected expedition and that we are currently in-level.
        /// </summary>
        public bool IsCurrentlyInExpedition()
            => IsCurrentExepdition() && GameStateManager.IsInExpedition;
    }

    // Attribute used to mark static functions which should autoregister to this processor
    [AttributeUsage(AttributeTargets.Method)]
    public class Callback : MidManager.Processor<Data>.Callback { }

    // Actual class wrapping an event processing instance
    public class Processor : MidManager.Processor<Data>
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
        public Processor SubscribedTo(MidManager.Processor<Game.Data> owner)
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
                for (i = 0; i < rundown.TierA.Count; i++) yield return new Expedition.Data(data, rundown, eRundownTier.TierA, i);
                for (i = 0; i < rundown.TierB.Count; i++) yield return new Expedition.Data(data, rundown, eRundownTier.TierB, i);
                for (i = 0; i < rundown.TierC.Count; i++) yield return new Expedition.Data(data, rundown, eRundownTier.TierC, i);
                for (i = 0; i < rundown.TierD.Count; i++) yield return new Expedition.Data(data, rundown, eRundownTier.TierD, i);
                for (i = 0; i < rundown.TierE.Count; i++) yield return new Expedition.Data(data, rundown, eRundownTier.TierE, i);
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

                // Overwrite the naming mode of this expedition to simplify lookups
                // Basically, now we don't need to know the expedition index in order to look it up - and the name remains the same :)
                expeditionData.Expedition.Descriptive.Prefix = expeditionData.ExpeditionName;
                expeditionData.Expedition.Descriptive.SkipExpNumberInName = true;

                // Try and register the expediton - make sure the namespace is clear
                if (!data.TryRegisterExpedition(expeditionData.ExpeditionName, expeditionData))
                {
                    FeatureLogger.Error($"Skipping processing for expedition due to duplicate naming: {expeditionData.ExpeditionName}");
                    continue;
                }

                // Processing!
                data.ExpeditionProcessor.Process(expeditionData);
            }
        }
    }

    extension(Game.Data gameData)
    {
        public Processor ExpeditionProcessor
            => (Processor)gameData.Manager.GetProcessor<Data>();
    }
}
