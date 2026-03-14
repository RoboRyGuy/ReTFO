
using Clonesoft.Json;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ReTFO.Archipelago.ModdedInstanceData.Processors;

using ReTFO.Archipelago.ModdedInstanceData.Model;


public static class Game
{
    /// <summary>
    /// Interface class used by all processors allowing them to be added to Game.Data.
    /// Note that despite the name starting with an I, this actually a class due to technical restraints
    /// </summary>
    public abstract class IProcessor
    {
        /// <summary>
        /// Shared non-generic base for Callback
        /// </summary>
        public abstract class CallbackBase : Attribute 
        {
            public abstract Type DataType { get; }
            public abstract Type DelegateType { get; }
        }

        /// <summary>
        /// Register a callback to this processor's event
        /// </summary>
        /// <param name="callback">The callback to register</param>
        public abstract void UntypedRegisterCallback(Delegate callback);

        /// <summary>
        /// Unregister a callback to this processor's event
        /// </summary>
        /// <param name="callback">The callback to unregister</param>
        public abstract void UntypedUnregisterCallback(Delegate callback);
    }

    /// <summary>
    /// Generic implemenetation of IProcessor allowing it to be cast type-safely
    /// Note that despite the name starting with an I, this actually a class due to technical restraints
    /// </summary>
    /// <typeparam name="TData">The type of data which will be passed to the processing event</typeparam>
    public abstract class IProcessor<TData> : IProcessor where TData : class
    {
        /// <summary>
        /// Attribute used to mark static functions which should autoregister to this processor
        /// </summary>
        [AttributeUsage(AttributeTargets.Method)]
        public class Callback : CallbackBase 
        {
            public override Type DataType => typeof(TData);
            public override Type DelegateType => typeof(Delegate);
        }

        /// <summary>
        /// Delegate type for the event
        /// </summary>
        /// <param name="data">The data to be passed to delegates registered to the processing event</param>
        public delegate void Delegate(TData data);

        /// <summary>
        /// Register a callback to this processor's event
        /// </summary>
        /// <param name="callback">The callback to register</param>
        public abstract void RegisterCallback(Delegate callback);

        public override void UntypedRegisterCallback(System.Delegate callback)
        {
            if (callback is Delegate del)
                RegisterCallback(del);
            else
                FeatureLogger.Error("Failed to register callback; callback is of wrong delegate type");
        }

        /// <summary>
        /// Unregister a callback to this processor's event
        /// </summary>
        /// <param name="callback">The callback to unregister</param>
        public abstract void UnregisterCallback(Delegate callback);

        public override void UntypedUnregisterCallback(System.Delegate callback)
        {
            if (callback is Delegate del)
                UnregisterCallback(del);
            else
                FeatureLogger.Error("Failed to unregister callback; callback is of wrong delegate type");
        }

        /// <summary>
        /// Allow anyone to to invoke processing
        /// </summary>
        /// <param name="data">The data to invoke processing on</param>
        public abstract void Process(TData data);

