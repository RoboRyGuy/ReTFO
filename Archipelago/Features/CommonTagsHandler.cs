using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features;

using ReTFO.Archipelago.Features.ZoneHandlers;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;


/// <summary>
/// Class implementing extension properties for shared randomization tags
/// </summary>
public static class RootRandomizationTags
{
    extension(Game.Data gameData)
    {
        /// <summary>
        /// Base of all tags; matcheas all entities. The only entities not derived from this do not support randomization.
        /// </summary>
        public TagResolver Tag_All
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("All", "Enables randomization of all items and locations", null));

        /// <summary>
        /// This tag is always placed in the whitelist, guaranteeing derived entites are randomized
        /// </summary>
        public TagResolver Tag_Always
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Always", "This tag is always in the whitelist", null));

        /// <summary>
        /// This tag is always placed in the blacklist, guaranteeing derived entities are not randomized
        /// </summary>
        public TagResolver Tag_Never
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Never", "This tag is always in the blacklist", null));

        /// <summary>
        /// Matches all locations. The only locations not derived from this do not support randomization.
        /// </summary>
        public TagResolver Tag_AllLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("All Locations", "Enables randomization of all locations", gd.Tag_All));

        /// <summary>
        /// Matcheas all items. The only items not derived from this do not support randomization.
        /// </summary>
        public TagResolver Tag_AllItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("All Items", "Enables randomization of all items", gd.Tag_All));

        /// <summary>
        /// Base tag for unlock items, which are floating items required to enter a group of regions.
        /// The best example of an unlock item is the expedition unlock item
        /// </summary>
        public TagResolver Tag_UnlockItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Unlock Items", "Used internally to identify items needed to access certain groups of regions.", null));

        /// <summary>
        /// Base tag for goal items. A player must collect all available goal items for AP to consider the slot won.
        /// </summary>
        public TagResolver Tag_GoalItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Goal Items", "Used internally to identify \"goal\" items. All available goal items must be collected for AP to consider the slot won.", gd.Tag_Never));

        /// <summary>
        /// Items which have no location associated with them. This is usually because the item cannot normally be acquired,
        ///  either because it does not exist or because the player usually starts with the item.
        /// </summary>
        public TagResolver Tag_FloatingItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Floating Items", "Items with no location, for example gear items or expedition unlocks.", gd.Tag_AllItems));

        /// <summary>
        /// Locations which are "empty". These locations do not contain an item by default, and can instead be provided a floating item
        ///  during multiworld setup. They are required for optional items to be collectable.
        /// </summary>
        public TagResolver Tag_EmptyLocations
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Empty Locations", "Locations which do not contain an item. Empty locations are used by floating items.", gd.Tag_AllLocations));

        /// <summary>
        /// Tag matching items which trigger scans
        /// </summary>
        public TagResolver Tag_ScanItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Scan Items", "Items which trigger scans", gd.Tag_AllItems));

        /// <summary>
        /// Tag matching all items which trigger any player to teleport
        /// </summary>
        public TagResolver Tag_WarpItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Warp Items", "All items which cause players to teleport", gd.Tag_AllItems));
    }
}

[InjectToIl2Cpp, EnableFeatureByDefault]
public class CommonTagsHandler : ArchipelagoFeature
{
    public override string Name => "Common Tags Handler";
    public override string Description => "Provides a set of common tags and implements options for those tags";
    public override FeatureGroup Group => FeatureGroups.VanillaHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    [Game.Callback]
    public void AddTagOptions(Game.Data data)
    {
        data.AddOption(new OptionWhiteOrBlacklist()
        {
            DisplayName = "Empty Location Randomization",
            Description =
                "Customize the randomization of all supported empty locations."
                + "\nOne empty location is added for each \"floating\" item that gets randomized to maintain"
                + " the item:location balance required by Archipelago."
                + "\nEnabling randomization of an empty location makes it available as a candidate for a floating item."
                + OptionWhiteOrBlacklist.DESC_SUFFIX,
            Category = Option.DEFAULT_OPTION_CATEGORY,
            Condition = new(),
            DefaultValue = 0,
            Tag = data.Tag_EmptyLocations,
        });

        data.AddOption(new OptionWhiteOrBlacklist()
        {
            DisplayName = "Warp Randomization",
            Description = "Customize the randomization of all supported warps." + OptionWhiteOrBlacklist.DESC_SUFFIX,
            Category = Option.DEFAULT_OPTION_CATEGORY,
            Condition = new(),
            DefaultValue = 2,
            Tag = data.Tag_WarpItems,
        });

        data.AddOption(new OptionWhiteOrBlacklist()
        {
            DisplayName = "Scan Randomization",
            Description = 
                "Customize the randomization of all supported scans." 
                + "\nCurrently, this includes event scans and certain objective scans, but not door scans."
                + OptionWhiteOrBlacklist.DESC_SUFFIX,
            Category = Option.DEFAULT_OPTION_CATEGORY,
            Condition = new(),
            DefaultValue = 2,
            Tag = data.Tag_ScanItems,
        });
    }
}
