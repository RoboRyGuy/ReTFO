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
        /// Items which are "optional", as in they don't exist if they're not randomized in.
        /// This is somewhat equivalent to simply getting the items as part of the starting inventory,
        ///  but it is not handled that way internally.
        /// </summary>
        public TagResolver Tag_OptionalItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Optional Items", "Items which only exist if they are randomized.", gd.Tag_AllItems));

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

        OptionID warpToggle = data.AddOption(new OptionToggle()
        {
            DisplayName = "Randomize Warps",
            Description = 
                "Randomize all supported warps. This includes warps triggered by events and"
                + " warps triggered by the dimension portal, but not \"dimension flashes\".",
            Category = Option.DEFAULT_OPTION_CATEGORY,
            Condition = new(),
            DefaultValue = 0,
        });

        data.AddOption(new OptionWhiteOrBlacklist()
        {
            Toggle = warpToggle,
            Tag = data.Tag_WarpItems,
            Condition = new(),
        });
    }
}
