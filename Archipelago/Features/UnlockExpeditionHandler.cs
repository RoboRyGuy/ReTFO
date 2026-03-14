using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
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

    private class ExpeditionUnlockItem : Item
    {
        public ExpeditionUnlockItem(Expedition.Data expedition)
            : base($"Expedition {expedition.ExpeditionName} Unlock", eRandomizationType.Progression, new List<string>() { ExpeditionUnlocksCat })
        {
            ExpeditionData = expedition;
        }

        [JsonIgnore]
        public Expedition.Data ExpeditionData { get; set; }

        public override void OnItemObtained(StateTracker stateTracker)
        {
            static IEnumerable<ExpeditionInTierData> UnpackRundown(RundownDataBlock block)
                => block.TierA.Iter()
                .Concat(block.TierB.Iter()).Concat(block.TierC.Iter())
                .Concat(block.TierD.Iter()).Concat(block.TierE.Iter());

            foreach (var expedition in Globals.Global.ActiveRundownIds.Select(RundownDataBlock.GetBlock).SelectMany(UnpackRundown))
            {
                if (Expedition.Data.FromExpedition(expedition) == ExpeditionData)
                {
                    expedition.Accessibility = eExpeditionAccessibility.AlwaysAllow;
                    break;
                }
            }
        }
    }

    public static Item GetExpeditionUnlockedItem(Expedition.Data data)
        => data.GetItem(new ExpeditionUnlockItem(data));

    [Expedition.Callback]
    public static void AddExpeditionUnlock(Expedition.Data data)
    {
        Item reqItem = GetExpeditionUnlockedItem(data);

        Path path = data.AddPath(
            data.GetOrCreateRegion(data.MenuRegionName),
            data.GetOrCreateRegion(data.MainLayer.FirstZone.ZoneName)
        );
        path.RequiredItem = reqItem.Name;
        path.RequiredItemCount = 1u;

        data.RegisterFloatingItem(reqItem);
    }

}
