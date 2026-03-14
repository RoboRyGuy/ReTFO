using Clonesoft.Json;
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
public class GatherTerminalHandler : ArchipelagoFeature
{
    public override string Name => "Gather Terminals Handler";
    public override string Description
        => "Handles the GatherTerminals objective type.\n"
        + "This objective type refers only to when you need to enter multiple special command.\n"
        + "Example: R5A3 Main";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /* TODO:
     *  Location: Detect when command is run
     *  Item: Add ability to receive item over network
     *  Region: Currently detected using OnSolve events
     */

    private class GatherTerminalCommandRanItem : Item
    {
        public GatherTerminalCommandRanItem(string name, Objective.Data data)
            : base(name, eRandomizationType.None, new List<string>() { "All", "Objective Items", "Terminal Commands", "Gather Terminal Commands" })
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.GatherTerminal;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"Execute {data.Objective.GatherTerminal_Command} on {data.Objective.GatherTerminal_RequiredCount} Terminals";
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
        return $"{ThisObjectiveName(data)} Command Executed";
    }

    private static string ThisLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Terminal #{count}";
    }

    private static string ThisRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} {count} Commands Executed";
    }

    // Objective requiring prisoners enter commands on a variety of terminals throughought the complex
    // Like a blend of GatherSmallItems and SpecialTerminalCommand
    [Objective.Callback]
    public void HandleGatherTerminalObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        // Seems like a logical assumption, but it's worth checking
        if (data.Objective.GatherTerminal_RequiredCount > data.Objective.GatherTerminal_SpawnCount)
        {
            FeatureLogger.Error($"{ThisObjectiveName(data)}: Expected at least as many terminal spawns as required terminals");
            return;
        }

        List<List<int>> regionSets = data.ObjectiveToTerminalRegionSets(data.Objective.GatherTerminal_SpawnCount).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();

        int last = data.ObjectiveStartRegion;
        Item item = data.GetItem(new GatherTerminalCommandRanItem(ThisItemName(data), data));
        for (int i = 1; i <= data.Objective.GatherTerminal_SpawnCount; i++)
        {
            data.AddLocation(
                ThisLocationName(data, i),
                regionSets[i - 1],
                eRandomizationType.None,
                true,
                item
            );

            string newName = ThisRegionName(data, i);
            int newRegion = data.GetOrCreateRegion(newName);
            Path path = data.AddPath(last, newRegion);
            path.RequiredItem = item.Name;
            path.RequiredItemCount = 1u;
            last = newRegion;

            // Add complete objective item
            if (i == data.Objective.GatherTerminal_RequiredCount)
                SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), newRegion);

            eventWrapper.Process(newRegion, newName);
            if (i >= data.Objective.GatherTerminal_RequiredCount && eventWrapper.IsDone)
                break;
        }
    }

}
