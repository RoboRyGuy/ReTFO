using LevelGeneration;
using Player;
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
using UnityEngine.UIElements;

public static class GatherTerminalHandler_Tags
{
    extension(Game.Data data)
    {
        public TagResolver Tag_GatherTerminalsCommandLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Gather Terminals Command Locations", "Locations checked executing gather terminals commands", gd.Tag_AllLocations));

        public TagResolver Tag_GatherTerminalsCommandItem
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Gather Terminals Command Items", "Items representing a gather terminals command having been executed", gd.Tag_AllItems));
    }

    extension(Objective.Data data)
    {
        public TagResolver Tag_GatherTerminalsCommandLocations_ByObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Gather Terminals Command Locations", "Locations checked executing gather terminals commands for a particular objective", gd.Tag_GatherTerminalsCommandLocations));

        public TagResolver Tag_GatherTerminalsCommandItem_ByObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Gather Terminals Command Items", "Items representing a gather terminals command having been executed for a particular objective", gd.Tag_GatherTerminalsCommandItem));
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

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.GatherTerminal;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return $"Execute {data.Objective.GatherTerminal_Command} on {data.Objective.GatherTerminal_RequiredCount} Terminals";
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

        // Helper to get the full name for This objective
        public static string ObjectiveName(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return data.ObjectiveName(ObjectiveSummary(data));
        }
    }

    // Names of regions for this objective
    private static class ThisRegions
    {
        // Region reached by executing a command
        public static string CommandExecuted(Objective.Data data, int count)
            => $"{This.ObjectiveName(data)} {count} command{(count == 1 ? "" : "s")} executed";
    }

    private static class GatherTerminals_CommandLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Command Location #{count}", "Location containing a gather terminals command", data.Tag_GatherTerminalsCommandLocations_ByObjective));

        public static LocationData MakeRandData() => new LocationData();
    }

    private class GatherTerminals_CommandItem : Item
    {
        public GatherTerminals_CommandItem(Objective.Data data, int count)
            : base(MakeTag(data, count), MakeRandData())
        {
            ObjectiveData = data;
            Count = count;
        }

        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Command Item #{count}", "Item obtained for completing a gather terminals command", data.Tag_GatherTerminalsCommandItem_ByObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, ObjectiveData.Tag_GatherTerminalsCommandItem_ByObjective);

        public Objective.Data ObjectiveData { get; set; }

        public int Count { get; set; }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (ObjectiveData.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ObjectiveData.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            const uint defaultStartText = 436196897;
            const uint defaultEndText = 2410856699;

            yield return () =>
            {
                var text = ObjectiveData.Objective.GatherTerminal_DownloadingText;
                if (text.Id == 0 && (text.UntranslatedText?.Length ?? 0) == 0)
                    terminal.m_command.AddOutput(TerminalLineType.ProgressWait, defaultStartText, 1f);
                else
                    terminal.m_command.AddOutput(TerminalLineType.ProgressWait, ObjectiveData.Objective.GatherTerminal_DownloadingText, MathF.Min(1f, ObjectiveData.Objective.GatherTerminal_DownloadTime));
            };

            yield return () =>
            {
                var items = WardenObjectiveManager.GetObjectiveItemCollection(ObjectiveData.LayerType, ObjectiveData.ObjectiveIndex);
                items[Count - 1].ObjectiveItemSolved = true;
                WardenObjectiveManager.OnLocalPlayerSolvedObjectiveItem(ObjectiveData.LayerType, items[Count - 1], true);

                var text = ObjectiveData.Objective.GatherTerminal_DownloadCompleteText;
                if (text.Id == 0 && (text.UntranslatedText?.Length ?? 0) == 0)
                    terminal.m_command.AddOutput(defaultEndText);
                else
                    terminal.m_command.AddOutput(ObjectiveData.Objective.GatherTerminal_DownloadCompleteText);
            };
        }
    }

    public static KeyedItem GetCommandItem(Objective.Data data, int count)
    {
        if (data.TryLookupItem(GatherTerminals_CommandItem.MakeTag(data, count), out var item))
            return item;

        Item newItem = new GatherTerminals_CommandItem(data, count);
        return new(data.AddItem(newItem), newItem);
    }

    // Objective requiring prisoners enter commands on a variety of terminals throughought the complex
    // Like a blend of GatherSmallItems and SpecialTerminalCommand
    [Objective.Callback]
    public void HandleGatherTerminalObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // Seems like a logical assumption, but it's worth checking
        if (data.Objective.GatherTerminal_RequiredCount > data.Objective.GatherTerminal_SpawnCount)
        {
            FeatureLogger.Error($"{This.ObjectiveName(data)}: Expected at least as many terminal spawns as required terminals");
            return;
        }

        List<List<RegionID>> regionSets = data.ObjectiveToTerminalRegionSets(data.Objective.GatherTerminal_SpawnCount).ToList();
        var eventWrapper = data.MakeOrWrapOnSolveEvents();

        RegionID last = data.ObjectiveStartRegion;
        for (int i = 1; i <= data.Objective.GatherTerminal_SpawnCount; i++)
        {
            KeyedItem commandItem = GetCommandItem(data, i);
            data.AddLocation(
                GatherTerminals_CommandLocation.MakeTag(data, i),
                regionSets[i - 1],
                GatherTerminals_CommandLocation.MakeRandData(),
                commandItem.ID
            );

            string newName = ThisRegions.CommandExecuted(data, i);
            RegionID newRegion = data.LookupOrCreateRegion(newName);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = newRegion,
                ReqItem = commandItem.PathReqs,
                ReqCount = 1u,
            });
            last = newRegion;

            // Add complete objective item
            if (i == data.Objective.GatherTerminal_RequiredCount)
                SharedObjectiveHandler.AddObjectiveCompleteItem(data, newRegion);

            eventWrapper.Process(newRegion, newName, true);
        }
    }

    [ArchivePatch(typeof(LG_ComputerTerminal), nameof(LG_ComputerTerminal.OnWardenObjectiveGatherCommandDone))]
    public static class LG_ComputerTermina__OnWardenObjectiveGatherCommandDone__Patch
    {
        public static bool Prefix(LG_ComputerTerminal __instance)
        {
            Objective.Data data = Expedition.Data.FromCurrentExpedition()
                .GetLayer(__instance.SpawnNode.LayerType)
                .GetObjectiveDatas().ElementAt(__instance.WardenObjectiveChainIndex);

            if (!This.IsCorrectObjective(data))
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

            RandomizationTag tag = GatherTerminals_CommandLocation.MakeTag(data, i + 1);
            if (!data.TryLookupLocation(tag, out KeyedLocation loc))
            {
                FeatureLogger.Error($"Failed to find location index while intercepting gather command: {data.LookupTagDef(tag).Name}");
                return true;
            }

            if (StateTracker.Get().NotifyFoundLocation(loc.ID, __instance.m_syncedInteractionSource).RandMode.IsTreatedAsRandom)
            {
                __instance.m_command.AddOutput("Discovered item(s):", false);
                __instance.m_command.AddOutput($"  {(loc.ItemID.IsNull ? "None" : data.LookupTagDef(data.LookupItem(loc.ItemID).NameTag).Name)}");
                return false;
            }
            else return true;
        }
    }

}
