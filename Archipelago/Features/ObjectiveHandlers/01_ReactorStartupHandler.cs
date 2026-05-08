using Il2CppInterop.Runtime;
using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Features.Terminals;
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

public static class ReactorStartupHandler_Tags
{
    extension (Game.Data data)
    {
        public TagResolver Tag_ReactorStartupReactorLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Reactor Startup Reactor Locations", "Locations checked by entering zones with Reactor Startup reactors (as opposed to Reactor Shutdown reactors)", gd.Tag_Never));

        public TagResolver Tag_ReactorStartupCodeLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Reactor Startup Code Locations", "Locations checked by finding Reactor Startup codes. Includes \"free\" codes", gd.Tag_TerminalLogLocations));

        public TagResolver Tag_ReactorStartupSkipLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Reactor Startup Skip Locations", "Location checked when a reactor wave is survived used to grant wave skip items", gd.Tag_Never));

        public TagResolver Tag_ReactorStartupReactorItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Reactor Startup Reactor", "Reactor item indicating a reactor startup can be performed", gd.Tag_Never));

        public TagResolver Tag_ReactorStartupCodeItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Reactor Startup Code", "Reactor item indicating the player has found a reactor code", gd.Tag_TerminalLogItems));

        public TagResolver Tag_ReactorStartupSkipItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Reactor Startup Skip", "Reactor items allowing players to skip to a particular wave", gd.Tag_Never));
    }

    extension (Objective.Data data)
    {
        public TagResolver Tag_ReactorStartupReactorLocations_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Reactor Startup Reactor Locations", "Locations checked by entering zones iwth Reactor Startup reactors for a particular objective", gd.Tag_ReactorStartupReactorLocations));

        public TagResolver Tag_ReactorStartupReactorItems_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Reactor Startup Reactors", "Reactor item indicating a reactor startup can be performed for a particular objective", gd.Tag_ReactorStartupReactorItems));

        public TagResolver Tag_ReactorStartupSkipLocations_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Reactor Startup Skip Locations", "Location checked when a reactor wave is survived used to grant wave skip items for a particular objective", gd.Tag_ReactorStartupSkipLocations));

        public TagResolver Tag_ReactorStartupCodeLocations_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Reactor Startup Codes", "Reactor code locations for a particular objective", gd.Tag_ReactorStartupCodeLocations));

        public TagResolver Tag_ReactorStartupCodeItems_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Reactor Startup Codes", "Reactor item indicating the player has found a reactor code for a particular objective", gd.Tag_ReactorStartupCodeItems));

        public TagResolver Tag_ReactorStartupSkipItems_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Reactor Startup Skip", "Reactor items allowing players to skip to a particular wave for a particular objective", gd.Tag_ReactorStartupSkipItems));
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

    // Implementation of common static methods for objective handlers
    private static class This
    {
        // Which objective This is for
        public const eWardenObjectiveType ObjectiveType
            = eWardenObjectiveType.Reactor_Startup;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return "Reactor Startup";
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
        // Region for when the reactor zone is entered (found)
        public static string FoundReactor(Objective.Data data)
            => $"{This.ObjectiveName(data)} Found Reactor";

        // Region entered when a reactor wave is survived (before code is entered)
        public static string SurvivedWave(Objective.Data data, int count)
            => $"{This.ObjectiveName(data)} Survived Wave #{count}";

        // Region entered when a reactor is completed. Multiple reactors can theoretically be completed per objective
        public static string CompletedStartup(Objective.Data data, int count = 1)
            => $"{This.ObjectiveName(data)} Completed Reactor Startup #{count}";
    }

    // Location containing a reactor item. In-game this would be the terminal itself, but
    //  reactor terminals can't be tracked via GameData so we just use the zone instead
    private static class ReactorStartup_ReactorLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Reactor Location #{count}", "A reactor location for a particular reactor in an objective", data.Tag_ReactorStartupReactorLocations_PerObjective));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    // Actual reactor item representing a reactor which can be completed
    private class ReactorStartup_ReactorItem : Item
    {
        public ReactorStartup_ReactorItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Reactor", "A particular reactor item", data.Tag_ReactorStartupReactorItems_PerObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }

        // Probably the easiest way to randomize this is to lock the reactor terminal >:)
    }

    // Location containing a reactor code
    private static class ReactorStartup_CodeLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Reactor Code #{count} Location", "A reactor code location for a particular reactor code", data.Tag_ReactorStartupCodeLocations_PerObjective));

        public static LocationData MakeRandData() => new LocationData();
    }

    // Item representing a reactor code for a particular wave
    private class ReactorStartup_CodeItem : Item
    {
        public ReactorStartup_CodeItem(Objective.Data data, int count)
            : base(MakeTag(data, count), MakeRandData())
        {
            ObjectiveData = data;
            Index = count - 1;
        }

        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Reactor Code #{count}", "A particular reactor code", data.Tag_ReactorStartupCodeItems_PerObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }
        
        public int Index { get; set; }

        private List<LG_WardenObjective_Reactor> GetReactors()
        {
            // Technically all valid reactors can be running in parallel... So we handle that, even if vanilla doesn't allow it
            List<LG_WardenObjective_Reactor> reactors = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<LG_WardenObjective_Reactor>())
                .Select(r => r.Cast<LG_WardenObjective_Reactor>())
                .Where(r => r.m_isWardenObjective)
                .Where(r => r.OriginLayer == ObjectiveData.LayerType)
                .Where(r => r.WardenObjectiveChainIndex == ObjectiveData.ObjectiveIndex)
                .ToList();

            if (reactors.Count == 0)
                FeatureLogger.Error("Failed to find valid reactor while granting reactor code item");

            return reactors;
        }

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (ObjectiveData.IsCurrentlyInExpedition())
                OnStartExpeditionWithItem(stateTracker, ObjectiveData);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (!ObjectiveData.IsSameExpedition(data)) return;

            var reactors = GetReactors();

            // Restoring the correct code from the list, if necessary - this will automatically fix the vanilla reactor UI and terminal command too
            foreach (var reactor in reactors)
            {
                if (reactor.m_stateReplicator.State.stateCount == (Index + 1))
                {
                    reactor.CurrentStateOverrideCode = reactor.GetOverrideCodes()[Index];
                    ProgressionObjective_ReactorStartup.Update(ObjectiveData, reactor);
                }
            }

            // In case the player wants to view it in the terminal for whatever reason
            stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            // We simply display this akin to displaying a normal log
            LG_WardenObjective_Reactor? reactor = GetReactors().FirstOrDefault();
            stateTracker.AddItemToTerminal(this);
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Retrieving {NameTag}", 2f);
                if (reactor != null)
                    terminal.AddLine(string.Format(ArchipelagoFeatureHelper.GetFeature<ReactorStartupHandler>().Localization.Get(182408469), reactor.GetOverrideCodes()[Index]));
                else
                    terminal.AddLine($"<#F00>Failed to find any reactors to pull codes from!</color>");
            };
        }
    }

    // Location for an event item used to skip waves
    private static class ReactorStartup_SkipLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Reactor Wave #{count} Skip Location", "A reactor skip location for a particular skip", data.Tag_ReactorStartupSkipLocations_PerObjective));

        public static LocationData MakeRandData() => new LocationData();
    }

    // Event item used to skip to waves
    private class ReactorStartup_SkipItem : Item
    {
        public ReactorStartup_SkipItem(Objective.Data data, int count)
            : base(MakeTag(data, count), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{This.ObjectiveName(data)} Reactor Skip #{count}", "A particular reactor skip", data.Tag_ReactorStartupSkipItems_PerObjective));

        public static ItemData MakeRandData() => new ItemData() { IsUseful = true, IsRandomLike = true };

        public Objective.Data ObjectiveData { get; set; }
    }

    public static KeyedItem GetReactorItem(Objective.Data data)
    {
        if (data.TryLookupItem(ReactorStartup_ReactorItem.MakeTag(data), out var item))
            return item;

        Item newItem = new ReactorStartup_ReactorItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    public static KeyedItem GetCodeItem(Objective.Data data, int count)
    {
        if (data.TryLookupItem(ReactorStartup_CodeItem.MakeTag(data, count), out var item))
            return item;

        Item newItem = new ReactorStartup_CodeItem(data, count);
        return new(data.AddItem(newItem), newItem);
    }

    public static KeyedItem GetSkipItem(Objective.Data data, int count)
    {
        if (data.TryLookupItem(ReactorStartup_SkipItem.MakeTag(data, count), out var item))
            return item;

        Item newItem = new ReactorStartup_SkipItem(data, count);
        return new(data.AddItem(newItem), newItem);
    }

    [Objective.Callback]
    public void HandleReactorStartupObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        // Reactor region pickup
        // The startup can be initiated from any reachable reactor in the list (for some reason)
        int count = 0;
        KeyedItem reactorItem = GetReactorItem(data);
        void addReactor(Zone.Data zone)
        {
            ++count;
            RegionList regions = data.LookupOrCreateRegion(zone.ZoneName);
            data.AddLocation(
                ReactorStartup_ReactorLocation.MakeTag(data, count),
                regions,
                ReactorStartup_ReactorLocation.MakeRandData(),
                reactorItem.ID
            );
        }

        foreach (var placement in data.ObjectiveData.ZonePlacementDatas.SelectMany(ps => ps.Iter()))
        {
            var targetZone = data.FindZoneByPlacement(placement);
            if (targetZone == null)
            {
                FeatureLogger.Error($"Failed to find reactor zone by placement: {This.ObjectiveName(data)}");
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
                    FeatureLogger.Debug($"Using geomorph for reactor objective: {This.ObjectiveName(data)}");
                    addReactor(zone);
                    break;
                }
            }
            if (count == 0)
                FeatureLogger.Error($"No reactor placements: {This.ObjectiveName(data)}");
        }

        // For each wave, there will be a "survive wave" region
        RegionID last = data.ObjectiveStartRegion;
        Path.RequiredItem reqItem = reactorItem.PathReqs;
        count = 0;
        foreach (var wave in data.Objective.ReactorWaves.Iter())
        {
            ++count;

            // Use the queued required item to enter this region
            string surviveName = ThisRegions.SurvivedWave(data, count);
            RegionID surviveRegion = data.LookupOrCreateRegion(surviveName);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = surviveRegion,
                ReqItem = reqItem,
                ReqCount = 1u,
            });
            last = surviveRegion;

            // Rewards for surviving!
            data.ProcessEvents(surviveRegion, surviveName, wave.Events ??= new(1));
            KeyedItem skipItem = GetSkipItem(data, count);
            data.AddLocation(
                ReactorStartup_SkipLocation.MakeTag(data, count),
                surviveRegion,
                ReactorStartup_SkipLocation.MakeRandData(),
                skipItem.ID
            );

            // Verification (code and code placement)
            KeyedItem codeItem = GetCodeItem(data, count);
            reqItem = codeItem.PathReqs; // Queue code as needed for next region
            RegionList placement;
            if (wave.VerifyInOtherZone)
            {
                Zone.Data codeZone = data.FindZoneByIndex(wave.ZoneForVerification)
                    ?? throw new NullReferenceException($"Failed to find zone for reactor code placement!");
                placement = codeZone.TerminalDatas.Select(t => data.LookupOrCreateRegion(t.TerminalName)).ToList();
            }
            else
            {
                placement = surviveRegion;
            }
            data.AddLocation(
                ReactorStartup_CodeLocation.MakeTag(data, count),
                placement,
                ReactorStartup_CodeLocation.MakeRandData(),
                codeItem.ID
            );
        }

        // If we can reach multiple reactors, then we can (probably) perform OnActivateOnSolve multiple times
        var eventWrapper = data.MakeOrWrapOnSolveEvents();
        count = 0;
        while (!eventWrapper.IsDone) // By design, always runs at least once
        {
            ++count;
            string completeStartupName = ThisRegions.CompletedStartup(data, count);
            RegionID completeStartupRegion = data.LookupOrCreateRegion(completeStartupName);
            data.AddPath(new Path()
            {
                StartingRegion = last,
                EndingRegion = completeStartupRegion,
                ReqItem = reqItem,
                ReqCount = 1u
            });
            reqItem = reactorItem.PathReqs; // All future events get associated with a different reactor
            last = completeStartupRegion;
            eventWrapper.Process(completeStartupRegion, completeStartupName);
        }

        // Objective can be completed after the first reactor
        if (!data.Objective.DoNotSolveObjectiveOnReactorComplete)
            SharedObjectiveHandler.AddObjectiveCompleteItem(data, data.LookupOrCreateRegion(ThisRegions.CompletedStartup(data, 1)));
    }

    /// <summary>
    /// The code is: EN dash, EN dash, dash, EN dash
    /// Nothing actually prevents the user from entering this code (and it being right) other than that 
    ///  they don't know that these are EN dashes and that they don't know / lack the hardware to type them.
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
            Objective.Data data = Expedition.Data.FromCurrentExpedition()
                .GetLayer(__instance.OriginLayer)
                .GetObjectiveDatas()
                .ElementAt(__instance.WardenObjectiveChainIndex);

            if (!This.IsCorrectObjective(data)) return;

            int count = 0;
            foreach (var wave in data.Objective.ReactorWaves)
            {
                ++count;
                if (!wave.HasVerificationTerminal) continue;

                if (!data.TryLookupLocation(ReactorStartup_CodeLocation.MakeTag(data, count), out var location))
                {
                    FeatureLogger.Error($"Failed to create association for reactor code: {data.ObjectiveName(null)} Code #{count}");
                    continue;
                }

                int entry = LG_LevelInteractionManager.Current.m_terminalItemsByKeyString.FindEntry(wave.VerificationTerminalSerial);
                if (entry == -1)
                {
                    FeatureLogger.Error("Failed to identify reactor terminal log.");
                    continue;
                }
                var pair = LG_LevelInteractionManager.Current.m_terminalItemsByKeyString.entries[entry];
                var comp = new UnityEngine.Component(pair.value.Pointer);
                var terminal = comp.GetComponent<LG_ComputerTerminal>();
                TerminalLogHelper.AssociateLog(terminal, wave.VerificationTerminalFileName, location.ID);
            }

            var po = CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_ReactorStartup>(This.ObjectiveName(data));
            ProgressionObjective_ReactorStartup.Setup(data, __instance);
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
            Objective.Data data = Expedition.Data.FromCurrentExpedition()
                .GetLayer(__instance.OriginLayer)
                .GetObjectiveDatas()
                .ElementAt(__instance.WardenObjectiveChainIndex);

            if (!This.IsCorrectObjective(data)) return;

            int currentState = Math.Max(__instance.m_stateReplicator.State.stateCount, 1);
            if (!data.TryLookupLocation(ReactorStartup_CodeLocation.MakeTag(data, currentState), out var loc))
            {
                FeatureLogger.Error("Unknown error while checking if reactor code is obtained");
                return;
            }

            StateTracker stateTracker = StateTracker.Get();
            if (stateTracker.CollectedItemCounts.GetValueOrDefault(loc.ItemID, 0) == 0 && stateTracker.TestRandomization(loc.Location).IsTreatedAsRandom)
                overrideCode = NotACode;

            ProgressionObjective_ReactorStartup.Update(data, __instance);
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
                Objective.Data data = Expedition.Data.FromCurrentExpedition()
                    .GetLayer(__instance.OriginLayer)
                    .GetObjectiveDatas()
                    .ElementAt(__instance.WardenObjectiveChainIndex);

                StateTracker stateTracker = StateTracker.Get();
                int count = __instance.m_stateReplicator.State.stateCount;
                stateTracker.NotifyFoundRegion(ThisRegions.SurvivedWave(data, count), null);

                if (data.TryLookupLocation(ReactorStartup_SkipLocation.MakeTag(data, count), out var skipLocation))
                    stateTracker.NotifyFoundLocation(skipLocation.ID, null);
                else
                    FeatureLogger.Error("Failed to find skip location while grant location check");

                if (!data.Objective.ReactorWaves[count - 1].VerifyInOtherZone)
                {
                    if (data.TryLookupLocation(ReactorStartup_CodeLocation.MakeTag(data, count), out var codeLocation))
                        stateTracker.NotifyFoundLocation(codeLocation.ID, null);
                    else
                        FeatureLogger.Error("Failed to find code location while grant free code location check");
                }
            }
            else if (interaction.type == eReactorInteraction.Finish_startup)
            {
                Objective.Data data = Expedition.Data.FromCurrentExpedition()
                    .GetLayer(__instance.OriginLayer)
                    .GetObjectiveDatas()
                    .ElementAt(__instance.WardenObjectiveChainIndex);
                ProgressionObjective_ReactorStartup.Update(data, __instance);
            }
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

            Objective.Data data = Expedition.Data.FromCurrentExpedition()
                .GetLayer(__instance.ConnectedReactor.OriginLayer)
                .GetObjectiveDatas()
                .ElementAt(__instance.ConnectedReactor.WardenObjectiveChainIndex);
            if (!This.IsCorrectObjective(data)) return;

            ProgressionObjective_ReactorStartup.Update(data, __instance.ConnectedReactor);
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
            Objective.Data data = Expedition.Data.FromCurrentExpedition()
                .GetLayer(terminal.ConnectedReactor.OriginLayer)
                .GetObjectiveDatas().ElementAt(terminal.ConnectedReactor.WardenObjectiveChainIndex);
            bool hasWave(int count)
            {
                if (data.TryLookupItem(ReactorStartup_SkipItem.MakeTag(data, count), out var item))
                    return stateTracker.CollectedItemCounts.GetValueOrDefault(item.ID, 0) > 0;

                FeatureLogger.Error($"Failed to get reactor skip item for wave: {count}");
                return false;
            }
            int currentCount = terminal.ConnectedReactor.m_stateReplicator.State.stateCount;

            do
            {
                if ((param2?.Length ?? 0) == 0) break;
                if (!int.TryParse(param2, out int desiredCount)) break;
                if (desiredCount < 1)
                {
                    terminal.AddLine($"You cannot skip to a wave before wave #1");
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

                if (!hasWave(desiredCount))
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

            var unlockedWaves = Enumerable.Range(currentCount + 1, terminal.ConnectedReactor.m_waveCountMax - currentCount).Where(hasWave);
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
        public static string MakeKey(Objective.Data data, LG_WardenObjective_Reactor reactor)
            => This.ObjectiveName(data) + reactor.Pointer.ToString();

        /// <summary>
        /// Set up a progression objective for a particular reactor objective
        /// </summary>
        public static void Setup(Objective.Data data, LG_WardenObjective_Reactor reactor)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_ReactorStartup>(MakeKey(data, reactor)).SetupWithData(data, reactor);

        /// <summary>
        /// Update the text for a particular reactor progression objective
        /// </summary>
        public static void Update(Objective.Data data, LG_WardenObjective_Reactor reactor)
            => CustomObjectiveHandler.GetObjectiveItem<ProgressionObjective_ReactorStartup>(MakeKey(data, reactor)).UpdateInternal(data, reactor);

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
        public void SetupWithData(Objective.Data data, LG_WardenObjective_Reactor reactor)
        {
            HeaderText = This.ObjectiveName(data);
            ScopeTarget = new(
                data.LayerType,
                data.LayerType,
                GameData.eLocalZoneIndex.Zone_0
            );
            Refresh();
        }

        /// <summary>
        /// Update this progression status using the provided data and reactor.
        /// </summary>
        /// <param name="data">The objective data for this reactor's objective</param>
        /// <param name="reactor">The reactor to update for</param>
        public void UpdateInternal(Objective.Data data, LG_WardenObjective_Reactor reactor)
        {
            IsActive = reactor.m_stateReplicator.State.status != eReactorStatus.Inactive_Idle
                && reactor.m_stateReplicator.State.status != eReactorStatus.Startup_complete;
            SubText = MakeFormattedText(data, reactor);
            Refresh();
        }

        /// <summary>
        /// Create the formatted text used to display this reactor's codes
        /// </summary>
        /// <param name="data">Objective data for the startup objective</param>
        /// <param name="reactor">The specific reactor that is currently being started up</param>
        /// <returns></returns>
        private string MakeFormattedText(Objective.Data data, LG_WardenObjective_Reactor reactor)
        {
            StateTracker stateTracker = StateTracker.Get();

            int currentWave = Math.Max(reactor.m_stateReplicator.State.stateCount, 1);
            int maxWaves = reactor.m_waveCountMax;
            string formatCode(string code, int index)
            {
                bool isObtained = false;
                if (data.TryLookupLocation(ReactorStartup_CodeLocation.MakeTag(data, index + 1), out var loc))
                    isObtained = stateTracker.CollectedItemCounts.GetValueOrDefault(loc.ItemID, 0) > 0 || !stateTracker.TestRandomization(loc.Location).IsTreatedAsRandom;
                else
                    FeatureLogger.Error($"Failed to lookup code location #{index + 1} during code UI formatting");

                string value = isObtained ? code : NotACode;
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
