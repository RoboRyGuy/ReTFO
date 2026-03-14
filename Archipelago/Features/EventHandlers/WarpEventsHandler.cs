using Clonesoft.Json;
using GameData;
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Text.Json.Serialization;

[EnableFeatureByDefault]
public class WarpEventsHandler : ArchipelagoFeature
{
    public override string Name => "Warp Events Helper";
    public override string Description => "Handles dimension warp events as paths";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// Dimension warp event targetting a specific zone
    /// </summary>
    private class DimensionWarpItem : Item
    { 
        public DimensionWarpItem(Zone.Data targetZone, bool clearDimension)
            : base($"Warp to {targetZone.ZoneName}{(clearDimension ? "(with DimensionClearing)" : "")}", eRandomizationType.None, new List<string> { "All", "Events", "Warps", "Event Warps" })
        {
            TargetZone = targetZone;
            ClearDimension = clearDimension;
        }

        [JsonIgnore]
        public Zone.Data TargetZone { get; set; }

        [JsonIgnore]
        public bool ClearDimension { get; set; }

        public override void OnItemObtained(StateTracker stateTracker)
        {
            if (TargetZone.IsCurrentExepdition())
                OnStartExpeditionWithItem(stateTracker, TargetZone);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (TargetZone == data)
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Initiating Dimension Warp to {Enum.GetName<eDimensionIndex>(TargetZone.LayerType)}", 2f);
                terminal.AddLine($"Warp will occur in 3 seconds. Enjoy the scenery :)");
            };

            yield return () => WorldEventManager.ExecuteEvent(new()
            {
                Type = eWardenObjectiveEventType.DimensionWarpTeam,
                DimensionIndex = TargetZone.LayerType,
                Layer = TargetZone.LayerType,
                LocalIndex = TargetZone.Zone?.LocalIndex ?? eLocalZoneIndex.Zone_0,
                Delay = 3f,
                ClearDimension = ClearDimension,
            });
        }
    }

    public static Item GetDimensionWarpEventItem(Zone.Data targetZone, bool cleanDimension)
        => targetZone.GetItem(new DimensionWarpItem(targetZone, cleanDimension));

    private static string GetDimensionWarpEventLocationName(Event.Data data, int count)
        => $"{data.EventName} - Dimension Warp #{count}";

    // Warps between dimensions when triggered by an event
    [Event.Callback]
    public static void AddEventWarps(Event.Data data)
    {
        int count = 0;
        foreach (var e in data)
        {
            // Filter out unwanted events. Note: we don't care about flashes because they're transient
            if (e.Type != eWardenObjectiveEventType.DimensionWarpTeam)
                continue;
            ++count;

            Zone.Data? targetZone = data.FindZoneByEvent(e);
            if (targetZone == null)
            {
                FeatureLogger.Warning($"Failed to identify warp target for event: {data.EventName}");
                continue;
            }

            // Add warp as item / location pair
            Item item = GetDimensionWarpEventItem(targetZone, e.ClearDimension);
            Location loc = data.AddLocation(
                GetDimensionWarpEventLocationName(data, count),
                data.EventRegion,
                eRandomizationType.Progression,
                false,
                item
            );

            // Mark event as location
            EventHelper.ConvertToCheckLocationEvent(e, loc.ID);

            // Add path represented by warp
            Path path = data.AddPath(
                data.EventRegion,
                data.GetOrCreateRegion(targetZone.ZoneName)
            );
            path.RequiredItem = item.Name;
            path.RequiredItemCount = 1u;
        }
    }

}
