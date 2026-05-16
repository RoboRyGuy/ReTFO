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

public static class TimedSequenceHandler_Tags
{
    extension(Game.Data data)
    {
        public TagResolver Tag_TimedSequenceMainLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Timed Sequence Main Locations", "Locations containing the main terminal for a timed sequence", gd.Tag_Never));

        public TagResolver Tag_TimedSequenceMainItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Timed Sequence Main Items", "Timed Sequence main terminal items", gd.Tag_Never));

        public TagResolver Tag_TimedSequenceVerifyLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Timed Sequence Verify Locations", "Locations containing a verify terminal for a timed sequence", gd.Tag_Never));

        public TagResolver Tag_TimedSequenceVerifyItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Timed Sequence Verify Items", "Timed Sequence verify terminal items", gd.Tag_Never));
    }

    extension(Objective.Data data)
    {
        public TagResolver Tag_TimedSequenceVerifyLocations_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Timed Sequence Verify Locations", "Locations containing a verify terminal for a particular timed sequence", gd.Tag_TimedSequenceVerifyLocations));

        public TagResolver Tag_TimedSequenceVerifyItems_PerObjective
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName(null)} Timed Sequence Verify Items", "Timed Sequence verify terminal items for a particular objective", gd.Tag_TimedSequenceVerifyItems));
    }
}


[EnableFeatureByDefault, AutomatedFeature]
public class TimedSequenceHandler : ArchipelagoFeature
{
    public override string Name => "Timed Sequence Handler";
    public override string Description
        => "Handles the TimedSequence objective type.\n"
        + "This handles specifically only the corrupted or \"dual\" uplink type"
        + "Example: R5C3";
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
            = eWardenObjectiveType.TimedTerminalSequence;

