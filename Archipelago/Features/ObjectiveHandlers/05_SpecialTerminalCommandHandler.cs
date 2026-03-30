using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class SpecialTerminalCommandHandler : ArchipelagoFeature
{
    public override string Name => "Special Terminal Command Handler";
    public override string Description
        => "Handles the SpecialTerminalCommand objective type.\n"
        + "This objective type refers only to when you need to enter a single special command.\n"
        + "Examples: R5A1 Main, R8E1 secondary";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /*
     * TODO:
     *  Location: Detect the command being run
     *  Item: Add ability to receive item over network
     *  Region: Currently detected using OnActivate events
     */

    private class STCLocation : Location
    {
        public STCLocation(string name, RegionList regions, Item? item)
            : base(name, regions, item) { }

        private static RandomizationData s_randData = new()
        {

        };
        public override RandomizationData RandData => s_randData;
    }

    private class STCItem : Item
    {
        public STCItem(string name, Objective.Data data)
            : base(name)
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }

        private static RandomizationData s_randData = new()
        {
            Categories = { "All", "Objective Items", "Terminal Commands", "Special Terminal Commands" },
        };
        public override RandomizationData RandData => s_randData;
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.SpecialTerminalCommand;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"Execute Command {data.Objective.SpecialTerminalCommand}";
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

    private static string ThisLocationName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Terminal";
    }

    private static string ThisRegionName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Command Executed";
    }

    // Objective requiring a single command be entered into a specific terminal
    [Objective.Callback]
    public void HandleSpecialTerminalCommandObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        Path path;

        var rawPlacements = data.ObjectiveData.ZonePlacementDatas.FirstOrDefault()?.Iter() ?? Enumerable.Repeat(new ZonePlacementData(), 1);
        var placement = data.PlacementsToTerminalRegions(rawPlacements).Select(info => info.Region);

        Item item = data.GetItem(new STCItem(ThisItemName(data), data));
        Location location = data.GetLocation(new STCLocation(
            ThisLocationName(data),
            placement.ToList(),
            item
        ));

        string commandExecutedName = ThisRegionName(data);
        int commandExecutedRegion = data.GetOrCreateRegion(commandExecutedName);
        path = data.AddPath(data.ObjectiveStartRegion, commandExecutedRegion);
        path.RequiredItem = item.Name;
        path.RequiredItemCount = 1;

        // Events triggered upon executing the command
        var eventWrapper = data.WrapOnActivateEvents();
        eventWrapper.Process(commandExecutedRegion, commandExecutedName);

        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), commandExecutedRegion);
    }

}
