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
        public LocationID Location_HSUScanReachables
            => LocationID.From(data, "HSU Scan Reachble Locations", data => new("Locations used to ensure the HSU scan is reachable", data.Location_Never));

        public ItemID Item_HSUScanReachables
            => ItemID.From(data, "HSU Scan Reachable Items", data => new("Items used to ensure the HSU scan is reachable", data.Item_Never));

        public LocationID Location_HSUScans
            => LocationID.From(data, "HSU Scan Locations", data => new("Locations checked by starting HSU scans", data.Location_All));

        public ItemID Item_HSUScans
            => ItemID.From(data, "HSU Scan Items", data => new("Items which start HSU scans", data.Item_Scans));
    }

    private static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.HSU_FindTakeSample;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension (Objective.Data data)
    {
        public RegionID Region_HSUScanStarted
            => RegionID.From(Checked(data), $"{data.ObjectiveName} HSU Scan Started", data => new("Region entered when the HSU scan is started", data.Region_Objective));

        public RegionID Region_HSUScanCompleted
            => RegionID.From(Checked(data), $"{data.ObjectiveName} HSU Scan Completed", data => new("Region entered when the HSU scan is completed", data.Region_Objective));

        public LocationID Location_HSUScanReachable_Instance
            => LocationID.From(data, $"{data.ObjectiveName} HSU Scan Reachble Location", data => new("Location used to ensure the HSU scan is reachable", data.Location_HSUScanReachables));

        public ItemID Item_HSUScanReachable_Instance
            => ItemID.From(data, $"{data.ObjectiveName} HSU Scan Reachable Item", data => new("Item used to ensure the HSU scan is reachable", data.Item_HSUScanReachables), new Item(new() { IsProgression = true }));

        public LocationID Location_HSUScan_Instance
            => LocationID.From(Checked(data), $"{data.ObjectiveName} HSU Scan Location", data => new("Location checked by starting a particular HSU scan", data.Location_HSUScans));

        public ItemID Item_HSUScan_Instance
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} HSU Scan", 
                data => new("Item which triggers a particular HSU scan", data.Item_HSUScans),
                new CollectHSUHandler.CollectHSU_ScanItem(data.Region_Objective)
            );
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

    // Item representing an HSU scan being started
    public class CollectHSU_ScanItem : TerminalItem
    {
        public CollectHSU_ScanItem(RegionID objective)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
        }
        
        public RegionID ObjectiveRegion { get; private init; }

        public override RegionID TargetRegion => ObjectiveRegion;

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Initiating HSU Scan", 2f);
                terminal.AddLine($"Scan will start in 3 seconds. Enjoy :)");
            };

            yield return () =>
            {
                Objective.Data data = new Objective.Data(stateTracker.GameData, ObjectiveRegion);
                var obj = data.GetWardenObjective().Cast<WO_HSUFindTakeSample>();
                obj.m_hsu.m_puzzle.AttemptInteract(ChainedPuzzles.eChainedPuzzleInteraction.Activate);
            };
        }
    }

    [Objective.Callback]
    public void HandleCollectHSUSample(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.HSU_FindTakeSample) return;

        // Starting and completing the HSU scan
        var placement = data.PlacementsToZoneRegions(data.ObjectiveData.ZonePlacementDatas[0]).Select(info => info.Region).ToList();

        ItemID scanReachable = data.Item_HSUScanReachable_Instance;
        data.Locations.CreateValue(
            data.Location_HSUScanReachable_Instance,
            placement,
            new LocationData(),
            scanReachable
        );

        ItemID scanItem = data.Item_HSUScan_Instance;
        data.Locations.CreateValue(
            data.Location_HSUScan_Instance,
            placement,
            new LocationData(),
            scanItem
        );

        // Scan start region
        RegionID scanStartedRegion = data.Region_HSUScanStarted;
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Objective,
            EndingRegion = scanStartedRegion,
            Reqs = new(
                new(Path.eType.Item, scanItem, 1u),
                new(Path.eType.Item, scanReachable, 1u)
            )
        });

        // Events triggered by starting the scan
        data.MakeOrWrapOnSolveEvents().Process(scanStartedRegion);

        // Scan completed region
        RegionID scanCompletedRegion = data.Region_HSUScanCompleted;
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

            Objective.Data data = Expedition.Data.GetFromCurrentExpedition()
                .GetLayer(__instance.m_hsu.OriginLayer)
                .GetObjectiveDatas()
                .ElementAt(__instance.m_hsu.WardenObjectiveChainIndex);

            LocationID id = data.Location_HSUScan_Instance; // Isolate for the lambda
            void OnInteract(PlayerAgent player)
            {
                if (!StateTracker.Get().NotifyFoundLocation(id, player).RandData.IsTreatedAsRandom)
                    __instance.m_hsu._Setup_b__14_2(player); // I'm guessing this is the lambda it usually gives the interact
            }

            __instance.m_hsu.m_pickupSampleInteraction.OnInteractionTriggered 
                = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<PlayerAgent>>(
                    new Action<PlayerAgent>(OnInteract)
            );
        }
    }

}
