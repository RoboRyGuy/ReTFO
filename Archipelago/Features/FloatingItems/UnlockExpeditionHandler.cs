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

namespace ReTFO.Archipelago.Features.FloatingItems;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class UnlockExpeditionHandler_Tags
{
    extension (Game.Data data)
    {
        public TagResolver Tag_ExpeditionUnlocks
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Expedition Unlock Items", "Items which trigger expeditions to be unlocked", gd.Tag_OptionalItems));
    }
}

// Handles the UnlockExpedition item, and the path between menu and the expedition's first zone
[EnableFeatureByDefault]
public class UnlockExpeditionHandler : ArchipelagoFeature
{
    public override string Name => "Unlock Expedition Handler";
    public override string Description 
        => "Locks expeditions and adds floating expedition unlock items which unlock them";
    public override FeatureGroup Group => FeatureGroups.FloatingHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private static IEnumerable<ExpeditionInTierData> UnpackRundown(RundownDataBlock rundown)
    {
        foreach (var expedition in rundown.TierA) yield return expedition;
        foreach (var expedition in rundown.TierB) yield return expedition;
        foreach (var expedition in rundown.TierC) yield return expedition;
        foreach (var expedition in rundown.TierD) yield return expedition;
        foreach (var expedition in rundown.TierE) yield return expedition;
    }

    private static IEnumerable<ExpeditionInTierData> GetActiveExpeditions()
        => Globals.Global.ActiveRundownIds?.Select(RundownDataBlock.GetBlock).SelectMany(UnpackRundown) ?? Enumerable.Empty<ExpeditionInTierData>();

    private class ExpeditionUnlockItem : Item
    {
        public ExpeditionUnlockItem(Expedition.Data expedition)
            : base(MakeTag(expedition), MakeRandData())
        {
            ExpeditionData = expedition;
            Tag2 = expedition.Tag_UnlockItems_ByExpedition; // Requires the relevant expedition to be part of the rando
        }

        public static TagResolver MakeTag(Expedition.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Expedition Unlock", "Item which unlocks a particular expedition", gd.Tag_ExpeditionUnlocks));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true, DoLoseOnStart = true };

        public Expedition.Data ExpeditionData { get; set; }

        private ExpeditionInTierData FindExpedition()
        {
            foreach (var expedition in GetActiveExpeditions())
                if (Expedition.Data.FromExpedition(expedition) == ExpeditionData) return expedition;
            FeatureLogger.Error($"Failed to find expedition in loaded rundowns: {ExpeditionData.ExpeditionName}");
            throw new NotSupportedException();
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player = null)
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

    public static KeyedItem GetExpeditionUnlockItem(Expedition.Data data)
    {
        if (data.TryLookupItem(ExpeditionUnlockItem.MakeTag(data), out var item))
            return item;

        Item newItem = new ExpeditionUnlockItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    [Expedition.Callback]
    public void AddExpeditionUnlock(Expedition.Data data)
    {
        KeyedItem reqItem = GetExpeditionUnlockItem(data);
        data.AddFloatingItem(reqItem.ID);
        data.AddPath(new Path()
        {
            Name = $"{data.ExpeditionName} Expdition Unlock",
            StartingRegion = data.MenuRegion,
            EndingRegion = data.StartingRegion,
            ReqItem = reqItem.PathReqs,
            ReqCount = 1u,
        });
    }

}
