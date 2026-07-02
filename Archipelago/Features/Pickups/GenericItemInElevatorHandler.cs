using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System.Linq;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Pickups;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class GenericItemInElevatorHandler_Tags
{
    extension (Game.Data gameData)
    {
        /// <summary>
        /// Parent of all generic-item-in-elevator locations
        /// </summary>
        public LocationID Location_GenericBigInElevator
            => LocationID.From(gameData, "Generic Big in Elevator Locations", data => new("Location checked by picking up a special type of item spawned in the elevator cage", data.Location_BigPickups));
    }

    extension (Expedition.Data data)
    {
        /// <summary>
        /// Generic big in elevator location for a particular expedition
        /// </summary>
        public LocationID Location_GenericBigInElevator_Instance
            => LocationID.From(
                data, 
                $"{data.ExpeditionName} Generic Big in Elevator Location", 
                data => new("The generic big in elevator location for a particular expedition", data.Location_GenericBigInElevator)
            );
    }
}

[EnableFeatureByDefault, AutomatedFeature]
public class GenericItemInElevatorHandler : ArchipelagoFeature
{
    public override string Name => "Generic Item in Elevator";
    public override string Description
        => "Handles items spawned via the Generic-Item-in-Elevator system.\n"
        + "This system allows prisoners to start with one non-objective-related Big Pickup in "
        + "the elevator, which is often used to perform a required part of the mission. It is "
        + "most commonly used to provide a Matter Wave Projecter at the start of an expedition.";
    public override FeatureGroup Group => FeatureGroups.PickupHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// Register the generic item in elevator during modded instance data processing
    /// </summary>
    [Expedition.Callback]
    public void AddGenericItemInElevator(Expedition.Data data)
    {
        var firstObjective = data.MainLayer.GetObjectiveDatas().First();
        if (firstObjective.Objective.GenericItemFromStart != 0)
        {
            data.Locations.CreateValue(
                data.Location_GenericBigInElevator_Instance,
                data.StartingZone.Region_Zone,
                new LocationData(),
                data.Item_BigPickup_Instance(firstObjective.Objective.GenericItemFromStart)
            );
        }
    }

    /// <summary>
    /// Normally we'd patch the relevant job, but that can causes null reference errors
    ///  for cargo cage items. Fortunately, we can grab them when it's done building
    /// </summary>
    [ArchivePatch(typeof(LG_Factory), nameof(LG_Factory.FactoryDone))]
    public static class LG_Factory__FactoryDone__Patch
    {
        public static void Postfix()
        {
            var data = Expedition.Data.GetFromCurrentExpedition()
                .MainLayer.GetObjectiveDatas().First();

            if (data.Objective.GenericItemFromStart != 0)
            {
                LocationID loc = data.Location_GenericBigInElevator_Instance;
                
                var comp = ElevatorCage.Current.m_cargoCage.m_itemsToMoveToCargo[0].GetComponentInChildren<CarryItemPickup_Core>();
                if (comp == null)
                {
                    FeatureLogger.Error("Failed to find generic item in elevator during build!");
                    return;
                }

                if (comp.ItemDataBlock.persistentID != data.Objective.GenericItemFromStart)
                    FeatureLogger.Warning("Associated incorrect item type with generic item from start!");
                PickupHelper.AssociateItem(comp, loc);
            }
        }
    }

}
