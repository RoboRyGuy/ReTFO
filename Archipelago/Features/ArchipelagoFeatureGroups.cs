using TheArchive.Core.FeaturesAPI;

namespace ReTFO.Archipelago.Features;

public static class ArchipelagoFeatureGroups
{
    public const string ArchipelagoFeatureGroupName = "Archipelago";
    public const string VanillaHandlersGroupName    = "Vanilla Handlers";
    public const string ZoneHandlersGroupName       = "Zone Handlers";
    public const string TerminalHandlersGroupName   = "Terminal Handlers";
    public const string PickupHandlersGroupName     = "Pickup Handlers";
    public const string EventHandlersGroupName      = "Event Handlers";
    public const string ObjectiveHandlersGroupName  = "Objective Handlers";

    extension(FeatureGroups)
    {
        public static FeatureGroup Archipelago
            => FeatureGroups.GetOrCreateTopLevelGroup(ArchipelagoFeatureGroupName);

        public static FeatureGroup VanillaHandlers
            => FeatureGroups.Archipelago.GetOrCreateSubGroup(VanillaHandlersGroupName);

        public static FeatureGroup ZoneHandlers
            => FeatureGroups.VanillaHandlers.GetOrCreateSubGroup(ZoneHandlersGroupName);

        public static FeatureGroup TerminalHandlers
            => FeatureGroups.VanillaHandlers.GetOrCreateSubGroup(TerminalHandlersGroupName);

        public static FeatureGroup PickupHandlers
            => FeatureGroups.VanillaHandlers.GetOrCreateSubGroup(PickupHandlersGroupName);

        public static FeatureGroup EventHandlers
            => FeatureGroups.VanillaHandlers.GetOrCreateSubGroup(EventHandlersGroupName);

        public static FeatureGroup ObjectiveHandlers
            => FeatureGroups.VanillaHandlers.GetOrCreateSubGroup(ObjectiveHandlersGroupName);
    }
}
