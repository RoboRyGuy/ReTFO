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
        /// <summary>
        /// Parent tag for all zone door unlock items
        /// </summary>
        public ItemID Item_DoorUnlockEvent
            => ItemID.From(gameData, "Zone Unlock Event Items", data => new("Event items which trigger sec doors to unlock", data.Item_Event));
    }

    extension (Zone.Data data)
    {
        /// <summary>
        /// Parent tag for unlock evnet items for a particular zone's door
        /// </summary>
        public ItemID Item_DoorUnlockEvent_ByZone
            => ItemID.From(
                data,
                $"{data.ZoneName} Unlock Event",
                data => new("Event item which unlocks a particular door", data.Item_DoorUnlockEvent)
            );

        /// <summary>
        /// Unlock event item for a particular zone
        /// </summary>
        /// <param name="isUnlock">If this event strictly unlocks the door, or (if false) also opens it</param>
        public ItemID Item_DoorUnlockEvent_Instance(bool isUnlock)
            => ItemID.From(
                data,
                $"{data.ZoneName} Unlock Event ({(isUnlock ? "Unlock" : "Open")})",
                data => new("Event item which immediately opens a particular door", data.Item_DoorUnlockEvent_ByZone),
                new UnlockEventHandler.UnlockZoneItem(data.Region_Zone, isUnlock)
            );
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

    public class UnlockZoneItem : ExpeditionItem
    {
        public UnlockZoneItem(RegionID zone, bool isUnlock)
            : base(MakeRandData())
        {
            ZoneRegion = zone;
            IsUnlock = isUnlock;
        }

        public static ItemData MakeRandData() => new ItemData { IsProgression = true };

        /// <summary>
        /// Zone that this event unlocks
        /// </summary>
        public RegionID ZoneRegion { get; private init; }

        /// <summary>
        /// If true, this is an unlock event; if false, this is an open event
        /// </summary>
        public bool IsUnlock { get; private init; }

        public override RegionID TargetRegion => ZoneRegion;

        public override void OnEnteredExpedition(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
        {
            Zone.Data zone = new(stateTracker.GameData, ZoneRegion);
            WorldEventManager.ExecuteEvent(new WardenObjectiveEventData
            {
                Type = eWardenObjectiveEventType.UnlockSecurityDoor,
                DimensionIndex = zone.LayerType,
                Layer = zone.LayerType,
                LocalIndex = zone.Zone!.LocalIndex
            });
        }
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
                EventHelper.CreateEventLocation(data, e, count, targetZone.Item_DoorUnlockEvent_Instance(e.Type == eWardenObjectiveEventType.UnlockSecurityDoor));
            else
                FeatureLogger.Debug($"Failed to find zone for unlock event: {data.EventName} #{count}");
        }
    }

}
