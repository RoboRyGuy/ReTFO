using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using EventList = Il2CppSystem.Collections.Generic.List<GameData.WardenObjectiveEventData>;

namespace ReTFO.Archipelago.Features.ZoneHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class ZoneEventsHandler_Tags
{
    extension (Zone.Data data)
    {
        public RegionID Region_OnBossDeathEvents 
            => RegionID.From(data, $"{data.ZoneName} OnBossDeath", data => new("Region entered by killing a boss in a particular zone", data.Region_Zone));
        
        public RegionID Region_OnDoorScanDoneEvents 
            => RegionID.From(data, $"{data.ZoneName} OnDoorScanDone", data => new("Region entered by completing a scan to unlock a particular zone door", data.Region_Zone));
        
        public RegionID Region_OnDoorScanStartEvents 
            => RegionID.From(data, $"{data.ZoneName} OnDoorScanStart", data => new("Region entered by starting a scan to unlock a particular zone door", data.Region_Zone));
        
        public RegionID Region_OnOpenDoorEvents 
            => RegionID.From(data, $"{data.ZoneName} OnOpenDoor", data => new("Region entered by opening a particular zone door", data.Region_Zone));
        
        public RegionID Region_OnPortalWarpEvents 
            => RegionID.From(data, $"{data.ZoneName} OnPortalWarp", data => new("Region entered by trigger a dimsion portal's warp in a particular zone", data.Region_Zone));
        
        public RegionID Region_OnTerminalDeactivateAlarmEvents 
            => RegionID.From(data, $"{data.ZoneName} OnTerminalDeactivateAlarm", data => new("Region entered by executing the DEACTIVATE_ALARMS command associated with a particular zone door's error alarm", data.Region_Zone));
        
        public RegionID Region_OnUnlockDoorEvents 
            => RegionID.From(data, $"{data.ZoneName} OnUnlockDoor", data => new("Region entered by unlocking a particular zone door", data.Region_Zone));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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

    /// <summary>
    /// Helper struct for below, since delegate* cannot be used in generics (ie in tuple types)
    /// </summary>
    private unsafe struct RegionEventPair
    {
        public RegionEventPair(delegate*<Zone.Data, RegionID> builder, EventList? events)
        {
            IDBuilder = builder;
            Events = events;
        }

        public readonly delegate*<Zone.Data, RegionID> IDBuilder;
        public readonly EventList? Events;
    }

    // Triggers some important zone events that don't really have a home elsewhere
    [Zone.Callback]
    public unsafe void AddZoneEvents(Zone.Data data)
    {
        if (data.Zone != null)
        {
            // Note: Using a delegate* to delay ID creation, preventing unecessary regions from being created
            RegionEventPair[] pairs =
            {
                new(&ZoneEventsHandler_Tags.get_Region_OnBossDeathEvents,               data.Zone.EventsOnBossDeath ),
                new(&ZoneEventsHandler_Tags.get_Region_OnDoorScanDoneEvents,            data.Zone.EventsOnDoorScanDone ),
                new(&ZoneEventsHandler_Tags.get_Region_OnDoorScanStartEvents,           data.Zone.EventsOnDoorScanStart ),
                new(&ZoneEventsHandler_Tags.get_Region_OnOpenDoorEvents,                data.Zone.EventsOnOpenDoor ),
                new(&ZoneEventsHandler_Tags.get_Region_OnPortalWarpEvents,              data.Zone.EventsOnPortalWarp ),
                new(&ZoneEventsHandler_Tags.get_Region_OnTerminalDeactivateAlarmEvents, data.Zone.EventsOnTerminalDeactivateAlarm ),
                new(&ZoneEventsHandler_Tags.get_Region_OnUnlockDoorEvents,              data.Zone.EventsOnUnlockDoor ),
            };
            foreach (var pair in pairs)
            {
                if (pair.Events?.Any() ?? false)
                {
                    RegionID id = pair.IDBuilder(data);
                    data.AddPath(new Path() {
                        StartingRegion = data.Region_Zone, 
                        EndingRegion = id
                    });
                    data.ProcessEvents(id, pair.Events!);
                }
            }
        }
        else if (data.DimensionData != null && data.DimensionData.EventsOnBossDeath.Any())
        {   // Only event of note in dimension data is OnBossDeath
            RegionID eventRegion = data.Region_OnBossDeathEvents;
            data.AddPath(new Path()
            {
                StartingRegion = data.Region_Zone, 
                EndingRegion = eventRegion
            });
            // TODO: Item detecting a boss being spawned?
            data.ProcessEvents(eventRegion, data.DimensionData.EventsOnBossDeath);
        }
    }

}
