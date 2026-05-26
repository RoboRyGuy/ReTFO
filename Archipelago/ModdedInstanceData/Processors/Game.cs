using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Game
{
    /// <summary>
    /// Data for specifically for and about this game. The base data class, which is also where we store all our generated data.
    /// </summary>
    public class Data
    {
        /// <summary>
        /// Simple reference type to accelerate copying Game.Data and decrease each copy's size
        /// </summary>
        private class StorageType
        {
            public bool IsComplete { get; set; } = false;
            public string? Name { get; set; } = null;
            public MidManager Manager { get; init; } = new();
            public Dictionary<string, Expedition.Data> ExpeditionLookup { get; init; } = new();
            public List<Region> RegionList { get; init; } = new();
            public Dictionary<string, RegionID> RegionLookup { get; init; } = new();
            public List<ReadOnlyPath> PathList { get; init; } = new();
            public List<RandomizationTagDefinition> TagDefinitions { get; init; } = new();
            public Dictionary<string, RandomizationTag> TagLookup { get; init; } = new();
            public List<Location> LocationList { get; init; } = new();
            public Dictionary<RandomizationTag, LocationID> LocationLookup { get; init; } = new();
            public List<Item> ItemList { get; init; } = new();
            public Dictionary<RandomizationTag, ItemID> ItemLookup { get; init; } = new();
            public List<ItemID> FloatingItems { get; init; } = new();
            public List<Option> Options { get; init; } = new();
        }

        /// <summary>
        /// Default constructor makes all-new data
        /// </summary>
        public Data(MidManager manager) 
        {
            Storage = new()
            {
                Manager = manager
            };

            // The first region must always be the Menu region
            LookupOrCreateRegion(MenuRegionName);

            // We need an "Empty" item
            AddItem(new(
                LookupOrCreateTag("Empty", "An item used to balance randomization during fill", this.Tag_Never),
                new ItemData() { IsFiller = true }
            ));
        }

        /// <summary>
        /// Copy constructor copies an existing data
        /// </summary>
        public Data(Data other)
        {
            Storage = other.Storage;
        }

        private StorageType Storage { get; init; }
        public bool IsComplete { get => Storage.IsComplete; set => Storage.IsComplete = value; }
        public string? Name { get => Storage.Name; set => Storage.Name = value; } // Unique name set AFTER done processing
        public MidManager Manager => Storage.Manager;
        private Dictionary<string, Expedition.Data> ExpeditionLookup => Storage.ExpeditionLookup;
        private List<Region> RegionList => Storage.RegionList;
        private Dictionary<string, RegionID> RegionLookup => Storage.RegionLookup;
        private List<ReadOnlyPath> PathList => Storage.PathList;
        // No dedicated path lookup; use each region's paths list to find relevant paths
        private List<RandomizationTagDefinition> TagDefinitions => Storage.TagDefinitions;
        private Dictionary<string, RandomizationTag> TagLookup => Storage.TagLookup;
        private List<Location> LocationList => Storage.LocationList;
        private Dictionary<RandomizationTag, LocationID> LocationLookup => Storage.LocationLookup;
        private List<Item> ItemList => Storage.ItemList;
        private Dictionary<RandomizationTag, ItemID> ItemLookup => Storage.ItemLookup;
        private List<ItemID> FloatingItems => Storage.FloatingItems;
        private List<Option> Options => Storage.Options;

        /// <summary>
        /// Attempt to register the given expedition data under the provided expedition name.
        /// </summary>
        /// <param name="name">The name of the expedition, typically in short form. IE: R1A1</param>
        /// <param name="data">The data for the expedition.</param>
        /// <returns>True if successful, false otherwise (the name is already taken)</returns>
        public bool TryRegisterExpedition(string name, Expedition.Data data)
        {
            if (IsComplete) FeatureLogger.Warning($"Adding late expedition: {name}");
            return ExpeditionLookup.TryAdd(name, data);
        }

        /// <summary>
        /// Get all the expeditions registered to this game data
        /// </summary>
        public IReadOnlyDictionary<string, Expedition.Data> GetAllExpeditions()
            => ExpeditionLookup;

        /// <summary>
        /// Attempt to look up expedition data by name
        /// </summary>
        /// <param name="name">The name of the expedition</param>
        /// <param name="data">The data for the expedition</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryLookupExpedition(string name, [NotNullWhen(true)] out Expedition.Data? data)
            => ExpeditionLookup.TryGetValue(name, out data);

        /// <summary>
        /// Attempts to retrieve a tag by name and parent. On fail, instead creates a new tag.
        /// </summary>
        /// <param name="tagName">The tag's name</param>
        /// <param name="tagDesc">The tag's description</param>
        /// <param name="parentResolver">A function which can get the parent of the tag</param>
        /// <returns>The desired tag</returns>
        public RandomizationTag LookupOrCreateTag(string tagName, string tagDesc, Func<Game.Data, RandomizationTag>? parentResolver)
        {
            if (!TagLookup.TryGetValue(tagName, out RandomizationTag result))
            {   // Invoke parent resolver first, since it'll likely add new tags and change this tag's index
                if (IsComplete) FeatureLogger.Warning($"Adding late tag: {tagName}");
                RandomizationTag parent = parentResolver?.Invoke(this) ?? new();
                result = new() { AsIndex = TagDefinitions.Count };
                TagDefinitions.Add(new(tagName, tagDesc, parent));
                TagLookup.Add(tagName, result);
            }
            return result;
        }

        /// <summary>
        /// Gets a collection of all tags and their definitions
        /// </summary>
        public IReadOnlyDictionary<RandomizationTag, RandomizationTagDefinition> GetAllTags()
            => new ReadOnlyListDict<RandomizationTag, RandomizationTagDefinition>(TagDefinitions);

        /// <summary>
        /// Attempt to look up a randomization tag with the provided name and parent
        /// </summary>
        /// <param name="tagName">The name of the tag</param>
        /// <param name="existingTag">The found tag, if successful; a null tag otherwise</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryLookupTag(string tagName, out RandomizationTag existingTag)
            => TagLookup.TryGetValue(tagName, out existingTag);

        /// <summary>
        /// Look up the definition for a tag
        /// </summary>
        /// <param name="tag">The tag to look up</param>
        /// <returns>The definitino of the tag</returns>
        public RandomizationTagDefinition LookupTagDef(RandomizationTag tag)
            => TagDefinitions[tag.AsIndex];

        #region TagMatching
        /// <summary>
        /// Test if a tag matches against another tag
        /// </summary>
        /// <param name="parent">The parent tag to match against</param>
        /// <param name="child">The child tag to test</param>
        /// <returns>True if the tags match, false otherwise</returns>
        public bool TagMatches(RandomizationTag parent, RandomizationTag child)
        {
            // Null tags are invalid for this test
            if (parent.IsNull) throw new ArgumentNullException(nameof(parent));
            if (child.IsNull) throw new ArgumentNullException(nameof(child));

            do
            {
                if (parent.Equals(child)) return true;
                child = TagDefinitions[child.AsIndex].Parent;
            } while (!child.IsNull);
            return false;
        }

        /// <summary>
        /// Test if one tag matches against any location tag
        /// </summary>
        /// <param name="parent">The parent tag to match against</param>
        /// <param name="loc">The location to test</param>
        /// <returns>True if the tags match, false otherwise</returns>
        public bool TagMatches(RandomizationTag parent, Location loc)
            => TagMatches(parent, loc.NameTag)
            || (!loc.Tag2.IsNull && TagMatches(parent, loc.Tag2))
            || (!loc.Tag3.IsNull && TagMatches(parent, loc.Tag3));

        /// <summary>
        /// Test if one tag matches against any item tag
        /// </summary>
        /// <param name="parent">The parent tag to match against</param>
        /// <param name="item">The item to test</param>
        /// <returns>True if the tags match, false otherwise</returns>
        public bool TagMatches(RandomizationTag parent, Item item)
            => TagMatches(parent, item.NameTag)
            || (!item.Tag2.IsNull && TagMatches(parent, item.Tag2))
            || (!item.Tag3.IsNull && TagMatches(parent, item.Tag3));

        /// <summary>
        /// Test if a tag matches against a collection of tags. Ideally, the collection is a HashSet or similar
        /// </summary>
        /// <param name="parents">The parent tag to match against</param>
        /// <param name="child">The child tag to test</param>
        /// <returns>True if the tags match, false otherwise</returns>
        public bool TagMatches(ICollection<RandomizationTag> parents, RandomizationTag child)
        {
            if (child.IsNull) throw new ArgumentNullException(nameof(child));
            if (parents.Count == 0) return false;
            if (parents.Count == 1) return TagMatches(parents.First(), child);

            do
            {
                if (parents.Contains(child)) return true;
                child = TagDefinitions[child.AsIndex].Parent;
            } while (!child.IsNull);
            return false;
        }

        /// <summary>
        /// Test if any tag in a location matches against a collection of tags.
        /// </summary>
        /// <param name="parents">The parent tag to match against</param>
        /// <param name="loc">The location to test</param>
        /// <returns>True if the tags match, false otherwise</returns>
        public bool AnyTagMatches(ICollection<RandomizationTag> parents, Location loc)
        {
            if (parents.Count == 0) return false;
            if (parents.Count == 1) return TagMatches(parents.First(), loc);
            return TagMatches(parents, loc.NameTag)
                || (!loc.Tag2.IsNull && TagMatches(parents, loc.Tag2))
                || (!loc.Tag3.IsNull && TagMatches(parents, loc.Tag3));
        }

        /// <summary>
        /// Test if all tags in the location match against a collection of tags
        /// </summary>
        /// <param name="parents">The parent tag to match against</param>
        /// <param name="loc">The location to test</param>
        /// <returns>True if the tags match, false otherwise</returns>
        public bool AllTagsMatch(ICollection<RandomizationTag> parents, Location loc)
        {
            if (parents.Count == 0) return false;
            return TagMatches(parents, loc.NameTag)
                && (loc.Tag2.IsNull || TagMatches(parents, loc.Tag2))
                && (loc.Tag3.IsNull || TagMatches(parents, loc.Tag3));
        }

        /// <summary>
        /// Test if any tag in an item matches against a collection of tags.
        /// </summary>
        /// <param name="parents">The parent tag to match against</param>
        /// <param name="item">The item to test</param>
        /// <returns>True if the tags match, false otherwise</returns>
        public bool AnyTagMatches(ICollection<RandomizationTag> parents, Item item)
        {
            if (parents.Count == 0) return false;
            if (parents.Count == 1) return TagMatches(parents.First(), item);
            return TagMatches(parents, item.NameTag)
                || (!item.Tag2.IsNull && TagMatches(parents, item.Tag2))
                || (!item.Tag3.IsNull && TagMatches(parents, item.Tag3));
        }

        /// <summary>
        /// Test if all tags in the item match against a collection of tags
        /// </summary>
        /// <param name="parents">The parent tag to match against</param>
        /// <param name="item">The item to test</param>
        /// <returns>True if the tags match, false otherwise</returns>
        public bool AllTagsMatch(ICollection<RandomizationTag> parents, Item item)
        {
            if (parents.Count == 0) return false;
            return TagMatches(parents, item.NameTag)
                && (item.Tag2.IsNull || TagMatches(parents, item.Tag2))
                && (item.Tag3.IsNull || TagMatches(parents, item.Tag3));
        }
        #endregion

        /// <summary>
        /// Lookup a RegionID; create the region if necessary
        /// </summary>
        /// <param name="regionName">The name of the region to get an ID of</param>
        /// <returns>The ID of the region</returns>
        public RegionID LookupOrCreateRegion(string regionName)
        {
            if (!RegionLookup.TryGetValue(regionName, out RegionID region))
            {
                if (IsComplete) FeatureLogger.Warning($"Adding late region: {regionName}");
                region = new RegionID() { AsIndex = RegionList.Count };
                RegionList.Add(new Region(regionName));
                RegionLookup.Add(regionName, region);
            }
            return region;
        }

        /// <summary>
        /// Get all registered regions
        /// </summary>
        public IReadOnlyDictionary<RegionID, Region> GetAllRegions()
            => new ReadOnlyListDict<RegionID, Region>(RegionList);

        /// <summary>
        /// Try to look up a region by name
        /// </summary>
        /// <param name="regionName">The name of the region to look up</param>
        /// <param name="region">The found region, or null if no region is found</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryLookupRegion(string regionName, out KeyedRegion region)
        {
            if (RegionLookup.TryGetValue(regionName, out RegionID regionID))
            {
                region = new(regionID, LookupRegion(regionID));
                return true;
            }
            else
            {
                region = new();
                return false;
            }
        }

        /// <summary>
        /// Get a region by ID
        /// </summary>
        /// <param name="id">The ID of the region</param>
        /// <returns>The region</returns>
        public ReadOnlyRegion LookupRegion(RegionID id)
            => LookupRegionProtected(id);

        public Region LookupRegionProtected(RegionID id)
            => RegionList[id.AsIndex];

        /// <summary>
        /// Set a particular region's reachable status
        /// </summary>
        /// <param name="id">ID of the region</param>
        /// <param name="isReachable">The new value for the region's reachable value</param>
        public void SetRegionReachable(RegionID id, bool isReachable)
        {
            int index = id.AsIndex;
            if (index < 0 || index >= RegionList.Count)
                throw new ArgumentException("Attempted to set reachability for a region which does not exist");

            Span<Region> regions = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(RegionList);
            regions[id.AsIndex].Reachable = isReachable;
        }

        /// <summary>
        /// Add a new path.
        /// </summary>
        /// <param name="path">The path to add</param>
        /// <returns>The ID of the newly-added path</returns>
        public PathID AddPath(ReadOnlyPath path)
        {
            if (IsComplete)
            {
                FeatureLogger.Warning($"Adding late path: {path.Name ?? "NO NAME"}");
                FeatureLogger.Warning($"                  From {LookupRegion(path.StartingRegion).Name}");
                FeatureLogger.Warning($"                    To {LookupRegion(path.EndingRegion).Name}");
            }

            if (path.ReqItem.Type != Path.RequiredItem.eType.None && path.ReqCount <= 0)
                FeatureLogger.Warning("Adding path with non-None path requirement but it has a reqcount of 0!");

            if (path.StartingRegion.IsNull)
                throw new ArgumentNullException("Cannot add path; starting region is null!");

            if (path.EndingRegion.IsNull)
                throw new ArgumentNullException("Cannot add path; ending region is null!");

            PathID id = new() { AsIndex = PathList.Count };
            PathList.Add(path);
            LookupRegionProtected(path.StartingRegion).AddPath(id);
            return id;
        }

        /// <summary>
        /// Gets all paths currently registered
        /// </summary>
        public IReadOnlyDictionary<PathID, ReadOnlyPath> GetAllPaths()
            => new ReadOnlyListDict<PathID, ReadOnlyPath>(PathList);

        /// <summary>
        /// Try to look up a path based on its start and end regions. 
        /// </summary>
        /// <param name="start">The ID of the starting region</param>
        /// <param name="end">The ID of the ending region</param>
        /// <param name="path">The path which was found, null otherwise</param>
        /// <returns>True if succesful, false otherwise</returns>
        /// <remarks>
        /// Multiple paths can exist between any given start and end region. This outputs the first matching path.
        /// </remarks>
        public bool TryLookupPath(RegionID start, RegionID end, out ReadOnlyPath path)
        {
            ReadOnlyRegion region = LookupRegion(start);
            path = region.ConnectedPaths
                .Select(LookupPath)
                .FirstOrDefault(p => p.EndingRegion.Equals(end));
            return !path.IsNull;
        }

        /// <summary>
        /// Get a path by ID
        /// </summary>
        /// <param name="id">The ID of the path</param>
        /// <returns>The found path object</returns>
        public ReadOnlyPath LookupPath(PathID id) => PathList[id.AsIndex];

        /// <summary>
        /// Set the required item count for a particular path
        /// </summary>
        /// <param name="id">The path to modify</param>
        /// <param name="newCount">The new count</param>
        /// <remarks>
        /// Used primarily during graph traversal to update direct requirements
        /// </remarks>
        public void SetPathReqCount(PathID id, uint newCount)
        {
            if (IsComplete) FeatureLogger.Warning($"Late path req modification: PathID {id.AsId}");
            Path newPath = PathList[id.AsIndex].MakeMutable();
            newPath.ReqCount = newCount;
            PathList[id.AsIndex] = newPath;
        }

        /// <summary>
        /// Shortcut to create a new base Location object and add it
        /// </summary>
        /// <param name="nameTag">Name tag for the location</param>
        /// <param name="regions">Regions the location can be found in</param>
        /// <param name="randData">Rnadomization data for the location</param>
        /// <returns>The new location's ID, or a null ID if it failed</returns>
        public LocationID AddLocation(RandomizationTag nameTag, RegionList regions, LocationData randData)
            => AddLocation(nameTag, regions, randData, new ItemID());

        /// <inheritdoc cref="AddLocation(RandomizationTag, Model.RegionList, LocationData)"/>
        /// <param name="item">ID of the item in this location, or a null ID for no item</param>
        public LocationID AddLocation(RandomizationTag nameTag, RegionList regions, LocationData randData, ItemID item)
            => AddLocation(new Location(nameTag, regions, randData) { ItemID = item });

        /// <summary>
        /// Try to add a location.
        /// </summary>
        /// <param name="location">The location to add</param>
        /// <returns>The ID of the newly-added location, or a null ID if the location name is taken</returns>
        public LocationID AddLocation(Location location)
        {
            if (location.NameTag.IsNull)
                throw new ArgumentNullException("Cannot register an item with a null name tag!");

            if (IsComplete) FeatureLogger.Warning($"Adding late location: {LookupTagDef(location.NameTag).Name}");

            if (LocationLookup.ContainsKey(location.NameTag))
            {
                FeatureLogger.Error($"Failed to add new location: {LookupTagDef(location.NameTag).Name}");
                return new();
            }

            LocationID id = new() { AsIndex = LocationList.Count };
            LocationList.Add(location);
            LocationLookup.Add(location.NameTag, id);

            if (location.OwningRegionIds.Distinct().Count() != location.OwningRegionIds.Length)
                FeatureLogger.Error($"Location is contained in the same region multiple times: {LookupTagDef(location.NameTag).Name}");

            foreach (var regionId in location.OwningRegionIds)
                RegionList[regionId.AsIndex].AddLocation(id);

            return id;
        }

        /// <summary>
        /// Get all locations currently registered
        /// </summary>
        public IReadOnlyDictionary<LocationID, Location> GetAllLocations()
            => new ReadOnlyListDict<LocationID, Location>(LocationList);

        /// <summary>
        /// Attempt to lookup a location by NameTag
        /// </summary>
        /// <param name="nameTag">The NameTag of the location</param>
        /// <param name="location">The found location</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryLookupLocation(RandomizationTag nameTag, out KeyedLocation location)
        {
            if (LocationLookup.TryGetValue(nameTag, out LocationID id))
            {
                location = new(id, LookupLocation(id));
                return true;
            }
            else
            {
                location = new();
                return false;
            }
        }

        /// <summary>
        /// Lookup a location by ID
        /// </summary>
        /// <param name="id">The ID of the location to be looked up</param>
        /// <returns>The found location</returns>
        /// <remarks>If this Game.Data provided the ID, the location is guaranteed to exist</remarks>
        public Location LookupLocation(LocationID id) => LocationList[id.AsIndex];

        /// <summary>
        /// Attempts to register a new item.
        /// </summary>
        /// <param name="item">The item to add</param>
        /// <returns>The ID of the newly-added item</returns>
        public ItemID AddItem(Item item)
        {
            if (item.NameTag.IsNull)
                throw new ArgumentNullException("Cannot register an item with a null name tag!");

            if (IsComplete) FeatureLogger.Warning($"Adding late item: {LookupTagDef(item.NameTag).Name}");

            if (ItemLookup.ContainsKey(item.NameTag))
            {
                string name = TagDefinitions[item.NameTag.AsIndex].Name;
                throw new ArgumentException($"An item with the NameTag {name} is already registered!");
            }

            ItemID id = new() { AsIndex = ItemList.Count };
            ItemList.Add(item);
            ItemLookup.Add(item.NameTag, id);
            return id;
        }

        /// <summary>
        /// Gets all registered items
        /// </summary>
        public IReadOnlyDictionary<ItemID, Item> GetAllItems() => new ReadOnlyListDict<ItemID, Item>(ItemList);

        /// <summary>
        /// Attempt to lookup an item by name.
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryLookupItem(RandomizationTag name, out KeyedItem item)
        {
            if (ItemLookup.TryGetValue(name, out ItemID id))
            {
                item = new(id, LookupItem(id));
                return true;

            }
            else
            {
                item = new();
                return false;
            }
        }

        /// <summary>
        /// Lookup an item by ID
        /// </summary>
        /// <param name="name">The name of the item</param>
        /// <returns>The item</returns>
        public Item LookupItem(ItemID id) => ItemList[id.AsIndex];

        /// <summary>
        /// Adds an item as a floating item, which can be randomized to any empty location.
        /// </summary>
        /// <param name="id">The ID of the item to add</param>
        public void AddFloatingItem(ItemID id)
        {
            if (IsComplete) FeatureLogger.Warning($"Adding late floating item: {LookupTagDef(LookupItem(id).NameTag).Name}");
            FloatingItems.Add(id);
        }

        /// <summary>
        /// Get all registered floating item IDs
        /// </summary>
        public IReadOnlyCollection<ItemID> GetAllFloatingItemIds() => FloatingItems;

        /// <summary>
        /// Register an option with the game data so users can have an easier time customizing their gameplay
        /// </summary>
        public void AddOption(Option option)
        {
            if (Options.Contains(option))
                throw new ArgumentException("Cannot register duplicate option");
            Options.Add(option);
        }

        /// <summary>
        /// Get all registered options.
        /// </summary>
        public IReadOnlyCollection<Option> GetAllOptions() => Options;

        /// <summary>
        /// Name of the very first region in the game.
        /// </summary>
        public const string s_menuRegionName = "Menu";

        /// <inheritdoc cref="s_menuRegionName"/>
        public string MenuRegionName => s_menuRegionName;

        /// <summary>
        /// The menu region itself
        /// </summary>
        public RegionID MenuRegion => LookupOrCreateRegion(MenuRegionName);

        /// <summary>
        /// Used as input to UnstuffPlacements
        /// </summary>
        public struct RegionInfo : IEquatable<RegionInfo>
        {
            /// <summary>
            /// Index of a region
            /// </summary>
            public RegionID Region;

            /// <summary>
            /// If the region is "bad", or difficult to access.
            /// For example, if the region is a terminal which is locked, or a zone which requires a key.
            /// </summary>
            public bool IsBad;

            public override bool Equals(object? obj) => obj is RegionInfo info && Region.Equals(info);
            public bool Equals(RegionInfo info) => Region.Equals(info.Region);
            public static bool operator ==(RegionInfo left, RegionInfo right) => left.Equals(right);
            public static bool operator !=(RegionInfo left, RegionInfo right) => !left.Equals(right);
            public override int GetHashCode() => Region.GetHashCode();
        }

        /// <summary>
        /// Given an enumeration of placement enumerations, under the assumption that the same placement cannot
        /// be used twice, creates N placement lists and unstuffs them.
        /// </summary>
        /// <param name="placements">The placements to unstuff</param>
        /// <param name="neededCount">How many placements are needed total</param>
        /// <returns>The unstuffed placments</returns>
        /// <remarks>
        /// This function solves the issue that some objectives will "stuff" all possible regions. This effectively
        ///  guarantees that should you check a region, a location is guaranteed to be in there. For example, in
        ///  R5C3 (Starvation) you must perform 2 corrupted uplinks in a room with 4 total terminals, one of which
        ///  is locked. By "unstuffing" our placmenets, Archipelago can recognize that you don't need the locked
        ///  terminal to perform the first uplink, even if you do need it to guarantee you can perform the second.
        ///  <br/>
        /// This unstuff algorithm uses a simplified "bad" system to priotize placements which avoid certain regions.
        ///  If we go back to R5C3, if we instead had 30 terminals in that room, this algorithm would still recognize
        ///  that the first uplink is possible by merit of the fact only one uplink can require the password terminal.
        ///  It is able to create exactly one placement requiring that terminal and the rest discluding it.
        /// </remarks>
        public IEnumerable<List<RegionID>> UnstuffPlacements(IEnumerable<IEnumerable<RegionInfo>> placements, int neededCount)
        {
            if (neededCount == 0) yield break;
            else if (!placements.Any())
            {
                FeatureLogger.Error("Failed to unstuff placements: No placements were provided");
                yield break;
            }

            // Create all the placements and sort them into groups
            List<HashSet<RegionInfo>> sets = placements.Select(ps => ps.Distinct().ToHashSet()).ToList();
            var setGroups = Enumerable.Range(0, neededCount)
                .Select(i => sets[i % sets.Count])
                .GroupBy(s => s, HashSet<RegionInfo>.CreateSetComparer());

            // Performing the actual unstuffing
            foreach (var group in setGroups)
            {
                int count = group.Count();

                // Not sure how this would happen but probably best to handle it
                if (count <= 0)
                    FeatureLogger.Exception(new NotSupportedException("Somehow had 0 placements in placement group!?"));

                // Identify bad regions and move them into a separate collection (without going over our available count)
                List<RegionInfo> badRegions = new();
                foreach (var info in group.Key)
                {
                    if (info.IsBad)
                    {
                        badRegions.Add(info);
                        count -= 1;
                        if (count == 0)
                            break;
                    }
                }
                foreach (var info in badRegions)
                    group.Key.Remove(info);

                // All placements include all "good" regions and 1 bad region
                foreach (var badRegion in badRegions)
                    yield return new List<RegionID>(group.Key.Append(badRegion).Select(info => info.Region));

                // Fill out the remaining required spots with only the good regions
                while (count-- > 0)
                    yield return group.Key.Select(info => info.Region).ToList();
            }
        }

        /// <summary>
        /// Called when processing is done to trim all lists, arrays, etc
        /// </summary>
        public void CleanUp()
        {
            ExpeditionLookup.TrimExcess();
            RegionList.TrimExcess();
            RegionLookup.TrimExcess();
            PathList.TrimExcess();
            TagDefinitions.TrimExcess();
            TagLookup.TrimExcess();
            LocationList.TrimExcess();
            LocationLookup.TrimExcess();
            ItemList.TrimExcess();
            ItemLookup.TrimExcess();
            FloatingItems.TrimExcess();

            foreach (var region in RegionList)
                region.CleanUp();
        }

    }

    /// <summary>
    /// Attribute used to mark static functions which should autoregister to this processor
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class Callback : MidManager.Processor<Data>.Callback { }

    /// <summary>
    /// Actual class wrapping an event processing instance
    /// </summary>
    public class Processor : MidManager.Processor<Data>
    {
        protected event Delegate? Event = null;

        public override void RegisterCallback(Delegate callback)
            => Event += callback;

        public override void UnregisterCallback(Delegate callback)
            => Event -= callback;

        public override void Process(Data data)
            => Event?.Invoke(data);
    }

    extension(Game.Data gameData)
    {
        /// <summary>
        /// Get the Game.Data processor from an instance of Game.Data
        /// </summary>
        public Processor GameProcessor
            => (Processor)gameData.Manager.GetProcessor<Data>();
    }
}
