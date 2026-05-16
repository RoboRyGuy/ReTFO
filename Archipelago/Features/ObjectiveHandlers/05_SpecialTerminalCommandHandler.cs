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
        public TagResolver Tag_SpecialCommandLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Special Command Locations", "Locations checked by executing special command objectives' commands", gd.Tag_Never));

        public TagResolver Tag_SpecialCommandItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Special Command Items", "Items awared for executing special command objectives' commands", gd.Tag_Never));
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

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.SpecialTerminalCommand;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return $"Execute Command {data.Objective.SpecialTerminalCommand}";
        }

        // True if This is the correct objective
        public static bool IsCorrectObjective(Objective.Data data)
            => data.Objective.Type == ObjectiveType;

        // Assert This is the correct objective, and log an error if it is not
        public static void CheckIsCorrectObjective(Objective.Data data)
        {
            if (!IsCorrectObjective(data))
                FeatureLogger.Error($"Wrong objective type! Expected {Enum.GetName(ObjectiveType)}, got {data.Objective.Type}");
        }
    }

    // Names of regions for this objective
    private static class ThisRegions
    {
        // Region reached by executing the special command
        public static string CommandExecuted(Objective.Data data)
            => $"{data.ObjectiveName()} Command Executed";
    }

    private static class STCLocation
    {
        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Special Command Location", "A special command location for a particular objective", gd.Tag_SpecialCommandLocations));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private class STCItem : Item
    {
        public STCItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Special Command Item", "A special command item for a particular objective", gd.Tag_SpecialCommandItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };
        
        public Objective.Data ObjectiveData { get; set; }
    }

    public static KeyedItem GetItem(Objective.Data data)
    {
        if (data.TryLookupItem(STCItem.MakeTag(data), out var item))
            return item;

        Item newItem = new STCItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    // Objective requiring a single command be entered into a specific terminal
    [Objective.Callback]
    public void HandleSpecialTerminalCommandObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        var rawPlacements = data.ObjectiveData.ZonePlacementDatas.FirstOrDefault()?.Iter() ?? Enumerable.Repeat(new ZonePlacementData(), 1);
        var placement = data.PlacementsToTerminalRegions(rawPlacements).Select(info => info.Region);

        KeyedItem item = GetItem(data);
        data.AddLocation(
            STCLocation.MakeTag(data),
            placement.ToArray(),
            STCLocation.MakeRandData(),
            item.ID
        );

        string commandExecutedName = ThisRegions.CommandExecuted(data);
        RegionID commandExecutedRegion = data.LookupOrCreateRegion(commandExecutedName);
        data.AddPath(new Path()
        {
            StartingRegion = data.ObjectiveStartRegion,
            EndingRegion = commandExecutedRegion,
            ReqItem = item.PathReqs,
            ReqCount = 1u,
        });

        // Events triggered upon executing the command
        var eventWrapper = data.WrapOnActivateEvents();
        eventWrapper.Process(commandExecutedRegion, commandExecutedName);

        SharedObjectiveHandler.AddObjectiveCompleteItem(data, commandExecutedRegion);
    }

}
