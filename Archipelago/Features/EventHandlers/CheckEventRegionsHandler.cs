using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.EventHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
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
    public static void InsertCheckRegionEvent(Event.Data data)
    {
        data.Insert(0, EventHelper.CreateCheckRegionEvent(data.EventRegion));
    }

}
