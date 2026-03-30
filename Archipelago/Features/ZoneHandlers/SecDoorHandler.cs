
using GameData;
using Player;
using ReTFO.Archipelago.Features.Pickups;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.ZoneHandlers;

using ReTFO.Archipelago.Features.EventHandlers;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

[EnableFeatureByDefault]
public class SecDoorHandler : ArchipelagoFeature
{
    public override string Name => "Sec Door Handler";
    public override string Description
        => "Adds sec doors as parths and applies locks to them";
    public override FeatureGroup Group => FeatureGroups.ZoneHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Add entrances between zones on the same layer
    [Zone.Callback]
    public static void AddZoneEntrances(Zone.Data data)
    {
        // Check if this zone generates a normal doorway
        if (data.Layout == null || data.Zone == null) return; // Dimension with only one zone
        if (data.Zone.Pointer == data.Layout.Zones[0].Pointer) return; // First zone in layer - handled by AddLayerEntrances

        // Create path
        Zone.Data entryZone;
        if (data.Zone.BuildFromLocalIndex == data.Zone.LocalIndex)
            entryZone = data.FirstZone; // Yes, this happens. Presumably an oversight in R8C1's secondary layout data
        else
            entryZone = data.FindZoneByIndex(data.Zone.BuildFromLocalIndex)!;
        int entryRegion = data.GetOrCreateRegion(entryZone.ZoneName);
        Path path = data.AddPath(
            entryRegion,
            data.GetOrCreateRegion(data.ZoneName)
        );

        // Handle locked doors
        path.Name = $"{data.ZoneName} Main Entry";
        LayerData? layerData = data.LayerDatas;
        if (layerData?.ZonesWithBulkheadEntrance.Contains(data.Zone.LocalIndex) ?? false)
        {   // This zone is locked by a bulkhead door
            path.RequiredItem = BulkheadKeyHandler.GetBulkheadKeyItem(data).Name;
            path.RequiredItemCount = 1;
        }
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Keycard_SecurityBox)
        {   // Typical colored key
            path.RequiredItem = ColoredKeyHandler.GetColoredKeyItem(data).Name;
            path.RequiredItemCount = 1;
        }
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.PowerGenerator_And_PowerCell)
        {   // Must power a specific generator with a cell
            path.RequiredItem = BigPickupHelper.GetBigPickupItem(data, BigPickupHelper.CellItemID).Name;
        }
        else if (data.Zone.ProgressionPuzzleToEnter.PuzzleType == eProgressionPuzzleType.Locked_No_Key)
        {   // Can only be unlocked by an event force unlocking it
            path.RequiredItem = data.NotAnItem;
            path.RequiredItemCount = 1u;
        }
        path.AlternateItem = UnlockEventHandler.GetUnlockEventItem(data).Name;

        // Finally, handle OnApproach events, which will live in the entry zone
        if (data.Zone.EventsOnApproachDoor.Any())
        {
            string eventName = $"{data.ZoneName} OnApproach";
            int eventRegion = data.GetOrCreateRegion(eventName);
            data.AddPath(entryRegion, eventRegion);
            data.ProcessEvents(eventRegion, eventName, data.Zone.EventsOnApproachDoor);
        }
    }

    // Add entrances to the first zones in secondary and overload
    [Layer.Callback]
    public static void AddLayerEntrances(Layer.Data data)
    {
        if (data.BuildFromData == null) return; // As a side effect, this limits processing to secondary and overload layers

        Zone.Data targetZone = data.FirstZone;
        Layer.Data sourceLayer = data.GetLayer(data.BuildFromData.LayerType);
        Zone.Data? entryZone = sourceLayer.FindZoneByIndex(data.BuildFromData.Zone);

        int entryRegion = data.GetOrCreateRegion(entryZone.ZoneName);
        Path path = data.AddPath(
            entryRegion,
            data.GetOrCreateRegion(targetZone.ZoneName)
        );

        path.Name = $"{data.LayerName} Layer Entry";
        if (sourceLayer.LayerDatas!.BulkheadDoorControllerPlacements.FirstOrDefault(p => p.ZoneIndex == data.BuildFromData.Zone) != null)
        {   // If there is a bulkhead DC in the zone this layer connects to, we can unlock this zone with a key
            path.RequiredItem = BulkheadKeyHandler.GetBulkheadKeyItem(data).Name;
            path.RequiredItemCount = 1;
        }
        else
        {   // Can only unlock via an event
            path.RequiredItem = data.NotAnItem;
            path.RequiredItemCount = 0xFF;
        }
        path.AlternateItem = UnlockEventHandler.GetUnlockEventItem(targetZone).Name;

        // Finally, handle OnApproach events, which will live in the entry zone
        if (targetZone.Zone!.EventsOnApproachDoor.Any())
        {
            string eventName = $"{targetZone.ZoneName} OnApproach";
            int eventRegion = data.GetOrCreateRegion(eventName);
            data.AddPath(entryRegion, eventRegion);
            data.ProcessEvents(eventRegion, eventName, targetZone.Zone.EventsOnApproachDoor);
        }
    }

    // Identifies and reports when players enter a new zone
    [ArchivePatch(typeof(PlayerAgent), nameof(PlayerAgent.SetCourseNode))]
    public static class PlayerAgent__SetCourseNode__Patch
    {
        public static void Postfix(PlayerAgent __instance)
        {
            Plugin plugin = Plugin.Get();
            Zone.Data zoneData = Zone.Data.FromZone(__instance.CourseNode.m_zone);
            plugin.StateTracker.NotifyFoundRegion(zoneData.ZoneName, __instance);
        }
    }
}
