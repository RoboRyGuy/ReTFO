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

public static class WarpEvent_Tags
{
    extension (Game.Data gameData)
    {
        public TagResolver Tag_WarpEventItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Warp Event Items", "Event items which trigger the team to teleport, typically between dimensions", gd.Tag_WarpItems));
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
    private class DimensionWarpItem : Item
    { 
        public DimensionWarpItem(Zone.Data targetZone, string? align, bool clearDimension)
            : base(MakeTag(targetZone, align, clearDimension), MakeRandData())
        {
            TargetZone = targetZone;
            ClearDimension = clearDimension;
            WarpAlign = align;
        }

        public static TagResolver MakeTag(Zone.Data targetZone, string? align, bool clearDimension)
            => new TagResolver(targetZone, gd => gd.LookupOrCreateTag(MakeName(targetZone, align, clearDimension), "A particular warp event item", gd.Tag_WarpEventItems));

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
        public Zone.Data TargetZone { get; set; }

        /// <summary>
        /// If true, the previous dimension will be cleared when the players leave
        /// </summary>
        public bool ClearDimension { get; set; }

        /// <summary>
        /// An align provided via the WorldObjectFilter of the warp event
        /// </summary>
        public string? WarpAlign { get; set; }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (TargetZone.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (TargetZone.IsSameExpedition(data))
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
                WorldEventObjectFilter = WarpAlign,
            });
        }
    }

    public static KeyedItem GetDimensionWarpEventItem(Zone.Data targetZone, string? align, bool cleanDimension)
    {
        if (targetZone.TryLookupItem(DimensionWarpItem.MakeTag(targetZone, align, cleanDimension), out var item))
            return item;

        Item newItem = new DimensionWarpItem(targetZone, align, cleanDimension);
        return new(targetZone.AddItem(newItem), newItem);
    }

    private static string GetDimensionWarpEventLocationName(Event.Data data, int count)
        => $"{data.EventName} - Dimension Warp #{count}";

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
            KeyedItem item = GetDimensionWarpEventItem(targetZone, e.WorldEventObjectFilter, e.ClearDimension);
            EventHelper.ConvertToCheckLocationEvent(data, e, count, item.ID);

            // Add path represented by warp
            data.AddPath(new Path()
            {
                StartingRegion = data.EventRegion,
                EndingRegion = data.LookupOrCreateRegion(targetZone.ZoneName),
                ReqItem = item.PathReqs,
                ReqCount = 1u,
            });
        }
    }

}
