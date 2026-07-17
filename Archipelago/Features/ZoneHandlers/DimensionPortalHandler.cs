using GameData;
using LevelGeneration;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ZoneHandlers;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class DimensionPortalHandler_Tags
{
    extension (Game.Data data)
    {
        public LocationID Location_DimensionPortalScans
            => LocationID.From(data, "Dimension Portal Scan Locations", data => new("Locations checked by powering up a dimension portal (ie R6B1)", data.Location_All));

        public ItemID Item_DimensionPortalScans
            => ItemID.From(data, "Dimension Portal Scans", data => new("The scan in dimension portal rooms (ie R6B1) which trigger a dimension warp", data.Item_Scans));

        public LocationID Location_DimensionPortalWarps
            => LocationID.From(data, "Dimension Portal Warp Locations", data => new("Locations checked by triggering a dimension warp using a dimension portal (ie R6B1)", data.Location_All));

        public ItemID Item_DimensionPortalWarps
            => ItemID.From(data, "Dimension Portal Warps", data => new("A warp normally triggerd by a dimension portal room (ie R6B1)", data.Item_Warps));
    }

    extension (Zone.Data data)
    {
        public RegionID Region_PortalKeyInserted
            => RegionID.From(data, $"{data.ZoneName} Dimension Portal Key Inserted", data => new("Region entered when the MWP is put into a particular dimension portal", data.Region_Zone));

        public RegionID Region_PortalScanCompleted
            => RegionID.From(data, $"{data.ZoneName} Dimension Portal Scan Completed", data => new("Region entered by completing the scan for a particular dimension portal", data.Region_Zone));


        public LocationID Location_DimensionPortalScan_Instance
            => LocationID.From(data, $"{data.ZoneName} Dimension Portal Scan Location", data => new("The location of a particular dimension portal's scan", data.Location_DimensionPortalScans));

        public ItemID Item_DimensionPortalScan_Instance
            => ItemID.From(
                data, 
                $"{data.ZoneName} Dimension Portal Scan", 
                data => new("A particular dimension portal's scan", data.Item_DimensionPortalScans),
                new DimensionPortalHandler.DimensionPortal_ScanItem(data.Region_Zone)
            );

        public LocationID Location_DimensionPortalWarp_Instance
            => LocationID.From(data, $"{data.ZoneName} Dimension Portal Warp Location", data => new("A particular dimension portal warp location", data.Location_DimensionPortalWarps));

        public ItemID Item_DimensionPortalWarp_Instance
            => ItemID.From(
                data, 
                $"{data.ZoneName} Dimension Portal Warp", 
                data => new("A warp triggered by a particular dimension portal", data.Item_DimensionPortalWarps),
                new DimensionPortalHandler.DimensionPortal_WarpItem(data.Region_Zone)
            );
    }

}

[EnableFeatureByDefault, AutomatedFeature]
public class DimensionPortalHandler : ArchipelagoFeature
{
    public override string Name => "Dimension Portal Handler";
    public override string Description
        => "Handles dimension warps triggered by portals (which require MWPs)"
        + "\nFor example, this handles the dimension warp in R6B1."
        + "\nDimension portals include the warp scan and the actual warp itself as separate items.";
    public override FeatureGroup Group => FeatureGroups.ZoneHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// Item which represents / triggers the dimension portal scan
    /// </summary>
    public class DimensionPortal_ScanItem : TerminalItem
    {
        public DimensionPortal_ScanItem(RegionID zone)
            : base(new ItemData() { IsProgression = true })
        {
            ZoneRegion = zone;
        }

        public RegionID ZoneRegion { get; private init; }

        public override RegionID TargetRegion => ZoneRegion;

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            Zone.Data data = new(stateTracker.GameData, ZoneRegion);
            LG_Zone? zone = data.GetLG_Zone();
            LG_DimensionPortalRoom? portalRoom = null;
            foreach (LG_Area area in zone?.m_areas.Iter() ?? Enumerable.Empty<LG_Area>())
            {
                portalRoom = area?.m_geomorph?.GetComponent<LG_DimensionPortalRoom>();
                if (portalRoom != null) break;
            }
            LG_DimensionPortal? portal = portalRoom?.m_core;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Initiating Dimension Portal Scan", 2f);
            };

