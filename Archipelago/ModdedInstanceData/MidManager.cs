using BepInEx;
using Clonesoft.Json;
using Clonesoft.Json.Serialization;
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ReTFO.Archipelago.ModdedInstanceData;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// Wraps modded instance data; creates it and manages its lifetime.
/// Also manages the processors needed to generate modded instance data.
/// </summary>
public class MidManager
{
    /// <summary>
    /// Interface class used by all processors allowing them to be added to the MidManager
    /// </summary>
    public abstract class Processor
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
    /// </summary>
    /// <typeparam name="TData">The type of data which will be passed to the processing event</typeparam>
    public abstract class Processor<TData> : Processor where TData : class
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

    public bool IsProcessed { get; protected set; } = false;
    public bool IsProcessing { get; protected set; } = false;
    protected Dictionary<Type, Processor> m_processorLookup { get; set; } = new();
    protected Game.Data? m_gameData { get; set; } = null;
    protected Game.Processor m_gameProcessor { get; set; } = new();

    public MidManager()
    {
        RegisterProcessor(m_gameProcessor);
    }

    /// <summary>
    /// Register a processor. Logs a warning if a processor of the given type is already registerd
    /// </summary>
    /// <typeparam name="TData">The data type of the processor being registered</typeparam>
    /// <param name="processor">The processor to register</param>
    public void RegisterProcessor<TData>(Processor<TData> processor) where TData : class
    {
        if (!m_processorLookup.TryAdd(typeof(TData), processor))
        {
            Processor existingProcessor = m_processorLookup[typeof(TData)];
            FeatureLogger.Warning(
                $"Cannot add new processor of type: {processor.GetType().Name}; "
                + $"an existing processor of type {existingProcessor.GetType().Name} is already registered for that data type!"
            );
        }
    }

    /// <summary>
    /// Get all registered processors by data type
    /// </summary>
    /// <returns>All the registered processors</returns>
    public IReadOnlyDictionary<Type, Processor> GetAllProcessors() => m_processorLookup;

    /// <summary>
    /// Get a processor for a specific type of data
    /// </summary>
    /// <typeparam name="TData">The type of data the processor is for</typeparam>
    /// <returns>The Typed processor</returns>
    /// <exception cref="NotSupportedException">An incorrectly-registered processor exists in the entry occupied by the requested TData</exception>
    /// <exception cref="KeyNotFoundException">The desired processor was not found</exception>
    public Processor<TData> GetProcessor<TData>() where TData : class
    {
        if (m_processorLookup.TryGetValue(typeof(TData), out Processor? genericProcessor))
        {
            if (genericProcessor is Processor<TData> typedProcessor) return typedProcessor;
            else throw new InvalidCastException($"Cannot converted processor of type {genericProcessor.GetType().Name} to {typeof(Processor<TData>).FullName}");
        }
        else
            throw new KeyNotFoundException($"There is no processor for the requested data type: {typeof(TData).FullName}");
    }

    /// <summary>
    /// Get a processor for a specific type of data, without type safety
    /// </summary>
    /// <param name="tData">The type of the data the processor processes</param>
    /// <returns>the processpr</returns>
    public Processor GetProcessor(Type tData)
    {
        if (m_processorLookup.TryGetValue(tData, out Processor? genericProcessor))
        {
            Type targetType = typeof(Processor<>).MakeGenericType(tData);
            if (targetType.IsAssignableFrom(genericProcessor.GetType())) return genericProcessor;
            else throw new InvalidCastException($"Cannot converted processor of type {genericProcessor.GetType().FullName} to {targetType.FullName}");
        }
        else
            throw new KeyNotFoundException($"There is no processor for the requested data type: {tData.FullName}");
    }

    /// <summary>
    /// Invalidate the current modded instance data, if there is any.
    /// This can cause issues if the GameData datablocks are not properly regenerated
    /// </summary>
    public void InvalidateModdedInstanceData()
    {
        if (IsProcessing) throw new NotSupportedException("Attempted to invalidate modded instance data while it's processing!");
        IsProcessed = false;
        m_gameData = null;
    }

