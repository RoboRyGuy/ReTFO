using GameData;
using ReTFO.Archipelago.Features.EventHandlers;
using ReTFO.Archipelago.FeaturesAPI;
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
        public LocationID Location_CompleteObjectives
            => LocationID.From(data, "Complete Objective Locations", data => new("Locations checked by completing objectives", data.Location_Never));

        public ItemID Item_CompleteObjectives
            => ItemID.From(data, "Complete Objective Items", data => new("Items representing objective completions", data.Item_Never));

        public LocationID Location_SectorClears
            => LocationID.From(data, "Sector Clear Locations", data => new("Locations checked by clearing a sector and successfully extracting (or equivalent)", data.Location_Never));
        
        public ItemID Item_SectorClears
            => ItemID.From(data, "Sector Clear Items", data => new("Items awarded by successfully clearing sectors in a cateogry of a particular layer", data.Item_Never));

        public LocationID Location_PEClears
            => LocationID.From(data, "PE Clear Locations", data => new("Locations checked by obtaining PE for expeditions", data.Location_Never));

        public ItemID Item_PEClears
            => ItemID.From(data, "PE Clear Items", data => new("Items awarded by successfully clearing all sectors of a 3-sector expedition at once", data.Item_Never));


        public LocationID Location_CompleteObjectives_LayerOnly(LayerType layer)
            => LocationID.From(data, $"{layer.GetName()} Complete Objective Locations", data => new("Locations checked by completing objectives which are part of a particular layer type", new()));

        public ItemID Item_CompleteObjectives_LayerOnly(LayerType layer)
            => ItemID.From(data, $"{layer.GetName()} Complete Objective Items", data => new("Items representing objective completions for a particular layer type", new()));

        public LocationID Location_SectorClears_LayerOnly(LayerType layer)
            => LocationID.From(data, $"{layer.GetName()} Sector Clear Locations", data => new("Locations checked by clearing a particular type of sector and successfully extracting (or equivalent)", new()));

        public ItemID Item_SectorClears_LayerOnly(LayerType layer)
            => ItemID.From(data, $"{layer.GetName()} Sector Clear Items", data => new("Items awarded by successfully clearing a particular type of sector and successfully extracting (or equivalent)", new()));
    }

    extension (Expedition.Data data)
    {
        public RegionID Region_PECleared
            => RegionID.From(data, $"{data.ExpeditionName} PE Cleared", data => new("Region entered when PE is achieved, only for expeditions with PE", data.Region_Expedition));

        public LocationID Location_PEClear_Instance
            => LocationID.From(data, $"{data.ExpeditionName} PE Clear Location", data => new("Location checked by obtaining PE for a particular expedition", data.Location_PEClears));

        public ItemID Item_PEClears_Instance
            => ItemID.From(
                data, 
                $"{data.ExpeditionName} PE Clear Item", 
                data => new("Item awarded for clearing PE for a particular expedition", data.Item_PEClears),
                new SharedObjectiveHandler.PEClearedItem(data.Region_AllExpeditions)
            );
    }

    extension (Layer.Data data)
    {
        public RegionID Region_GoToWin
            => RegionID.From(data, $"{data.LayerName} Go To Win", data => new("Region entered when all layer objectives are completed", data.Region_Layer));

        public RegionID Region_SectorCleared
            => RegionID.From(data, $"{data.LayerName} Sector Cleared", data => new("Region entered when a particular sector is cleared", data.Region_Layer));

        public LocationID Location_CompleteObjectives_ByLayer
            => LocationID.From(data, $"{data.LayerName} Complete Objective Locations", data => new("Parent tag of complete objective locations for a particular layer", data.Location_CompleteObjectives, data.Location_CompleteObjectives_LayerOnly(data.LayerType)));

        public ItemID Item_CompleteObjective_Instance
            => ItemID.From(
                data, 
                $"{data.LayerName} Complete Objectives", 
                data => new("An objective clear item, indicating one of the objectives was cleared for a particular layer.", data.Item_CompleteObjectives, data.Item_CompleteObjectives_LayerOnly(data.LayerType)),
                new SharedObjectiveHandler.CompleteObjectiveItem(data.Region_Layer)
            );

        public LocationID Location_SectorClear_Instance
            => LocationID.From(data, $"{data.LayerName} Sector Clear Location", data => new("The location of a sector clear item in for a particular layer in a particular expedition", data.Location_SectorClears, data.Location_SectorClears_LayerOnly(data.LayerType)));

        public ItemID Item_SectorClear_Instance
            => ItemID.From(
                data, 
                $"{data.LayerName} Sector Clear Item", 
                data => new("The sector clear item for a particular layer in a particular expedition", data.Item_SectorClears, data.Item_SectorClears_LayerOnly(data.LayerType)),
                new SharedObjectiveHandler.SectorClearedItem(data.Region_Layer)
            );
    }

    extension (Objective.Data data)
    {
        public LocationID Location_CompleteObjective_Instance
            => LocationID.From(data, $"{data.ObjectiveName} Completed Location", data => new("The location of a particular objective completion item", data.Location_CompleteObjectives_ByLayer));
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
    /// Category used to customize goal options
    /// </summary>
    public const string GOAL_CATEGORY = "Goal";

    /// <summary>
    /// Item representing a single objective being completed.
    /// Each sector can have multiple objectives, chained together. However, they typically only have one.
    /// </summary>
    public class CompleteObjectiveItem : Item
    {
        public CompleteObjectiveItem(RegionID layer)
            : base(new ItemData() { IsProgression = true })
        {
            LayerRegion = layer;
        }

        public RegionID LayerRegion { get; private init; }
    }

    /// <summary>
    /// Item representing a sector being cleared for a particular layer.
    /// Category[0] represents any sector being cleared for a particular expedition.
    /// </summary>
    public class SectorClearedItem : Item
    {
        public SectorClearedItem(RegionID layer)
            : base(new ItemData() { IsProgression = true, IsRandomLike = true })
        {
            LayerRegion = layer;
        }

        public RegionID LayerRegion { get; private init; }
    }

    public class PEClearedItem : Item
    {
        public PEClearedItem(RegionID expedition)
            : base(new ItemData() { IsProgression = true, IsRandomLike = true })
        {
            ExpeditionRegion = expedition;
        }

        public RegionID ExpeditionRegion { get; private init; }
    }

    /// <summary>
    /// Adds goal options to the YAML
    /// </summary>
    [Game.Callback]
    public void AddGoalOptions(Game.Data data)
    {
        OptionID mainInput = data.AddOption(new OptionToggle(
            displayName: "Require Mains",
            description: "If true, clearing the goal requires clearing all selected expeditions' main layers. Otherwise, it does not.",
            category: GOAL_CATEGORY,
            categorySort: Array.Empty<uint>(),
            defaultValue: 1,
            condition: new()
        ));

        data.AddOption(new OptionAddAll(
            condition: mainInput,
            target: Option.eDictTarget.GoalItems,
            tag: data.Item_SectorClears_LayerOnly(LayerType.Main)
        ));


        OptionID secondariesInput = data.AddOption(new OptionToggle(
            displayName: "Require Secondaries",
            description: "If true, clearing the goal requires clearing all selected expeditions' secondary layers. Otherwise, it does not.",
            category: GOAL_CATEGORY,
            categorySort: Array.Empty<uint>(),
            defaultValue: 1,
            condition: new()
        ));

        data.AddOption(new OptionAddAll(
            condition: secondariesInput,
            target: Option.eDictTarget.GoalItems,
            tag: data.Item_SectorClears_LayerOnly(LayerType.Secondary)
        ));


        OptionID overloadsInput = data.AddOption(new OptionToggle(
            displayName: "Require Overloads",
            description: "If true, clearing the goal requires clearing all selected expeditions' overload layers. Otherwise, it does not.",
            category: GOAL_CATEGORY,
            categorySort: Array.Empty<uint>(),
            defaultValue: 1,
            condition: new()
        ));

        data.AddOption(new OptionAddAll(
            condition: overloadsInput,
            target: Option.eDictTarget.GoalItems,
            tag: data.Item_SectorClears_LayerOnly(LayerType.Overload)
        ));


        OptionID pesInput = data.AddOption(new OptionToggle(
            displayName: "Require Prisoner Efficiencies",
            description: "If true, clearing the goal requires clearing all selected expeditions' Prisoner Efficiencies. Otherwise, it does not.",
            category: GOAL_CATEGORY,
            categorySort: Array.Empty<uint>(),
            defaultValue: 0,
            condition: new()
        ));

        data.AddOption(new OptionAddAll(
            condition: pesInput,
            target: Option.eDictTarget.GoalItems,
            tag: data.Item_PEClears
        ));
    }

    /// <summary>
    /// Handles prisoner efficiency region / reward
    /// </summary>
    [Expedition.Callback]
    public void AddPrisonerEfficiencyStuffs(Expedition.Data data)
    {
        var layers = data.RealLayers.ToList();
        if (layers.Count < 3) return;
        if (layers.Count > 3) throw new NotSupportedException("Archipelago currently does not support more than 3 layers in reality!");

        RegionID peRegion = data.Region_PECleared;
        data.AddPath(new()
        {
            StartingRegion = layers[2].Region_SectorCleared,
            EndingRegion = peRegion,
            ReqItem = new(Path.PathReq.eType.Item, layers[1].Item_SectorClear_Instance),
            ReqCount = 1,
        });

        data.Locations.CreateValue(
            data.Location_PEClear_Instance,
            peRegion,
            new LocationData(),
            data.Item_PEClears_Instance
        );
    }

    /// <summary>
    /// Handles gotowin and objective clears
    /// </summary>
    [Layer.Callback]
    public void AddCommonObjectiveElements(Layer.Data data)
    {
        var objectives = data.GetObjectiveDatas().ToList();
        if (!objectives.Any()) return;

        // Below we attach objectives to their layers.
        // Here we attach layers to their expedition.
        // This results in all objectives reachable upon entering the expedition, which matches in-game logic
        // (This is most noticable for R7C2, where you start the secondary's terminal command before entering the sector at all)
        data.AddPath(new()
        {
            StartingRegion = data.Region_Expedition,
            EndingRegion = data.Region_Layer,
        });

        // Add the GoToWin path
        RegionID gotoWinRegion = data.Region_GoToWin;
        data.AddPath(new()
        {
            StartingRegion = data.Region_Layer,
            EndingRegion = gotoWinRegion,
            ReqItem = new(Path.PathReq.eType.Category, data.Item_CompleteObjective_Instance),
            ReqCount = (uint)data.GetObjectiveDatas().Count(),
        });
            
        // A final region is added with the reward for reaching extraction with the objective complete
        RegionID sectorClearedRegion = data.Region_SectorCleared;

        // If this is secondary or overload, we'll need main to be clearable. Otherwise, we need to reach extraction
        if (data.LayerType.IsMainLayer)
        {
            data.AddPath(new()
            {
                StartingRegion = gotoWinRegion,
                EndingRegion = sectorClearedRegion,
                ReqItem = new(Path.PathReq.eType.Item, data.Item_Extraction_Instance),
                ReqCount = 1u,
                AlternateItem = new(Path.PathReq.eType.Category, data.Item_WinEvent_ByExpedition),
            });
        }
        else
        {
            data.AddPath(new()
            {
                StartingRegion = gotoWinRegion,
                EndingRegion = sectorClearedRegion,
                ReqItem = new(Path.PathReq.eType.Item, data.MainLayer.Item_SectorClear_Instance),
                ReqCount = 1u,
            });
        }

        // If using an instant win item, we skip the gotowin region entirely
        if (data.LayerType.IsMainLayer)
        {
            data.AddPath(new()
            {
                StartingRegion = data.Region_Layer,
                EndingRegion = sectorClearedRegion,
                ReqItem = new(Path.PathReq.eType.Category, data.Item_WinEvent_ByExpedition),
                ReqCount = 1u,
            });
        }

        // Events triggered either when the last objective is complete or when extraction is reached
        Objective.Data lastObjective = objectives.Last();
        if (lastObjective.Objective.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.OnObjectiveCompleted)
            data.ProcessEvents(gotoWinRegion, lastObjective.Objective.EventsOnGotoWin ??= new(1));
        else if (lastObjective.Objective.EventsOnGotoWinTrigger == eRetrieveExitWaveTrigger.WhenExitScanMakesProgress)
            // Note that the win region is just extraction reachable + sector cleared, ie the conditions needed to start the extraction scan
            data.ProcessEvents(sectorClearedRegion, lastObjective.Objective.EventsOnGotoWin ??= new(1)); 
        else throw new ArgumentException($"Objective.EventsOnGotoWinTrigger has an unexpected value for expedition: {data.ExpeditionName}");

        data.Locations.CreateValue(
            data.Location_SectorClear_Instance,
            sectorClearedRegion,
            new LocationData(),
            data.Item_SectorClear_Instance
        );
    }

    /// <summary>
    /// Connects objective regions to their respective layers
    /// </summary>
    [Objective.Callback]
    public void ConnectObjectiveRegions(Objective.Data data)
    {
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Layer,
            EndingRegion = data.Region_Objective,
        });
    }

    /// <summary>
    /// Helper to add the "complete objective" item to one region during objective handling
    /// </summary>
    /// <param name="data">The objective to add the item to</param>
    /// <param name="regions">The regions to add the completion to</param>
    /// <returns>The created location ID. May be null if it failed to create a new location</returns>
    public static LocationID AddObjectiveCompleteItem(Objective.Data data, RegionList regions)
    {
        LocationID result = data.Location_CompleteObjective_Instance;
        data.Locations.CreateValue(
            result,
            regions,
            new LocationData() { IsAutoDiscovered = true },
            data.Item_CompleteObjective_Instance
        );
        return result;
    }

    [ArchivePatch(typeof(RundownManager), nameof(RundownManager.OnExpeditionEnded))]
    public static class RundownManager__OnExpeditionEnded__Patch
    {
        public static void Postfix(ExpeditionEndState endState)
        {
            if (endState != ExpeditionEndState.Success)
            {
                FeatureLogger.Debug("Expedition did not end in success. No clears awarded");
                return;
            }

            StateTracker stateTracker = StateTracker.Get();
            Expedition.Data data = Expedition.Data.GetFromCurrentExpedition();

            // Main is always awarded, even if we didn't clear it (by convention)
            var main = data.MainLayer;
            stateTracker.NotifyFoundRegion(main.Region_SectorCleared, null);
            stateTracker.NotifyFoundLocation(main.Location_SectorClear_Instance, null);

            bool secondaryCleared = false; // For PE check
            if (data.HasSecondary)
            {
                // This is the most consistent way to check if it was cleared
                if (WardenObjectiveManager.CurrentState.second_status == eWardenObjectiveStatus.WardenObjectiveItemSolved)
                {
                    secondaryCleared = true;
                    var secondary = data.GetLayer(LayerType.Secondary);
                    stateTracker.NotifyFoundLocation(secondary.Location_SectorClear_Instance, null);
                    stateTracker.NotifyFoundRegion(secondary.Region_SectorCleared, null);
                }
                else
                    FeatureLogger.Debug("Secondary clear not awarded due to it not being cleared");
            }

            if (data.HasOverload)
            {
                // This is the most consistent way to check if it was cleared
                if (WardenObjectiveManager.CurrentState.third_status == eWardenObjectiveStatus.WardenObjectiveItemSolved)
                {
                    var overload = data.GetLayer(LayerType.Overload);
                    stateTracker.NotifyFoundRegion(overload.Region_SectorCleared, null);
                    stateTracker.NotifyFoundLocation(data.GetLayer(LayerType.Overload).Location_SectorClear_Instance, null);

                    if (secondaryCleared)
                    {   // Awarded PE as well
                        stateTracker.NotifyFoundRegion(data.Region_PECleared, null);
                        stateTracker.NotifyFoundLocation(data.Location_PEClear_Instance, null);
                    }
                }
                else
                    FeatureLogger.Debug("Overload clear not awarded due to it not being cleared");
            }
        }
    }

}