            yield return () =>
            {
                if (portal != null)
                {
                    terminal.AddLine($"Scan commencing. Enjoy?");
                    portal.m_portalChainPuzzleInstance.AttemptInteract(ChainedPuzzles.eChainedPuzzleInteraction.Activate);
                    portal.OnPortalKeyInsertSequenceDone?.Invoke(portal);
                }
                else
                {
                    terminal.AddLine($"<#F00>Failed to find dimension portal! Item returned to terminal.</color>");
                    stateTracker.AddItemToTerminal(itemId);
                }
            };
        }
    }

    /// <summary>
    /// Item representing a warp by a DimensionPortal to a particular zone
    /// </summary>
    public class DimensionPortal_WarpItem : TerminalItem
    {
        public DimensionPortal_WarpItem(RegionID zone)
            : base(new ItemData() { IsProgression = true })
        {
            ZoneRegion = zone;
        }

        public RegionID ZoneRegion { get; private init; }

        public override RegionID TargetRegion => ZoneRegion;

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
        {
            Zone.Data data = new(stateTracker.GameData, ZoneRegion);
            LG_Zone? zone = data.GetLG_Zone();
            LG_DimensionPortalRoom? portalRoom = null;
            foreach (LG_Area area in zone?.m_areas.Iter() ?? Enumerable.Empty<LG_Area>())
            {
                portalRoom = area?.m_geomorph?.GetComponent<LG_DimensionPortalRoom>();
                if (portalRoom != null) break;
            }
            LG_DimensionPortal? portal = portalRoom?.m_core;

            yield return () =>
            {
                terminal.AddLine(TerminalLineType.SpinningWaitDone, $"Initiating Dimension Portal Warp", 2f);
            };

            yield return () =>
            {
                if (portal != null)
                {
                    terminal.AddLine($"Initiating Dimension Warp via Dimension Portal. Goodbye!");
                    portal._Setup_b__61_0(); // This is the lambda normally supplied to the chained puzzle instance
                    stateTracker.AddItemToTerminal(itemId);
                }
                else
                {
                    terminal.AddLine($"<#F00>Failed to find dimension portal! Item returned to terminal.</color>");
                    stateTracker.AddItemToTerminal(itemId);
                }
            };
        }
    }

    // Warps between dimensions triggered by the portal room
    [Zone.Callback]
    public void AddZoneWarps(Zone.Data data)
    {
        // TODO: We don't have a good way to identify portal geos, so we're using the naming convention
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

        // Putting in the key and starting the scan
        RegionID keyInsertedRegion = data.Region_PortalKeyInserted;
        data.AddPath(new Path()
        {
            StartingRegion = data.Region_Zone,
            EndingRegion = keyInsertedRegion,
            ReqItem = new(Path.PathReq.eType.ItemConsumed, data.Item_BigPickup_MWP),
            ReqCount = 1u
        });

        ItemID scanItem = data.Item_DimensionPortalScan_Instance;
        data.Locations.CreateValue(
            data.Location_DimensionPortalScan_Instance,
            keyInsertedRegion,
            new LocationData(),
            scanItem
        );

        // Completing the scan and warping
        RegionID scanCompletedRegion = data.Region_PortalScanCompleted;
        data.AddPath(new Path()
        {
            StartingRegion = keyInsertedRegion,
            EndingRegion = scanCompletedRegion,
            ReqItem = new(Path.PathReq.eType.Item, scanItem),
            ReqCount = 1u,
        });

        ItemID warpItem = data.Item_DimensionPortalWarp_Instance;
        data.Locations.CreateValue(
            data.Location_DimensionPortalWarp_Instance,
            scanCompletedRegion,
            new LocationData(),
            warpItem
        );

        data.AddPath(new Path()
        {
            StartingRegion = scanCompletedRegion,
            EndingRegion = targetZone.Region_Zone,
            ReqItem = new(Path.PathReq.eType.ItemConsumed, warpItem),
            ReqCount = 1u,
        });
    }

    [ArchivePatch(typeof(LG_DimensionPortal), nameof(LG_DimensionPortal.PortalKeyInsertSequenceDone))]
    public static class LG_DimensionPortal__PortalKeyInsertSequenceDone__Patch
    {
        public static bool Prefix(LG_DimensionPortal __instance)
        {
            Zone.Data data = Zone.Data.GetFromZone(__instance.SpawnNode.m_zone);
            LocationID id = data.Location_DimensionPortalScan_Instance;
            Location loc = StateTracker.Get().NotifyFoundLocation(id, null);
            return !loc.RandData.IsTreatedAsRandom;
        }
    }

    /// <summary>
    /// When randomizing, we replace the dimension portal's puzzle callback with a notify location call
    /// </summary>
    [ArchivePatch(typeof(LG_DimensionPortal), nameof(LG_DimensionPortal.Setup))]
    public static class LG_DimensionPortal__Setup__Patch
    {
        public static void Postfix(LG_DimensionPortal __instance)
        {
            Zone.Data data = Zone.Data.GetFromZone(__instance.SpawnNode.m_zone);
            LocationID id = data.Location_DimensionPortalWarp_Instance;
            Location loc = data.Locations.LookUpValueChecked(id);
            if (loc.RandData.IsTreatedAsRandom)
            {
                var notifyFoundLocation = () => { StateTracker.Get().NotifyFoundLocation(id, null); };
                __instance.m_portalChainPuzzleInstance.OnPuzzleSolved = new Il2CppAction(notifyFoundLocation);
            }
        }
    }
}
