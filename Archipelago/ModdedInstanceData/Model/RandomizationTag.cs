using ReTFO.Archipelago.Utilities;
using System;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using GameData;
using global::Archipelago.MultiClient.Net.Models;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// A RandomizationTag used for randomization purposes
/// </summary>
public struct RandomizationTag : INullable, IIndex, IComparable<RandomizationTag>, IEquatable<RandomizationTag>
{
    /// <summary>
    /// Constructs a null RandomizationTag
    /// </summary>
    public RandomizationTag() => Value = 0;

    /// <summary>
    /// Creates a new randomization tag with the provided value
    /// </summary>
    public RandomizationTag(int value) => Value = value;

    /// <summary>
    /// Index of the tag definition in the relevant lookup table
    /// </summary>
    public int Value { get; private init; }

    public bool IsNull => Value == 0;
    public int AsIndex { get => Value - 1; init => Value = value + 1; }
    public int CompareTo(RandomizationTag other) => Value.CompareTo(other.Value);
    public bool Equals(RandomizationTag other) => Value.Equals(other.Value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RandomizationTag tag && Equals(tag);
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>
/// A definition for a randomization tag; metadata associated with a tag
/// </summary>
public struct RandomizationTagDefinition
{
    /// <summary>
    /// Create a randomization tag definition using the provided values;
    /// </summary>
    /// <param name="name">Name of the tag</param>
    /// <param name="description">A description of what the tag is used on</param>
    /// <param name="parent">The parent tag; this can be the default tag (null)</param>
    public RandomizationTagDefinition(string name, string description, RandomizationTag parent)
    {
        Name = name;
        Description = description;
        Parent = parent;
    }

    /// <summary>
    /// The name of this tag. Tag names must be unique.
    /// </summary>
    public string Name { get; private init; }

    /// <summary>
    /// A description of what this tag controls.
    /// </summary>
    public string Description { get; private init; }

    /// <summary>
    /// The parent of this tag, if any
    /// </summary>
    public RandomizationTag Parent { get; private init; }
}

/// <summary>
/// Helper struct for resolving tags. Allows tags to be immediately retrieved or
///  for retrieval to be delayed until needed, depending on use case.
/// </summary>
public struct TagResolver
{
    public TagResolver(Game.Data data, Func<Game.Data, RandomizationTag> resolver)
    {
        DataForSelfResolving = data;
        Resolver = resolver;
    }

    /// <summary>
    /// Data to use if this needs to implictly resolve to a tag
    /// </summary>
    public Game.Data DataForSelfResolving { get; init; }

    /// <summary>
    /// Function to call to resolve self
    /// </summary>
    public Func<Game.Data, RandomizationTag> Resolver { get; init; }

    /// <summary>
    /// Cause a tag resolver to resolve itself
    /// </summary>
    public RandomizationTag SelfResolve() => Resolver.Invoke(DataForSelfResolving);

    public static implicit operator Func<Game.Data, RandomizationTag>(TagResolver resolver) => resolver.Resolver;
    public static implicit operator RandomizationTag(TagResolver resolver) => resolver.SelfResolve();
}

/// <summary>
/// Class implementing extension properties for shared randomization tags
/// </summary>
public static class RootRandomizationTags
{
    extension (Game.Data gameData)
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
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Unlock Items", "Base randomization tag for unlock items, which are floating items required to enter a swath of regions. For example, expedition unlocks.", null));

        /// <summary>
        /// Base tag for goal items. A player must collect all available goal items for AP to consider the slot won.
        /// </summary>
        public TagResolver Tag_GoalItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Goal Items", "Base randomization tag for goal items, which are items AP uses to determine if a player won. All available goal items must be collected for AP to consider the slot won.", gd.Tag_Never));

        /// <summary>
        /// Items which are "optional", as in they don't exist if they're not randomized in.
        /// This is somewhat equivalent to simply getting the items as part of the starting inventory,
        ///  but it is not handled that way internally.
        /// </summary>
        public TagResolver Tag_OptionalItems
            => new TagResolver(gameData, gd => gd.LookupOrCreateTag("Optional Items", "Floating items which, if not randomized, will be starting items", gd.Tag_AllItems));

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

    extension (Expedition.Data data)
    {
        /// <summary>
        /// Base tag for goal items tied to a specific expedition.
        /// Used as a special tag when regenerating reachability when the rando starts.
        /// </summary>
        public TagResolver Tag_UnlockItems_ByExpedition
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Unlock Items", "Base tag for unlock items for a specific expedition", gd.Tag_UnlockItems));

        /// <summary>
        /// Base tag for goal items for a specific expedition.
        /// Used as a special tag when checking randomization settings to ensure solvability.
        /// </summary>
        public TagResolver Tag_GoalItems_ByExpedition
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Goal Items", "Base tag for goal items for a specific expedition", gd.Tag_GoalItems));
    }

}