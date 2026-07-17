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

public static class WarpEvent_Tags
{
    extension (Game.Data gameData)
    {
        /// <summary>
        /// Parent tag for all event items which trigger warps
        /// </summary>
        public ItemID Item_WarpEvents
            => ItemID.From(
                gameData,
                "Warp Event Items",
                data => new("Event items which trigger the team to teleport, typically between dimensions", data.Item_Warps)
            );
    }

    extension (Zone.Data data)
    {
        /// <summary>
        /// Item which warps the team to a particular zone
        /// </summary>
        public ItemID Item_WarpEvent_ByZone
            => ItemID.From(data, $"Team Warps to {data.ZoneName}", data => new("Event items which trigger the team to teleport to a particular zone", data.Item_WarpEvents));

        public ItemID Item_WarpEvent_Instance(string? targetAlign, bool withDimensionClearing)
            => ItemID.From(
                data,
                $"Team Warp to {data.ZoneName}{(targetAlign != null ? $", Align: \"{targetAlign}\"" : string.Empty)}{(withDimensionClearing ? ", with ClearDimension" : string.Empty)}",
                data => new("A particular variation of a team warp going to a particular zone", data.Item_WarpEvent_ByZone),
                new WarpEventsHandler.DimensionWarpItem(data.Region_Zone, targetAlign, withDimensionClearing)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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
    public class DimensionWarpItem : TerminalItem
    { 
        public DimensionWarpItem(RegionID targetZone, string? align, bool clearDimension)
            : base(MakeRandData())
        {
            TargetZone = targetZone;
            ClearDimension = clearDimension;
            WarpAlign = align;
        }

        public static string MakeName(Zone.Data targetZone, string? align, bool clearDimension)
        {
            if (align != null)
                return $"Warp to {targetZone.LayerName} (Align: {align}) {(clearDimension ? " (with DimensionClearing)" : "")}";
            else
                return $"Warp to {targetZone.ZoneName} {(clearDimension ? " (with DimensionClearing)" : "")}";
        }

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        /// <summary>
        /// The zone the warp goes to
        /// </summary>
        public RegionID TargetZone { get; private init; }

        /// <summary>
        /// If true, the previous dimension will be cleared when the players leave
        /// </summary>
        public bool ClearDimension { get; private init; }

        /// <summary>
        /// An align provided via the WorldObjectFilter of the warp event
        /// </summary>
        public string? WarpAlign { get; private init; }

        public override RegionID TargetRegion => TargetZone;

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            Zone.Data zone = new(stateTracker.GameData, TargetZone);
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Initiating Dimension Warp to {Enum.GetName<eDimensionIndex>(zone.LayerType)}", 2f);
                terminal.AddLine($"Warp will occur in 3 seconds. Enjoy the scenery :)");
            };

            yield return () => WorldEventManager.ExecuteEvent(new()
            {
                Type = eWardenObjectiveEventType.DimensionWarpTeam,
                DimensionIndex = zone.LayerType,  
                Layer = zone.LayerType,
                LocalIndex = zone.Zone?.LocalIndex ?? eLocalZoneIndex.Zone_0,
                Delay = 3f,
                ClearDimension = ClearDimension,
                WorldEventObjectFilter = WarpAlign,
            });
        }
    }

    // Warps between dimensions when triggered by an event
    [Event.Callback]
    public void AddEventWarps(Event.Data data)
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
            EventHelper.CreateEventLocation(data, e, count, targetZone.Item_WarpEvent_Instance(e.WorldEventObjectFilter, e.ClearDimension));

            // Add path represented by warp
            data.AddPath(new Path()
            {
                StartingRegion = data.Region_Event,
                EndingRegion = targetZone.Region_Zone,
                ReqItem = new(Path.PathReq.eType.Category, targetZone.Item_WarpEvent_ByZone),
                ReqCount = 1u,
            });
        }
    }

}
