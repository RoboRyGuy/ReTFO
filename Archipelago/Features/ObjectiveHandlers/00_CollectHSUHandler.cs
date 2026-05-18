using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
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

public static class CollectHSUHandler_Tags
{
    extension (Game.Data data)
    {
        public TagResolver Tag_HSUScanLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("HSU Scan Locations", "Locations checked by starting HSU scans", gd.Tag_AllLocations));

        public TagResolver Tag_HSUScanItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("HSU Scan Items", "Items which start HSU scans", gd.Tag_AllItems));
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class CollectHSUHandler : ArchipelagoFeature
{
    public override string Name => "Collect HSU Handler";
    public override string Description
        => "Handles the Collect DNA Sample objective type.\n"
        + "Example: R1A1";
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
            = eWardenObjectiveType.HSU_FindTakeSample;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return "Collect HSU";
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
        // Region reached by starting the HSU scan
        public static string ScanStartedRegion(Objective.Data data)
            => $"{data.ObjectiveName()} HSU Scan Started";

        // Region reached by completing the HSU scan
        public static string ScanCompletedRegion(Objective.Data data)
            => $"{data.ObjectiveName()} HSU Scan Completed";
    }

    // Location representing an HSU scan being started
    private static class CollectHSU_ScanLocation
    {
        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} HSU Scan Location", "Location checked by starting a particular HSU scan", gd.Tag_HSUScanLocations));

        public static LocationData MakeRandData() => new LocationData() { };
    }

    // Item representing an HSU scan being started
    private class CollectHSU_ScanItem : Item
    {
        public CollectHSU_ScanItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }
        
        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} HSU Scan", "Item which triggers a particular HSU scan", gd.Tag_HSUScanItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData;

        public override Expedition.Data? RequiredExpedition => ObjectiveData;

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
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Initiating HSU Scan", 2f);
                terminal.AddLine($"Scan will start in 3 seconds. Enjoy :)");
            };

            yield return () =>
            {
                var obj = ObjectiveData.GetWardenObjective().Cast<WO_HSUFindTakeSample>();
                obj.m_hsu.m_puzzle.AttemptInteract(ChainedPuzzles.eChainedPuzzleInteraction.Activate);
            };
        }
    }

    public static KeyedItem GetHSUScanItem(Objective.Data data)
    {
        if (data.TryLookupItem(CollectHSU_ScanItem.MakeTag(data), out var item))
            return item;

        Item newItem = new CollectHSU_ScanItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    [Objective.Callback]
    public void HandleCollectHSUSample(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data)) return;

        // Starting and completing the HSU scan
        RegionList hsuRegions = data.PlacementsToZoneRegions(data.ObjectiveData.ZonePlacementDatas[0]).Select(info => info.Region).ToList();
        KeyedItem scanItem = GetHSUScanItem(data);
        data.AddLocation(
            CollectHSU_ScanLocation.MakeTag(data),
            hsuRegions,
            CollectHSU_ScanLocation.MakeRandData(),
            scanItem.ID
        );

        // Scan start region
        string scanStartedRegionName = ThisRegions.ScanStartedRegion(data);
        RegionID scanStartedRegion = data.LookupOrCreateRegion(scanStartedRegionName);
        data.AddPath(new Path()
        {
            StartingRegion = data.ObjectiveStartRegion,
            EndingRegion = scanStartedRegion,
            ReqItem = scanItem.PathReqs,
            ReqCount = 1u
        });

        // Events triggered by starting the scan
        data.MakeOrWrapOnSolveEvents().Process(scanStartedRegion, scanStartedRegionName);

        // Scan completed region
        string scanCompletedRegionName = ThisRegions.ScanCompletedRegion(data);
        RegionID scanCompletedRegion = data.LookupOrCreateRegion(scanCompletedRegionName);
        data.AddPath(new Path()
        {
            StartingRegion = scanStartedRegion,
            EndingRegion = scanCompletedRegion,
        });

        // Place the objective completion here, too
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, scanCompletedRegion);
    }

    /// <summary>
    /// When the HSU's puzzle is set up, sweep in and overwrite the scan trigger
    /// </summary>
    [ArchivePatch(typeof(LG_HSU.LG_HSUScannerJob), nameof(LG_HSU.LG_HSUScannerJob.Build))]
    public static class OnHSUSetup
    {
        public static void Postfix(LG_HSU.LG_HSUScannerJob __instance)
        {
            if (!__instance.m_hsu.m_isWardenObjective) return;

            Objective.Data data = Expedition.Data.FromCurrentExpedition()
                .GetLayer(__instance.m_hsu.OriginLayer)
                .GetObjectiveDatas()
                .ElementAt(__instance.m_hsu.WardenObjectiveChainIndex);

            if (!data.TryLookupLocation(CollectHSU_ScanLocation.MakeTag(data), out var loc))
            {
                FeatureLogger.Error("Failed to find HSU Scan Location while creating associations.");
                return;
            }

            LocationID id = loc.ID; // Isolate for the lambda
            void OnInteract(PlayerAgent player)
            {
                if (!StateTracker.Get().NotifyFoundLocation(id, player).RandMode.IsTreatedAsRandom)
                    __instance.m_hsu._Setup_b__14_2(player); // I'm guessing this is the lambda it usually gives the interact
            }

            __instance.m_hsu.m_pickupSampleInteraction.OnInteractionTriggered 
                = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<PlayerAgent>>(
                    new Action<PlayerAgent>(OnInteract)
            );
        }
    }

}
