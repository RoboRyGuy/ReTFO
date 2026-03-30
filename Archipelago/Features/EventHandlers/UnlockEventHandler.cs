using Clonesoft.Json;
using GameData;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class UnlockEventHandler : ArchipelagoFeature
{
    public override string Name => "Unlock Event Handler";
    public override string Description
        => "Handles events which unlock or open doors";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private class UnlockZoneItem : Item
    {
        public UnlockZoneItem(Zone.Data data)
            : base($"{data.ZoneName} Unlock Event")
        {
            ZoneData = data;
        }

        /// <summary>
        /// Zone that this event unlocks
        /// </summary>
        [JsonIgnore]
        public Zone.Data ZoneData { get; set; }

        private static RandomizationData s_randData = new()
        {
            IsProgression = true,
            Categories = new() { "All", "Events", "Unlock Events" }
        };
        public override RandomizationData RandData => s_randData;

        private void UnlockZoneNow()
        {
            WorldEventManager.ExecuteEvent(new WardenObjectiveEventData
            {
                Type = eWardenObjectiveEventType.UnlockSecurityDoor,
                DimensionIndex = ZoneData.LayerType,
                Layer = ZoneData.LayerType,
                LocalIndex = ZoneData.Zone!.LocalIndex
            });
        }

        public override void OnItemObtained(StateTracker stateTracker, long sourceLocationId, PlayerAgent? player)
        {
            if (Expedition.Data.FromCurrentExpedition() == ZoneData.ExpeditionData)
                UnlockZoneNow();
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (data == ZoneData.ExpeditionData)
                UnlockZoneNow();
        }
    }

    public static Item GetUnlockEventItem(Zone.Data data)
        => data.GetItem(new UnlockZoneItem(data));

    [Event.Callback]
    public static void ProcessUnlockEvents(Event.Data data)
    {
        int count = 0;
        foreach (var e in data)
        {
            if (e.Type != eWardenObjectiveEventType.UnlockSecurityDoor && e.Type != eWardenObjectiveEventType.OpenSecurityDoor)
                continue;
            ++count;

            Zone.Data? targetZone = data.FindZoneByEvent(e);
            if (targetZone != null)
            {
                Item item = GetUnlockEventItem(targetZone);
                EventHelper.ConvertToCheckLocationEvent(data, e, count, item);
            }
            else
            {
                FeatureLogger.Debug($"Failed to find zone for unlock event: {data.EventName} #{count}");
            }
        }
    }

}
