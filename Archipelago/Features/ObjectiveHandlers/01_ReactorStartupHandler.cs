using Il2CppInterop.Runtime;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Features.Terminals;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ObjectiveHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class ReactorStartupHandler_Tags
{
    extension (Game.Data data)
    {
        public LocationID Location_ReactorStartupReactors
            => LocationID.From(data, "Reactor Startup Reactor Locations", data => new("Locations checked by entering zones with Reactor Startup reactors (as opposed to Reactor Shutdown reactors)", data.Location_Never));

        public LocationID Location_ReactorStartupCodes
            => LocationID.From(data, "Reactor Startup Code Locations", data => new("Locations checked by finding Reactor Startup codes. Includes \"free\" codes", data.Location_TerminalLogs));

        public LocationID Location_ReactorStartupSkips
            => LocationID.From(data, "Reactor Startup Skip Locations", data => new("Location checked when a reactor wave is survived used to grant wave skip items", data.Location_Never));

        public ItemID Item_ReactorStartupReactors
            => ItemID.From(data, "Reactor Startup Reactors", data => new("Reactor item indicating a reactor startup can be performed", data.Item_Never));

        public ItemID Item_ReactorStartupCodes
            => ItemID.From(data, "Reactor Startup Codes", data => new("Reactor item indicating the player has found a reactor code", data.Item_Codes));

        public ItemID Item_ReactorStartupSkips
            => ItemID.From(data, "Reactor Startup Skips", data => new("Reactor items allowing players to skip to a particular wave", data.Item_Never));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.Reactor_Startup;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension (Objective.Data data)
    {
        public RegionID Region_ReactorStartupFoundReactor()
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Found Reactor", data => new("Region entered when at least one reactor startup reactor is found", data.Region_Objective));

        public RegionID Region_ReactorStartupSurvivedWave(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Survived Wave #{count}", data => new("Region entered when a particular reactor startup wave is survived", data.Region_Objective));

        public RegionID Region_ReactorStartupCompletedStartup(int count = 1)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Completed Reactor Startup #{count}", data => new("Region entered when a particular number of reactor startups are completed", data.Region_Objective));


        public LocationID Location_ReactorStartupReactors_PerObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Reactor Startup Reactor Locations", data => new("Locations checked by entering zones iwth Reactor Startup reactors for a particular objective", data.Location_ReactorStartupReactors));

        public LocationID Location_ReactorStartupSkips_PerObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Reactor Startup Skip Locations", data => new("Location checked when a reactor wave is survived used to grant wave skip items for a particular objective", data.Location_ReactorStartupSkips));

        public LocationID Location_ReactorStartupCodes_PerObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Reactor Startup Codes", data => new("Reactor code locations for a particular objective", data.Location_ReactorStartupCodes));

        public ItemID Item_ReactorStartupReactors_PerObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Reactor Startup Reactors", data => new("Reactor item indicating a reactor startup can be performed for a particular objective", data.Item_ReactorStartupReactors));

        public ItemID Item_ReactorStartupCodes_PerObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Reactor Startup Codes", data => new("Reactor item indicating the player has found a reactor code for a particular objective", data.Item_ReactorStartupCodes));

        public ItemID Item_ReactorStartupSkips_PerObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Reactor Startup Skip", data => new("Reactor items allowing players to skip to a particular wave for a particular objective", data.Item_ReactorStartupSkips));


        public LocationID Location_ReactorStartupReactor_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Reactor Startup Reactor Location #{count}", data => new("A particular reactor startup reactor location", data.Location_ReactorStartupReactors_PerObjective));

        public LocationID Location_ReactorStartupCode_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Reactor Startup Code #{count}", data => new("A particular reactor code's location", data.Location_ReactorStartupCodes_PerObjective));

        public LocationID Location_ReactorStartupSkip_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Reactor Startup Skip Location #{count}", data => new("A particular reactor skip item's location", data.Location_ReactorStartupSkips_PerObjective));

        public ItemID Item_ReactorStartupReactor_Instance(int count)
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Reactor Startup Reactor #{count}", 
                data => new("A particular reactor startup reactor", data.Item_ReactorStartupReactors_PerObjective),
                new ReactorStartupHandler.ReactorStartup_ReactorItem(data.Region_Objective, count)
            );

        public ItemID Item_ReactorStartupCode_Instance(int count)
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Reactor Startup Code #{count}", 
                data => new("A particular reactor startup code", data.Item_ReactorStartupCodes_PerObjective),
                new ReactorStartupHandler.ReactorStartup_CodeItem(data.Region_Objective, count)
            );

        public ItemID Item_ReactorStartupSkip_Instance(int count)
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Reactor Startup Skip #{count}", 
                data => new("A particular reactor startup skip item", data.Item_ReactorStartupSkips_PerObjective),
                new ReactorStartupHandler.ReactorStartup_SkipItem(data.Region_Objective, count)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class ReactorStartupHandler : ArchipelagoFeature
{
    public override string Name => "Reactor Startup Handler";
    public override string Description
        => "Handles the Reactor Startup objective type.\n"
        + "Example: R1C1";
    public override FeatureGroup Group => FeatureGroups.ObjectiveHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public new static IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ??= Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        APCommandHandler.RegisterCommand(m_skipToWaveCommand ??= new());
    }

    public override void OnDisable()
    {
        base.OnDisable();
        APCommandHandler.UnregisterCommand(m_skipToWaveCommand ??= new());
    }

    private SkipToWaveSubcommand? m_skipToWaveCommand = null;

    // Location class so the log helper can inform us when a log is found even if not randomized
    public class ReactorStartup_CodeLocation : TerminalLogHelper.TerminalLogLocation
    {
        public ReactorStartup_CodeLocation(RegionList regions, LocationData randData, ItemID itemId, int codeCount) 
            : base(regions, randData, itemId) 
        {
            CodeCount = codeCount;
        }

        public LG_WardenObjective_Reactor? Reactor { get; set; } = null;
        public int CodeCount { get; private init; }

        public override void OnNotRandomized(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            if (Reactor == null)
                FeatureLogger.Warning("Failed to update reactor UI: Location was not associated with reactor");
            else
                ProgressionObjective_ReactorStartup.NotifyFoundCode(Reactor, CodeCount - 1);
        }
    }

    // Actual reactor item representing a reactor which can be completed
    public class ReactorStartup_ReactorItem : Item
    {
        public ReactorStartup_ReactorItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public int Count { get; private init; }
    }

    // Item representing a reactor code for a particular wave
    public class ReactorStartup_CodeItem : ExpeditionItem
    {
        public ReactorStartup_CodeItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Index = count - 1;
        }

        public RegionID ObjectiveRegion { get; private init; }
        
        public int Index { get; private init; }

        public override RegionID TargetRegion => ObjectiveRegion;

        private List<LG_WardenObjective_Reactor> GetReactors(StateTracker stateTracker)
        {
            // Technically all valid reactors can be running in parallel... So we handle that, even if vanilla doesn't allow it
            Objective.Data data = new Objective.Data(stateTracker.GameData, ObjectiveRegion);
            List<LG_WardenObjective_Reactor> reactors = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<LG_WardenObjective_Reactor>())
                .Select(r => r.Cast<LG_WardenObjective_Reactor>())
                .Where(r => r.m_isWardenObjective)
                .Where(r => r.OriginLayer == data.LayerType)
                .Where(r => r.WardenObjectiveChainIndex == data.ObjectiveIndex)
                .ToList();

            if (reactors.Count == 0)
                FeatureLogger.Error("Failed to find valid reactor while granting reactor code item");

            return reactors;
        }

        public override void OnEnteredExpedition(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player, ItemID itemId)
        {
            stateTracker.AddItemToTerminal(itemId);
            foreach (var reactor in GetReactors(stateTracker))
            {
                ProgressionObjective_ReactorStartup.NotifyFoundCode(reactor, Index);
                if (reactor.m_stateReplicator.State.stateCount == (Index + 1))
                    reactor.CurrentStateOverrideCode = reactor.GetOverrideCodes()[Index];
            }
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            // We simply display this akin to displaying a normal log
            LG_WardenObjective_Reactor? reactor = GetReactors(stateTracker).FirstOrDefault();
            stateTracker.AddItemToTerminal(itemId);
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Retrieving {StateTracker.Get().GameData.Items.LookUpName(itemId)}", 2f);
                if (reactor != null)
                    terminal.AddLine(string.Format(ArchipelagoFeatureHelper.GetFeature<ReactorStartupHandler>().Localization.Get(182408469), reactor.GetOverrideCodes()[Index]));
                else
                    terminal.AddLine($"<#F00>Failed to find any reactors to pull codes from!</color>");
            };
        }
    }

    // Event item used to skip to waves
    public class ReactorStartup_SkipItem : Item
    {
        public ReactorStartup_SkipItem(RegionID objective, int count)
            : base(new ItemData() { IsUseful = true, IsRandomLike = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public int Count { get; private init; }
    }

    [Objective.Callback]
    public void HandleReactorStartupObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.Reactor_Startup) return;

        // Reactor region pickup
        // The startup can be initiated from any reachable reactor in the list (for some reason)
        int count = 0;
        void addReactor(Zone.Data zone)
        {
            ++count;
            data.Locations.CreateValue(
                data.Location_ReactorStartupReactor_Instance(count),
                zone.Region_Zone,
                new LocationData() { IsAutoDiscovered = true },
                data.Item_ReactorStartupReactor_Instance(count)
            );
        }

        foreach (var placement in data.ObjectiveData.ZonePlacementDatas.SelectMany(ps => ps.Iter()))
        {
            var targetZone = data.FindZoneByPlacement(placement);
            if (targetZone == null)
            {
                FeatureLogger.Error($"Failed to find reactor zone by placement: {data.ObjectiveName}");
                continue;
            }
            addReactor(targetZone);
        }

        if (count == 0)
        {   // If no reactors were placed, we can search all zones and hope to find a well-named geomorph
            foreach (var zone in data.AllZones)
            {
                if (zone.CustomGeo == null) continue;
                if (zone.CustomGeo.Contains("hall", StringComparison.OrdinalIgnoreCase)) continue;
                if (zone.CustomGeo.Contains("reactor", StringComparison.OrdinalIgnoreCase))
                {
                    FeatureLogger.Debug($"Using geomorph for reactor objective: {data.ObjectiveName}");
                    addReactor(zone);
                    break;
                }
            }
            if (count == 0)
                FeatureLogger.Error($"No reactor placements: {data.ObjectiveName}");
        }

        // For each wave, there will be a "survive wave" region
        RegionID last = data.Region_Objective;
        Path.PathReq reqItem = new(Path.PathReq.eType.Category, data.Item_ReactorStartupReactors_PerObjective);
        count = 0;
        foreach (var wave in data.Objective.ReactorWaves.Iter())
        {
            ++count;

            // Use the queued required item to enter this region
            RegionID surviveRegion = data.Region_ReactorStartupSurvivedWave(count);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = surviveRegion,
                ReqItem = reqItem,
                ReqCount = 1u,
            });
            last = surviveRegion;

            // Rewards for surviving!
            data.ProcessEvents(surviveRegion, wave.Events ??= new(1));
            ItemID skipItem = data.Item_ReactorStartupSkip_Instance(count);
            data.Locations.CreateValue(
                data.Location_ReactorStartupSkip_Instance(count),
                surviveRegion,
                new LocationData(),
                skipItem
            );

            // Verification (code and code placement)
            ItemID codeItem = data.Item_ReactorStartupCode_Instance(count);
            reqItem = new(Path.PathReq.eType.Category, codeItem); // Queue code as needed for next region
            if (wave.VerifyInOtherZone)
            {
                Zone.Data codeZone = data.FindZoneByIndex(wave.ZoneForVerification)
                    ?? throw new NullReferenceException($"Failed to find zone for reactor code placement!");
                data.Locations.SetValue(
                    data.Location_ReactorStartupCode_Instance(count),
                    new ReactorStartup_CodeLocation(
                        codeZone.TerminalDatas.Select(t => t.Region_Terminal).ToArray(),
                        new LocationData(),
                        codeItem,
                        count
                    )
                );
            }
            else
            {
                data.Locations.CreateValue(
                    data.Location_ReactorStartupCode_Instance(count),
                    surviveRegion,
                    new LocationData(),
                    data.Item_ReactorStartupCode_Instance(count)
                );
            }
        }

        // If we can reach multiple reactors, then we can (probably) perform OnActivateOnSolve multiple times
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        count = 0;
        while (!eventWrapper.IsDone) // By design, always runs at least once
        {
            ++count;
            RegionID completeStartupRegion = data.Region_ReactorStartupCompletedStartup(count);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = completeStartupRegion,
                ReqItem = reqItem,
                ReqCount = checked((uint)count)
            });
            reqItem = new(Path.PathReq.eType.Category, data.Item_ReactorStartupReactors_PerObjective);
            last = completeStartupRegion;
            eventWrapper.Process(completeStartupRegion);
        }

        // Objective can be completed after the first reactor
        if (!data.Objective.DoNotSolveObjectiveOnReactorComplete)
            SharedObjectiveHandler.AddObjectiveCompleteItem(data, data.Region_ReactorStartupCompletedStartup(1));
    }

    /// <summary>
    /// The code is: EN dash, EN dash, dash, EN dash
    /// Nothing actually prevents a player from entering this code (and it being right) other than that 
    ///  they don't know that these are EN dashes and that they don't know how / lack the hardware to type them.
    /// EN Dash is typed with ALT+0150. Dash is just the standard minus-hyphen key.
    /// </summary>
    const string NotACode = "––-–";

    /// <summary>
    /// Reactor codes are distributed post-build. The terminal and log names are then
    ///  stored in the reactor wave data. Very convenient for us...
    /// This is also a convenient time to add our custom progression manager.
    /// </summary>
    [ArchivePatch(typeof(LG_WardenObjective_Reactor), nameof(LG_WardenObjective_Reactor.OnBuildDone))]
    public static class LG_WardenObjective_Reactor__OnBuildDone__Patch
    {
        public static void Postfix(LG_WardenObjective_Reactor __instance)
        {
            Objective.Data data = Expedition.Data.GetFromCurrentExpedition()
                .GetLayer(__instance.OriginLayer)
                .GetObjectiveDatas()
                .ElementAt(__instance.WardenObjectiveChainIndex);

            if (data.Objective.Type != eWardenObjectiveType.Reactor_Startup) return;

            int count = 0;
            foreach (var wave in data.Objective.ReactorWaves)
            {
                ++count;
                if (!wave.HasVerificationTerminal) continue;

                LocationID location = data.Location_ReactorStartupCode_Instance(count);
                ReactorStartup_CodeLocation? loc = data.Locations.LookUpValueChecked(location) as ReactorStartup_CodeLocation;
                if (loc == null)
                    FeatureLogger.Error("Failed to update reactor startup log location with reactor instance!");
                else
                    loc.Reactor = __instance;

                int entry = LG_LevelInteractionManager.Current.m_terminalItemsByKeyString.FindEntry(wave.VerificationTerminalSerial);
                if (entry == -1)
                {
                    FeatureLogger.Error("Failed to identify reactor terminal log.");
                    continue;
                }
                var pair = LG_LevelInteractionManager.Current.m_terminalItemsByKeyString.entries[entry];
                var comp = new UnityEngine.Component(pair.value.Pointer);
                var terminal = comp.GetComponent<LG_ComputerTerminal>();
                TerminalLogHelper.AssociateLog(terminal, wave.VerificationTerminalFileName, location);
            }

            ProgressionObjective_ReactorStartup.Setup(__instance);
        }
    }

    /// <summary>
    /// If we haven't unlocked the reactor code, we'll simply block it.
    /// Note that this is called when the round starts (when the pre-wave countdown begins).
    /// </summary>
    [ArchivePatch(typeof(LG_WardenObjective_Reactor), nameof(LG_WardenObjective_Reactor.TryGetCurrentWaveData))]
    public static class LG_WardenObjective_Reactor__TryGetCurrentWaveData__Patch
    {
        public static void Postfix(LG_WardenObjective_Reactor __instance, ref string overrideCode)
        {
            Objective.Data data = Expedition.Data.GetFromCurrentExpedition()
                .GetLayer(__instance.OriginLayer)
                .GetObjectiveDatas()
                .ElementAt(__instance.WardenObjectiveChainIndex);

            if (data.Objective.Type != eWardenObjectiveType.Reactor_Startup) return;

            int currentState = Math.Max(__instance.m_stateReplicator.State.stateCount, 1);
            LocationID id = data.Location_ReactorStartupCode_Instance(currentState);
            Location loc = data.Locations.LookUpValueChecked(id);

            StateTracker stateTracker = StateTracker.Get();
            if (stateTracker.CollectedItemCounts.GetValueOrDefault(loc.ItemID, 0) <= 0 && loc.RandData.IsTreatedAsRandom)
                overrideCode = NotACode;

            ProgressionObjective_ReactorStartup.Update(__instance);
        }
    }

    /// <summary>
    /// When updating the state, if it's a verify state we can notify that the code was found.
    /// Otherwise, if it's a completion state we can hide our progression objective
    /// </summary>
    [ArchivePatch(typeof(LG_WardenObjective_Reactor), nameof(LG_WardenObjective_Reactor.AttemptInteract), new Type[] { typeof(pReactorInteraction) })]
    public static class LG_WardenObjective_Reactor__AttemptInteract__Patch
    {
        public static void Postfix(LG_WardenObjective_Reactor __instance, pReactorInteraction interaction)
        {
            if (interaction.type == eReactorInteraction.WaitForVerify_startup)
            {
                Objective.Data data = Expedition.Data.GetFromCurrentExpedition()
                    .GetLayer(__instance.OriginLayer)
                    .GetObjectiveDatas()
                    .ElementAt(__instance.WardenObjectiveChainIndex);

                StateTracker stateTracker = StateTracker.Get();
                int count = __instance.m_stateReplicator.State.stateCount;
                stateTracker.NotifyFoundRegion(data.Region_ReactorStartupSurvivedWave(count), null);

                LocationID id = data.Location_ReactorStartupSkip_Instance(count);
                stateTracker.NotifyFoundLocation(id, null);

                if (!data.Objective.ReactorWaves[count - 1].VerifyInOtherZone)
                {
                    id = data.Location_ReactorStartupCode_Instance(count);
                    if (!stateTracker.NotifyFoundLocation(id, null).RandData.IsTreatedAsRandom);
                        ProgressionObjective_ReactorStartup.NotifyFoundCode(__instance, count - 1);
                }
            }
            else if (interaction.type == eReactorInteraction.Finish_startup)
                ProgressionObjective_ReactorStartup.Update(__instance);
        }
    }

    /// <summary>
    /// A OnPostRecall patch used to update our progression objectives when recalling to ensure they're still a-ok
    /// </summary>
    [ArchivePatch(typeof(LG_ComputerTerminal), nameof(LG_ComputerTerminal.OnPostRecall))]
    public static class LG_ComputerTerminal__OnPostRecall__Patch
    {
        public static void Postfix(LG_ComputerTerminal __instance)
        {
            if (__instance.ConnectedReactor == null) return;
            if (!__instance.ConnectedReactor.m_isWardenObjective) return;

            ProgressionObjective_ReactorStartup.Update(__instance.ConnectedReactor);
        }
    }

    /// <summary>
    /// Handles the SKIPTOWAVE subcommand for the AP command
    /// </summary>
    private class SkipToWaveSubcommand : APCommandHandler.SubCommand
    {
        public SkipToWaveSubcommand()
        {
            SubCommandName = "SKIPTOWAVE";
        }

        public override string HelpText
            => "Skips to a specific reactor wave. Must be used on a reactor terminal. Only skips forward."
            + "\nNote: Immediately executes all events related to completing the waves you skip. This may include enemy spawns."
            + "\nNote: You can unlock the ability to skip to a wave by surviving a wave."
            + "\n      For example, surviving wave #1 will unlock the ability to skip to the start of wave #1."
            + "\n      You do not need to enter the verification code, you only need to survive the wave.";

        public override void Execute(LG_ComputerTerminal terminal, string fullLine, string subCommand, string param2)
        {
            if (terminal.ConnectedReactor == null)
            {
                terminal.AddLine("This sub-command must be used on a reactor terminal!");
                return;
            }

            StateTracker stateTracker = StateTracker.Get();
            Objective.Data data = Expedition.Data.GetFromCurrentExpedition()
                .GetLayer(terminal.ConnectedReactor.OriginLayer)
                .GetObjectiveDatas().ElementAt(terminal.ConnectedReactor.WardenObjectiveChainIndex);

            bool hasSkipForWave(int count)
            {
                ItemID skipItem = data.Item_ReactorStartupSkip_Instance(count);
                return StateTracker.Get().CollectedItemCounts.GetValueOrDefault(skipItem, 0) > 0;
            }

            int currentCount = terminal.ConnectedReactor.m_stateReplicator.State.stateCount;
            do
            {
                if ((param2?.Length ?? 0) == 0) break;
                if (!int.TryParse(param2, out int desiredCount)) break;
                if (desiredCount < 2)
                {
                    terminal.AddLine($"You cannot skip to a wave before wave #2");
                    break;
                }

                if (desiredCount <= currentCount && terminal.ConnectedReactor.m_stateReplicator.State.status != eReactorStatus.Inactive_Idle)
                {
                    terminal.AddLine($"You can only skip forward using this command. Current wave: {currentCount}");
                    break;
                }
                else if (desiredCount > terminal.ConnectedReactor.m_waveCountMax)
                {
                    terminal.AddLine($"This reactor only has {terminal.ConnectedReactor.m_waveCountMax} waves; cannot skip past that!");
                    break;
                }

                if (!hasSkipForWave(desiredCount))
                {
                    terminal.AddLine($"Skipping to reactor wave #{desiredCount} has not been unlocked!");
                    break;
                }

                // Invoking events leading to the desired wave count, which ensures doors are opened among other things
                for (int i = Math.Max(currentCount, 1); i < desiredCount; i++)
                    WorldEventManager.ExecuteEvents(data.Objective.ReactorWaves[i - 1].Events, 0f);

                // Setting the state via StateReplicator's property will push it over the network too
                pReactorState newState = new()
                {
                    stateCount = desiredCount,
                    stateProgress = 0f,
                    status = eReactorStatus.Startup_intro,
                    verifyFailed = false,
                };
                terminal.ConnectedReactor.m_stateReplicator.State = newState;

                terminal.AddLine($"Jumping to reactor wave #{desiredCount}");
                return;
            } while (false);

            var unlockedWaves = Enumerable.Range(currentCount + 1, terminal.ConnectedReactor.m_waveCountMax - currentCount).Where(hasSkipForWave);
            if (unlockedWaves.Any())
                terminal.AddLine($"Waves you may currently skip to: {string.Join(", ", unlockedWaves)}");
            else
                terminal.AddLine($"Waves you may currently skip to: None");
        }
    }

    /// <summary>
    /// Custom progression objective object used to display codes during reactor startup sequence
    /// </summary>
    public class ProgressionObjective_ReactorStartup : CustomObjectiveHandler.ObjectiveItem
    {
        /// <summary>
        /// Make the string key used to find the progression objective for a particular reactor objective
        /// </summary>
        public static string MakeKey(LG_WardenObjective_Reactor reactor)
            => reactor.Pointer.ToString();

        /// <summary>
        /// Set up a progression objective for a particular reactor objective
        /// </summary>
        public static void Setup(LG_WardenObjective_Reactor reactor)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_ReactorStartup>(MakeKey(reactor)).SetupInternal(reactor);

        /// <summary>
        /// Update the text for a particular reactor progression objective
        /// </summary>
        public static void Update(LG_WardenObjective_Reactor reactor)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_ReactorStartup>(MakeKey(reactor)).UpdateInternal(reactor);

        /// <summary>
        /// Notify that a reactor code was found
        /// </summary>
        public static void NotifyFoundCode(LG_WardenObjective_Reactor reactor, int codeIndex)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_ReactorStartup>(MakeKey(reactor)).NotifyFoundCodeInternal(reactor, codeIndex);

        private BitArray? m_obtainedCodes = null;

        public override void Setup()
        {
            IsActive = false;
            Scope = CustomObjectiveHandler.eObjectiveScope.Layer;
            ObjectiveTag = "CODES";
            HeaderText = "REACTOR STARTUP OBJECTIVE";
            SubText = "Retrieving data...";
            base.Setup();
        }

        /// <summary>
        /// Set up this progression objective to use the provided data and reactor
        /// </summary>
        public void SetupInternal(LG_WardenObjective_Reactor reactor)
        {
            m_obtainedCodes = new(reactor.m_waveCountMax);
            LayerType layer = reactor.OriginLayer; // Where the objective originated
            int count = reactor.WardenObjectiveChainIndex + 1;

            HeaderText = $"{layer.GetName()} Objective #{count} (Reactor Startup)";
            ScopeTarget = new(
                dimension: layer,
                layer: layer,
                zone: GameData.eLocalZoneIndex.Zone_0
            );
            Refresh();
        }

        /// <summary>
        /// Update this progression status using the provided data and reactor.
        /// </summary>
        public void UpdateInternal(LG_WardenObjective_Reactor reactor)
        {
            IsActive = reactor.m_stateReplicator.State.status != eReactorStatus.Inactive_Idle
                && reactor.m_stateReplicator.State.status != eReactorStatus.Startup_complete;
            SubText = MakeFormattedText(reactor);
            Refresh();
        }

        /// <summary>
        /// Notify this UI that a particular code has been found for a particular reactor
        /// </summary>
        public void NotifyFoundCodeInternal(LG_WardenObjective_Reactor reactor, int codeIndex)
        {
            if (m_obtainedCodes == null)
                FeatureLogger.Error("Cannot update reactor startup UI; it is not yet set up!");
            else if (codeIndex < 0 || codeIndex >= m_obtainedCodes.Count)
                FeatureLogger.Warning("Failed to update reactor startup UI; code count is out of bounds!");
            else
                m_obtainedCodes[codeIndex] = true;
            UpdateInternal(reactor);
        }

        /// <summary>
        /// Create the formatted text used to display this reactor's codes
        /// </summary>
        /// <param name="reactor">The specific reactor that is currently being started up</param>
        /// <returns></returns>
        private string MakeFormattedText(LG_WardenObjective_Reactor reactor)
        {
            int currentWave = Math.Max(reactor.m_stateReplicator.State.stateCount, 1);
            int maxWaves = reactor.m_waveCountMax;
            string formatCode(string code, int index)
            {
                string value = (m_obtainedCodes != null && m_obtainedCodes[index]) ? code : NotACode;
                return $"Wave #{index + 1} Code: {value}{((index + 1) == currentWave ? " <" : "")}";
            }
            var codes = reactor.GetOverrideCodes().RangeSubset(0, maxWaves).Select(formatCode).ToList();

            int midWave = currentWave;
            IEnumerable<string> texts;
            if (maxWaves < 6)
                texts = codes;
            else if (midWave < 4)
                texts = Enumerable.Range(0, 4).Select(i => codes[i]).Append(" ···");
            else if ((maxWaves - midWave) < 3)
                texts = Enumerable.Range(maxWaves - 4, 4).Select(i => codes[i]).Prepend(" ···");
            else
                texts = Enumerable.Range(midWave - 2, 3).Select(i => codes[i]).Prepend(" ···").Append(" ···");

            return string.Join("\n> ", texts);
        }
    }
}
