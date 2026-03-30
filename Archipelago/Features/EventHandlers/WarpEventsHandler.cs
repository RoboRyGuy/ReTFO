using Clonesoft.Json;
using GameData;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

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
            : base($"Warp to {targetZone.ZoneName}{(clearDimension ? "(with DimensionClearing)" : "")}")
        {
            TargetZone = targetZone;
            ClearDimension = clearDimension;
        }

        /// <summary>
        /// The zone the warp goes to
        /// </summary>
        [JsonIgnore]
        public Zone.Data TargetZone { get; set; }

        /// <summary>
        /// If true, the previous dimension will be cleared when the players leave
        /// </summary>
        [JsonIgnore]
        public bool ClearDimension { get; set; }

        private static RandomizationData s_randData = new()
        {
            IsProgression = true,
            Categories = new() { "All", "Events", "Warps", "Event Warps" },
        };
        public override RandomizationData RandData => s_randData;

        public override void OnItemObtained(StateTracker stateTracker, long sourceLocationId, PlayerAgent? player)
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
            EventHelper.ConvertToCheckLocationEvent(data, e, count, item);

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
