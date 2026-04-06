using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using SNetwork;
using System.Runtime.CompilerServices;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using UnityEngine;

namespace ReTFO.Archipelago.Features.Pickups;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// Utility for associating pickups with locations so that those locations get checked when the pickup is grabbed.
/// </summary>
[InjectToIl2Cpp, EnableFeatureByDefault]
public class PickupHelper : ArchipelagoFeature
{
    public override string Name => "Pickups Helper";
    public override string Description 
        => "Provides utilites used by other features to manage pickups (including big pickups)";
    public override FeatureGroup Group => FeatureGroups.EventHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// Component placed on pickups to mark them as being associated with a location.
    /// </summary>
    [InjectToIl2Cpp]
    private class ContainsLocationPickupComp : MonoBehaviour
    {
        /// <summary>
        /// ID of the location being stored
        /// </summary>
        public long StoredLocation = 0;
    }

    /// <summary>
    /// Associate a location with a pickup item
    /// </summary>
    /// <param name="item">The item to associate with the location</param>
    /// <param name="locationId">The location to associate</param>
    /// <param name="despawnIfFound">If true, will despawn the pickup if the location has already been checked</param>
    /// <remarks>
    /// This method works by placing a component on the item which can be checked at pickup time.
    /// If the item does not have a pickup sync comp, then this will never be checked.
    /// </remarks>
    public static void AssociateItem(ItemInLevel item, long locationId, bool despawnIfFound=true)
    {
        ContainsLocationPickupComp? comp = item.GetComponent<ContainsLocationPickupComp>();
        if (comp == null)
            comp = item.gameObject.AddComponent<ContainsLocationPickupComp>();
        else if (comp.StoredLocation != 0)
        {
            Game.Data gameData = Plugin.Get().MidManager.GetProcessedGameData();
            int locLength = Math.Max(comp.StoredLocation.ToString().Length, locationId.ToString().Length);
            string formatString = new string('0', locLength);
            FeatureLogger.Error(
                $"Overwriting location on pickup!\n"
                + $"  Old Location: [{comp.StoredLocation.ToString(formatString)}] {gameData.LookupLocation(comp.StoredLocation)}"
                + $"  New Location: [{         locationId.ToString(formatString)}] {gameData.LookupLocation(locationId)}"
            );
        }

        comp.StoredLocation = locationId;

        StateTracker stateTracker = StateTracker.Get();
        Location loc = stateTracker.MidManager.GetProcessedGameData().LookupLocation(locationId);
        var randomization = stateTracker.TestRandomization(loc);
        if (despawnIfFound && stateTracker.HasLocation(locationId) && randomization.IsTreatedAsRandom)
        {
            // Try to despawn the item
            item.internalSync.Cast<LG_PickupItem_Sync>().GetReplicator().Cast<SNet_Replicator>().Despawn(); 
        }
        else if (randomization.IsRandomized)
        {
            Interact_Pickup_PickupItem? pickup = item.PickupInteraction.TryCast<Interact_Pickup_PickupItem>();
            if (pickup != null)
            {
                // Note: The location could be be re-scouted mid game. Can't optimize out backup because of that
                string backupName = stateTracker.MidManager.GetProcessedGameData().LookupItem(loc.ItemID).Name;
                pickup.SetName(new Il2CppFunc_string(() => loc.ScoutedItem?.ItemDisplayName ?? backupName));
            }
        }
    }

    /// <summary>
    /// Before picking up an item, check if it's associated with any locations.
    /// If it is, block the pickup and notify that those locations have been found.
    /// </summary>
    [ArchivePatch(typeof(LG_PickupItem_Sync), nameof(LG_PickupItem_Sync.AttemptPickupInteraction))]
    public static class LG_PickupItem_Sync__AttemptPickupInteraction__Patch
    {
        // Just doing it this way for fun - a lambda would probably be more organized
        private class Closure
        {
            public Closure(long loc, LG_PickupItem_Sync item) { m_loc = loc; m_item = item; }
            long m_loc;
            LG_PickupItem_Sync m_item;
            public void Invoke()
            {
                StateTracker stateTracker = StateTracker.Get();
                if (stateTracker.TestRandomization(m_loc).IsTreatedAsRandom)
                {
                    m_item.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, null);
                    FeatureLogger.Debug("Despawned item");
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Prefix(LG_PickupItem_Sync __instance, ePickupItemInteractionType interaction, ref SNet_Player player, bool droppedOnFloor = false, bool forceUpdate = false)
        {
            if (interaction != ePickupItemInteractionType.Pickup)
                return; // We only care about when it's being picked up

            ContainsLocationPickupComp? comp = __instance.GetComponent<ContainsLocationPickupComp>();
            if (comp == null)
                return; // Not associated with a location

            StateTracker stateTracker = StateTracker.Get();
            PlayerAgent agent = player.PlayerAgent.Cast<PlayerAgent>();
            var randomization = stateTracker.NotifyFoundLocation(comp.StoredLocation, agent, new Closure(comp.StoredLocation, __instance).Invoke);
            if (randomization.IsTreatedAsRandom)
            {
                player = null!; // Prevent the pickup (and send it to the void)
                __instance.GetReplicator().Cast<SNet_Replicator>().Despawn();
            }

            // Big pickups can re-enter the level. If our comp is still there, it would
            //  be rechecked on next pickup, potentially causing issues
            UnityEngine.Object.Destroy(comp);
        }
    }

}
