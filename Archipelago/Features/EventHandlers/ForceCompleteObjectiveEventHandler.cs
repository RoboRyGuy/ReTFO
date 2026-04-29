using GameData;
using ReTFO.Archipelago.Features.ObjectiveHandlers;
using ReTFO.Archipelago.FeaturesAPI;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault, AutomatedFeature]
public class ForceCompleteObjectiveEventHandler : ArchipelagoFeature
{
    public override string Name => "Force Complete Objective Event Handler";
    public override string Description
        => "Handles the ForceCompleteObjective event type, which immediately completes an objective.";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    [Event.Callback]
    public void ProcessForceCompleteEvents(Event.Data data)
    {
        int count = 0;
        foreach (var e in data)
        {
            // Filter out unwanted events
            if (e.Type != eWardenObjectiveEventType.ForceCompleteObjective)
                continue;
            ++count;

            Layer.Data layer = data.GetLayer(e.Layer);
            KeyedItem item = SharedObjectiveHandler.GetCompleteObjectiveItem(layer.GetObjectiveDatas().First());
            EventHelper.ConvertToCheckLocationEvent(data, e, count, item.ID);
        }
    }

}
