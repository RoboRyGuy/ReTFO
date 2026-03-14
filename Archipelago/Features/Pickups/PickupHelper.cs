
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using SNetwork;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.Pickups;

// Utility for associating pickups with locations so that those locations get checked when the pickup is grabbed.
[InjectToIl2Cpp, EnableFeatureByDefault]
public class PickupHelper : ArchipelagoFeature
{
    public override string Name => "Pickups Helper";
    public override string Description => "Provides utilites used by other features to manage pickups (including big pickups)";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    // Component placed on pickups to mark them as being associated with a location
    [InjectToIl2Cpp]
    private class ContainsLocationPickupComp : MonoBehaviour
    {
        // Stored locations list. Typically, we only store 1 location
        public List<long> StoredLocations = new(1);
    }

    /// <summary>
    /// Associate a location with a pickup item
    /// </summary>
    /// <param name="item">The item to associate with the location</param>
    /// <param name="locationId">The location to associate</param>
    /// <remarks>
    /// This method works by placing a component on the item which can be checked at pickup time.
    /// If the item does not have a pickup sync comp, then this will never be checked.
    /// </remarks>
    public static void AssociateItem(ItemInLevel item, long locationId)
    {
        ContainsLocationPickupComp? comp = item.GetComponent<ContainsLocationPickupComp>();
        if (comp == null)
            comp = item.gameObject.AddComponent<ContainsLocationPickupComp>();
        comp.StoredLocations.Add(locationId);
    }

    /// <summary>
    /// Before picking up an item, check if it's associated with any locations
    /// If it is, block the pickup and notify that those locations have been found
    /// </summary>
    [ArchivePatch(typeof(LG_PickupItem_Sync), nameof(LG_PickupItem_Sync.AttemptPickupInteraction))]
    public static class LG_PickupItem_Sync__AttemptPickupInteraction__Patch
    {
        public static void Prefix(LG_PickupItem_Sync __instance, ePickupItemInteractionType interaction, ref SNet_Player player, bool droppedOnFloor = false, bool forceUpdate = false)
        {
            if (interaction != ePickupItemInteractionType.Pickup)
                return; // We only care about when it's being picked up

            ContainsLocationPickupComp? comp = __instance.GetComponent<ContainsLocationPickupComp>();
            if (comp != null)
            {
                Plugin plugin = Plugin.Get();
                if (plugin.StateTracker.NotifyFoundLocations(comp.StoredLocations))
                    player = null!; // A null player picks it up, effectively despawning it
            }
        }
    }

}
