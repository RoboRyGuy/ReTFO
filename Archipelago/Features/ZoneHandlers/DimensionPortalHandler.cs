using GameData;
using LevelGeneration;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ZoneHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class DimensionPortalHandler : ArchipelagoFeature
{
    public override string Name => "Dimension Portal Handler";
    public override string Description
        => "Handles dimension warps triggered by portals (which require MWPs)"
        + "\nFor example, this handles the dimension warp in R6B1";
    public override FeatureGroup Group => FeatureGroups.ZoneHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Warps between dimensions triggered by the portal room
    [Zone.Callback]
    public static void AddZoneWarps(Zone.Data data)
    {
        // TODO: We don't have a good way to identify geos, so we're using the naming convention
        if (!(data.CustomGeo?.Contains("_portal_", StringComparison.OrdinalIgnoreCase) ?? false)) return;

        // All vanilla portals target this zone
        Zone.Data targetZone = data.GetLayer(LayerType.Dimension_1).FirstZone;

        // If the relevant geomorph is loaded in, we can get the correct zone directly from it
        // TODO: Utility to load in singular geomorphs on demand
        ComplexResourceSetDataBlock? complex = ComplexResourceSetDataBlock.GetBlock(data.Expedition.Expedition.ComplexResourceData);
        UnityEngine.GameObject? go = complex?.GetCustomGeomorph(data.CustomGeo);
        LG_DimensionPortal? portal = go?.GetComponentInChildren<LG_DimensionPortal>();
        if (portal != null)
            targetZone = data.FindZoneExact(portal.m_targetDimension, portal.m_targetZone) 
                ?? throw new NullReferenceException($"Failed to find portal target despite finding portal asset");
        else
            FeatureLogger.Warning($"{data.ZoneName}: Using default portal location for presumed DimensionPortal");

        Path path = data.AddPath(
            data.GetOrCreateRegion(data.ZoneName),
            data.GetOrCreateRegion(targetZone.ZoneName)
        );

        path.RequiredItem = BigPickupHelper.GetBigPickupItem(data, BigPickupHelper.MatterWaveProjectorID).Name;
        path.RequiredItemCount = 1u;
    }
}
