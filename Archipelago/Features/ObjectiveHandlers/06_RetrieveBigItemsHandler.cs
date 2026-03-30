using Clonesoft.Json;
using GameData;
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
public class RetrieveBigItemsHandler : ArchipelagoFeature
{
    public override string Name => "Retrieve Big Items Handler";
    public override string Description
        => "Handles the RetrieveBigItems objective type.\n"
        + "Example: R2A1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /*
     * TODO:
     *  Location: Identify when big items are picked up, and if they are warden objective items
     *  Item: Allow the item to be received over the network
     *        Allow items to be put into categories, so each pickup can be its own indexed item
     *  Region: Currently detected via OnSolve events
     */

    private class BigRetrievalLocation : Location
    {
        public BigRetrievalLocation(string name, RegionList regions, Item? item)
            : base(name, regions, item) { }

        private static RandomizationData s_randData = new()
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    private class BigRetrievalItem : Item
    {
        public BigRetrievalItem(string name, Objective.Data data)
            : base(name)
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }

        private static RandomizationData s_randData = new()
        {
            Categories = { "All", "Objective Items", "Big Pickups", "Big Retrieval Items" },
        };
        public override RandomizationData RandData => s_randData;
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.RetrieveBigItems;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"Retrieve {data.Objective.Retrieve_Items.Count}x Big Items";
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

    private static string ThisItemName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Retrieval Target Grabbed";
    }

    private static string ThisLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        ItemDataBlock? item = ItemDataBlock.GetBlock(data.Objective.Retrieve_Items[count - 1]);
        if (item == null)
            FeatureLogger.Error($"Failed to find big item datablock for objective: {ThisObjectiveName(data)}");
        return $"{ThisObjectiveName(data)} Retrieval Target #{count} ({item?.publicName ?? "null!"})";
    }

    private static string ThisRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} {count} Big Items Retrieved";
    }

    // Objective requiring the retrieval of one or more big pickups, which may be of multiple (varying) item types
    [Objective.Callback]
    public void HandleRetrieveBigItemsObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.RetrieveBigItems)
            return;

        /* Similar to small items, we create one region per item we need to pickup
         * Each region will contain the events relevant to picking up that number of pickups
         * Placements are looped, so if we only have one list of zones it's reused; if two, it alternates; etc
         */
        List<List<int>> regionSets = data.ObjectiveToZoneRegionSets(data.Objective.Retrieve_Items.Count).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();

        Item item = data.GetItem(new BigRetrievalItem(ThisItemName(data), data));
        int last = data.ObjectiveStartRegion;
        for (int i = 1; i <= data.Objective.Retrieve_Items.Count; i++)
        {
            // Note that retrieval targets cannot be used as normal items, and so cannot currently be added the same way
            data.AddLocation(new BigRetrievalLocation(
                ThisLocationName(data, i),
                regionSets[i - 1],
                item
            ));

            string regionName = ThisRegionName(data, i);
            int newRegion = data.GetOrCreateRegion(regionName);
            Path path = data.AddPath(last, newRegion);
            path.RequiredItem = item.Name;
            path.RequiredItemCount = 1u;
            last = newRegion;

            eventWrapper.Process(newRegion, regionName);
        }

        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), last);
    }

}
