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
        public LocationID Location_TimedSequenceMains
            => LocationID.From(data, "Timed Sequence Main Terminal Locations", data => new("Locations containing the main terminal for a timed sequence", data.Location_Never));

        public ItemID Item_TimedSequenceMains
            => ItemID.From(data, "Timed Sequence Main Terminals", data => new("The main (central) terminals for timed sequences", data.Item_Never));

        public LocationID Location_TimedSequenceVerifies
            => LocationID.From(data, "Timed Sequence Verify Terminal Locations", data => new("Locations containing a verify terminal for all timed sequences", data.Location_Never));

        public ItemID Item_TimedSequenceVerifies
            => ItemID.From(data, "Timed Sequence Verify Terminals", data => new("The verification terminals for timed sequences", data.Item_Never));
    }

    public static Objective.Data Checked(Objective.Data data)
    {
        const eWardenObjectiveType CHECK_TYPE = eWardenObjectiveType.TimedTerminalSequence;
        if (data.Objective.Type != CHECK_TYPE)
            FeatureLogger.Warning($"Fetched an ID for the wrong objective type. Desired: {Enum.GetName(CHECK_TYPE)}, actual: {Enum.GetName(data.Objective.Type)}");
        return data;
    }

    extension(Objective.Data data)
    {
        public RegionID Region_TimedRoundStarted(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Start Round {count}", data => new("Region entered by starting a particular timed sequence verification round", data.Region_Objective));

        public RegionID Region_TimedRoundFailed(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Fail Round {count}", data => new("Region entered by failing a particular timed sequence verification round", data.Region_Objective));

        public RegionID Region_TimedRoundCompleted(int count)
            => RegionID.From(Checked(data), $"{data.ObjectiveName} Complete Round {count}", data => new("Region entered by successfully completing a particular timed sequence verification round", data.Region_Objective));


        public LocationID Location_TimedSequenceVerifies_PerObjective
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Timed Sequence Verify Terminal Locations", data => new("Locations containing a verify terminal for a particular timed sequence", data.Location_TimedSequenceVerifies));

        public ItemID Item_TimedSequenceVerifies_PerObjective
            => ItemID.From(Checked(data), $"{data.ObjectiveName} Timed Sequence Verify Terminals", data => new("Timed Sequence verify terminal items for a particular objective", data.Item_TimedSequenceVerifies));


        public LocationID Location_TimedSequenceMain_Instance
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Timed Sequence Main Terminal Location", data => new("Locations containing a verify terminal for a particular timed sequence", data.Location_TimedSequenceVerifies));

        public ItemID Item_TimedSequenceMain_Instance
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Timed Sequence Main Terminal", 
                data => new("Timed Sequence verify terminal items for a particular objective", data.Item_TimedSequenceVerifies),
                new TimedSequenceHandler.TimedSequence_MainItem(data.Region_Objective)
            );

        public LocationID Location_TimedSequenceVerify_Instance(int count)
            => LocationID.From(Checked(data), $"{data.ObjectiveName} Timed Sequence Verify Terminal Location #{count}", data => new("Locations containing a verify terminal for a particular timed sequence", data.Location_TimedSequenceVerifies));

        public ItemID Item_TimedSequenceVerify_Instance(int count)
            => ItemID.From(
                Checked(data), 
                $"{data.ObjectiveName} Timed Sequence Verify Terminal #{count}", 
                data => new("Timed Sequence verify terminal items for a particular objective", data.Item_TimedSequenceVerifies),
                new TimedSequenceHandler.TimedSequence_VerifyItem(data.Region_Objective, count)
            );
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

    public class TimedSequence_MainItem : Item
    {
        public TimedSequence_MainItem(RegionID objective)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
        }
        
        public RegionID ObjectiveRegion { get; private init; }
    }

    public class TimedSequence_VerifyItem : Item
    {
        public TimedSequence_VerifyItem(RegionID objective, int count)
            : base(new ItemData() { IsProgression = true })
        {
            ObjectiveRegion = objective;
            Count = count;
        }

        public RegionID ObjectiveRegion { get; private init; }

        public int Count { get; private init; }
    }

    // Objective requiring the completion of one or more timed terminal sequences
    [Objective.Callback]
    public void HandleTimedSequenceObjective(Objective.Data data)
    {
        if (data.Objective.Type != eWardenObjectiveType.TimedTerminalSequence)
            return;

        if (data.Objective.TimedTerminalSequence_NumberOfRounds < 1)
        {
            FeatureLogger.Error($"{data.ObjectiveName}: Cannot have fewer than 1 TimedSequence round!");
            return;
        }

        // Technically, you could get lucky. However, I'm going to state you need access to all terminals to do more than just start the sequence
        // Also, to be safe, we're manually making region sets (to avoid the unpacking process)
        var regionSets = data.PlacementsToTerminalRegions(data.ObjectiveData.ZonePlacementDatas);

        // Place main terminal in world
        ItemID mainTerminalItem = data.Item_TimedSequenceMain_Instance;
        data.Locations.CreateValue(
            data.Location_TimedSequenceMain_Instance,
            regionSets.First().Select(info => info.Region).ToList(),
            new LocationData() { IsAutoDiscovered = true },
            mainTerminalItem
        );

        // Place verificaiton terminals in world
        int count = 0;
        foreach (var regionSet in regionSets.Skip(1))
        {
            ++count;
            data.Locations.CreateValue(
                data.Location_TimedSequenceVerify_Instance(count),
                regionSet.Select(i => i.Region).ToList(),
                new LocationData() { IsAutoDiscovered = true },
                data.Item_TimedSequenceVerify_Instance(count)
            );
        }

        // Note: The first iteration (i = 1) has been unrolled so that we can add checks to the paths
        RegionID startRegion, failRegion, succeedRegion;
        int i = 1;
        startRegion = data.Region_TimedRoundStarted(i);
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Objective,
            EndingRegion = startRegion,
            ReqItem = new(Path.RequiredItem.eType.Item, mainTerminalItem),
            ReqCount = 1u,
        });
        if (data.Objective.TimedTerminalSequence_EventsOnSequenceStart.Count >= i)
            data.ProcessEvents(startRegion, data.Objective.TimedTerminalSequence_EventsOnSequenceStart[i - 1]);

        failRegion = data.Region_TimedRoundFailed(i);
        data.AddPath(new Path()
        {
            StartingRegion = startRegion,
            EndingRegion = failRegion,
        });
        if (data.Objective.TimedTerminalSequence_EventsOnSequenceFail.Count >= i)
            data.ProcessEvents(failRegion, data.Objective.TimedTerminalSequence_EventsOnSequenceFail[i - 1]);

        succeedRegion = data.Region_TimedRoundCompleted(i);
        data.AddPath(new Path()
        {
            StartingRegion = startRegion,
            EndingRegion = succeedRegion,
            ReqItem = new(Path.RequiredItem.eType.Category, data.Item_TimedSequenceVerifies),
            ReqCount = (uint)count,
        });
        if (data.Objective.TimedTerminalSequence_EventsOnSequenceDone.Count >= i)
            data.ProcessEvents(succeedRegion, data.Objective.TimedTerminalSequence_EventsOnSequenceDone[i - 1]);

        for (i = 2; i <= data.Objective.TimedTerminalSequence_NumberOfRounds; i++)
        {
            startRegion = data.Region_TimedRoundStarted(i);
            data.AddPath(new Path()
            {
                StartingRegion = succeedRegion,
                EndingRegion = startRegion,
            });
            if (data.Objective.TimedTerminalSequence_EventsOnSequenceStart.Count >= i)
                data.ProcessEvents(startRegion, data.Objective.TimedTerminalSequence_EventsOnSequenceStart[i - 1]);

            failRegion = data.Region_TimedRoundFailed(i);
            data.AddPath(new Path()
            {
                StartingRegion = startRegion,
                EndingRegion = failRegion,
            });
            if (data.Objective.TimedTerminalSequence_EventsOnSequenceFail.Count >= i)
                data.ProcessEvents(failRegion, data.Objective.TimedTerminalSequence_EventsOnSequenceFail[i - 1]);

            succeedRegion = data.Region_TimedRoundCompleted(i);
            data.AddPath(new Path()
            {
                StartingRegion = startRegion,
                EndingRegion = succeedRegion,
            });
            if (data.Objective.TimedTerminalSequence_EventsOnSequenceDone.Count >= i)
                data.ProcessEvents(succeedRegion, data.Objective.TimedTerminalSequence_EventsOnSequenceDone[i - 1]);
        }

        // OnActivateOnSolveItem triggers when the full sequence is compelte
        if (data.Objective.OnActivateOnSolveItem && data.Objective.EventsOnActivate.Any())
            data.ProcessEvents(succeedRegion, data.Objective.EventsOnActivate);

        // Place CompleteObjective item in the final succeed region
        SharedObjectiveHandler.AddObjectiveCompleteItem(data, succeedRegion);
    }

}
