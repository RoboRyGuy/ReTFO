using Clonesoft.Json;
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

    /* TODO:
     *  Location: 
     *  Item: 
     *  Region: 
     */

    /// <summary>
    /// TODO: Implement this
    /// A location implemented via the TimedSequence objective
    /// </summary>
    private class TimedSequenceLocation : Location
    {
        public TimedSequenceLocation(string name, RegionList regions, Item? item = null)
            : base(name, regions, item)
        {
            Name = name;
            OwningRegionIds = regions;
            ItemID = item?.ID ?? 0L;
        }

        private static RandomizationData s_randData = new() 
        {
            AutoDiscover = true,
        };
        public override RandomizationData RandData => s_randData;
    }

    /// <summary>
    /// Main terminal used for the sequence, where Init and Confirm are entered
    /// </summary>
    private class MainTerminalItem : Item
    {
        public MainTerminalItem(string name, Objective.Data data)
            : base(name)
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }

        private static RandomizationData s_randData = new()
        {
            Categories = new() { "All", "Objective Items", "Terminal Commands", "Timed Sequence Main Terminals" },
        };
        public override RandomizationData RandData => s_randData;
    }

    /// <summary>
    /// Verify terminal(s) used for sequence, where Verify is entered
    /// </summary>
    private class VerifyTerminalItem : Item
    {
        public VerifyTerminalItem(string name, Objective.Data data)
            : base(name)
        {
            ObjectiveData = data;
        }

        [JsonIgnore]
        public Objective.Data ObjectiveData { get; set; }

        private static RandomizationData s_randData = new()
        {
            Categories = new() { "All", "Objective Items", "Terminal Commands", "Timed Sequence Verify Terminals" },
        };
        public override RandomizationData RandData => s_randData;
    }

    private const eWardenObjectiveType ThisObjectiveType
        = eWardenObjectiveType.TimedTerminalSequence;

    private static string ThisObjectiveSummary(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"Perform {data.Objective.TimedTerminalSequence_NumberOfRounds}-Round Timed Sequence";
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

    private static string ThisMainTerminalItemName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Main Terminal";
    }

    private static string ThisMainTerminalLocationName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Main Terminal (Location)";
    }

    private static string ThisVerifyTerminalItemName(Objective.Data data)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Verify Terminal";
    }

    private static string ThisVerifyTerminalLocationName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Verify Terminal #{count} (Location)";
    }

    private static string ThisStartRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Started Round #{count}";
    }

    private static string ThisFailRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Failed Round #{count}";
    }

    private static string ThisCompleteRegionName(Objective.Data data, int count)
    {
        CheckThisIsCorrectObjective(data);
        return $"{ThisObjectiveName(data)} Completed Round #{count}";
    }

    // Objective requiring the completion of one or more timed terminal sequences
    [Objective.Callback]
    public static void HandleTimedSequenceObjective(Objective.Data data)
    {
        if (!ThisIsCorrectObjective(data))
            return;

        if (data.Objective.TimedTerminalSequence_NumberOfRounds < 1)
        {
            FeatureLogger.Error($"{ThisObjectiveName(data)}: Cannot have fewer than 1 TimedSequence round!");
            return;
        }

        // Technically, you could get lucky. However, I'm going to state you need access to all terminals to do more than just start the sequence
        // Also, to be safe, we're manually making region sets (to avoid the unpacking process)
        var regionSets = data.PlacementsToTerminalRegions(data.ObjectiveData.ZonePlacementDatas);

        // Place main terminal in world
        Item mainTerminalItem = data.GetItem(new MainTerminalItem(ThisMainTerminalItemName(data), data));
        Location mainTerminalLocation = data.GetLocation(new TimedSequenceLocation(
            ThisMainTerminalItemName(data),
            regionSets.First().Select(info => info.Region).ToList(),
            mainTerminalItem
        ));

        // Place verificaiton terminals in world
        Item verifyTerminalItem = data.GetItem(new VerifyTerminalItem(ThisVerifyTerminalItemName(data), data));
        int count = 0;
        foreach (var regionSet in regionSets.Skip(1))
        {
            ++count;
            Location verifyTerminalLocation = data.GetLocation(new TimedSequenceLocation(
                ThisVerifyTerminalLocationName(data, count),
                regionSet.Select(i => i.Region).ToList(),
                verifyTerminalItem
            ));
        }

        // Note: The first iteration (i = 1) has been unrolled so that we can add checks to the paths
        Path path;
        string startName, failName, succeedName;
        int startRegion, failRegion, succeedRegion;
        int i = 1;

        startName = ThisStartRegionName(data, i);
        startRegion = data.GetOrCreateRegion(startName);
        path = data.AddPath(data.ObjectiveStartRegion, startRegion);
        path.RequiredItem = mainTerminalItem.Name;
        path.RequiredItemCount = 1u;
        if (data.Objective.TimedTerminalSequence_EventsOnSequenceStart.Count >= i)
            data.ProcessEvents(startRegion, startName, data.Objective.TimedTerminalSequence_EventsOnSequenceStart[i - 1]);

        failName = ThisFailRegionName(data, i);
        failRegion = data.GetOrCreateRegion(failName);
        data.AddPath(startRegion, failRegion);
        if (data.Objective.TimedTerminalSequence_EventsOnSequenceFail.Count >= i)
            data.ProcessEvents(failRegion, failName, data.Objective.TimedTerminalSequence_EventsOnSequenceFail[i - 1]);

        succeedName = ThisCompleteRegionName(data, i);
        succeedRegion = data.GetOrCreateRegion(succeedName);
        path = data.AddPath(startRegion, succeedRegion);
        path.RequiredItem = verifyTerminalItem.Name;
        path.RequiredItemCount = (uint)(count);
        if (data.Objective.TimedTerminalSequence_EventsOnSequenceDone.Count >= i)
            data.ProcessEvents(succeedRegion, succeedName, data.Objective.TimedTerminalSequence_EventsOnSequenceDone[i - 1]);

        for (i = 2; i <= data.Objective.TimedTerminalSequence_NumberOfRounds; i++)
        {
            startName = ThisStartRegionName(data, i);
            startRegion = data.GetOrCreateRegion(startName);
            path = data.AddPath(data.ObjectiveStartRegion, startRegion);
            if (data.Objective.TimedTerminalSequence_EventsOnSequenceStart.Count >= i)
                data.ProcessEvents(startRegion, startName, data.Objective.TimedTerminalSequence_EventsOnSequenceStart[i - 1]);

            failName = ThisFailRegionName(data, i);
            failRegion = data.GetOrCreateRegion(failName);
            data.AddPath(startRegion, failRegion);
            if (data.Objective.TimedTerminalSequence_EventsOnSequenceFail.Count >= i)
                data.ProcessEvents(failRegion, failName, data.Objective.TimedTerminalSequence_EventsOnSequenceFail[i - 1]);

            succeedName = ThisCompleteRegionName(data, i);
            succeedRegion = data.GetOrCreateRegion(succeedName);
            path = data.AddPath(startRegion, succeedRegion);
            if (data.Objective.TimedTerminalSequence_EventsOnSequenceDone.Count >= i)
                data.ProcessEvents(succeedRegion, succeedName, data.Objective.TimedTerminalSequence_EventsOnSequenceDone[i - 1]);
        }

        // OnActivateOnSolveItem triggers when the full sequence is compelte
        if (data.Objective.OnActivateOnSolveItem && data.Objective.EventsOnActivate.Any())
            data.ProcessEvents(succeedRegion, succeedName, data.Objective.EventsOnActivate);

        // Place CompleteObjective item in the final succeed region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, ThisObjectiveSummary(data), succeedRegion);
    }

}
