using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class GatherTerminalHandler_Tags
{
    extension(Game.Data data)
    {
        public LocationID Location_GatherTerminalsCommands
            => LocationID.From(data, "Gather Terminals Command Locations", data => new("Locations checked executing gather terminals commands", data.Location_All));

        public ItemID Item_GatherTerminalsCommands
            => ItemID.From(data, "Gather Terminals Command Items", data => new("Items representing a gather terminals command having been executed", data.Item_All));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.GatherTerminal;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension(Objective.Data data)
    {
        public RegionID Region_GatherCommandExecuted(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} {count} command{(count == 1 ? "" : "s")} executed", data => new("Region entered by executing a particular number of Gather Termianl commands for a particualr objective", data.Region_Objective));


        public LocationID Location_GatherTerminalsCommands_ByObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Gather Terminals Command Locations", data => new("Locations checked by executing gather terminals commands for a particular objective", data.Location_GatherTerminalsCommands));

        public ItemID Item_GatherTerminalsCommands_ByObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Gather Terminals Command Items", data => new("Items representing a gather terminals command having been executed for a particular objective", data.Item_GatherTerminalsCommands));


        public LocationID Location_GatherTerminalsCommand_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Gather Terminals Command Locations #{count}", data => new("A particular checked by executing a gather terminals commands", data.Location_GatherTerminalsCommands_ByObjective));

        public ItemID Item_GatherTerminalsCommand_Instance(int count)
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Gather Terminals Command #{count}", 
                data => new("An item representing a particular gather terminals command having been executed", data.Item_GatherTerminalsCommands_ByObjective),
                new GatherTerminalHandler.GatherTerminals_CommandItem(data.Region_Objective, count)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
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

    public class GatherTerminals_CommandItem : TerminalItem
    {
        public GatherTerminals_CommandItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public int Count { get; private init; }

        public override RegionID TargetRegion => ObjectiveRegion;

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            const uint defaultStartText = 436196897;
            const uint defaultEndText = 2410856699;
            Objective.Data data = new(stateTracker.GameData, ObjectiveRegion);

            yield return () =>
            {
                var text = data.Objective.GatherTerminal_DownloadingText;
                if (text.Id == 0 && (text.UntranslatedText?.Length ?? 0) == 0)
                    terminal.m_command.AddOutput(TerminalLineType.ProgressWait, defaultStartText, 1f);
                else
                    terminal.m_command.AddOutput(TerminalLineType.ProgressWait, data.Objective.GatherTerminal_DownloadingText, MathF.Min(1f, data.Objective.GatherTerminal_DownloadTime));
            };

            yield return () =>
            {
                var items = WardenObjectiveManager.GetObjectiveItemCollection(data.LayerType, data.ObjectiveIndex);
                items[Count - 1].ObjectiveItemSolved = true;
                WardenObjectiveManager.OnLocalPlayerSolvedObjectiveItem(data.LayerType, items[Count - 1], true);

                var text = data.Objective.GatherTerminal_DownloadCompleteText;
                if (text.Id == 0 && (text.UntranslatedText?.Length ?? 0) == 0)
                    terminal.m_command.AddOutput(defaultEndText);
                else
                    terminal.m_command.AddOutput(data.Objective.GatherTerminal_DownloadCompleteText);
            };
        }
    }

    // Objective requiring prisoners enter commands on a variety of terminals throughought the complex
    // Like a blend of GatherSmallItems and SpecialTerminalCommand
    [Objective.Callback]
    public void HandleGatherTerminalObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.GatherTerminal)
            return;

        // Seems like a logical assumption, but it's worth checking
        if (data.Objective.GatherTerminal_RequiredCount > data.Objective.GatherTerminal_SpawnCount)
        {
            FeatureLogger.Error($"{data.ObjectiveName}: Expected at least as many terminal spawns as required terminals");
            return;
        }

        List<List<RegionID>> regionSets = data.ObjectiveToTerminalRegionSets(data.Objective.GatherTerminal_SpawnCount).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();

        RegionID last = data.Region_Objective;
        ItemID gatherCategory = data.Item_GatherTerminalsCommands_ByObjective;
        for (int i = 1; i <= data.Objective.GatherTerminal_SpawnCount; i++)
        {
            ItemID commandItem = data.Item_GatherTerminalsCommand_Instance(i);
            data.Locations.CreateValue(
                data.Location_GatherTerminalsCommand_Instance(i),
                regionSets[i - 1],
                new LocationData(),
                commandItem
            );

            RegionID newRegion = data.Region_GatherCommandExecuted(i);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = newRegion,
                ReqItem = new(Path.RequiredItem.eType.Category, gatherCategory),
                ReqCount = (uint)i,
            });
            last = newRegion;

            // Add complete objective item
            if (i == data.Objective.GatherTerminal_RequiredCount)
                SharedObjectiveHandler.AddObjectiveCompleteItem(data, newRegion);

            eventWrapper.Process(newRegion, true);
        }
    }

    /// <summary>
    /// For some reason, the objective chain index is not set up
    /// normally, so this will fix that.
    /// </summary>
    [ArchivePatch(typeof(LG_ComputerTerminal), nameof(LG_ComputerTerminal.SetupAsWardenObjectiveGatherTerminal))]
    public static class LG_ComputerTerminal__SetupAsWardenObjectiveGatherTerminal__Patch
    {
        public static void Postfix(LG_ComputerTerminal __instance, int objectiveChainIndex)
            => __instance.WardenObjectiveChainIndex = objectiveChainIndex;
    }

    /// <summary>
    /// Intercept the command itself and potentially block it. Notify it was triggered.
    /// </summary>
    [ArchivePatch(typeof(LG_ComputerTerminal), nameof(LG_ComputerTerminal.OnWardenObjectiveGatherCommandDone))]
    public static class LG_ComputerTermina__OnWardenObjectiveGatherCommandDone__Patch
    {
        public static bool Prefix(LG_ComputerTerminal __instance)
        {
            var func = () =>
            {
                Objective.Data data = Expedition.Data.GetFromCurrentExpedition()
                    .GetLayer(__instance.SpawnNode.LayerType)
                    .GetObjectiveDatas().ElementAt(__instance.WardenObjectiveChainIndex);

                if (data.Objective.Type != eWardenObjectiveType.GatherTerminal)
                {
                    FeatureLogger.Error("Failed to find objective data while intercepting gather command!");
                    return true;
                }

                var itemCollection = WardenObjectiveManager.GetObjectiveItemCollection(data.LayerType, data.ObjectiveIndex);
                int i;
                for (i = 0; i < itemCollection.Count; i++)
                    if (itemCollection[i].Pointer == __instance.Pointer) break;

                if (i == itemCollection.Count)
                {
                    FeatureLogger.Error("Failed to find terminal index while intercepting gather command!");
                    return true;
                }

                LocationID id = data.Location_GatherTerminalsCommand_Instance(i + 1);
                Location loc = StateTracker.Get().NotifyFoundLocation(id, __instance.m_syncedInteractionSource);
                if (loc.RandData.IsTreatedAsRandom)
                {
                    __instance.m_command.AddOutput("Discovered item(s):", false);

                    string itemName() => loc.ScoutedItemName ?? (loc.ItemID.IsNull ? "None" : data.Items.LookUpName(loc.ItemID));
                    string itemGame() => loc.ScoutedGameName ?? "DEBUG";
                    string itemPlayer() => loc.ScoutedPlayerName ?? StateTracker.Config.Username;

                    __instance.m_command.AddOutput($"   Item: {itemName()}", false);
                    __instance.m_command.AddOutput($"  World: {itemGame()}", false);
                    __instance.m_command.AddOutput($"  Owner: {itemPlayer()}", false);

                    return false;
                }
                else return true;
            };
            return func();
        }
    }

}