        /// <summary>
        /// Registers static callabacks marked with the Callback attribute to this event
        /// A helper method intended for use by inherited classes in their constructors
        /// </summary>
        protected virtual void RegisterStaticCallbacks()
        {
            BindingFlags bf = BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;

            var methods = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        return e.Types.OfType<Type>();
                    }
                }).SelectMany(t => t.GetMethods(bf))
                .Where(m => m.CustomAttributes.Any(a => a.AttributeType.IsAssignableTo(typeof(Callback))));

            foreach (var method in methods)
            {
                Delegate? del = Delegate.CreateDelegate(typeof(Delegate), method) as Delegate;
                if (del == null)
                {
                    FeatureLogger.Warning($"Failed to register callback {method.DeclaringType?.FullName}.{method.Name} to event; failed to convert to delegate type");
                    continue;
                }
                RegisterCallback(del);
            }
        }
    }

    /// <summary>
    /// Interface class passed to Game.Event giving acess to common (shared) processing data
    /// </summary>
    public abstract class Data
    {
        // Minimal interface implementation //

        /// <summary>
        /// The Processors registered to this Game.Data
        /// </summary>
        [JsonIgnore]
        public abstract Dictionary<Type, IProcessor> Processors { get; }

        /// <summary>
        /// List of registered regions
        /// </summary>
        public abstract List<Region> RegionList { get; }

        /// <summary>
        /// Region lookup to quickly retrieve regions by name
        /// </summary>
        [JsonIgnore]
        public abstract Dictionary<string, int> RegionLookup { get; }

        /// <summary>
        /// List of registered locations
        /// </summary>
        public abstract List<Location> LocationList { get; }

        /// <summary>
        /// Location lookup to quickly retrieve locations by name
        /// </summary>
        [JsonIgnore]
        public abstract Dictionary<string, Location> LocationLookup { get; }

        /// <summary>
        /// List of registered items
        /// </summary>
        public abstract List<Item> ItemList { get; }

        /// <summary>
        /// List of "floating" items, which are not assigned to a location
        /// This list can contain duplicates, and references the items from ItemList
        /// </summary>
        public abstract List<long> FloatingItemIds { get; }

        /// <summary>
        /// Item lookup to quickly retrieve items by name
        /// </summary>
        [JsonIgnore]
        public abstract Dictionary<string, Item> ItemLookup { get; }

        /// <summary>
        /// Register a processor. Logs a warning if a processor of the given type is already registerd
        /// </summary>
        /// <typeparam name="TData">The data type of the processor being registered</typeparam>
        /// <param name="processor">The processor to register</param>
        public void RegisterProcessor<TData>(IProcessor<TData> processor) where TData : class
        {
            if (!Processors.TryAdd(typeof(TData), processor))
                FeatureLogger.Error($"Attempted to overwrite existing processor type: {typeof(IProcessor<TData>).FullName}");
        }

        /// <summary>
        /// Get a processor for a specific type of data
        /// </summary>
        /// <typeparam name="TData">The type of data the processor is for</typeparam>
        /// <returns>The Typed processor</returns>
        /// <exception cref="NotSupportedException">An incorrectly-registered processor exists in the entry occupied by the requested TData</exception>
        /// <exception cref="KeyNotFoundException">The desired processor was not found</exception>
        public IProcessor<TData> GetProcessor<TData>() where TData : class
        {
            if (!Processors.TryGetValue(typeof(TData), out var processor))
                throw new KeyNotFoundException($"Game.Data does not have a processor for: {typeof(TData).FullName}");

            if (processor is IProcessor<TData> typedProcessor)
                return typedProcessor;
            else
            {
                throw new NotSupportedException(
                    $"Game.Data Processor type incorrectly registered; expected type is {typeof(IProcessor<TData>).FullName}, but actual type is {processor.GetType().FullName}"
                );
            }
        }

        /// <summary>
        /// Get a processor for a specific type of data, without type safety
        /// </summary>
        /// <param name="tData">The type of the data the processor processes</param>
        /// <returns>the processpr</returns>
        public IProcessor GetProcessor(Type tData)
        {
            if (!Processors.TryGetValue(tData, out var processor))
                throw new KeyNotFoundException($"Game.Data does not have a processor for: {tData.FullName}");

            var desiredType = typeof(IProcessor<>).MakeGenericType(new Type[] { tData });
            if (processor.GetType().IsAssignableTo(desiredType))
                return processor;
            else
            {
                throw new NotSupportedException(
                    $"Game.Data Processor type incorrectly registered; expected type is {desiredType.FullName}, but actual type is {processor.GetType().FullName}"
                );
            }
        }

        /// <summary>
        /// Get a region ID. Create a new region if necessary
        /// </summary>
        /// <param name="regionName">The name of th region to get an ID of</param>
        /// <returns>The region with the given ID</returns>
        public int GetOrCreateRegion(string regionName)
        {
            int id;
            if (!RegionLookup.TryGetValue(regionName, out id))
            {
                id = RegionList.Count;
                RegionLookup[regionName] = id;
                RegionList.Add(new Region(regionName));
            }
            return id;
        }

        /// <summary>
        /// Lookup a region by name
        /// </summary>
        /// <param name="name">The namne of the region to lookup</param>
        /// <returns>The Region with the given name</returns>
        /// <exception cref="NullReferenceException">Name is null</exception>
        /// <exception cref="KeyNotFoundException">There is no region with the given name</exception>
        public Region LookupRegion(string name)
            => LookupRegion(RegionLookup[name]);

        /// <summary>
        /// Lookup a region by ID
        /// </summary>
        /// <param name="id">The ID of the region to lookup</param>
        /// <returns>The Region with the given ID</returns>
        /// <exception cref="ArgumentOutOfRangeException">There is no region with the given ID</exception>
        public Region LookupRegion(int id)
            => RegionList[id];

        /// <summary>
        /// Create and add a new path
        /// </summary>
        /// <param name="start">ID of the starting region</param>
        /// <param name="end">ID of the ending region</param>
        /// <returns>The newly-created path</returns>
        public Path AddPath(int start, int end)
        {
            Path path = new(start, end);
            Region region = LookupRegion(start);
            region.ConnectedPaths.Add(path);
            return path;
        }

        /// <summary>
        /// Lookup a path based on start and end region. 
        /// </summary>
        /// <param name="start">The ID of the starting region</param>
        /// <param name="end">The ID of the ending region</param>
        /// <returns>The path</returns>
        public Path LookupPath(int start, int end)
        {
            Region region = LookupRegion(start);
            return region.ConnectedPaths.First(r => r.StartingRegion == start && r.EndingRegion == end);
        }

        /// <summary>
        /// Try to add a location. Logs an error if a location with that name already exists, returning the existing location
        /// </summary>
        /// <param name="name">Name of the new location</param>
        /// <param name="regions">Regions the new location can be found in</param>
        /// <param name="type">The type of the location</param>
        /// <param name="autoDiscover">If true, the region is discovered automatically when all its regions are discovered</param>
        /// <param name="item">The item contained in the region</param>
        /// <returns>Either the new location if added successfully, or the existing location if one exists</returns>
        public virtual Location AddLocation(string name, RegionList regions, eRandomizationType type, bool autoDiscover, Item? item = null)
        {
            Location? location;
            if (!LocationLookup.TryGetValue(name, out location))
            {
                location = new(name, LocationList.Count + 1, regions, type, autoDiscover, item);
                LocationLookup[location.Name] = location;
                LocationList.Add(location);

                if (location.OwningRegionIds.Count == 0)
                    FeatureLogger.Error($"Location is unreachable; not connected to any regions: {name}");
                foreach (var regionId in location.OwningRegionIds)
                    LookupRegion(regionId).ConnectedLocationIds.Add(location.ID);
            }
            else
                FeatureLogger.Error($"Failed to add duplicate location: {name}");
            return location;
        }

        /// <summary>
        /// Fetch a location by name
        /// </summary>
        /// <param name="name">The nameof the location</param>
        /// <returns>The location</returns>
        /// <exception cref="NullReferenceException">If name is null</exception>
        /// <exception cref="KeyNotFoundException">If there is no location with that name</exception>
        public Location LookupLocation(string name)
            => LocationLookup[name];

        /// <summary>
        /// Fetch a location by ID
        /// </summary>
        /// <param name="id">ID of the location</param>
        /// <returns>The location</returns>
        /// <exception cref="ArgumentOutOfRangeException">There is no such location with that ID</exception>
        public Location LookupLocation(long id)
        {
            if (id <= 0 || id > LocationList.Count)
                FeatureLogger.Error("Bad ID during location lookup");
            return checked(LocationList[(int)(id - 1)]);
        }

        /// <summary>
        /// Attempts to register @item and return it. If an item with the same name is already registered, logs an error and returns that one instead
        /// </summary>
        /// <typeparam name="TItem">The type of item to add</typeparam>
        /// <param name="item">The item to add</param>
        /// <returns>The newly-added item (the same as the input), or the existing item if one with the same name already exists</returns>
        public TItem AddItem<TItem>(TItem item)
            where TItem : Item
        {
            if (ItemLookup.TryGetValue(item.Name, out var actual))
            {
                FeatureLogger.Error($"Item {item.Name} is already registered");
                if (actual is TItem typedItem)
                    return typedItem;
                FeatureLogger.Error($"Item {item.Name} is already registered as a different item type");
                return item;
            }

            item.ID = ItemList.Count + 1;
            ItemLookup[item.Name] = item;
            ItemList.Add(item);
            return item;
        }

        /// <summary>
        /// Attempts to register an item and return it. If an item with the same name is already registered and it is the right type, returns that one instead
        /// </summary>
        /// <typeparam name="TItem">The type of the item being registered</typeparam>
        /// <param name="item">The item to register</param>
        /// <returns>The registered item; this is the input if no existing item was found, or an existing item if it exists</returns>
        public TItem GetItem<TItem>(TItem item)
            where TItem : Item
        {
            Item? actual;
            if (ItemLookup.TryGetValue(item.Name, out actual))
            {
                if (actual is TItem typedItem)
                    return typedItem;
                FeatureLogger.Error($"Item {item.Name} is already registered as a different item type");
                return item;
            }

            item.ID = ItemList.Count + 1;
            ItemLookup[item.Name] = item;
            ItemList.Add(item);
            return item;
        }

        /// <summary>
        /// Adds an item as a floating item, meaning it will be given a random empty location during randomization
        /// </summary>
        /// <param name="item">The item to add. This item must be registered</param>
        public void RegisterFloatingItem(Item item)
            => FloatingItemIds.Add(GetItem(item).ID);

        /// <summary>
        /// Fetch an item by name
        /// </summary>
        /// <param name="name">The name of the item</param>
        /// <returns>The item</returns>
        /// <exception cref="NullReferenceException">If name is null</exception>
        /// <exception cref="KeyNotFoundException">If no item is registered for the given name</exception>
        public Item LookupItem(string name)
            => ItemLookup[name];

        /// <summary>
        /// Fetch an item by id
        /// </summary>
        /// <param name="id">The ID of the item</param>
        /// <returns>The item</returns>
        /// <exception cref="ArgumentOutOfRangeException">No such item with the given ID exists</exception>
        public Item LookupItem(long id)
        {
            if (id <= 0 || id > ItemList.Count)
                FeatureLogger.Error("Bad ID during item lookup");
            return checked(ItemList[(int)(id - 1)]);
        }

        /// <summary>
        /// String value to be used for items which do not exist.
        /// Typically used by Paths which are blocked and not unblockable.
        /// </summary>
        public virtual string NotAnItem => "NotAnItem";

        /// <summary>
        /// Name of the very first region in the game.
        /// </summary>
        public virtual string MenuRegionName => "Menu";

        /// <summary>
        /// Used as input to UnstuffPlacements
        /// </summary>
        public struct RegionInfo
        {
            /// <summary>
            /// Index of a region
            /// </summary>
            public int Region;

            /// <summary>
            /// If the region is "bad", or difficult to access.
            /// For example, if the region is a terminal which is locked, or a zone which requires a key.
            /// </summary>
            public bool IsBad;

            public override bool Equals(object? obj) => obj is RegionInfo info && Region == info.Region;
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
        public IEnumerable<List<int>> UnstuffPlacements(IEnumerable<IEnumerable<RegionInfo>> placements, int neededCount)
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
                    yield return new List<int>(group.Key.Append(badRegion).Select(info => info.Region));

                // Fill out the remaining required spots with only the good regions
                while (count-- > 0)
                    yield return group.Key.Select(info => info.Region).ToList();
            }
        }
    }

    /// <summary>
    /// Minimal concrete implementation of Data
    /// </summary>
    protected class BaseData : Data
    {
        /// <summary>
        /// Standard constructor
        /// </summary>
        public BaseData()
        {
            processors = new();
            regionList = new();
            regionLookup = new();
            locationList = new();
            locationLookup = new();
            itemList = new();
            itemLookup = new();
            floatingItemIds = new();
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="source">The Game.Data to copy</param>
        public BaseData(BaseData source)
        {
            processors = source.processors;
            regionList = source.regionList;
            regionLookup = source.regionLookup;
            locationList = source.locationList;
            locationLookup = source.locationLookup;
            itemList = source.itemList;
            itemLookup = source.itemLookup;
            floatingItemIds = source.floatingItemIds;
        }

        // Concretes
        private readonly Dictionary<Type, IProcessor> processors;
        private readonly List<Region> regionList;
        private readonly Dictionary<string, int> regionLookup;
        private readonly List<Location> locationList;
        private readonly Dictionary<string, Location> locationLookup;
        private readonly List<Item> itemList;
        private readonly Dictionary<string, Item> itemLookup;
        private readonly List<long> floatingItemIds;

        // Interface implementation
        public override Dictionary<Type, IProcessor> Processors => processors;
        public override List<Region> RegionList => regionList;
        public override Dictionary<string, int> RegionLookup => regionLookup;
        public override List<Location> LocationList => locationList;
        public override Dictionary<string, Location> LocationLookup => locationLookup;
        public override List<Item> ItemList => itemList;
        public override Dictionary<string, Item> ItemLookup => itemLookup;
        public override List<long> FloatingItemIds => floatingItemIds;
    }

    /// <summary>
    /// Attribute used to mark static functions which should autoregister to this processor
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class Callback : Game.IProcessor<Data>.Callback { }

    /// <summary>
    /// Actual class wrapping an event processing instance
    /// </summary>
    public class Processor : Game.IProcessor<Data>
    {
        public Processor()
            => RegisterStaticCallbacks();

        protected event Delegate? Event = null;

        public override void RegisterCallback(Delegate callback)
            => Event += callback;

        public override void UnregisterCallback(Delegate callback)
            => Event -= callback;

        public override void Process(Data data)
            => Event?.Invoke(data);
    }

    /// <summary>
    /// Allow the creation of game data. This is typically only called by MidManager, which is where data should be obtained from
    /// </summary>
    /// <returns></returns>
    public static Game.Data MakeData()
        => new BaseData();


    extension(Game.Data gameData)
    {
        /// <summary>
        /// Get the Game.Data processor from an instance of Game.Data
        /// </summary>
        public Processor GameProcessor
            => (Processor)gameData.GetProcessor<Data>();
    }
}