    /// <summary>
    /// Get the current game data without invoking processing. Useful for adding processors, etc
    /// </summary>
    public Game.Data GetUnprocessedGameData()
    {
        if (m_gameData != null) 
            return m_gameData;
        else
            return m_gameData = new(this);
    }

    /// <summary>
    /// Get the current game data. Invoke processing if unprocessed.
    /// </summary>
    public Game.Data GetProcessedGameData()
    {
        ProcessData();
        return GetUnprocessedGameData();
    }

    /// <summary>
    /// Process the contained game data now. Please only do this as needed
    /// </summary>
    public void ProcessData()
    {
        if (IsProcessed) return;
        else if (IsProcessing) throw new NotSupportedException("Process data request received while already processing!");

        IsProcessing = true;
        
        var gameData = GetUnprocessedGameData();
        m_gameProcessor.Process(gameData);

        DoGraphTraversal(gameData, true, null, null, true);
        
        IsProcessing = false;
        IsProcessed = true;

        // We've most likely touched these blocks, so we're going to mark them dirty. Not sure if this really does anything
        RundownDataBlock.FileDirty = true;
        LevelLayoutDataBlock.FileDirty = true;
        WardenObjectiveDataBlock.FileDirty = true;
        DimensionDataBlock.FileDirty = true;
        TextDataBlock.FileDirty = true;

        // Finally, we can trim up some of our lists
        gameData.CleanUp();
    }

    // This little bit is stolen straight from https://stackoverflow.com/a/21953690
    [DllImport("shell32", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, nint hToken = 0);
    private static Guid DownloadsGUID => new("374DE290-123F-4565-9164-39C4925E467B");

    // A json contract resolver which blocks serialization of extension properties
    // Note that this is a somewhat lazy resolver with the assumption we won't be serializing much or often
    private class NoExtensionsContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyType != null && !property.Ignored)
            {
                if (property.PropertyType.IsAssignableTo(typeof(Game.Data)) && property.PropertyType != typeof(Game.Data))
                    property.Ignored = true;
            }

