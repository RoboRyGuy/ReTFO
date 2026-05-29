using BepInEx;
using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

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
        /// <remarks>
        /// This has been removed because it has rightfully been identified as unecessary and 
        ///  it trips malware checks.
        /// </remarks>
        //protected virtual void RegisterStaticCallbacks()
        //{
        //    BindingFlags bf = BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;
        //
        //    var methods = AppDomain.CurrentDomain
        //        .GetAssemblies()
        //        .SelectMany(a =>
        //        {
        //            try
        //            {
        //                return a.GetTypes();
        //            }
        //            catch (ReflectionTypeLoadException e)
        //            {
        //                return e.Types.OfType<Type>();
        //            }
        //        }).SelectMany(t => t.GetMethods(bf))
        //        .Where(m => m.CustomAttributes.Any(a => a.AttributeType.IsAssignableTo(typeof(Callback))));
        //
        //    foreach (var method in methods)
        //    {
        //        Delegate? del = Delegate.CreateDelegate(typeof(Delegate), method) as Delegate;
        //        if (del == null)
        //        {
        //            FeatureLogger.Warning($"Failed to register callback {method.DeclaringType?.FullName}.{method.Name} to event; failed to convert to delegate type");
        //            continue;
        //        }
        //        RegisterCallback(del);
        //    }
        //}
    }

    public bool IsProcessed { get; protected set; } = false;
    public bool IsProcessing { get; protected set; } = false;
    protected Dictionary<Type, Processor> m_processorLookup { get; set; } = new();
    protected Game.Data? m_gameData { get; set; } = null;
    protected Game.Processor m_gameProcessor { get; set; } = new();
    protected Dictionary<string, string?> m_namedHashes { get; set; } = new() 
    { 
        { "W1TND_RuXefq1sntwN0vbGUtYcLtZEybhigDPRdtSQo=", null } // Vanilla game hash. Null is reserved for vanilla
    };

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
    /// Names a specific hash.
    /// When game data is processed, if the resulting hash used to identify it
    ///  matches a name in the named hash dictionary, it will use that name instead.
    /// </summary>
    /// <remarks>
    /// This will overwrite existing hashes. This is generally frowned on because
    ///  it decreases compatibility between games, but is allowed with the understanding
    ///  that the developer choosing to do so likely knows better than this hash.
    /// </remarks>
    /// <param name="hash">The hash to name</param>
    /// <param name="name">The name to use instead</param>
    public void AddNamedHash(string hash, string name) => m_namedHashes[hash] = name;

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
    /// Process the contained game data immediately. Please only do this as needed
    /// </summary>
    public void ProcessData()
    {
        if (IsProcessed) return;
        else if (IsProcessing) throw new NotSupportedException("Process data request received while already processing!");

        FeatureLogger.Notice("Beginning MID generation");
        IsProcessing = true;

        // Get all our entities
        var gameData = GetUnprocessedGameData();
        Event.Processor processor = gameData.EventProcessor;
        m_gameProcessor.Process(gameData);
        gameData.CleanUp(); // Trims lists

        // Check that the game is winnable and such
        FeatureLogger.Notice("Checking for winability");
        if (DoGraphTraversal(gameData, true, null, true))
            FeatureLogger.Success("Game is beatable!");
        else
            FeatureLogger.Fail("Game is not beatable!");

        // Creating the game's name
        using SHA256 sha = SHA256.Create();
        byte[] delim = [ 0 ];

        var strings = Enumerable.Empty<string>()
            .Concat(gameData.GetAllExpeditions().Select(e => e.Key))
            .Concat(gameData.GetAllRegions().Select(r => r.Value.Name))
            .Concat(gameData.GetAllPaths().Select(p => p.Value.Name ?? "null"))
            .Concat(gameData.GetAllTags().Select(t => t.Value.Name))
        ;
        foreach (string s in strings)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            sha.TransformBlock(delim, 0, delim.Length, null, 0);
        }

        sha.TransformFinalBlock(delim, 0, 0);
        string hash = Convert.ToBase64String(sha.Hash!).Replace('/', '_').Replace('+', '-');
        if (m_namedHashes.TryGetValue(hash, out var name))
            gameData.Name = name;
        else
            gameData.Name = hash.Substring(0, 10);

        // Marking the data complete
        // This is used during graph traversal to ensure the path reqs are calculated and
        //  to start logging warnings if modifications are made past this point
        gameData.IsComplete = true;

        IsProcessing = false;
        IsProcessed = true;
        FeatureLogger.Notice("MID generation completed!");
        FeatureLogger.Notice($"World Hash: {hash}");
        if (gameData.Name == null)
            FeatureLogger.Notice("World is Vanilla");
        else
            FeatureLogger.Notice($"World Name: {gameData.Name}");

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

    /// <summary>
    /// Export game data as a JSON file to the designated path.
    /// </summary>
    /// <param name="directory">
    /// The full path of the directory to export to.
    /// If null, defaults to the downloads folder.
    /// </param>
    public void ExportMidData(string? directory = null)
    {
        Game.Data gameData = GetProcessedGameData();

        if (directory == null)
            directory = SHGetKnownFolderPath(DownloadsGUID, 0);
        string filename = System.IO.Path.Combine(directory, gameData.Name == null ? "GTFO.ini" : $"GTFO-{gameData.Name}.ini");

        DoGraphTraversal(gameData, true, null, false);

        // Identify regions rechable by all possible expeditions
        List<MidExpeditionData> eData = new();
        HashSet<RegionID> reachableRegionIds = new();
        Queue<PathID> queuedPaths = new();

        foreach (var pair in gameData.GetAllExpeditions())
        {
            reachableRegionIds.Clear();
            queuedPaths.Clear();

            reachableRegionIds.Add(pair.Value.StartingRegion);
            foreach (var path in gameData.LookupRegion(reachableRegionIds.First()).ConnectedPaths)
                queuedPaths.Enqueue(path);

            while (queuedPaths.Count > 0)
            {
                var path = gameData.LookupPath(queuedPaths.Dequeue());
                if (reachableRegionIds.Contains(path.EndingRegion)) continue;
                
                var newRegion = gameData.LookupRegion(path.EndingRegion);
                if (!newRegion.Reachable) continue;
                
                reachableRegionIds.Add(path.EndingRegion);
                foreach (var id in newRegion.ConnectedPaths) 
                    queuedPaths.Enqueue(id);
            }

            eData.Add(new() { Name = pair.Key, ReachableRegions = reachableRegionIds.ToList() });
        }

        var dumpData = new
        {
            name = gameData.Name,
            version = Version.Parse(Plugin.Version),
            expeditions = eData,
            tags = gameData.GetAllTags().Select(t => new KeyedRandomizationTag(t.Key, t.Value)).ToList(),
            regions = gameData.GetAllRegions().Select(r => new KeyedRegion(r.Key, r.Value)).ToList(),
            paths = gameData.GetAllPaths().Select(p => new KeyedPath(p.Key, p.Value)).ToList(),
            locations = gameData.GetAllLocations().Select(l => new KeyedLocation(l.Key, l.Value)).ToList(),
            items = gameData.GetAllItems().Select(i => new KeyedItem(i.Key, i.Value)).ToList(),
            floating_items = gameData.GetAllFloatingItemIds(),
            options = gameData.GetAllOptions().Select(o => new KeyedOption(o.Key, o.Value)).ToList(),
        };

        JsonSerializerSettings settings = new() { Formatting = Formatting.Indented };
        settings.Converters.Add(new Clonesoft.Json.Converters.StringEnumConverter());
        settings.Converters.Add(new SimplifiedListConverter<long>(20));   // Compress long lists of longs (unpacked IDs) for readability
        settings.Converters.Add(new SimplifiedListConverter<string>(15)); // Compress long lists of strings (Expedition Names) for readability
        settings.Converters.Add(new IdConverter());                       // Convert IDs to longs
        Type[] containerTypes = [ 
            typeof(KeyedRandomizationTag), typeof(KeyedRegion), typeof(ReadOnlyRegion), typeof(KeyedPath), 
            typeof(ReadOnlyPath), typeof(KeyedLocation), typeof(KeyedItem), typeof(KeyedOption) 
        ];
        Type[] inlinedTypes = [ 
            typeof(RandomizationTagDefinition), typeof(ReadOnlyRegion), typeof(Region), typeof(ReadOnlyPath), 
            typeof(Path), typeof(Location), typeof(Item), typeof(OptionBase) 
        ];
        settings.Converters.Add(new InlineConverter(containerTypes, inlinedTypes));
        string json = JsonConvert.SerializeObject(dumpData, settings);
        File.WriteAllText(filename, json);
        FeatureLogger.Success($"MID data saved to: {filename}");
    }

    /// <summary>
    /// Export tags as a CSV to the designated path.
    /// </summary>
    /// <param name="filename">
    /// The full path of the file to export to.
    /// If null, defaults to a file in the downloads folder.
    /// </param>
    public void ExportTagsToCSV(string? filename = null)
    {
        if (filename == null)
            filename = System.IO.Path.Combine(SHGetKnownFolderPath(DownloadsGUID, 0), "gtfoTags.csv");

        IEnumerable<string> text = Enumerable.Repeat("\"ID\",\"NAME\",\"PARENT\",\"DESCRIPTION\"", 1)
            .Concat(GetProcessedGameData().GetAllTags().Select(pair => $"\"{pair.Key.AsId.ToString()}\"\"{pair.Value.Name}\",\"{pair.Value.Parent}\",\"{pair.Value.Description}\""));
        
        File.WriteAllLines(filename, text);
        FeatureLogger.Success($"Tags saved to: {filename}");
    }

    /// <summary>
    /// Struct used to help serialize tags for JSON (for hierarchal viewing)
    /// </summary>
    [DataContract]
    private struct JsonTag
    {
        [DataMember(Name = "id")] public RandomizationTag ID { get; init; }
        [DataMember(Name = "name")] public string Name { get; init; }
        [DataMember(Name = "description")] public string Description { get; init; }
        private List<JsonTag>? m_children;
        [DataMember(Name = "children", EmitDefaultValue = false)] public List<JsonTag>? Children
        { 
            get => m_children; 
            init => m_children = (value?.Count ?? 0) == 0 ? null : value; 
        }
    }

    /// <summary>
    /// Export tags as a JSON to the designated path
    /// </summary>
    /// <param name="filename">
    /// The full path of the file to export to.
    /// If null, defaults to a file in the downloads folder.
    /// </param>
    public void ExportTagsToJSON(string? filename = null)
    {
        if (filename == null)
            filename = System.IO.Path.Combine(SHGetKnownFolderPath(DownloadsGUID, 0), "gtfoTags.json");

        Game.Data data = GetProcessedGameData();

        // Create lookup to greatly accelerate this process
        Dictionary<RandomizationTag, List<RandomizationTag>?> tagsByParent = new();
        foreach (var tag in data.GetAllTags())
        {
            if (tagsByParent.TryGetValue(tag.Value.Parent, out var list))
                list!.Add(tag.Key);
            else
                tagsByParent.Add(tag.Value.Parent, new() { tag.Key });
        }

        // Helper to create hierarchal structure
        List<JsonTag> MakeJsonRecursive(RandomizationTag parentTag)
        {
            return (tagsByParent.GetValueOrDefault(parentTag, null) ?? Enumerable.Empty<RandomizationTag>())
                .Select(t => new KeyedRandomizationTag(t, data.LookupTagDef(t)))
                .Select(t => new JsonTag()
                {
                    ID = t.ID,
                    Name = t.Definition.Name,
                    Description = t.Definition.Description,
                    Children = MakeJsonRecursive(t.ID)
                }).ToList();
        }

        // Output to file as JSON
        var obj = new { Tags = MakeJsonRecursive(new RandomizationTag()) };
        JsonSerializerSettings settings = new() { Formatting = Formatting.Indented };
        settings.Converters.Add(new IdConverter());
        string json = JsonConvert.SerializeObject(obj, settings);
        File.WriteAllText(filename, json);
        FeatureLogger.Success($"Tags saved to: {filename}");
    }

    /// <summary>
    /// Performs a graph traversal on the provided Game.Data, optionally performing processing / modifications along the way
    /// </summary>
    /// <param name="gameData">The Game.Data data to traverse</param>
    /// <param name="doProcessing">If true, overwrite the region reachability. Also calculates and add direct path requirements if necessary</param>
    /// <param name="expeditions">The list of expeditions to test for traversal. If null, includes all expeditions</param>
    /// <param name="logDebugInfo">If true and the Game.Data is not beatable, log info describing the stuck state to help debug why it's considered unbeatable</param>
    /// <returns>True if the Game.Data can be fully traversed (all goal items reachable), false otherwise</returns>
    /// <remarks>All floating items are collected immediately with the assumption they'll be placed somewhere reachable</remarks>
    public static bool DoGraphTraversal(Game.Data gameData, bool doProcessing = false, ICollection<Expedition.Data>? expeditions = null, bool logDebugInfo = true)
    {
        // Handle default values
        expeditions ??= gameData.GetAllExpeditions().Select(e => e.Value).ToHashSet(new Expedition.Data.Comparer());

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
            if (item.RequiredExpedition != null && !expeditions.Contains(item.RequiredExpedition)) return; // This item is not considered part of the expedition set
            collectedItems.Add(item);
            Path.RequiredItem req = item.PathReqs;
            if (req.Type == Path.RequiredItem.eType.None) return;
            else if (req.Type == Path.RequiredItem.eType.Item) itemCounts[req.Target] = itemCount(req.Target) + 1;
            else if (req.Type == Path.RequiredItem.eType.Category) categoryCounts[req.Target] = catsCount(req.Target) + 1;
        }

        // Abstractions for getting / setting reachability
        bool getReachable(RegionID id) => doProcessing ? gameData.LookupRegion(id).Reachable : isReachable[id.AsIndex];
        void setReachable(RegionID id) { if (doProcessing) gameData.SetRegionReachable(id, true); else isReachable[id.AsIndex] = true; }

        // Item helpers
        int itemCount(RandomizationTag tag) => itemCounts.GetValueOrDefault(tag, 0);
        int catsCount(RandomizationTag tag) => categoryCounts.GetValueOrDefault(tag, 0);

        int usedItemCount(RegionID region, RandomizationTag tag) => gameData.IsComplete ? 0 : usedItemsPerRegion[region.AsIndex].Item1?.GetValueOrDefault(tag, 0) ?? 0;
        int usedCatsCount(RegionID region, RandomizationTag tag) => gameData.IsComplete ? 0 : usedItemsPerRegion[region.AsIndex].Item2?.GetValueOrDefault(tag, 0) ?? 0;

        // Starting state
        RegionID startingRegionID = gameData.MenuRegion;
        ReadOnlyRegion startingRegion = gameData.LookupRegion(startingRegionID);
        setReachable(startingRegionID);

        // Floating items are auto-collected
        foreach (ItemID id in gameData.GetAllFloatingItemIds())
            collectItem(id);

        // Collecting items in the starting region
        foreach (Location location in startingRegion.ConnectedLocationIds.Select(gameData.LookupLocation))
            if (location.OwningRegionIDs.Length == 1 && !location.ItemID.IsNull) collectItem(location.ItemID);

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
                int mainCount;
                if (path.ReqItem.Type == Path.RequiredItem.eType.Item)
                    mainCount = itemCount(path.ReqItem.Target) - usedItemCount(path.StartingRegion, path.ReqItem.Target);
                else if (path.ReqItem.Type == Path.RequiredItem.eType.Category)
                    mainCount = catsCount(path.ReqItem.Target) - usedCatsCount(path.StartingRegion, path.ReqItem.Target);
                else mainCount = 0;

                int alternateCount ;
                if (path.AlternateItem.Type == Path.RequiredItem.eType.Item)
                    alternateCount = itemCount(path.AlternateItem.Target);
                else if (path.AlternateItem.Type == Path.RequiredItem.eType.Category)
                    alternateCount = catsCount(path.AlternateItem.Target);
                else alternateCount = 0;

                if ((path.ReqItem.Type == Path.RequiredItem.eType.None) || (mainCount >= path.ReqCount) || (alternateCount >= 1))
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
                            if (mainCount >= path.ReqCount)
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
                            else // if (reqItem.Type == Path.RequiredItem.eType.Category)
                            {
                                oldDict = usedItemsPerRegion[path.StartingRegion.AsIndex].Item2;
                                usedItemsPerRegion[path.EndingRegion.AsIndex] = Tuple.Create(usedItemsPerRegion[path.StartingRegion.AsIndex].Item1, dict)!;
                            }

                            foreach (var pair in oldDict ?? Enumerable.Empty<KeyValuePair<RandomizationTag, int>>())
                                dict.Add(pair.Key, pair.Value);
                            dict[reqItem.Target] = dict.GetValueOrDefault(reqItem.Target, 0) + reqCount;
                        }
                        else
                            usedItemsPerRegion[path.EndingRegion.AsIndex] = usedItemsPerRegion[path.StartingRegion.AsIndex];

                        if (doProcessing && path.ReqItem.Type != Path.RequiredItem.eType.None)
                        {
                            uint count;
                            if (path.ReqItem.Type == Path.RequiredItem.eType.Item) 
                                count = (uint)usedItemCount(path.StartingRegion, path.ReqItem.Target);
                            else // if (path.ReqItem.Type == Path.RequiredItem.eType.Category) 
                                count = (uint)usedCatsCount(path.StartingRegion, path.ReqItem.Target);
                            gameData.SetPathReqCount(queuedPaths[i], count + path.ReqCount);
                        }
                    }

                    // Collect all locations newly available because of this region
                    foreach (var loc in gameData.LookupRegion(path.EndingRegion).ConnectedLocationIds.Select(gameData.LookupLocation))
                    {
                        if (loc.OwningRegionIDs.Any(id => !getReachable(id))) continue;
                        collectItem(loc.ItemID);
                    }

                    // Finally, remove the queued path
                    queuedPaths.RemoveAt(i--);
                }
            }
        }

        // ----------------------------------------------------------------------------------------

        // We've stopped making progress. Check if we've won!
        // @Todo: Can't use HashSet, we need multiset. Too lazy to implement right now
        List<Item> requiredItems = gameData.GetAllLocations()
            .Select(pair => pair.Value.ItemID)
            .Concat(gameData.GetAllFloatingItemIds())
            .Where(id => !id.IsNull)
            .Select(gameData.LookupItem)
            .Where(i => i.RequiredExpedition == null || expeditions.Contains(i.RequiredExpedition))
            .Where(item => gameData.TagMatches(gameData.Tag_GoalItems, item))
            .ToList();
        foreach (var item in collectedItems) 
            requiredItems.Remove(item); // Intentionally ignore cases where there is no such item to remove

        if (requiredItems.Count == 0) return true;
        if (!logDebugInfo) return false;

        // "Pretty" formatting for debugging
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
            ConsoleManager.ConsoleStream.WriteLine($"  {(reachable ? "[ Reachable ]" : "[Unreachable]")} [{pair.Key.AsId.ToString("000")}] {pair.Value.Name}");
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
            ConsoleManager.ConsoleStream.WriteLine($"  Start: [{path.StartingRegion.AsId.ToString("000")}] {gameData.LookupRegion(path.StartingRegion).Name}");
            ConsoleManager.SetConsoleColor(ConsoleColor.Red);
            ConsoleManager.ConsoleStream.WriteLine($"  End:   [{path.EndingRegion.AsId.ToString("000")}] {gameData.LookupRegion(path.EndingRegion).Name}");
            ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
            if (path.ReqItem.Type == Path.RequiredItem.eType.None)
                ConsoleManager.ConsoleStream.WriteLine($"  No required item!");
            else if (path.ReqItem.Type == Path.RequiredItem.eType.Item)
                ConsoleManager.ConsoleStream.WriteLine($"  Item:  {(usedItemCount(path.StartingRegion, path.ReqItem.Target) + path.ReqCount).ToString("000")}x {gameData.LookupTagDef(path.ReqItem.Target).Name}");
            else //(path.ReqItem.Type == Path.RequiredItem.eType.Category)
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
            if (loc.OwningRegionIDs.All(getReachable)) continue;
            Item item = gameData.LookupItem(loc.ItemID);
            if (!gameData.TagMatches(neededTags, item.PathReqs.Target)) continue;

            ConsoleManager.ConsoleStream.WriteLine();
            ConsoleManager.ConsoleStream.WriteLine($"  Name: {gameData.LookupTagDef(loc.NameTag).Name}");
            ConsoleManager.ConsoleStream.WriteLine($"  Item: {gameData.LookupTagDef(item.NameTag).Name}");
            if (!item.Tag2.IsNull)
                ConsoleManager.ConsoleStream.WriteLine($"   Cat: {gameData.LookupTagDef(item.Tag2).Name}");
            if (!item.Tag3.IsNull)
                ConsoleManager.ConsoleStream.WriteLine($"   Cat: {gameData.LookupTagDef(item.Tag3).Name}");

            ConsoleManager.ConsoleStream.WriteLine($"  Regions:");
            if (loc.OwningRegionIDs.Length == 0)
            {
                ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                ConsoleManager.ConsoleStream.WriteLine("  LOCATION HAS NO REGIONS AND CANNOT BE DISCOVERED");
            }
            else foreach (var i in loc.OwningRegionIDs)
            {
                bool reachable = getReachable(i);
                if (reachable) ConsoleManager.SetConsoleColor(ConsoleColor.Green);
                else ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                ConsoleManager.ConsoleStream.WriteLine($"   {(reachable ? "[ Reachable ]" : "[Unreachable]")} [{i.AsId.ToString("000")}] {gameData.LookupRegion(i).Name}");
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
