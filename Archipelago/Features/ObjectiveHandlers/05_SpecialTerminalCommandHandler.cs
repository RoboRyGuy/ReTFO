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

public static class SpecialTerminalCommandHandler_Tags
{
    extension (Game.Data data)
    {
        public LocationID Location_SpecialCommands
            => LocationID.From(data, "Special Command Locations", data => new("Locations checked by executing special command objectives' commands", data.Location_Never));

        public ItemID Item_SpecialCommands
            => ItemID.From(data, "Special Command Items", data => new("Items awarded for executing special command objectives' commands", data.Item_Never));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.SpecialTerminalCommand;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension(Objective.Data data)
    {
        public RegionID Region_SpecialCommandExecuted
            => RegionID.From(data, $"{data.ObjectiveName} Command Executed", data => new("Region entered by executing the objective's special command", data.Region_Objective));

        public LocationID Location_SpecialCommand_Instance
            => LocationID.From(data, $"{data.ObjectiveName} Special Command Location", data => new("A special command location for a particular objective", data.Location_SpecialCommands));

        public ItemID Item_SpecialCommand_Instance
            => ItemID.From(
                data,
                $"{data.ObjectiveName} Special Command Item",
                data => new("A special command item for a particular objective", data.Item_SpecialCommands),
                new SpecialTerminalCommandHandler.STCItem(data.Region_Objective)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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

    public class STCItem : Item
    {
        public STCItem(RegionID objective)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
        }
        
        public RegionID ObjectiveRegion { get; private init; }
    }

    // Objective requiring a single command be entered into a specific terminal
    [Objective.Callback]
    public void HandleSpecialTerminalCommandObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.SpecialTerminalCommand)
            return;

        var rawPlacements = data.ObjectiveData.ZonePlacementDatas.FirstOrDefault()?.Iter() ?? Enumerable.Repeat(new ZonePlacementData(), 1);
        var placement = data.PlacementsToTerminalRegions(rawPlacements).Select(info => info.Region);

        ItemID item = data.Item_SpecialCommand_Instance;
        data.Locations.CreateValue(
            data.Location_SpecialCommand_Instance,
            placement.ToArray(),
            new LocationData() { IsAutoDiscovered = true },
            item
        );

        RegionID commandExecutedRegion = data.Region_SpecialCommandExecuted;
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Objective,
            EndingRegion = commandExecutedRegion,
            ReqItem = new(Path.RequiredItem.eType.Item, item),
            ReqCount = 1u,
        });

        // Events triggered upon executing the command
        var eventWrapper = data.WrapOnActivateEvents();
        eventWrapper.Process(commandExecutedRegion);

        SharedObjectiveHandler.AddObjectiveCompleteItem(data, commandExecutedRegion);
    }

}
