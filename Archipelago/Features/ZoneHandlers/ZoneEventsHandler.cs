using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using EventList = Il2CppSystem.Collections.Generic.List<GameData.WardenObjectiveEventData>;

namespace ReTFO.Archipelago.Features.ZoneHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class ZoneEventsHandler : ArchipelagoFeature
{
    public override string Name => "Zone Events Handler";
    public override string Description
        => "Triggers processing of several zone-based events"
        + "Includes the following events:\n"
        + " - OnBossDeath\n"
        + " - OnDoorScanDone\n"
        + " - OnDoorScanStart\n"
        + " - OnOpenDoor\n"
        + " - OnPortalWarp\n"
        + " - OnTerminalDeactivateAlarm\n"
        + " - OnUnlockDoor";
    public override FeatureGroup Group => FeatureGroups.ZoneHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Triggers some important zone events that don't really have a home elsewhere
    [Zone.Callback]
    public static void AddZoneEvents(Zone.Data data)
    {
        int region = data.GetOrCreateRegion(data.ZoneName);
        if (data.Zone != null)
        {
            Tuple<string, EventList?>[] pairs =
            {
                Tuple.Create<string, EventList?>( $"{data.ZoneName} OnBossDeath",               data.Zone.EventsOnBossDeath ),
                Tuple.Create<string, EventList?>( $"{data.ZoneName} OnDoorScanDone",            data.Zone.EventsOnDoorScanDone ),
                Tuple.Create<string, EventList?>( $"{data.ZoneName} OnDoorScanStart",           data.Zone.EventsOnDoorScanStart ),
                Tuple.Create<string, EventList?>( $"{data.ZoneName} OnOpenDoor",                data.Zone.EventsOnOpenDoor ),
                Tuple.Create<string, EventList?>( $"{data.ZoneName} OnPortalWarp",              data.Zone.EventsOnPortalWarp ),
                Tuple.Create<string, EventList?>( $"{data.ZoneName} OnTerminalDeactivateAlarm", data.Zone.EventsOnTerminalDeactivateAlarm ),
                Tuple.Create<string, EventList?>( $"{data.ZoneName} OnUnlockDoor",              data.Zone.EventsOnUnlockDoor ),
            };
            foreach (var pair in pairs)
            {
                if (pair.Item2.Any())
                {
                    int eventRegion = data.GetOrCreateRegion(pair.Item1);
                    data.AddPath(region, eventRegion);
                    data.ProcessEvents(eventRegion, pair.Item1, pair.Item2!);
                }
            }
        }
        else if (data.DimensionData != null && data.DimensionData.EventsOnBossDeath.Any())
        {   // Only event of note in dimension data is OnBossDeath
            string eventName = $"{data.ZoneName} OnBossDeath";
            int eventRegion = data.GetOrCreateRegion(eventName);
            data.AddPath(region, eventRegion);
            // TODO: Item detecting a boss being spawned?
            data.ProcessEvents(eventRegion, eventName, data.DimensionData.EventsOnBossDeath);
        }
    }

}
