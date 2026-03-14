using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
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
public class ProcessItemHandler : ArchipelagoFeature
{
    public override string Name => "Process Item Handler";
    public override string Description
        => "Handles the ActiveSmallHSU objective type.\n"
        + "Example: R3A1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /* TODO:
     *  Location: Big Pickup not handled here
     *            Processor auto-discover on zone entry? Or on look-at?
     *  Item: Big Pickup not handled here
     *        Cannot receive processor over network. Marker substitution not viable
     *  Region: Found BigPickup currently not detected
     *          ProcessedItem detected using OnProcess events
     */

    private class ProcessItemProcessorItem : Item
    {
        public ProcessItemProcessorItem(string name, Objective.Data data)
            : base(name, eRandomizationType.None, new List<string>() { "All", "Objective Items", "Geomorphs", "Item Processors" })
        {
            objective_data = data;
        }

        [JsonIgnore]
        public Objective.Data objective_data { get; set; }
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.ActivateSmallHSU;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        ItemDataBlock startItem = ItemDataBlock.GetBlock(data.Objective.ActivateHSU_ItemFromStart);
        if (startItem == null)
            FeatureLogger.Error($"Failed to find start item for objective: {data.ObjectiveName(null)}");
        ItemDataBlock endItem = ItemDataBlock.GetBlock(data.Objective.ActivateHSU_ItemAfterActivation);
        if (endItem == null)
            return $"Process \"{startItem?.publicName ?? "null!"}\"";
        else
            return $"Process \"{startItem?.publicName ?? "null!"}\" into \"{endItem?.publicName ?? "null!"}\"";
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

    private static string ThisStartLocationName(Objective.Data data)
    {   // Note that this item only spawns in the elevator; big pickup distributions are used for non-start spawns
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Start Item (in elevator)";
    }

    private static string ThisProcessorItemName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Processor";
    }

    private static string ThisProcessorLocationName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Processor (Location)";
    }

    private static string ThisItemRegionName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Start Item Retrieved";
    }

    private static string ThisProcessedRegionName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Item Processed";
    }

    // Objective requiring an item be brought to be "processed" and then brought to extraction
    [Objective.Callback]
    public void HandleActivateSmallHSUObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // Two-step objective: Find the item, then get to the processor
        // Fun fact: Any item with the correct id can be processed to complete the objective. There's only ever one such item per level, though

        // Add the item to the elevator zone, if necessary
        if (data.Objective.ActivateHSU_BringItemInElevator)
        {
            BigPickupHelper.AddBigPickupLocation(
                data,
                ThisStartLocationName(data),
                data.Objective.ActivateHSU_ItemFromStart,
                data.GetOrCreateRegion(data.GetLayer(LayerType.Main).FirstZone.ZoneName)
            );
        }

        // Collected item zone
        int collectItemRegion = data.GetOrCreateRegion(ThisItemRegionName(data));
        Path path = data.AddPath(data.ObjectiveStartRegion, collectItemRegion);
        path.RequiredItem = BigPickupHelper.GetBigPickupItem(data, data.Objective.ActivateHSU_ItemFromStart).Name;
        path.RequiredItemCount = 1;

        // Add the processor to the expedition
        Item processorItem = data.GetItem(new ProcessItemProcessorItem(ThisProcessorItemName(data), data));
        data.AddLocation(
            ThisProcessorLocationName(data),
            data.ObjectiveData.ZonePlacementDatas.SelectMany(data.PlacementsToZoneRegions).Select(info => info.Region).ToList(),
            eRandomizationType.None,
            true,
            processorItem
        );

        // Processed item region
        string processedItemName = ThisProcessedRegionName(data);
        int processedItemRegion = data.GetOrCreateRegion(processedItemName);
        path = data.AddPath(collectItemRegion, processedItemRegion);
        path.RequiredItem = processorItem.Name;
        path.RequiredItemCount = 1u;

        // Events triggered by initiating processing on the small HSU - both sets are always triggered (I think)
        if (data.Objective.EventsOnActivate.Any())
            data.ProcessEvents(processedItemRegion, processedItemName, data.Objective.EventsOnActivate);
        data.ProcessEvents(processedItemRegion, processedItemName, data.Objective.ActivateHSU_Events ??= new(1));

        // Place objective complete item in the post-processing region if the objective can be completed that way
        if (data.Objective.ActivateHSU_ObjectiveCompleteAfterInsertion)
            SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), processedItemRegion);
    }

}
