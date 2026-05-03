using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Features.EventHandlers;
using System;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class SharedObjectiveHandler_Tags
{
    extension (Game.Data data)
    {
        public TagResolver Tag_CompleteObjectiveLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Complete Objective Locations", "Locations checked by completing objectives", gd.Tag_Never));

        public TagResolver Tag_CompleteObjectiveItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Complete Objective Items", "Items representing objective completions", gd.Tag_Never));

        public TagResolver Tag_SectorClearLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Sector Clear Locations", "Locations checked by clearing a sector and successfully extracting (or equivalent)", gd.Tag_Never));
    }

    extension (Expedition.Data data)
    {
        public TagResolver Tag_SectorClearItems_ByExpedition
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Sector Clear Items", "Items awarded by successfully clearing a sector in a particular expedition", data.Tag_GoalItems_ByExpedition));
    }

    extension (Layer.Data data)
    {
        public TagResolver Tag_CompleteObjectiveItems_PerLayer
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.LayerName} Complete Objective Items", "Items representing objective completions for a particular layer in an expedition", gd.Tag_Never));
    }
}

/// <summary>
/// Handles several items related to objectives, since currently I'm too lazy to separate these 
/// out into their own features
/// </summary>
/// <remarks>
/// TODO: This needs to be split into several sub handlers
///  - Objective Completions
///  - Sector Clears
/// </remarks>
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
    /// Randomization category used by sector clear items
    /// </summary>
    public const string SectorClearsCat = "Sector Clears";

    /// <summary>
    /// Generic complete objective location
    /// </summary>
    public static class CompleteObjectiveLocation
    {
        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Completion Location", "Location checked by completing a particular objective", gd.Tag_CompleteObjectiveLocations));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    /// <summary>
    /// Item representing a single objective being completed.
    /// Each sector can have multiple objectives, chained together. However, they typically only have one.
    /// </summary>
    public class CompleteObjectiveItem : Item
    {
        public CompleteObjectiveItem(Objective.Data layer)
            : base(MakeTag(layer), MakeRandData())
        {
            Layer = layer;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Completion", "Item representing a particular objective being cleared", data.Tag_CompleteObjectiveItems_PerLayer));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data Layer { get; set; }

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, Layer.Tag_CompleteObjectiveItems_PerLayer);
    }

    /// <summary>
    /// Location used by SectorCleared items
    /// </summary>
    private static class SectorClearedLocation
    {
        public static TagResolver MakeTag(Layer.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.LayerName} Sector Clear Location", "Location checked by clearing a particular layer and successfull extracting (or equivalent)", gd.Tag_SectorClearLocations));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    /// <summary>
    /// Item representing a sector being cleared for a particular layer.
    /// Category[0] represents any sector being cleared for a particular expedition.
    /// </summary>
    public class SectorClearedItem : Item
    {
        public SectorClearedItem(Layer.Data layer)
            : base(MakeTag(layer), MakeRandData())
        {
            Layer = layer;
        }

        public static TagResolver MakeTag(Layer.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.LayerName} Sector Clear", "Item representing a particular sector layer was successfull cleared", data.Tag_SectorClearItems_ByExpedition));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Layer.Data Layer { get; set; }
    }

    /// <summary>
    /// Get the "Complete Objective" item, signaling an objective for this layer has been completed
    /// </summary>
    /// <param name="data">The layer objective being completed is on</param>
    /// <returns>The CompleteObjectiveItem</returns>
    public static KeyedItem GetCompleteObjectiveItem(Objective.Data data)
    {
        if (data.TryLookupItem(CompleteObjectiveItem.MakeTag(data), out var item))
            return item;

        Item newItem = new CompleteObjectiveItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    /// <summary>
    /// Get the "Sector Cleared" item, signaling an expedtion was completed with a sector (main, secondary, or overload) cleared
    /// </summary>
    /// <param name="data">The layer for the sector being cleared</param>
    /// <returns>The SectorCleared item</returns>
    public static KeyedItem GetSectorClearedItem(Layer.Data data)
    {
        if (data.TryLookupItem(SectorClearedItem.MakeTag(data), out var item))
            return item;

        Item newItem = new SectorClearedItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    // Adds common regions, locations, and items for all layers. Things like the sector clear, the elevator dropped region and events, etc
    [Layer.Callback]
    public void AddCommonObjectiveElements(Layer.Data data)
    {
        var objectives = data.GetObjectiveDatas().ToList();
        if (!objectives.Any()) return;

        Objective.Data firstObjective = objectives.First();
        Objective.Data lastObjective = objectives.Last();

        // All objectives are immediately available upon level start. We'll connect them to a virtual "Elevator Landed" zone
        //  so when hints are given players aren't wondering why they need to navigate to the start of the level
        RegionID startRegion = data.ObjectiveStartRegion;
        if (data.LayerType.IsMainLayer)
        {
            data.AddPath(new Path()
            {
                StartingRegion = data.StartingRegion,
                EndingRegion = startRegion
            });
        }

        // Objects attach directly to the start region, so we can skip straight to processing the final regions
        // One region is added for the gotowin events
        string gotoWinName = $"{data.LayerName} Goto Win";
        RegionID gotoWinRegion = data.LookupOrCreateRegion(gotoWinName);
        data.AddPath(new Path()
        {
            StartingRegion = startRegion,
            EndingRegion = gotoWinRegion,
            ReqItem = GetCompleteObjectiveItem(data.GetObjectiveDatas().First()).PathReqs,
            ReqCount = (uint)data.GetObjectiveDatas().Count(),
            AlternateItem = data.LayerType == LayerType.Main ? WinEventHandler.GetInstantWinPathReqs(data) : new()
        });

        // A final region is added with the reward for reaching extraction with the objective complete
        string winLayerName = $"{data.LayerName} Sector Cleared";
        RegionID winLayerRegion = data.LookupOrCreateRegion(winLayerName);
        Path path = new()
        {
            StartingRegion = gotoWinRegion,
            EndingRegion = winLayerRegion,
        };

        // If this is secondary or overload, we'll need main to be clearable. Otherwise, we need to reach extraction
        if (data.LayerType.IsMainLayer)
        {
            path.ReqItem = ExtractionHandler.GetExtractionReachableItem(data).PathReqs;
            path.ReqCount = 1u;
            path.AlternateItem = WinEventHandler.GetInstantWinPathReqs(data);
        }
        else
        {
            path.ReqItem = GetSectorClearedItem(data.MainLayer).PathReqs;
            path.ReqCount = 1u;
        }
        data.AddPath(path);

        // Events triggered either when the last objective is complete or when extraction is reached
        if (lastObjective.Objective.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.OnObjectiveCompleted)
            data.ProcessEvents(gotoWinRegion, gotoWinName, lastObjective.Objective.EventsOnGotoWin ??= new(1));
        else if (lastObjective.Objective.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.WhenExitScanMakesProgress)
            // Note that the win region is just extraction reachable + sector cleared, ie the conditions needed to start the extraction scan
            data.ProcessEvents(winLayerRegion, winLayerName, lastObjective.Objective.EventsOnGotoWin ??= new(1)); 
        else throw new ArgumentException($"Objective.EventsOnGotoWinTrigger has an unexpected value for expedition: {lastObjective.ExpeditionName}");

        KeyedItem sectorClearedItem = GetSectorClearedItem(data);
        data.AddLocation(
            SectorClearedLocation.MakeTag(data),
            winLayerRegion,
            SectorClearedLocation.MakeRandData(),
            sectorClearedItem.ID
        );
    }

    /// <summary>
    /// Helper to add the "complete objective" item to one region during objective handling
    /// </summary>
    /// <param name="data">The objective to add the item to</param>
    /// <param name="regions">The regions to add the completion to</param>
    /// <returns>The created location ID. May be null if it failed to create a new location</returns>
    public static LocationID AddObjectiveCompleteItem(Objective.Data data, RegionList regions)
    {
        KeyedItem item = GetCompleteObjectiveItem(data);
        return data.AddLocation(
            CompleteObjectiveLocation.MakeTag(data),
            regions,
            CompleteObjectiveLocation.MakeRandData(),
            item.ID
        );
    }

    [ArchivePatch(typeof(RundownManager), nameof(RundownManager.OnExpeditionEnded))]
    public static class RundownManager__OnExpeditionEnded__Patch
    {
        public static void Postfix(ExpeditionEndState endState)
        {
            if (endState != ExpeditionEndState.Success) return;

            StateTracker stateTracker = StateTracker.Get();
            Expedition.Data data = Expedition.Data.FromCurrentExpedition();

            // Main is always awarded, even if we didn't clear it (by convention)
            if (data.TryLookupLocation(SectorClearedLocation.MakeTag(data.MainLayer), out var mainLoc))
                stateTracker.NotifyFoundLocation(mainLoc.ID, null);
            else
                FeatureLogger.Error("Failed to award main sector clear!");

            if (data.HasSecondary)
            {
                // This is the most consistent way to check if it was cleared
                if (WardenObjectiveManager.CurrentState.second_status == eWardenObjectiveStatus.WardenObjectiveItemSolved)
                {
                    if (data.TryLookupLocation(SectorClearedLocation.MakeTag(data.GetLayer(LayerType.Secondary)), out var loc))
                        stateTracker.NotifyFoundLocation(loc.ID, null);
                    else
                        FeatureLogger.Error("Failed to award secondary sector clear!");
                }
                else
                    FeatureLogger.Debug("Secondary clear not awared due to it not being cleared");
            }

            if (data.HasOverload)
            {
                // This is the most consistent way to check if it was cleared
                if (WardenObjectiveManager.CurrentState.third_status == eWardenObjectiveStatus.WardenObjectiveItemSolved)
                {
                    if (data.TryLookupLocation(SectorClearedLocation.MakeTag(data.GetLayer(LayerType.Overload)), out var loc))
                        stateTracker.NotifyFoundLocation(loc.ID, null);
                    else
                        FeatureLogger.Error("Failed to award overload sector clear!");
                }
                else
                    FeatureLogger.Debug("Overload clear not awared due to it not being cleared");
            }
        }
    }

}
