using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Features.EventHandlers;
using ReTFO.Archipelago.Features.Pickups;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// Handles several items related to objectives, since currently I'm too lazy to separate these 
/// out into their own features
/// TODO: This needs to be split into several sub handlers
/// </summary>
[EnableFeatureByDefault, AutomatedFeature]
public class SharedObjectiveHandler : ArchipelagoFeature
{
    public override string Name => "Shared Objective Handler";
    public override string Description 
        => "Handles several items shared by all objectives.";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// Location where you can find generic item in elevator items.
    /// These are misc non-objective items the player can drop with; very rare.
    /// </summary>
    private class GenericItemInElevatorLocation : Location
    {
        public GenericItemInElevatorLocation(Expedition.Data data, RegionList regions, Item? item)
            : base($"{data.ExpeditionName} Generic Item-in-Elevator", regions, item) { }

        private static RandomizationData s_randData = new()
        {

        };
        public override RandomizationData RandData => s_randData;
    }

    /// <summary>
    /// Randomization category used by sector clear items
    /// </summary>
    public const string SectorClearsCat = "Sector Clears";

    /// <summary>
    /// Generic complete objective location
    /// </summary>
    public class CompleteObjectiveLocation : Location
    {
        public CompleteObjectiveLocation(Objective.Data data, string? objectiveSummary, RegionList regions, Item? item)
            : base($"{data.ObjectiveName(objectiveSummary)} Complete", regions, item)
        { }

        private static RandomizationData s_randData = new()
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    /// <summary>
    /// Item representing a single objective being completed.
    /// Each sector can have multiple objectives, chained together. However, they typically only have one.
    /// </summary>
    public class CompleteObjectiveItem : Item
    {
        public CompleteObjectiveItem(Layer.Data layer)
            : base($"{layer.LayerName} Objective Completed")
        {
            this.Layer = layer;
        }

        [JsonIgnore]
        public Layer.Data Layer { get; set; }

        private static RandomizationData s_randData = new()
        {
            Categories = new() { "Objective Completions" },
        };
        public override RandomizationData RandData => s_randData;
    }

    /// <summary>
    /// Location used by SectorCleared items
    /// </summary>
    private class SectorClearedLocation : Location
    {
        public SectorClearedLocation(Layer.Data data, RegionList regions, Item? item)
            : base($"{data.LayerName} Sector Cleared", regions, item)
        {

        }

        private static RandomizationData s_randData = new()
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;

    }

    /// <summary>
    /// Item representing a sector being cleared for a particular layer.
    /// Category[0] represents any sector being cleared for a particular expedition.
    /// </summary>
    public class SectorClearedItem : Item
    {
        public SectorClearedItem(Layer.Data layer)
            : base($"{layer.LayerName} Sector Cleared")
        {
            this.layer = layer;
        }

        [JsonIgnore]
        public Layer.Data layer { get; set; }

        public override List<string> Categories => new(1) { $"{layer.ExpeditionName} Any Sector Cleared" };

        public override RandomizationData RandData => new()
        {
            Categories = { SectorClearsCat, $"{layer.ExpeditionName} Completion Items" },
        };
    }

    // Add the shared objective and sector cleared regions not handled in individual objective processing
    [Layer.Callback]
    public static void AddCommonObjectiveRegions(Layer.Data data)
    {
        var objectives = data.getObjectiveDatas();
        if (!objectives.Any()) return;

        var firstObjective = objectives.First();
        var lastObjective = objectives.Last();

        // All objectives are immediately available upon level start. We'll connect them to a virtual "Elevator Landed" zone
        //  so when hints are given players aren't wondering why they need to navigate to the start of the level
        int startRegion = data.ObjectiveStartRegion;
        if (data.LayerType.IsMainLayer)
        {
            int firstRegion = data.GetOrCreateRegion(data.FirstZone.ZoneName);
            data.AddPath(firstRegion, startRegion);
            data.ProcessEvents(startRegion, data.ObjectiveStartRegionName, firstObjective.Objective.EventsOnElevatorLand ??= new(1));

            // I believe GenericInElevator also only works for the first objective of the main layer
            if (firstObjective.Objective.GenericItemFromStart != 0)
            {
                Item item = BigPickupHelper.GetBigPickupItem(data, firstObjective.Objective.GenericItemFromStart);
                data.GetLocation(new GenericItemInElevatorLocation(data, firstRegion, item));
            }
        }

        // One region is added for the gotowin events
        string gotoWinName = $"{data.LayerName} Goto Win";
        int gotoWinRegion = data.GetOrCreateRegion(gotoWinName);
        Path path = data.AddPath(startRegion, gotoWinRegion);
        path.RequiredItem = GetCompleteObjectiveItem(data).Name;
        path.RequiredItemCount = (uint)objectives.Count();
        path.AlternateItem = data.LayerType == LayerType.Main ? WinEventHandler.GetInstantWinItem(data).Name : null;

        // A final region is added with the reward for reaching extraction with the objective complete
        string winLayerName = $"{data.LayerName} Sector Cleared";
        int winLayerRegion = data.GetOrCreateRegion(winLayerName);
        path = data.AddPath(gotoWinRegion, winLayerRegion);
        path.RequiredItem = ExtractionHandler.GetExtractionReachableItem(data).Name;
        path.RequiredItemCount = 1;
        path.AlternateItem = WinEventHandler.GetInstantWinItem(data).Name;

        // Events triggered either when the last objective is complete or when extraction is reached
        if (lastObjective.Objective.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.OnObjectiveCompleted)
            data.ProcessEvents(gotoWinRegion, gotoWinName, lastObjective.Objective.EventsOnGotoWin ??= new(1));
        else if (lastObjective.Objective.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.WhenExitScanMakesProgress)
            data.ProcessEvents(winLayerRegion, winLayerName, lastObjective.Objective.EventsOnGotoWin ??= new(1));
        else throw new ArgumentException($"Objective.EventsOnGotoWinTrigger is an unexpected value for expedition: {lastObjective.ExpeditionName}");

        Item sectorClearedItem = GetSectorClearedItem(data);
        Location sectorClearedLocation = data.GetLocation(
            new SectorClearedLocation(data, winLayerRegion, sectorClearedItem)
        );
    }

    /// <summary>
    /// Get the "Complete Objective" item, signaling an objective for this layer has been completed
    /// </summary>
    /// <param name="data">The layer objective being completed is on</param>
    /// <returns>The CompleteObjectiveItem</returns>
    public static Item GetCompleteObjectiveItem(Layer.Data data)
        => data.GetItem(new CompleteObjectiveItem(data));

    /// <summary>
    /// Get the "Sector Cleared" item, signaling an expedtion was completed with a sector (main, secondary, or overload) cleared
    /// </summary>
    /// <param name="data">The layer for the sector being cleared</param>
    /// <returns>The SectorCleared item</returns>
    public static Item GetSectorClearedItem(Layer.Data data)
        => data.GetItem(new SectorClearedItem(data));

    /// <summary>
    /// Helper to add the "complete objective" item to one region during objective handling
    /// </summary>
    /// <param name="data">The objective to add the item to</param>
    /// <param name="objectiveSummary">A brief summary of the objective, used for naming; optional, may be null</param>
    /// <param name="regions">The regions to add the completion to</param>
    public static void AddObjectiveCompleteItem(Objective.Data data, string? objectiveSummary, RegionList regions)
    {
        Item item = GetCompleteObjectiveItem(data);
        Location location = data.GetLocation(new CompleteObjectiveLocation(data, objectiveSummary, regions, item));
    }

}
