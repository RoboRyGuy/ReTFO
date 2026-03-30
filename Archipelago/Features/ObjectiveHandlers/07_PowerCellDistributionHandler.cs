using Clonesoft.Json;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class PowerCellDistributionHandler : ArchipelagoFeature
{
    public override string Name => "Powercell Distribution Handler";
    public override string Description
        => "Handles the PowerCellDistribution objective type.\n"
        + "Example: R2B2";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /* TODO:
     *  Location: Cells from start are auto-found, non-randomizable
     *            Generator locations could be randomized, but we'd have to despawn the generators post-level-load
     *  Item: Cells from start are not handled here (only placed)
     *        Generators could be randomized, but we'd have to spawn in new ones when received? Not sure how that'd work
     *  Region: The CellFound region is not currently discoverable
     *          The GenPowered region is discoverable using OnSolve events only
     */

    private class PowerCellDistributionCellLocation : Location
    {
        public PowerCellDistributionCellLocation(string name, RegionList regions, Item? item)
            : base(name, regions, item) { }

        private static RandomizationData s_randData = new()
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;
    }


    private class PowerCellDistributionGenLocation : Location
    {
        public PowerCellDistributionGenLocation(string name, RegionList regions, Item? item)
            : base(name, regions, item) { }

        private static RandomizationData s_randData = new()
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    private class PowerCellDistributionGenItem : Item
    {
        public PowerCellDistributionGenItem(string name, Objective.Data data)
            : base(name)
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }

        private static RandomizationData s_randData = new()
        {
            Categories = new() { "All", "Objective Items", "Function Markers", "Generators" },
        };
        public override RandomizationData RandData => s_randData;
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.PowerCellDistribution;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"Distribute {data.Objective.PowerCellsToDistribute} Power Cells";
    }

    private static bool ThisIsCorrectObjective(Objective.Data data)
        => data.Objective.Type == ThisObjectiveType;

    private static void CheckThisIsCorrectObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            FeatureLogger.Error($"Wrong objective type! Expected {Enum.GetName(ThisObjectiveType)}, got {data.Objective.Type}");
    }

    private static string ThisObjectiveName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return data.ObjectiveName(ThisObjectiveSummary(data));
    }

    private static string ThisCellLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Starting Cell #{count}";
    }

    private static string ThisGenItemName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Generator Found";
    }

    private static string ThisGenLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Generator #{count}";
    }

    private static string ThisCellFoundRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Obtained {count} Cells";
    }

    private static string ThisGenPoweredRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Found {count} Generators";
    }


    // Objective requiring power cells be taken from the elevator zone and to various generators throughout the layer
    [Objective.Callback]
    public void HandlePowerCellDistributionObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // Place starting cells in elevator zone - Only for main layer (and possibly only for first objective?)
        if (data.LayerType.IsMainLayer) // && data.ObjectiveIndex == 0)
        {
            int region = data.GetOrCreateRegion(data.FirstZone.ZoneName);
            for (int i = 1; i <= data.Objective.PowerCellsToDistribute; i++)
            {
                Item item = BigPickupHelper.GetBigPickupItem(data, BigPickupHelper.CellItemID);
                data.GetLocation(new PowerCellDistributionCellLocation(
                    ThisCellLocationName(data, i),
                    region,
                    item
                ));
            }
        }

        // TODO: This objective has somewhat complicated cell implications, ie if doors are locked by cells. Can't think of any issues in vanilla, off the top of my head
        // For each gen needed, create two regions: One checks for access to cells, the other to gens
        List<List<int>> regionSets = data.ObjectiveToZoneRegionSets(data.Objective.PowerCellsToDistribute).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        int last = data.ObjectiveStartRegion;
        Item genItem = data.GetItem(new PowerCellDistributionGenItem(ThisGenItemName(data), data));
        for (int i = 1; i <= data.Objective.PowerCellsToDistribute; i++)
        {
            // Place gen
            data.GetLocation(new PowerCellDistributionGenLocation(
                ThisGenLocationName(data, i),
                regionSets[i - 1],
                genItem
            ));

            // Check that we have enough cells
            int cellRegion = data.GetOrCreateRegion(ThisCellFoundRegionName(data, i));
            Path path = data.AddPath(last, cellRegion);
            path.RequiredItem = BigPickupHelper.GetBigPickupItem(data, BigPickupHelper.CellItemID).Name;
            path.RequiredItemCount = 1u;

            // Check that we've found enough gens
            string genName = ThisGenPoweredRegionName(data, i);
            int genRegion = data.GetOrCreateRegion(genName);
            path = data.AddPath(cellRegion, genRegion);
            path.RequiredItem = genItem.Name;
            path.RequiredItemCount = 1u;
            last = genRegion;

            // Recgonize events triggered by inserting a cell
            eventWrapper.Process(genRegion, genName);
        }

        // Place objective complete item in last region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), last);
    }

}
