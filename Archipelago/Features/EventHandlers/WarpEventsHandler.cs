using GameData;
using LevelGeneration;
using ReTFO.Archipelago.Features.Terminals;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Linq;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
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

    private JumpCommand? m_warpCommand = null;
    private static ulong s_seenDimensions = 0x1;

    public override void OnEnable()
    {
        base.OnEnable();
        APCommandHandler.RegisterCommand(m_warpCommand ??= new());
    }

    public override void OnDisable()
    {
        base.OnDisable();
        APCommandHandler.UnregisterCommand(m_warpCommand ??= new());
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
                Reqs = new(Path.eType.Category, targetZone.Item_WarpEvent_ByZone, 1u),
            });
        }
    }

    /// <summary>
    /// Notify the handler that a dimension has been seen and its warp should become available
    /// in the jump terminal command.
    /// </summary>
    /// <param name="dimension">The dimension which has been seen</param>
    public static void NotifySeenDimension(eDimensionIndex dimension)
        => s_seenDimensions |= 1UL << ((int)dimension);

    /// <summary>
    /// Handles the jump command
    /// </summary>
    private class JumpCommand : APCommandHandler.SubCommand
    {
        public JumpCommand()
        {
            SubCommandName = "JUMP";
        }

        public override string HelpText => 
            "Allows players to jump to any dimension they've already been to."
            + "\nYou must fully warp to a dimension to unlock it, not a temporary warp.";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            eDimensionIndex? target = null;

            const int MAX_DIMENSIONS = sizeof(ulong) * 8;

            if (param2?.Length > 0)
            {
                if (int.TryParse(param2, out int index))
                    target = (eDimensionIndex)index;
                else if (Enum.TryParse<eDimensionIndex>(param2, out eDimensionIndex dimIndex))
                    target = dimIndex;
                if (!target.HasValue)
                    terminal.AddLine("Failed to parse desired dimension index. Please use a whole number from 0-63.");
            }

            if (target.HasValue && ((int)target.Value) >= MAX_DIMENSIONS)
            {
                terminal.AddLine($"<#F00><i>Cannot jump to dimension <#F80>{param2}</color>; no such dimension exists!</i></color>");
                target = null;
            }

            if (target.HasValue && (s_seenDimensions & (1UL << ((int)target))) == 0)
            {
                terminal.AddLine($"<#F00><i>Cannot warp to <#F80>{Enum.GetName(target.Value)}</color>; not yet seen!</i></color>");
                target = null;
            }

            if (target.HasValue && target.Value == terminal.SpawnNode.m_dimension.DimensionIndex)
            {
                terminal.AddLine($"<#F00><i>Cannot warp to <#F80>{Enum.GetName(target.Value)}</color>; you are already in this dimension!</i></color>");
                target = null;
            }

            if (target.HasValue)
            {
                terminal.AddLine($"Initiating dimension warp to <#F80>{Enum.GetName(target.Value) ?? ((int)target).ToString()}</color>");
                WorldEventManager.ExecuteEvent(new WardenObjectiveEventData
                {
                    Type = eWardenObjectiveEventType.DimensionWarpTeam,
                    DimensionIndex = target.Value,
                    Delay = 3f,
                });
            }
            else
            {
                var available = Enumerable.Range(0, MAX_DIMENSIONS)
                    .Where(i => ((1UL << i) & s_seenDimensions) != 0UL)
                    .Select(i => $"{i} - <#F80>{Enum.GetName((eDimensionIndex)i)}</color>{((eDimensionIndex)i == terminal.SpawnNode.m_dimension.DimensionIndex ? " <i>(Current Dimension)</i>" : string.Empty)}");
                terminal.AddLine($"Available dimension warps:\n  {string.Join("\n  ", available)}");
            }

        }
    }

    /// <summary>
    /// Check when players warp to a dimension
    /// </summary>
    [ArchivePatch(typeof(WorldEventManager), nameof(WorldEventManager.ExecuteEvent_Internal))]
    public static class WorldEventManager__ExecuteEvent_Internal__Patch
    {
        public static void Postfix(WardenObjectiveEventData eData, bool __runOriginal)
        {
            if (__runOriginal && eData.Type == eWardenObjectiveEventType.DimensionWarpTeam)
                NotifySeenDimension(eData.DimensionIndex);
        }
    }

    /// <summary>
    /// When starting an expedition, clear seen dimensions lists
    /// </summary>
    [ArchivePatch(typeof(WardenObjectiveManager), nameof(WardenObjectiveManager.OnLocalPlayerStartExpedition))]
    public static class WardenObjectiveManager__OnLocalPlayerStartExpedition__Patch
    {
        public static void Postfix()
            => s_seenDimensions = 0x1;
    }

}
