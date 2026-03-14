using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.Features.EventHandlers;
using ReTFO.Archipelago.FeaturesAPI;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class UnlockEventHandler : ArchipelagoFeature
{
    public override string Name => "Unlock Event Handler";
    public override string Description
        => "Handles events which unlock or open doors";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    private class UnlockZoneItem : Item
    {
        public UnlockZoneItem(Zone.Data data)
            : base($"{data.ZoneName} Unlock Event", eRandomizationType.Progression, new List<string> { "All", "Events", "Unlock Events" })
        {
            ZoneData = data;
        }

        [JsonIgnore]
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

        public override void OnItemObtained(StateTracker stateTracker)
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

    private static string GetUnlockEventName(Event.Data data, Zone.Data targetZone, int count)
        => $"{data.EventName} - Unlock Event {count} (for {targetZone.ZoneName})";


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
                string locationName = GetUnlockEventName(data, targetZone, count);
                data.AddLocation(
                    locationName,
                    data.EventRegion,
                    eRandomizationType.Progression,
                    false,
                    GetUnlockEventItem(targetZone)
                );
                EventHelper.ConvertToCheckLocationEvent(e, data, locationName);
            }
        }
    }

}
