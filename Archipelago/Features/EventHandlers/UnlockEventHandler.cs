using GameData;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class UnlockEventHandler_Tags
{
    extension (Game.Data gameData)
    {
        public TagResolver Tag_UnlockEventItem
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Unlock Event Items", "Event items which trigger sec doors to unlock", gd.Tag_EventItems));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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
            : base(MakeTag(data), MakeRandData())
        {
            ZoneData = data;
        }

        public static TagResolver MakeTag(Zone.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ZoneName} Unlock Event", "Event item which unlocks a particular door", gd.Tag_UnlockEventItem));

        public static ItemData MakeRandData() => new ItemData { IsProgression = true };

        /// <summary>
        /// Zone that this event unlocks
        /// </summary>
        public Zone.Data ZoneData { get; set; }

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

        public override Expedition.Data? RequiredExpedition => ZoneData;

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (ZoneData.IsCurrentlyInExpedition()) UnlockZoneNow();
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ZoneData.IsSameExpedition(data)) UnlockZoneNow();
        }
    }

    public static KeyedItem GetUnlockEventItem(Zone.Data data)
    {
        if (data.TryLookupItem(UnlockZoneItem.MakeTag(data), out var item))
            return item;

        Item newItem = new UnlockZoneItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    [Event.Callback]
    public void ProcessUnlockEvents(Event.Data data)
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
                EventHelper.ConvertToCheckLocationEvent(data, e, count, GetUnlockEventItem(targetZone).ID);
            }
            else
            {
                FeatureLogger.Debug($"Failed to find zone for unlock event: {data.EventName} #{count}");
            }
        }
    }

}