        // Summary for This objective
        public static string ObjectiveSummary(Objective.Data data)
        {
            CheckIsCorrectObjective(data);
            return $"Perform {data.Objective.TimedTerminalSequence_NumberOfRounds}-Round Timed Sequence";
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
        // Region reached by starting a Timed Sequence
        public static string StartRound(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Start Round {count}";

        // Region reached by failing a Time Sequence
        public static string FailRound(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Fail Round {count}";

        // Region reached by completing a Time Sequence
        public static string CompleteRound(Objective.Data data, int count)
            => $"{data.ObjectiveName()} Complete Round {count}";
    }

    private static class TimedSequence_MainLocation
    {
        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Main Terminal Location", "Location of a particular terminal", gd.Tag_TimedSequenceMainLocations));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private static class TimedSequence_VerifyLocation
    {
        public static TagResolver MakeTag(Objective.Data data, int count)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Verify Terminal Location ${count}", "Location of a particular terminal", data.Tag_TimedSequenceVerifyLocations_PerObjective));

        public static LocationData MakeRandData() => new LocationData() { IsAutoDiscovered = true };
    }

    private class TimedSequence_MainItem : Item
    {
        public TimedSequence_MainItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Main Terminal Item", "A particular terminal", gd.Tag_TimedSequenceMainItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }
    }

    private class TimedSequence_VerifyItem : Item
    {
        public TimedSequence_VerifyItem(Objective.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ObjectiveData = data;
        }

        public static TagResolver MakeTag(Objective.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ObjectiveName()} Verify Terminal Item", "A particular terminal", data.Tag_TimedSequenceVerifyItems_PerObjective));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Objective.Data ObjectiveData { get; set; }

        public override Path.RequiredItem PathReqs => new(Path.RequiredItem.eType.Category, ObjectiveData.Tag_TimedSequenceVerifyItems_PerObjective);
    }

    public static KeyedItem GetMainTerminal(Objective.Data data)
    {
        if (data.TryLookupItem(TimedSequence_MainItem.MakeTag(data), out var item))
            return item;

        Item newItem = new TimedSequence_MainItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    public static KeyedItem GetVerifyTerminal(Objective.Data data)
    {
        if (data.TryLookupItem(TimedSequence_VerifyItem.MakeTag(data), out var item))
            return item;

        Item newItem = new TimedSequence_VerifyItem(data);
        return new(data.AddItem(newItem), newItem);
    }


    // Objective requiring the completion of one or more timed terminal sequences
    [Objective.Callback]
    public void HandleTimedSequenceObjective(Objective.Data data)
    {
        if (!This.IsCorrectObjective(data))
            return;

        if (data.Objective.TimedTerminalSequence_NumberOfRounds < 1)
        {
            FeatureLogger.Error($"{data.ObjectiveName()}: Cannot have fewer than 1 TimedSequence round!");
            return;
        }

        // Technically, you could get lucky. However, I'm going to state you need access to all terminals to do more than just start the sequence
        // Also, to be safe, we're manually making region sets (to avoid the unpacking process)
        var regionSets = data.PlacementsToTerminalRegions(data.ObjectiveData.ZonePlacementDatas);

        // Place main terminal in world
        KeyedItem mainTerminalItem = GetMainTerminal(data);
        data.AddLocation(
            TimedSequence_MainLocation.MakeTag(data),
            regionSets.First().Select(info => info.Region).ToList(),
            TimedSequence_MainLocation.MakeRandData(),
            mainTerminalItem.ID
        );

        // Place verificaiton terminals in world
        KeyedItem verifyTerminalItem = GetVerifyTerminal(data);
        int count = 0;
        foreach (var regionSet in regionSets.Skip(1))
        {
            ++count;
            data.AddLocation(
                TimedSequence_VerifyLocation.MakeTag(data, count),
                regionSet.Select(i => i.Region).ToList(),
                TimedSequence_VerifyLocation.MakeRandData(),
                verifyTerminalItem.ID
            );
        }

        // Note: The first iteration (i = 1) has been unrolled so that we can add checks to the paths
        string startName, failName, succeedName;
        RegionID startRegion, failRegion, succeedRegion;
        int i = 1;

        startName = ThisRegions.StartRound(data, i);
        startRegion = data.LookupOrCreateRegion(startName);
        data.AddPath(new Path()
        {
            StartingRegion = data.ObjectiveStartRegion,
            EndingRegion = startRegion,
            ReqItem = mainTerminalItem.PathReqs,
            ReqCount = 1u,
        });
        if (data.Objective.TimedTerminalSequence_EventsOnSequenceStart.Count >= i)
            data.ProcessEvents(startRegion, startName, data.Objective.TimedTerminalSequence_EventsOnSequenceStart[i - 1]);

        failName = ThisRegions.FailRound(data, i);
        failRegion = data.LookupOrCreateRegion(failName);
        data.AddPath(new Path()
        {
            StartingRegion = startRegion,
            EndingRegion = failRegion,
        });
        if (data.Objective.TimedTerminalSequence_EventsOnSequenceFail.Count >= i)
            data.ProcessEvents(failRegion, failName, data.Objective.TimedTerminalSequence_EventsOnSequenceFail[i - 1]);

        succeedName = ThisRegions.CompleteRound(data, i);
        succeedRegion = data.LookupOrCreateRegion(succeedName);
        data.AddPath(new Path()
        {
            StartingRegion = startRegion,
            EndingRegion = succeedRegion,
            ReqItem = verifyTerminalItem.PathReqs,
            ReqCount = (uint)count,
        });
        if (data.Objective.TimedTerminalSequence_EventsOnSequenceDone.Count >= i)
            data.ProcessEvents(succeedRegion, succeedName, data.Objective.TimedTerminalSequence_EventsOnSequenceDone[i - 1]);

        for (i = 2; i <= data.Objective.TimedTerminalSequence_NumberOfRounds; i++)
        {
            startName = ThisRegions.StartRound(data, i);
            startRegion = data.LookupOrCreateRegion(startName);
            data.AddPath(new Path()
            {
                StartingRegion = data.ObjectiveStartRegion,
                EndingRegion = startRegion,
            });
            if (data.Objective.TimedTerminalSequence_EventsOnSequenceStart.Count >= i)
                data.ProcessEvents(startRegion, startName, data.Objective.TimedTerminalSequence_EventsOnSequenceStart[i - 1]);

            failName = ThisRegions.FailRound(data, i);
            failRegion = data.LookupOrCreateRegion(failName);
            data.AddPath(new Path()
            {
                StartingRegion = startRegion,
                EndingRegion = failRegion,
            });
            if (data.Objective.TimedTerminalSequence_EventsOnSequenceFail.Count >= i)
                data.ProcessEvents(failRegion, failName, data.Objective.TimedTerminalSequence_EventsOnSequenceFail[i - 1]);

            succeedName = ThisRegions.CompleteRound(data, i);
            succeedRegion = data.LookupOrCreateRegion(succeedName);
            data.AddPath(new Path()
            {
                StartingRegion = startRegion,
                EndingRegion = succeedRegion,
            });
            if (data.Objective.TimedTerminalSequence_EventsOnSequenceDone.Count >= i)
                data.ProcessEvents(succeedRegion, succeedName, data.Objective.TimedTerminalSequence_EventsOnSequenceDone[i - 1]);
        }

        // OnActivateOnSolveItem triggers when the full sequence is compelte
        if (data.Objective.OnActivateOnSolveItem && data.Objective.EventsOnActivate.Any())
            data.ProcessEvents(succeedRegion, succeedName, data.Objective.EventsOnActivate);

        // Place CompleteObjective item in the final succeed region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, succeedRegion);
    }

}
