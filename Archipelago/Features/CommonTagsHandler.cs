using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;


/// <summary>
/// Class implementing extension properties for shared randomization tags
/// </summary>
public static class CommonTagsHandler_Tags
{
    extension(Game.Data data)
    {
        /// <summary>
        /// Parent tag of all regions that are always randomized.
        /// </summary>
        public RegionID Region_Always
            => RegionID.From(data, "Always", () => new("Parent tag of all regions that are always randomized.", new()));

        /// <summary>
        /// Parent tag of all regions that cannot be randomized.
        /// </summary>
        public RegionID Region_Never
            => RegionID.From(data, "Never", () => new("Parent tag of all regions that cannot be randomized.", new()));

        // ========================================================================================

        /// <summary>
        /// Parent tag of all locations that can be randomized.
        /// </summary>
        public LocationID Location_All
            => LocationID.From(data, "All", () => new("Parent tag of all locations that can be randomized.", new()));

        /// <summary>
        /// Parent tag of all locations that are always randomized.
        /// </summary>
        public LocationID Location_Always
            => LocationID.From(data, "Always", () => new("Parent tag of all locations that are always randomized.", new()));

        /// <summary>
        /// Parent tag of all locations that cannot be randomized.
        /// </summary>
        public LocationID Location_Never
            => LocationID.From(data, "Never", () => new("Parent tag of all locations that cannot be randomized.", new()));

        /// <summary>
        /// Locations which are "empty". These locations do not contain an item by default, and can instead be provided a floating item
        ///  during multiworld setup. They are required for optional items to be collectable.
        /// </summary>
        public LocationID Location_Empty
            => LocationID.From(data, "Empty Locations", data => new("Locations which do not contain an item. Empty locations are used by floating items.", data.Location_All));

        // ========================================================================================

        /// <summary>
        /// Parent tag of all items that can be randomized.
        /// </summary>
        public ItemID Item_All
            => ItemID.From(data, "All", () => new("Parent tag of all items that can be randomized.", new()));

        /// <summary>
        /// Parent tag of all items that are always randomized.
        /// </summary>
        public ItemID Item_Always
            => ItemID.From(data, "Always", () => new("Parent tag of all items that are always randomized.", new()));

        /// <summary>
        /// Items which can never be randomized
        /// </summary>
        public ItemID Item_Never
            => ItemID.From(data, "Never", () => new("Parent of all items that cannot be randomized.", new()));

        /// <summary>
        /// Items which trigger scans
        /// </summary>
        public ItemID Item_Scans
            => ItemID.From(data, "Scan Items", data => new("Items which trigger scans", data.Item_All));

        /// <summary>
        /// Items which cause players to teleport
        /// </summary>
        public ItemID Item_Warps
            => ItemID.From(data, "Warp Items", data => new("Items which cause players to teleport", data.Item_All));

        /// <summary>
        /// Items representing codes of some kind; for example, terminal passwords and reactor codes.
        /// </summary>
        public ItemID Item_Codes
            => ItemID.From(data, "Code Items", data => new("Items representing codes of some kind; for example, terminal passwords and reactor codes.", data.Item_All));

        public OptionID Option_IsFakeGeneration
            => ArchipelagoFeatureHelper.GetFeature<CommonTagsHandler>().Option_IsFakeGeneration(data);
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

    private OptionID m_isFakeGeneration = new();
    public OptionID Option_IsFakeGeneration(Game.Data data)
        => m_isFakeGeneration.IsNull ? (m_isFakeGeneration = data.AddOption(new OptionIsFakeGeneration())) : m_isFakeGeneration;

    [Game.Callback]
    public void AddTagOptions(Game.Data data)
    {
        // Ensuring these special tags end up defined, just for simplicity's sake
        CommonTagsHandler_Tags.get_Region_Always(data);
        CommonTagsHandler_Tags.get_Region_Never(data);
        CommonTagsHandler_Tags.get_Location_Always(data);
        CommonTagsHandler_Tags.get_Location_Never(data);
        CommonTagsHandler_Tags.get_Item_Always(data);
        CommonTagsHandler_Tags.get_Item_Never(data);

        LocationID loc = data.Location_Empty;
        data.AddOption(new OptionLocationTagOption(
            displayName: "Empty Location Randomization",
            description:
                "Customize the randomization of all supported empty locations."
                + "\nOne empty location is added for each \"floating\" item that gets randomized to maintain"
                + " the item:location balance required by Archipelago."
                + "\nEnabling randomization of an empty location makes it available as a candidate for a floating item."
                + OptionTagOption.DESC_SUFFIX,
            category: Option.DEFAULT_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, loc),
            condition: new(),
            defaultValue: 0,
            tag: loc
        ));

        ItemID item = data.Item_Warps;
        data.AddOption(new OptionItemTagOption(
            displayName: "Warp Randomization",
            description: "Customize the randomization of all supported warps." + OptionTagOption.DESC_SUFFIX,
            category: Option.DEFAULT_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, item),
            condition: new(),
            defaultValue: 2,
            tag: item
        ));

        item = data.Item_Scans;
        data.AddOption(new OptionItemTagOption(
            displayName: "Scan Randomization",
            description:
                "Customize the randomization of all supported scans."
                + "\nCurrently, this includes event scans and certain objective scans, but not door scans."
                + OptionTagOption.DESC_SUFFIX,
            category: Option.DEFAULT_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, item),
            condition: new(),
            defaultValue: 2,
            tag: item
        ));

        item = data.Item_Codes;
        data.AddOption(new OptionItemTagOption(
            displayName: "Code Randomization",
            description:
                "Customize the randomization of all supported codes."
                + "\nThis is currently reactor codes and terminal password."
                + OptionTagOption.DESC_SUFFIX,
            category: Option.DEFAULT_OPTION_CATEGORY,
            categorySort: Option.MakeSortKey(data, item),
            condition: new(),
            defaultValue: 0,
            tag: item
        ));
    }
}
