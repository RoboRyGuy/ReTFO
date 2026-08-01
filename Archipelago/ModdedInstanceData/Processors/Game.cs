using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.ModdedInstanceData.Model;

public static class Game
{
    /// <summary>
    /// Separates core game data into a separate scope object
    /// </summary>
    private record class ScopeData
    {
        public ScopeData(MidManager manager) => Manager = manager;
        public MidManager Manager { get; init; }
        public bool IsComplete { get; set; } = false;
        public string? Name { get; set; } = null;
        public TagStorage<RegionID, Region> RegionStorage { get; init; } = new();
        public TagStorage<LocationID, Location> LocationStorage { get; init; } = new();
        public TagStorage<ItemID, Item> ItemStorage { get; init; } = new();
        public List<Path> Paths { get; init; } = new();
        public List<(RegionID, ItemID)> FloatingItems { get; init; } = new();
        public List<OptionBase> Options { get; init; } = new();
        public ChoiceState[]? Choices { get; set; } = null;
    }

    /// <summary>
    /// Data for specifically for and about this game. The base data class, which is also where we store all our generated data.
    /// </summary>
    public class Data
    {
        /// <summary>
        /// Controlled access to game data's region storage
        /// </summary>
        public struct RegionStorageView
        {
            public RegionStorageView(Game.Data data) => m_data = data;
            private readonly Data m_data;

