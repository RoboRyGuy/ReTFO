using Clonesoft.Json;
using GameData;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

// Handles the UnlockExpedition item, and the path between menu and the expedition's first zone
[EnableFeatureByDefault]
public class UnlockExpeditionHandler : ArchipelagoFeature
{
    public override string Name => "Unlock Expedition Handler";
    public override string Description 
        => "Locks expeditions and adds floating expedition unlock items which unlock them";
    public override FeatureGroup Group => FeatureGroups.Archipelago;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public const string ExpeditionUnlocksCat = "Expedition Unlocks";

    private static IEnumerable<ExpeditionInTierData> UnpackRundown(RundownDataBlock rundown)
    {
        foreach (var expedition in rundown.TierA) yield return expedition;
        foreach (var expedition in rundown.TierB) yield return expedition;
        foreach (var expedition in rundown.TierC) yield return expedition;
        foreach (var expedition in rundown.TierD) yield return expedition;
        foreach (var expedition in rundown.TierE) yield return expedition;
    }

    private static IEnumerable<ExpeditionInTierData> GetActiveExpeditions()
        => Globals.Global.ActiveRundownIds.Select(RundownDataBlock.GetBlock).SelectMany(UnpackRundown);

    // Lock all expeditions and only unlock ones we have the item for
    public override void OnEnable()
    {
        foreach (var expedition in GetActiveExpeditions())
            expedition.Accessibility = eExpeditionAccessibility.AlwayBlock;

        var stateTracker = StateTracker.Get();
        foreach (var pair in stateTracker.CollectedItemCounts)
            if (pair.Key is ExpeditionUnlockItem unlockItem) unlockItem.OnItemObtained(stateTracker, 0);
    }

    // Unlock all expeditions
    public override void OnDisable()
    {
        foreach (var expedition in GetActiveExpeditions())
            expedition.Accessibility = eExpeditionAccessibility.AlwaysAllow;
    }

    private class ExpeditionUnlockItem : Item
    {
        public ExpeditionUnlockItem(Expedition.Data expedition)
            : base($"Expedition {expedition.ExpeditionName} Unlock")
        {
            ExpeditionData = expedition;
            m_randData = new RandomizationData()
            {
                IsProgression = true,
                DoUncollectOnRandom = true,
                Categories = new() { expedition.ExpeditionName, $"{expedition.ExpeditionName} Start Items", ExpeditionUnlocksCat }
            };
        }

        [JsonIgnore]
        public Expedition.Data ExpeditionData { get; set; }

        [JsonIgnore]
        private RandomizationData m_randData;
        public override RandomizationData RandData => m_randData;

        private ExpeditionInTierData FindExpedition()
        {
            foreach (var expedition in GetActiveExpeditions())
                if (Expedition.Data.FromExpedition(expedition) == ExpeditionData) return expedition;
            FeatureLogger.Error($"Failed to find expedition in loaded rundowns: {ExpeditionData.ExpeditionName}");
            throw new NotSupportedException();
        }

        public override void OnItemObtained(StateTracker stateTracker, long sourceLocationId, PlayerAgent? player = null)
        {
            if (ArchipelagoFeatureHelper.GetFeature<UnlockExpeditionHandler>().Enabled)
                FindExpedition().Accessibility = eExpeditionAccessibility.AlwaysAllow;
        }

        public override void OnItemLost(StateTracker stateTracker)
        {
            if (ArchipelagoFeatureHelper.GetFeature<UnlockExpeditionHandler>().Enabled)
                FindExpedition().Accessibility = eExpeditionAccessibility.AlwayBlock;
        }
    }

    public static Item GetExpeditionUnlockItem(Expedition.Data data)
        => data.GetItem(new ExpeditionUnlockItem(data));

    [Expedition.Callback]
    public static void AddExpeditionUnlock(Expedition.Data data)
    {
        Item reqItem = GetExpeditionUnlockItem(data);

        Path path = data.AddPath(
            data.GetOrCreateRegion(data.MenuRegionName),
            data.GetOrCreateRegion(data.MainLayer.FirstZone.ZoneName)
        );
        path.RequiredItem = reqItem.Name;
        path.RequiredItemCount = 1u;

        data.AddFloatingItem(reqItem);
    }

}
