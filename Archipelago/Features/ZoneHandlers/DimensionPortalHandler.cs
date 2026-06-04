using GameData;
using LevelGeneration;
using Player;
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
        public TagResolver Tag_DimensionPortalScanLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Dimension Portal Scan Locations", "Locations checked by powering up a dimension portal (ie R6B1)", gd.Tag_AllLocations));

        public TagResolver Tag_DimensionPortalScanItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Dimension Portal Scans", "The scan in dimension portal rooms (ie R6B1) which trigger a dimension warp", gd.Tag_ScanItems));

        public TagResolver Tag_DimensionPortalWarpLocations
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Dimension Portal Warp Locations", "Locations checked by triggering a dimension warp using a dimension portal (ie R6B1)", gd.Tag_AllLocations));

        public TagResolver Tag_DimensionPortalWarpItems
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Dimension Portal Warps", "A warp normally triggerd by a dimension portal room (ie R6B1)", gd.Tag_WarpItems));
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
    /// Regions associated with this feature
    /// </summary>
    public static class ThisRegions
    {
        /// <summary>
        /// Region reached after the portal key is inserted
        /// </summary>
        public static string KeyInsertedRegion(Zone.Data data) 
            => $"{data.ZoneName} Dimension Portal Key Inserted";
        
        /// <summary>
        /// Region reached after the portal scan is completed
        /// </summary>
        public static string ScanCompletedRegion(Zone.Data data)
            => $"{data.ZoneName} Dimension Portal Scan Completed";
    }

    /// <summary>
    /// Location containing the scan for the dimension portal for a particular zone
    /// </summary>
    public static class DimensionPortal_ScanLocation
    {
        public static TagResolver MakeTag(Zone.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ZoneName} Dimension Portal Scan Location", "A location checked by startinga a dimension portal scan", gd.Tag_DimensionPortalScanLocations));

        public static LocationData MakeRandData() => new LocationData();
    }

    /// <summary>
    /// Item which represents / triggers the dimension portal scan
    /// </summary>
    public class DimensionPortal_ScanItem : Item
    {
        public DimensionPortal_ScanItem(Zone.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ZoneData = data;
        }

        public static TagResolver MakeTag(Zone.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ZoneName} Dimension Portal Scan", "A scan used by a dimension portal", gd.Tag_DimensionPortalScanItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Zone.Data ZoneData;

        public override Expedition.Data? RequiredExpedition => ZoneData;

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (ZoneData.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ZoneData.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            LG_Zone? zone = ZoneData.GetLG_Zone();
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
                    stateTracker.AddItemToTerminal(this);
                }
            };
        }
    }

    /// <summary>
    /// Location containing a warp for a dimension portal
    /// </summary>
    public static class DimensionPortal_WarpLocation
    {
        public static TagResolver MakeTag(Zone.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ZoneName} Dimension Portal Warp Location", "A location checked by triggering a particular dimension portal's warp", gd.Tag_DimensionPortalWarpLocations));

        public static LocationData MakeRandData() => new LocationData();
    }

    /// <summary>
    /// Item representing a warp by a DimensionPortal to a particular zone
    /// </summary>
    public class DimensionPortal_WarpItem : Item
    {
        public DimensionPortal_WarpItem(Zone.Data data)
            : base(MakeTag(data), MakeRandData())
        {
            ZoneData = data;
        }

        public static TagResolver MakeTag(Zone.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ZoneName} Dimension Portal Warp", "A warp triggerd by a particular dimension portal in the zone", gd.Tag_DimensionPortalWarpItems));

        public static ItemData MakeRandData() => new ItemData() { IsProgression = true };

        public Zone.Data ZoneData;

        public override Expedition.Data? RequiredExpedition => ZoneData;

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player)
        {
            if (ZoneData.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ZoneData.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            LG_Zone? zone = ZoneData.GetLG_Zone();
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
                }
                else
                {
                    terminal.AddLine($"<#F00>Failed to find dimension portal! Item returned to terminal.</color>");
                    stateTracker.AddItemToTerminal(this);
                }
            };
        }
    }

    /// <summary>
    /// Get a dimension portal scan item
    /// </summary>
    /// <param name="data">The zone with the dimension portal which contains the scan</param>
    public static KeyedItem GetDimensionPortalScanItem(Zone.Data data)
    {
        if (data.TryLookupItem(DimensionPortal_ScanItem.MakeTag(data), out var item))
            return item;

        Item newItem = new DimensionPortal_ScanItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    /// <summary>
    /// Get a warp item for a particular zone
    /// </summary>
    /// <param name="data">The zone of the dimension portal which triggers the warp</param>
    public static KeyedItem GetDimensionPortalWarpItem(Zone.Data data)
    {
        if (data.TryLookupItem(DimensionPortal_WarpItem.MakeTag(data), out var item))
            return item;

        Item newItem = new DimensionPortal_WarpItem(data);
        return new(data.AddItem(newItem), newItem);
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
        RegionID keyInsertedRegion = data.LookupOrCreateRegion(ThisRegions.KeyInsertedRegion(data));
        data.AddPath(new Path()
        {
            StartingRegion = data.LookupOrCreateRegion(data.ZoneName),
            EndingRegion = keyInsertedRegion,
            ReqItem = BigPickupHandler.GetBigPickupItem(data, BigPickupHandler.MatterWaveProjectorID).Item.PathReqs,
            ReqCount = 1u
        });


        KeyedItem scanItem = GetDimensionPortalScanItem(data);
        data.AddLocation(
            DimensionPortal_ScanLocation.MakeTag(data),
            keyInsertedRegion,
            DimensionPortal_ScanLocation.MakeRandData(),
            scanItem.ID
        );

        // Completing the scan and warping
        RegionID scanCompletedRegion = data.LookupOrCreateRegion(ThisRegions.ScanCompletedRegion(data));
        data.AddPath(new Path()
        {
            StartingRegion = keyInsertedRegion,
            EndingRegion = scanCompletedRegion,
            ReqItem = scanItem.Item.PathReqs,
            ReqCount = 1u,
        });

        KeyedItem warpItem = GetDimensionPortalWarpItem(data);
        data.AddLocation(
            DimensionPortal_WarpLocation.MakeTag(data),
            scanCompletedRegion,
            DimensionPortal_WarpLocation.MakeRandData(),
            warpItem.ID
        );

        data.AddPath(new Path()
        {
            StartingRegion = scanCompletedRegion,
            EndingRegion = data.LookupOrCreateRegion(targetZone.ZoneName),
            ReqItem = warpItem.Item.PathReqs,
            ReqCount = 1u,
        });
    }

    [ArchivePatch(typeof(LG_DimensionPortal), nameof(LG_DimensionPortal.PortalKeyInsertSequenceDone))]
    public static class LG_DimensionPortal__PortalKeyInsertSequenceDone__Patch
    {
        public static bool Prefix(LG_DimensionPortal __instance)
        {
            Zone.Data data = Zone.Data.FromZone(__instance.SpawnNode.m_zone);
            if (data.TryLookupLocation(DimensionPortal_ScanLocation.MakeTag(data), out var loc))
            {
                if (StateTracker.Get().NotifyFoundLocation(loc.ID, null).RandData.IsTreatedAsRandom)
                    return false;
            }
            else
            {
                FeatureLogger.Error("Failed to check dimension portal scan location!");
            }
            return true;
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
            Zone.Data data = Zone.Data.FromZone(__instance.SpawnNode.m_zone);
            if (data.TryLookupLocation(DimensionPortal_WarpLocation.MakeTag(data), out var loc))
            {
                if (loc.Location.RandData.IsTreatedAsRandom)
                {
                    var notifyFoundLocation = () => { StateTracker.Get().NotifyFoundLocation(loc.ID, null); };
                    __instance.m_portalChainPuzzleInstance.OnPuzzleSolved = new Il2CppAction(notifyFoundLocation);
                }
            }
            else
                FeatureLogger.Error("Failed to create association for Dimension Portal Warp Location!");
        }
    }
}