            /// <summary>
            /// Check / register this new region
            /// </summary>
            private void RegisterRegion(RegionID id, Region region)
            {
                if (m_data.IsComplete)
                    FeatureLogger.Warning($"Late adding / modifying region: {id} \"{LookUpName(id)}\"");

                if (region.ConnectedPaths.Any())
                    throw new NotSupportedException(
                        $"{id} \"{LookUpName(id)}\" created with a connected path."
                        + "\nAt this time, new regions cannot define connected paths."
                    );

                foreach (LocationID lid in region.ConnectedLocations)
                {
                    if (!m_data.LocationStorage.ContainsID(lid))
                        throw new NotSupportedException(
                            $"{id} \"{LookUpName(id)}\" created with undefined connected location {lid}."
                            + "\nThe connected locations in a region must be well-defined at all times."
                        );

                    Location? loc = m_data.LocationStorage.LookUpValue(lid);
                    if (loc == null)
                        throw new NotSupportedException(
                            $"{id} \"{LookUpName(id)}\" created with null connected location {lid}."
                            + "\nThe connected locations in a region must be non-null."
                        );

                    loc.AddOwningRegionIDs(id);
                }
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.Count"/>
            public int Count => m_data.RegionStorage.Count;

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllIDs"/>
            public IEnumerable<RegionID> GetAllIDs() => m_data.RegionStorage.GetAllIDs();

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllEntries"/>
            public IReadOnlyDictionary<RegionID, TagStorage<RegionID, Region>.TagEntry> GetAllEntries() => m_data.RegionStorage.GetAllEntries();

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllValues"/>
            public IReadOnlyDictionary<RegionID, Region> GetAllValues() => m_data.RegionStorage.GetAllValues();

            /// <inheritdoc cref="TagStorage{TID, TItem}.Create"/>
            public RegionID Create(string name, TagDefinition<RegionID> definition, Region item = default)
            {
                RegionID result = m_data.RegionStorage.Create(name, definition, item);
                RegisterRegion(result, item);
                return result;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate(string, Func{TagDefinition{TID}}, TItem})"/>
            public RegionID LookUpOrCreate(string name, Func<TagDefinition<RegionID>> definitionFactory, Region item = default)
            {
                TryLookUpOrCreate(out RegionID id, name, definitionFactory, item);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate(out TID, string, Func{TagDefinition{TID}}, TItem})"/>
            public bool TryLookUpOrCreate(out RegionID result, string name, Func<TagDefinition<RegionID>> definitionFactory, Region item = default)
            {
                if (m_data.RegionStorage.TryLookUpOrCreate(out result, name, definitionFactory, item))
                {
                    RegisterRegion(result, item);
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate(string, Func{TagDefinition{TID}}, Func{TItem}})"/>
            public RegionID LookUpOrCreate(string name, Func<TagDefinition<RegionID>> definitionFactory, Func<Region> valueFactory)
            {
                TryLookUpOrCreate(out RegionID id, name, definitionFactory, valueFactory);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate(out TID, string, Func{TagDefinition{TID}}, Func{TItem}})"/>
            public bool TryLookUpOrCreate(out RegionID result, string name, Func<TagDefinition<RegionID>> definitionFactory, Func<Region> valueFactory)
            {
                if (m_data.RegionStorage.TryLookUpOrCreate(out result, name, definitionFactory, valueFactory))
                {
                    RegisterRegion(result, m_data.RegionStorage.LookUpValue(result));
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate{TData}(TData, string, Func{TData, TagDefinition{TID}}, TItem)"/>
            public RegionID LookUpOrCreate<TData>(TData data, string name, Func<TData, TagDefinition<RegionID>> definitionFactory, Region item = default)
            {
                TryLookUpOrCreate(out RegionID id, data, name, definitionFactory, item);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate{TData}(TData, string, Func{TData, TagDefinition{TID}}, TItem)"/>
            public bool TryLookUpOrCreate<TData>(out RegionID result, TData data, string name, Func<TData, TagDefinition<RegionID>> definitionFactory, Region item = default)
            {
                if (m_data.RegionStorage.TryLookUpOrCreate(out result, data, name, definitionFactory, item))
                {
                    RegisterRegion(result, item);
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate{TData}(TData, string, Func{TData, TagDefinition{TID}}, Func{TData, TItem})"/>
            public RegionID LookUpOrCreate<TData>(TData data, string name, Func<TData, TagDefinition<RegionID>> definitionFactory, Func<TData, Region> valueFactory) where TData : Game.Data
            {
                TryLookUpOrCreate(out RegionID id, data, name, definitionFactory, valueFactory);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate{TData}(out TID, TData, string, Func{TData, TagDefinition{TID}}, Func{TData, TItem})"/>
            public bool TryLookUpOrCreate<TData>(out RegionID result, TData data, string name, Func<TData, TagDefinition<RegionID>> definitionFactory, Func<TData, Region> valueFactory) where TData : Game.Data
            {
                if (m_data.RegionStorage.TryLookUpOrCreate(out result, data, name, definitionFactory, valueFactory))
                {
                    RegisterRegion(result, m_data.RegionStorage.LookUpValue(result));
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpID"/>
            public bool TryLookUpID(string name, out RegionID id) => m_data.RegionStorage.TryLookUpID(name, out id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpEntry"/>
            public TagStorage<RegionID, Region>.TagEntry LookUpEntry(RegionID id) => m_data.RegionStorage.LookUpEntry(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpName"/>
            public string LookUpName(RegionID id) => m_data.RegionStorage.LookUpName(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpDefinition"/>
            public TagDefinition<RegionID> LookUpDefinition(RegionID id) => m_data.RegionStorage.LookUpDefinition(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpValue"/>
            public Region LookUpValue(RegionID id) => m_data.RegionStorage.LookUpValue(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpValueChecked"/>
            //public Region LookUpValueChecked(RegionID id) => m_data.LocationStorage.LookUpValueChecked(id);

            /// <summary>
            /// Set the value stored in a region
            /// </summary>
            /// <param name="id">ID of the region to modify</param>
            /// <param name="newValue">The new value to store</param>
            public void SetData(RegionID id, object? newValue)
            {
                Region region = m_data.RegionStorage.LookUpValue(id);
                if (region.RegionData != null)
                    FeatureLogger.Warning("Overwriting region data for region: " + LookUpName(id));
                m_data.RegionStorage.SetValue(id, new(region) { RegionData = newValue });
            }

            /// <summary>
            /// Get the region data stored in a particular region. Throws on fail.
            /// </summary>
            /// <typeparam name="T">The expected type of the region data</typeparam>
            /// <param name="id">The ID of the region to fetch the data from</param>
            /// <returns>The typed region data.</returns>
            public T GetData<T>(RegionID id) where T : class
                => m_data.RegionStorage.LookUpValue(id).GetData<T>();

            /// <summary>
            /// Helper to extract custom data from a region type-safely.
            /// If the stored data is null, returns false; if the stored data is non-null but cannot
            ///  be cast to the requested type, throws; else, returns true and sets the result to the value.
            /// </summary>
            public bool GetDataAllowNull<T>(RegionID id, [MaybeNullWhen(false)] out T result) where T : class
                => LookUpValue(id).GetDataAllowNull(out result);

            /// <summary>
            /// Try to cast a region's RegionData to the requested type; returns true if 
            ///  successful, false if the data is null or cannot be cast
            /// </summary>
            public bool TryGetData<T>(RegionID id, [NotNullWhen(true)] out T? result) where T : class
                => LookUpValue(id).TryGetData<T>(out result);

            /// <inheritdoc cref="TagStorage{TID, TItem}.ContainsID(TID)"/>
            public bool ContainsID(RegionID id) => m_data.RegionStorage.ContainsID(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.IsChild(TID, TID)"/>
            public bool IsChild(RegionID child, RegionID parent) => m_data.RegionStorage.IsChild(child, parent);

            /// <inheritdoc cref="TagStorage{TID, TItem}.IsChild(TID, IReadOnlyCollection{TID})"/>
            public bool IsChild(RegionID child, IReadOnlyCollection<RegionID> parents) => m_data.RegionStorage.IsChild(child, parents);

            /// <inheritdoc cref="TagStorage{TID, TItem}.MakeChain"/>
            public RegionID[] MakeChain(RegionID id) => m_data.RegionStorage.MakeChain(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllParents"/>
            public HashSet<RegionID> GetAllParents(IEnumerable<RegionID> ids) => m_data.RegionStorage.GetAllParents(ids);
        }

        /// <summary>
        /// Controlled access to game data's location storage
        /// </summary>
        public struct LocationStorageView
        {
            public LocationStorageView(Game.Data data) => m_data = data;
            private readonly Data m_data;

            /// <summary>
            /// Check / register this new location
            /// </summary>
            private void RegisterLocation(LocationID id, Location? location)
            {
                if (m_data.IsComplete)
                    FeatureLogger.Warning($"Late adding / modifying location: {id} \"{LookUpName(id)}\"");

                if (location == null) return;

                foreach (RegionID rid in location.OwningRegionIDs)
                {
                    if (!m_data.RegionStorage.ContainsID(rid))
                        throw new NotSupportedException(
                            $"{id} \"{LookUpName(id)}\" created with undefined owning region {rid}."
                            + "The owning regions in a location must be well-defined at all times."
                        );

                    m_data.RegionStorage.SetValue(
                        rid, 
                        m_data.RegionStorage.LookUpValue(rid).WithAdded(id)
                    );
                }

                // Normally, I'd check ItemID here.
                // However, there is no viable control on ItemID after this point,
                //  so such a check would be kinda useless.
                // We will simply allow ItemID to be non-well-defined until I figure
                //  out a better pattern which lets me secure it
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.Count"/>
            public int Count => m_data.LocationStorage.Count;

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllIDs"/>
            public IEnumerable<LocationID> GetAllIDs() => m_data.LocationStorage.GetAllIDs();

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllEntries"/>
            public IReadOnlyDictionary<LocationID, TagStorage<LocationID, Location>.TagEntry> GetAllEntries() => m_data.LocationStorage.GetAllEntries();

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllValues"/>
            public IReadOnlyDictionary<LocationID, Location?> GetAllValues() => m_data.LocationStorage.GetAllValues()!;

            /// <inheritdoc cref="TagStorageExtensions.GetAllValuesNonNull"/>
            public IEnumerable<KeyValuePair<LocationID, Location>> GetAllValuesNonNull() => m_data.LocationStorage.GetAllValuesNonNull();

            /// <inheritdoc cref="TagStorage{TID, TItem}.Create"/>
            public LocationID Create(string name, TagDefinition<LocationID> definition, Location? item = null)
            {
                LocationID result = m_data.LocationStorage.Create(name, definition, item);
                RegisterLocation(result, item);
                return result;
            }

            /// <summary>
            /// Shortcut to create a new location
            /// </summary>
            /// <param name="name">The name of the location</param>
            /// <param name="definition">Tag definition for the location</param>
            /// <param name="owningRegions">The location's owning region</param>
            /// <param name="randData">The location's location data</param>
            /// <param name="itemId">The item held by the location</param>
            /// <returns>The created location ID</returns>
            public LocationID Create(string name, TagDefinition<LocationID> definition, RegionList owningRegions, LocationData randData, ItemID itemId = new())
                => Create(name, definition, new(owningRegions, randData, itemId));

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate(string, Func{TagDefinition{TID}}, TItem})"/>
            public LocationID LookUpOrCreate(string name, Func<TagDefinition<LocationID>> definitionFactory, Location? item = null)
            {
                TryLookUpOrCreate(out LocationID id, name, definitionFactory, item);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate(out TID, string, Func{TagDefinition{TID}}, TItem})"/>
            public bool TryLookUpOrCreate(out LocationID result, string name, Func<TagDefinition<LocationID>> definitionFactory, Location? item = null)
            {
                if (m_data.LocationStorage.TryLookUpOrCreate(out result, name, definitionFactory, item))
                {
                    RegisterLocation(result, item);
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate(string, Func{TagDefinition{TID}}, Func{TItem}})"/>
            public LocationID LookUpOrCreate(string name, Func<TagDefinition<LocationID>> definitionFactory, Func<Location> valueFactory)
            {
                TryLookUpOrCreate(out LocationID id, name, definitionFactory, valueFactory);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate(out TID, string, Func{TagDefinition{TID}}, Func{TItem}})"/>
            public bool TryLookUpOrCreate(out LocationID result, string name, Func<TagDefinition<LocationID>> definitionFactory, Func<Location> valueFactory)
            {
                if (m_data.LocationStorage.TryLookUpOrCreate(out result, name, definitionFactory, valueFactory))
                {
                    RegisterLocation(result, m_data.LocationStorage.LookUpValue(result));
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate{TData}(TData, string, Func{TData, TagDefinition{TID}}, TItem)"/>
            public LocationID LookUpOrCreate<TData>(TData data, string name, Func<TData, TagDefinition<LocationID>> definitionFactory, Location? item = null)
            {
                TryLookUpOrCreate(out LocationID id, data, name, definitionFactory, item);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate{TData}(TData, string, Func{TData, TagDefinition{TID}}, TItem)"/>
            public bool TryLookUpOrCreate<TData>(out LocationID result, TData data, string name, Func<TData, TagDefinition<LocationID>> definitionFactory, Location? item = null)
            {
                if (m_data.LocationStorage.TryLookUpOrCreate(out result, data, name, definitionFactory, item))
                {
                    RegisterLocation(result, item);
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate{TData}(TData, string, Func{TData, TagDefinition{TID}}, Func{TData, TItem})"/>
            public LocationID LookUpOrCreate<TData>(TData data, string name, Func<TData, TagDefinition<LocationID>> definitionFactory, Func<TData, Location> valueFactory) where TData : Game.Data
            {
                TryLookUpOrCreate(out LocationID id, data, name, definitionFactory, valueFactory);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate{TData}(out TID, TData, string, Func{TData, TagDefinition{TID}}, Func{TData, TItem})"/>
            public bool TryLookUpOrCreate<TData>(out LocationID result, TData data, string name, Func<TData, TagDefinition<LocationID>> definitionFactory, Func<TData, Location> valueFactory) where TData : Game.Data
            {
                if (m_data.LocationStorage.TryLookUpOrCreate(out result, data, name, definitionFactory, valueFactory))
                {
                    RegisterLocation(result, m_data.LocationStorage.LookUpValue(result));
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpID"/>
            public bool TryLookUpID(string name, out LocationID id) => m_data.LocationStorage.TryLookUpID(name, out id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpEntry"/>
            public TagStorage<LocationID, Location>.TagEntry LookUpEntry(LocationID id) => m_data.LocationStorage.LookUpEntry(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpName"/>
            public string LookUpName(LocationID id) => m_data.LocationStorage.LookUpName(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpDefinition"/>
            public TagDefinition<LocationID> LookUpDefinition(LocationID id) => m_data.LocationStorage.LookUpDefinition(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpValue"/>
            public Location? LookUpValue(LocationID id) => m_data.LocationStorage.LookUpValue(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpValueChecked"/>
            public Location LookUpValueChecked(LocationID id) => m_data.LocationStorage.LookUpValueChecked(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.SetValue"/>
            public void SetValue(LocationID id, Location item)
            {
                if (m_data.LocationStorage.LookUpValue(id) != null)
                    throw new InvalidOperationException($"Cannot overwrite location; currently not supported! Location name: {LookUpName(id)}");
                if (item == null)
                    throw new NullReferenceException($"Cannot overwrite location with null value; currently not supported! Location name: {LookUpName(id)}");

                m_data.LocationStorage.SetValue(id, item);
                RegisterLocation(id, item);
            }

            /// <summary>
            /// Create a location value for the provided ID. Throws if it already has a value.
            /// </summary>
            /// <param name="id">ID of the location to create a value for</param>
            /// <param name="owningRegions">List of regions the location is in</param>
            /// <param name="randData">Location data for the new location</param>
            /// <param name="item">The item stored in the location, if any</param>
            public void CreateValue(LocationID id, RegionList owningRegions, LocationData randData, ItemID item = new())
                => SetValue(id, new Location(owningRegions, randData, item));

            /// <inheritdoc cref="TagStorage{TID, TItem}.ContainsID(TID)"/>
            public bool ContainsID(LocationID id) => m_data.LocationStorage.ContainsID(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.IsChild(TID, TID)"/>
            public bool IsChild(LocationID child, LocationID parent) => m_data.LocationStorage.IsChild(child, parent);

            /// <inheritdoc cref="TagStorage{TID, TItem}.IsChild(TID, IReadOnlyCollection{TID})"/>
            public bool IsChild(LocationID child, IReadOnlyCollection<LocationID> parents) => m_data.LocationStorage.IsChild(child, parents);

            /// <inheritdoc cref="TagStorage{TID, TItem}.MakeChain"/>
            public LocationID[] MakeChain(LocationID id) => m_data.LocationStorage.MakeChain(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllParents"/>
            public HashSet<LocationID> GetAllParents(IEnumerable<LocationID> ids) => m_data.LocationStorage.GetAllParents(ids);
        }

        /// <summary>
        /// Controlled access to game data's item storage
        /// </summary>
        public struct ItemStorageView
        {
            public ItemStorageView(Game.Data data) => m_data = data;
            private readonly Data m_data;

            /// <summary>
            /// Check / register this new item
            /// </summary>
            private void RegisterItem(ItemID id, Item? item)
            {
                if (m_data.IsComplete)
                    FeatureLogger.Warning($"Late adding / modifying item: {id} \"{LookUpName(id)}\"");

                if (item == null) return;
                // There isn't really anything to do, for now
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.Count"/>
            public int Count => m_data.ItemStorage.Count;

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllIDs"/>
            public IEnumerable<ItemID> GetAllIDs() => m_data.ItemStorage.GetAllIDs();

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllEntries"/>
            public IReadOnlyDictionary<ItemID, TagStorage<ItemID, Item>.TagEntry> GetAllEntries() => m_data.ItemStorage.GetAllEntries();

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllValues"/>
            public IReadOnlyDictionary<ItemID, Item?> GetAllValues() => m_data.ItemStorage.GetAllValues()!;

            /// <inheritdoc cref="TagStorageExtensions.GetAllValuesNonNull"/>
            public IEnumerable<KeyValuePair<ItemID, Item>> GetAllValuesNonNull() => m_data.ItemStorage.GetAllValuesNonNull();

            /// <inheritdoc cref="TagStorage{TID, TItem}.Create"/>
            public ItemID Create(string name, TagDefinition<ItemID> definition, Item? item = null)
            {
                ItemID result = m_data.ItemStorage.Create(name, definition, item);
                RegisterItem(result, item);
                return result;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate(string, Func{TagDefinition{TID}}, TItem})"/>
            public ItemID LookUpOrCreate(string name, Func<TagDefinition<ItemID>> definitionFactory, Item? item = null)
            {
                TryLookUpOrCreate(out ItemID id, name, definitionFactory, item);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate(out TID, string, Func{TagDefinition{TID}}, TItem})"/>
            public bool TryLookUpOrCreate(out ItemID result, string name, Func<TagDefinition<ItemID>> definitionFactory, Item? item = null)
            {
                if (m_data.ItemStorage.TryLookUpOrCreate(out result, name, definitionFactory, item))
                {
                    RegisterItem(result, item);
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate(string, Func{TagDefinition{TID}}, Func{TItem}})"/>
            public ItemID LookUpOrCreate(string name, Func<TagDefinition<ItemID>> definitionFactory, Func<Item> valueFactory)
            {
                TryLookUpOrCreate(out ItemID id, name, definitionFactory, valueFactory);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate(out TID, string, Func{TagDefinition{TID}}, Func{TItem}})"/>
            public bool TryLookUpOrCreate(out ItemID result, string name, Func<TagDefinition<ItemID>> definitionFactory, Func<Item> valueFactory)
            {
                if (m_data.ItemStorage.TryLookUpOrCreate(out result, name, definitionFactory, valueFactory))
                {
                    RegisterItem(result, m_data.ItemStorage.LookUpValue(result));
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate{TData}(TData, string, Func{TData, TagDefinition{TID}}, TItem)"/>
            public ItemID LookUpOrCreate<TData>(TData data, string name, Func<TData, TagDefinition<ItemID>> definitionFactory, Item? item = null)
            {
                TryLookUpOrCreate(out ItemID id, data, name, definitionFactory, item);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate{TData}(TData, string, Func{TData, TagDefinition{TID}}, TItem)"/>
            public bool TryLookUpOrCreate<TData>(out ItemID result, TData data, string name, Func<TData, TagDefinition<ItemID>> definitionFactory, Item? item = null)
            {
                if (m_data.ItemStorage.TryLookUpOrCreate(out result, data, name, definitionFactory, item))
                {
                    RegisterItem(result, item);
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpOrCreate{TData}(TData, string, Func{TData, TagDefinition{TID}}, Func{TData, TItem})"/>
            public ItemID LookUpOrCreate<TData>(TData data, string name, Func<TData, TagDefinition<ItemID>> definitionFactory, Func<TData, Item> valueFactory) where TData : Game.Data
            {
                TryLookUpOrCreate(out ItemID id, data, name, definitionFactory, valueFactory);
                return id;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpOrCreate{TData}(out TID, TData, string, Func{TData, TagDefinition{TID}}, Func{TData, TItem})"/>
            public bool TryLookUpOrCreate<TData>(out ItemID result, TData data, string name, Func<TData, TagDefinition<ItemID>> definitionFactory, Func<TData, Item> valueFactory) where TData : Game.Data
            {
                if (m_data.ItemStorage.TryLookUpOrCreate(out result, data, name, definitionFactory, valueFactory))
                {
                    RegisterItem(result, m_data.ItemStorage.LookUpValue(result));
                    return true;
                }
                return false;
            }

            /// <inheritdoc cref="TagStorage{TID, TItem}.TryLookUpID"/>
            public bool TryLookUpID(string name, out ItemID id) => m_data.ItemStorage.TryLookUpID(name, out id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpEntry"/>
            public TagStorage<ItemID, Item>.TagEntry LookUpEntry(ItemID id) => m_data.ItemStorage.LookUpEntry(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpName"/>
            public string LookUpName(ItemID id) => m_data.ItemStorage.LookUpName(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpDefinition"/>
            public TagDefinition<ItemID> LookUpDefinition(ItemID id) => m_data.ItemStorage.LookUpDefinition(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpValue"/>
            public Item? LookUpValue(ItemID id) => m_data.ItemStorage.LookUpValue(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.LookUpValueChecked"/>
            public Item LookUpValueChecked(ItemID id) => m_data.ItemStorage.LookUpValueChecked(id)!;

            /// <inheritdoc cref="TagStorage{TID, TItem}.SetValue"/>
            //public void SetValue(ItemID id, Item? item) => m_data.ItemStorage.SetValue(id, item);

            /// <inheritdoc cref="TagStorage{TID, TItem}.ContainsID(TID)"/>
            public bool ContainsID(ItemID id) => m_data.ItemStorage.ContainsID(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.IsChild(TID, TID)"/>
            public bool IsChild(ItemID child, ItemID parent) => m_data.ItemStorage.IsChild(child, parent);

            /// <inheritdoc cref="TagStorage{TID, TItem}.IsChild(TID, IReadOnlyCollection{TID})"/>
            public bool IsChild(ItemID child, IReadOnlyCollection<ItemID> parents) => m_data.ItemStorage.IsChild(child, parents);

            /// <inheritdoc cref="TagStorage{TID, TItem}.MakeChain"/>
            public ItemID[] MakeChain(ItemID id) => m_data.ItemStorage.MakeChain(id);

            /// <inheritdoc cref="TagStorage{TID, TItem}.GetAllParents"/>
            public HashSet<ItemID> GetAllParents(IEnumerable<ItemID> ids) => m_data.ItemStorage.GetAllParents(ids);
        }

        /// <summary>
        /// The custom data stored in the region object for this data
        /// </summary>
        private readonly ScopeData GameScopeData;

        /// <summary>
        /// The menu region, which is the origin region
        /// </summary>
        public RegionID Region_Menu { get; private init; }

        /// <summary>
        /// The default filler item. Generally, don't use this
        /// </summary>
        public ItemID Item_Empty { get; private init; }

        /// <summary>
        /// Standard constructor for new data
        /// </summary>
        public Data(MidManager manager)
        {
            GameScopeData = new(manager);
            Region_Menu = Regions.Create(
                "Menu",
                new("The origin region for GTFO; where the player starts, from which all regions must be reachable", this.Region_Always),
                new Region() { RegionData = GameScopeData }
            );
            Item_Empty = Items.LookUpOrCreate(
                "Empty",
                () => new("An item used to balance randomization during fill.", new()),
                () => new Item(new() { IsFiller = true })
            );
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public Data(Data other)
        {
            GameScopeData = other.GameScopeData;
            Region_Menu = other.Region_Menu;
            Item_Empty = other.Item_Empty;
        }

        /// <summary>
        /// If true, data generation is completed for this game
        /// </summary>
        public bool IsComplete { get => GameScopeData.IsComplete; set => GameScopeData.IsComplete = value; }
        
        /// <summary>
        /// The name of the game, uniuqely identifying the item, location, and regionset available
        /// </summary>
        public string? Name { get => GameScopeData.Name; set => GameScopeData.Name = value; } // Unique name set AFTER done processing

        /// <summary>
        /// The manager used to generate this data
        /// </summary>
        public MidManager Manager => GameScopeData.Manager;

        /// <summary>
        /// Regions stored by this game data
        /// </summary>
        public RegionStorageView Regions => new(this);
        private TagStorage<RegionID, Region> RegionStorage => GameScopeData.RegionStorage;

        /// <summary>
        /// Locations stored by this game data
        /// </summary>
        public LocationStorageView Locations => new(this);
        public TagStorage<LocationID, Location> LocationStorage => GameScopeData.LocationStorage;

        /// <summary>
        /// Items stored by this game data
        /// </summary>
        public ItemStorageView Items => new(this);
        public TagStorage<ItemID, Item> ItemStorage => GameScopeData.ItemStorage;

        /// <summary>
        /// Paths stored by this game data
        /// </summary>
        private List<Path> Paths => GameScopeData.Paths;

        /// <summary>
        /// Floating items stored by this game data, currently as tuples along with their regions
        /// </summary>
        private List<(RegionID, ItemID)> FloatingItems => GameScopeData.FloatingItems;

        /// <summary>
        /// Options stored by this game data
        /// </summary>
        private List<OptionBase> Options => GameScopeData.Options;

        /// <summary>
        /// Choice states stored by this game data.
        /// Generally not populated as they're not needed for gameplay.
        /// Choice states can be created <see cref="MidManager.TryComputeChoices(Data)"/>
        /// </summary>
        public ChoiceState[]? Choices
        {
            get => GameScopeData.Choices;
            set => GameScopeData.Choices = value;
        }

        /// <summary>
        /// Set a particular region's reachable status
        /// </summary>
        /// <param name="id">ID of the region</param>
        /// <param name="isReachable">The new value for the region's reachable value</param>
        public void SetRegionReachable(RegionID id, bool isReachable)
            => RegionStorage.SetValue(id, Regions.LookUpValue(id).WithReachable(isReachable));

        /// <summary>
        /// Set a particular region's randomization status
        /// </summary>
        /// <param name="id">ID of the region</param>
        /// <param name="isRandomized">The new value for the region's randomized value</param>
        public void SetRegionRandomized(RegionID id, bool isRandomized)
            => RegionStorage.SetValue(id, Regions.LookUpValue(id).WithRandomized(isRandomized));

        /// <summary>
        /// Add a new path.
        /// </summary>
        /// <param name="path">The path to add</param>
        /// <returns>The ID of the newly-added path</returns>
        public PathID AddPath(Path path)
        {
            if (IsComplete)
            {
                FeatureLogger.Warning($"Adding late path: {path.Name ?? "NO NAME"}");
                FeatureLogger.Warning($"            From: {Regions.LookUpName(path.StartingRegion)}");
                FeatureLogger.Warning($"              To: {Regions.LookUpName(path.EndingRegion)}");
            }

            if (!path.Reqs.IsNone && path.Reqs.Any(r => r.Count == 0))
                FeatureLogger.Warning("Added path with at least one requirement which requires 0 items!");

            if (path.StartingRegion.IsNull)
                throw new ArgumentNullException("Cannot add path; starting region is null!");

            if (path.EndingRegion.IsNull)
                throw new ArgumentNullException("Cannot add path; ending region is null!");

            PathID id = new() { AsIndex = Paths.Count };
            Paths.Add(path);
            RegionStorage.SetValue(path.StartingRegion, Regions.LookUpValue(path.StartingRegion).WithAdded(id));
            return id;
        }

        /// <summary>
        /// Gets all paths currently registered
        /// </summary>
        public IReadOnlyDictionary<PathID, Path> GetAllPaths()
            => new ReadOnlyListDict<PathID, Path>(Paths);

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
        public bool TryLookUpPath(RegionID start, RegionID end, out Path path)
        {
            Region region = Regions.LookUpValue(start);
            path = region.ConnectedPaths
                .Select(LookUpPath)
                .FirstOrDefault(p => p.EndingRegion.Equals(end));
            return !path.IsNull;
        }

        /// <summary>
        /// Get a path by ID
        /// </summary>
        /// <param name="id">The ID of the path</param>
        /// <returns>The found path object</returns>
        public Path LookUpPath(PathID id) => Paths[id.AsIndex];

        /// <summary>
        /// Add requirement to an existing path.
        /// </summary>
        /// <param name="id">The path to modify</param>
        /// <param name="newReq">The new requirement</param>
        /// <remarks>
        /// Used primarily during graph traversal to update direct requirements
        /// </remarks>
        public void AddPathReq(PathID id, Path.PathReq newReq)
        {
            if (IsComplete) FeatureLogger.Warning($"Late path req modification: PathID {id.ID}");
            int index = id.AsIndex;
            Paths[index] = new(Paths[index]) { Reqs = Paths[index].Reqs.WithAdded(newReq) };
        }

        /// <summary>
        /// Add requirement to an existing path.
        /// </summary>
        /// <param name="id">The path to modify</param>
        /// <param name="newReqs">The new requirements</param>
        /// <remarks>
        /// Used primarily during graph traversal to update direct requirements
        /// </remarks>
        public void AddPathReq(PathID id, params Path.PathReq[] newReqs)
        {
            if (IsComplete) FeatureLogger.Warning($"Late path req modification: PathID {id.ID}");
            int index = id.AsIndex;
            Paths[index] = new(Paths[index]) { Reqs = Paths[index].Reqs.WithAdded(newReqs) };
        }

        /// <summary>
        /// Adds an item as a floating item, which can be randomized to any empty location.
        /// The provided scope indicates which region must be randomized for the item to be applicable.
        /// </summary>
        /// <param name="item">The ID of the item to add</param>
        /// <param name="scope">The ID of the region used to enabled/disable the item, or a null ID if always in scope</param>
        public void AddFloatingItem(RegionID scope, ItemID item)
        {
            FloatingItems.Add((scope, item));
        }

        /// <summary>
        /// Get all registered floating item IDs
        /// </summary>
        public IReadOnlyCollection<(RegionID, ItemID)> GetAllFloatingItems() => FloatingItems;

        /// <summary>
        /// Register an option component so users can have an easier time customizing their gameplay.
        /// Note that the duplication check only checks per-instance, and ignores if an identical copy is being registered.
        /// </summary>
        public OptionID AddOption(OptionBase option)
        {
            if (Options.Contains(option))
                throw new ArgumentException("Cannot register duplicate option");
            Options.Add(option);
            return new OptionID { AsIndex = Options.Count - 1 };
        }

        /// <summary>
        /// Get all registered options.
        /// </summary>
        public IReadOnlyDictionary<OptionID, OptionBase> GetAllOptions()
            => new ReadOnlyListDict<OptionID, OptionBase>(Options);

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
            RegionStorage.TrimExcess();
            LocationStorage.TrimExcess();
            ItemStorage.TrimExcess();
            Paths.TrimExcess();
            FloatingItems.TrimExcess();
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
