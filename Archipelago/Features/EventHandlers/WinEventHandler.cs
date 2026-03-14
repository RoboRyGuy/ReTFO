using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class WinEventHandler : ArchipelagoFeature
{
    public override string Name => "Win Event Handler";
    public override string Description
        => "Handles the Win event type.\n"
        + "Win events immediately complete the main sector and end the expedition, skipping extract.\n"
        + "The WinOnDeath event is treated as a win event, with the assumption you will die very soon.";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Get the "Instant Win" item, signaling an objective for this layer has been completed
    public static Item GetInstantWinItem(Expedition.Data data, bool onDeath = false)
        => data.GetItem(new InstantWinItem(data, onDeath));

    /// <summary>
    /// Item represnting the instant win (and win on death) events
    /// Note that because these are treated the same, only one can exist per expedition. 
    /// If an expedition has both types, this might sometimes trigger the wrong one when randomized
    /// </summary>
    private class InstantWinItem : Item
    {
        public InstantWinItem(Expedition.Data data, bool onDeath)
            : base($"{data.ExpeditionName} Instant Win", eRandomizationType.None, new List<string>() { "Objective Completions" })
        {
            ExpeditionData = data;
            OnDeath = onDeath;
        }

        [JsonIgnore]
        public Expedition.Data ExpeditionData { get; set; }

        [JsonIgnore]
        public bool OnDeath { get; set; }
    }

    // Add win event items and set up win event locations
    [Event.Callback]
    public static void ProcessWinEvents(Event.Data data)
    {
        int count = 0;
        foreach (var e in data)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ForceInstantWin && e.Type != eWardenObjectiveEventType.WinOnDeath)
                continue;
            ++count;

            Location loc = data.AddLocation(
                $"{data.EventName} - Instant Win Event {count}",
                data.EventRegion,
                eRandomizationType.None,
                true,
                GetInstantWinItem(data, e.Type == eWardenObjectiveEventType.WinOnDeath)
            );
        }
    }

}