            return property;
        }
    }

    /// <summary>
    /// Export game data as a JSON file to the designated path.
    /// </summary>
    /// <param name="filename">
    /// The full path of the file to export to.
    /// If null, defaults to a file in the downloads folder.
    /// </param>
    public void ExportGameData(string? filename = null)
    {
        if (filename == null)
            filename = System.IO.Path.Combine(SHGetKnownFolderPath(DownloadsGUID, 0), "moddedInstanceData.json");

        Game.Data gameData = GetProcessedGameData();
        JsonSerializerSettings settings = new()
        {
            Formatting = Formatting.Indented,
        };
        settings.ContractResolver = new NoExtensionsContractResolver();
        settings.Converters.Add(new Clonesoft.Json.Converters.StringEnumConverter());
        settings.Converters.Add(new ListConverter<int>(20));
        settings.Converters.Add(new ListConverter<long>(15));
        string json = JsonConvert.SerializeObject(gameData, settings);
        File.WriteAllText(filename, json);
    }

    /// <summary>
    /// Performs a graph traversal on the provided Game.Data, optionally performing processing / modifications along the way
    /// </summary>
    /// <param name="gameData">The Game.Data data to traverse</param>
    /// <param name="doProcessing">If true, overwrite the region reachability. Also calculates and add direct path requirements if necessary</param>
    /// <param name="logDebugInfo">If true and the Game.Data is not beatable, log info describing the stuck state to help debug why it's considered unbeatable</param>
    /// <param name="unlockTags">List of whitelist tags matching floating items required to clear the game; these items will be added to the starting item pool</param>
    /// <param name="goalTags">List of whitelist tags matching items that are required for the game to be considered "beatable"</param>
    /// <returns>True if the Game.Data can be fully traversed (all goal items reachable), false otherwise</returns>
    public static bool DoGraphTraversal(Game.Data gameData, bool doProcessing = false, ICollection<RandomizationTag>? unlockTags = null, ICollection<RandomizationTag>? goalTags = null, bool logDebugInfo = true)
    {
        // Handle default values
        unlockTags ??= [ gameData.Tag_UnlockItems ] ;
        goalTags ??= [ gameData.Tag_GoalItems ];

        // Progress tracking
        // List of items that have been collected
        List<Item> collectedItems = new();

        // Counts by tag for items requiring an "item" name
        Dictionary<RandomizationTag, int> itemCounts = new();

        // Counts by tag for items requiring a "category" name
        Dictionary<RandomizationTag, int> categoryCounts = new();

        // Used item and category counts for each region
        // In the tuple, the first is the item count, and the second is the category count
        List<Tuple<SortedList<RandomizationTag, int>?, SortedList<RandomizationTag, int>?>> usedItemsPerRegion = gameData.IsComplete ? new(0)
            : Enumerable.Repeat<Tuple<SortedList<RandomizationTag, int>?, SortedList<RandomizationTag, int>?>>(Tuple.Create<SortedList<RandomizationTag, int>?, SortedList<RandomizationTag, int>?>(null, null), gameData.GetAllRegions().Count).ToList();

        // IsReachable lookup for each region. Used when doProcessing is false
        List<bool> isReachable = doProcessing ? null!
            : Enumerable.Repeat(false, gameData.GetAllRegions().Count).ToList();

        // Paths queued for checking
        List<PathID> queuedPaths = new();

        // If processing, we can reset all path's reachability
        if (doProcessing)
            foreach (var pair in gameData.GetAllRegions()) gameData.SetRegionReachable(pair.Key, false);

        // Reachability for a reagion
        void collectItem(ItemID id)
        {
            if (id.IsNull) return;
            Item item = gameData.LookupItem(id);
            if (!item.RandData.IsProgression) return; // Only progression items can be considered for path reqs
            collectedItems.Add(item);
            Path.RequiredItem req = item.PathReqs;
            if (req.Type == Path.RequiredItem.eType.None) return;
            else if (req.Type == Path.RequiredItem.eType.Item) itemCounts[req.Target] = itemCount(req.Target) + 1;
            else if (req.Type == Path.RequiredItem.eType.Category) categoryCounts[req.Target] = catsCount(req.Target) + 1;
        }

        bool getReachable(RegionID id) => doProcessing ? gameData.LookupRegion(id).Reachable : isReachable[id.AsIndex];
        void setReachable(RegionID id) { if (doProcessing) gameData.SetRegionReachable(id, true); else isReachable[id.AsIndex] = true; }

        // Item helpers
        int itemCount(RandomizationTag tag) => itemCounts.GetValueOrDefault(tag, 0);
        int catsCount(RandomizationTag tag) => categoryCounts.GetValueOrDefault(tag, 0);

        int usedItemCount(RegionID region, RandomizationTag tag) => gameData.IsComplete ? 0 : usedItemsPerRegion[region.AsIndex].Item1?.GetValueOrDefault(tag, 0) ?? 0;
        int usedCatsCount(RegionID region, RandomizationTag tag) => gameData.IsComplete ? 0 : usedItemsPerRegion[region.AsIndex].Item2?.GetValueOrDefault(tag, 0) ?? 0;

        int calcCount(RegionID region, Path.RequiredItem item)
        {
            if (item.Type == Path.RequiredItem.eType.None) return 0;
            else if (item.Type == Path.RequiredItem.eType.Item)
                return itemCount(item.Target) - usedItemCount(region, item.Target);
            else
                return catsCount(item.Target) - usedCatsCount(region, item.Target);
        }

        // Starting state
        RegionID startingRegionID = gameData.MenuRegion;
        ReadOnlyRegion startingRegion = gameData.LookupRegion(startingRegionID);
        setReachable(startingRegionID);

        foreach (ItemID id in gameData.GetAllFloatingItemIds())
        {
            Item item = gameData.LookupItem(id);
            RandomizationTagDefinition def1 = gameData.LookupTagDef(item.NameTag);
            RandomizationTagDefinition def2 = item.Tag2.IsNull ? new() : gameData.LookupTagDef(item.Tag2);

            if (def1.Name.Contains("Expedition Unlock")) { }

            if (gameData.AnyTagMatches(unlockTags, gameData.LookupItem(id)))
                collectItem(id);
        }

        foreach (Location location in startingRegion.ConnectedLocationIds.Select(gameData.LookupLocation))
            if (location.OwningRegionIds.Length == 1 && !location.ItemID.IsNull) collectItem(location.ItemID);

        queuedPaths.AddRange(startingRegion.ConnectedPaths);

        // Traversal iterations
        int newCount = 1; // Number of regions found
        while (newCount > 0)
        {
            newCount = 0;
            for (int i = 0; i < queuedPaths.Count; i++)
            {
                // Whether it's worth checking this path
                ReadOnlyPath path = gameData.LookupPath(queuedPaths[i]);
                if (getReachable(path.EndingRegion))
                {
                    queuedPaths.RemoveAt(i--);
                    continue;
                }

                // Checking if path is traversable
                int mainCount = calcCount(path.StartingRegion, path.ReqItem);
                int alternateCount = calcCount(path.StartingRegion, path.AlternateItem);

                if ((path.ReqItem.Type == Path.RequiredItem.eType.None) || (mainCount > 0) || (alternateCount > 0))
                {
                    setReachable(path.EndingRegion);
                    ++newCount;
                    queuedPaths.AddRange(gameData.LookupRegion(path.EndingRegion).ConnectedPaths);

                    // Since this is the first time we're here, mark the direct requirements
                    if (!gameData.IsComplete)
                    {
                        if (path.ReqItem.Type != Path.RequiredItem.eType.None)
                        {
                            Path.RequiredItem reqItem;
                            int reqCount;
                            if (mainCount > 0)
                            {
                                reqItem = path.ReqItem;
                                reqCount = (int)path.ReqCount;
                            }
                            else
                            {
                                reqItem = path.AlternateItem;
                                reqCount = 1;
                            }

                            SortedList<RandomizationTag, int> dict = new();
                            SortedList<RandomizationTag, int>? oldDict;

                            if (reqItem.Type == Path.RequiredItem.eType.Item)
                            {
                                oldDict = usedItemsPerRegion[path.StartingRegion.AsIndex].Item1;
                                usedItemsPerRegion[path.EndingRegion.AsIndex] = Tuple.Create(dict, usedItemsPerRegion[path.StartingRegion.AsIndex].Item2)!;
                            }
                            else if (reqItem.Type == Path.RequiredItem.eType.Category)
                            {
                                oldDict = usedItemsPerRegion[path.StartingRegion.AsIndex].Item2;
                                usedItemsPerRegion[path.EndingRegion.AsIndex] = Tuple.Create(usedItemsPerRegion[path.StartingRegion.AsIndex].Item1, dict)!;
                            }
                            else throw new NotSupportedException("Expected path req to be Item or Category!");

                            foreach (var pair in oldDict ?? Enumerable.Empty<KeyValuePair<RandomizationTag, int>>())
                                dict.Add(pair.Key, pair.Value);
                            dict[reqItem.Target] = dict.GetValueOrDefault(reqItem.Target, 0) + reqCount;
                        }
                        else
                            usedItemsPerRegion[path.EndingRegion.AsIndex] = usedItemsPerRegion[path.StartingRegion.AsIndex];

                        if (doProcessing && path.ReqItem.Type != Path.RequiredItem.eType.None)
                        {
                            uint count;
                            if (path.ReqItem.Type == Path.RequiredItem.eType.Item) count = (uint)usedItemCount(path.EndingRegion, path.ReqItem.Target);
                            else if (path.ReqItem.Type == Path.RequiredItem.eType.Category) count = (uint)usedCatsCount(path.EndingRegion, path.ReqItem.Target);
                            else throw new NotSupportedException("Expected path req to be Item or Category!");
                            gameData.SetPathReqCount(queuedPaths[i], count);
                        }
                    }

                    // Collect all locations newly available because of this region
                    foreach (var loc in gameData.LookupRegion(path.EndingRegion).ConnectedLocationIds.Select(gameData.LookupLocation))
                    {
                        if (loc.OwningRegionIds.Any(id => !getReachable(id))) continue;
                        collectItem(loc.ItemID);
                    }

                    // Finally, remove the queued path
                    queuedPaths.RemoveAt(i--);
                }
            }
        }

        // ----------------------------------------------------------------------------------------

        // We've stopped making progress. Check if we've won!
        if (doProcessing) gameData.IsComplete = true;

        // @Todo: Can't use HashSet, we need multiset. Too lazy to implement right now
        List<Item> requiredItems = gameData.GetAllLocations()
            .Select(pair => pair.Value.ItemID)
            .Where(id => !id.IsNull)
            .Select(gameData.LookupItem)
            .Where(item => gameData.AnyTagMatches(goalTags, item))
            .ToList();
        foreach (var item in collectedItems) 
            requiredItems.Remove(item); // Intentionally ignore cases where there is no such item to remove

        // "Pretty" formatting for debugging
        if (requiredItems.Count == 0) return true;
        if (!logDebugInfo) return false;

        if (!logDebugInfo) return false;

        FeatureLogger.Error($"Graph traversal failed for game!");

        // ----------------------------------------------------------------------------------------

        ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
        ConsoleManager.ConsoleStream.WriteLine($"\n    Missing Item{(requiredItems.Count > 1 ? "s" : "")} Required for Completion:");

        ConsoleManager.SetConsoleColor(ConsoleColor.White);
        foreach (var item in requiredItems)
            ConsoleManager.ConsoleStream.WriteLine($"  - {gameData.LookupTagDef(item.NameTag).Name}");

        // ----------------------------------------------------------------------------------------

        ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
        ConsoleManager.ConsoleStream.WriteLine("\n    Regions:");

        bool printed = false;
        foreach (var pair in gameData.GetAllRegions())
        {
            bool reachable = getReachable(pair.Key);
            if (reachable) ConsoleManager.SetConsoleColor(ConsoleColor.Green);
            else ConsoleManager.SetConsoleColor(ConsoleColor.Red);
            ConsoleManager.ConsoleStream.WriteLine($"  {(reachable ? "[ Reachable ]" : "[Unreachable]")} [{pair.Key.Value.ToString("000")}] {pair.Value.Name}");
            printed = true;
        }
        if (!printed) ConsoleManager.ConsoleStream.WriteLine($"\n  NO REGIONS FOUND");

        // ----------------------------------------------------------------------------------------

        ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
        ConsoleManager.ConsoleStream.WriteLine("\n    Blocked paths:");
        PathID[] sortedPaths = new PathID[queuedPaths.Count];
        queuedPaths.CopyTo(sortedPaths);
        Array.Sort(sortedPaths);

        printed = false;
        foreach (var pathID in sortedPaths)
        {
            ReadOnlyPath path = gameData.LookupPath(pathID);
            ConsoleManager.ConsoleStream.WriteLine();
            ConsoleManager.ConsoleStream.WriteLine($"  Name:  {path.Name ?? "None"}");
            ConsoleManager.SetConsoleColor(ConsoleColor.Green);
            ConsoleManager.ConsoleStream.WriteLine($"  Start: [{path.StartingRegion.Value.ToString("000")}] {gameData.LookupRegion(path.StartingRegion).Name}");
            ConsoleManager.SetConsoleColor(ConsoleColor.Red);
            ConsoleManager.ConsoleStream.WriteLine($"  End:   [{path.EndingRegion.Value.ToString("000")}] {gameData.LookupRegion(path.EndingRegion).Name}");
            ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
            if (path.ReqItem.Type == Path.RequiredItem.eType.None)
                ConsoleManager.ConsoleStream.WriteLine($"  No required item!");
            else if (path.ReqItem.Type == Path.RequiredItem.eType.Item)
                ConsoleManager.ConsoleStream.WriteLine($"  Item:  {(usedItemCount(path.StartingRegion, path.ReqItem.Target) + path.ReqCount).ToString("000")}x {gameData.LookupTagDef(path.ReqItem.Target).Name}");
            if (path.ReqItem.Type == Path.RequiredItem.eType.Category)
                ConsoleManager.ConsoleStream.WriteLine($"  Cats:  {(usedCatsCount(path.StartingRegion, path.ReqItem.Target) + path.ReqCount).ToString("000")}x {gameData.LookupTagDef(path.ReqItem.Target).Name}");
            if (path.AlternateItem.Type != Path.RequiredItem.eType.None)
                ConsoleManager.ConsoleStream.WriteLine($"  Alt:   001x {gameData.LookupTagDef(path.AlternateItem.Target).Name}");
            printed = true;
        }
        if (!printed) ConsoleManager.ConsoleStream.WriteLine($"\n  NO BLOCKED PATHS FOUND");

        // ----------------------------------------------------------------------------------------

        ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
        ConsoleManager.ConsoleStream.WriteLine("\n    Notable unfound locations:");

        HashSet<RandomizationTag> neededTags = queuedPaths.Select(gameData.LookupPath).SelectMany(p =>
            Enumerable.Empty<RandomizationTag>().Append(p.ReqItem.Target).Append(p.AlternateItem.Target)
        ).ToHashSet();
            
        printed = false;
        foreach (var loc in gameData.GetAllLocations().Select(pair => pair.Value))
        {
            if (loc.ItemID.IsNull) continue;
            Item item = gameData.LookupItem(loc.ItemID);
            if (!gameData.AnyTagMatches(neededTags, item)) continue;
            if (loc.OwningRegionIds.All(getReachable)) continue;

            ConsoleManager.ConsoleStream.WriteLine();
            ConsoleManager.ConsoleStream.WriteLine($"  Name: {gameData.LookupTagDef(loc.NameTag).Name}");
            ConsoleManager.ConsoleStream.WriteLine($"  Item: {gameData.LookupTagDef(item.NameTag).Name}");
            if (!item.Tag2.IsNull)
                ConsoleManager.ConsoleStream.WriteLine($"   Cat: {gameData.LookupTagDef(item.Tag2).Name}");
            if (!item.Tag3.IsNull)
                ConsoleManager.ConsoleStream.WriteLine($"   Cat: {gameData.LookupTagDef(item.Tag3).Name}");

            ConsoleManager.ConsoleStream.WriteLine($"  Regions:");
            if (loc.OwningRegionIds.Length == 0)
            {
                ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                ConsoleManager.ConsoleStream.WriteLine("  LOCATION HAS NO REGIONS AND CANNOT BE DISCOVERED");
            }
            else foreach (var i in loc.OwningRegionIds)
            {
                bool reachable = getReachable(i);
                if (reachable) ConsoleManager.SetConsoleColor(ConsoleColor.Green);
                else ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                ConsoleManager.ConsoleStream.WriteLine($"   {(reachable ? "[ Reachable ]" : "[Unreachable]")} [{i.Value.ToString("000")}] {gameData.LookupRegion(i).Name}");
            }
            ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
            printed = true;
        }

        if (neededTags.Count == 0)
            ConsoleManager.ConsoleStream.WriteLine($"\n  NO NEEDED ITEMS FOUND");
        else if (!printed)
            ConsoleManager.ConsoleStream.WriteLine($"\n  NO NOTABLE LOCATIONS FOUND");

        ConsoleManager.ConsoleStream.WriteLine();
        return false;
    }
}
