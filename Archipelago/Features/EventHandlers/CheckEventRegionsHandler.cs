using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault, AutomatedFeature]
public class CheckEventRegionsHandler : ArchipelagoFeature
{
    public override string Name => "Check Event Regions Handler";
    public override string Description
        => "Adds check region events to event data to automatically identify when event regions are entered";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    [Event.Callback]
    public void InsertCheckRegionEvent(Event.Data data)
    {
        WardenObjectiveEventData sourceData;
        if (data.Count > 0) sourceData = data[0];
        else sourceData = Il2CppSystem.Activator.CreateInstance(data.EventType).Cast<WardenObjectiveEventData>();
        data.Insert(0, EventHelper.CreateCheckRegionEvent(sourceData, data.Region_Event));
    }
}
