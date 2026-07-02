using BepInEx;
using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.Features.ObjectiveHandlers;
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
        { "os2wxWxv1I5a61i-5BOVTSOZRsPZTKDH_KUTMzoOQdQ=", null } // Vanilla game hash. Null is reserved for vanilla
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
        if (DoGraphTraversal(gameData, true, true))
            FeatureLogger.Success("Game is beatable!");
        else
            FeatureLogger.Fail("Game is not beatable!");

        // Creating the game's name
        using SHA256 sha = SHA256.Create();
        byte[] delim = [ 0 ];

        var strings = Enumerable.Empty<string>()
            .Concat(gameData.Regions.GetAllEntries().Select(r => r.Value.Name))
            .Concat(gameData.Locations.GetAllEntries().Select(r => r.Value.Name))
            .Concat(gameData.Items.GetAllEntries().Select(r => r.Value.Name))
            .Concat(gameData.GetAllPaths().Select(p => p.Value.Name ?? "null"))
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

        DoGraphTraversal(gameData, true, false);

        var dumpData = new
        {
            name = gameData.Name,
            version = Version.Parse(Plugin.Version),
            regions = gameData.Regions.GetAllEntries(),
            locations = gameData.Locations.GetAllEntries(),
            items = gameData.Items.GetAllEntries(),
            paths = gameData.GetAllPaths().ToList(),
            floating_items = gameData.GetAllFloatingItems(),
            options = gameData.GetAllOptions().ToList(),
        };

        JsonSerializerSettings settings = new() { Formatting = Formatting.Indented };
        settings.Converters.Add(new Clonesoft.Json.Converters.StringEnumConverter());
        settings.Converters.Add(new SimplifiedListConverter<long>(20));   // Compress long lists of longs (unpacked IDs) for readability
        settings.Converters.Add(new SimplifiedListConverter<string>(15)); // Compress long lists of strings (Expedition Names) for readability
        settings.Converters.Add(new IdConverter());                       // Convert IDs to longs
        //Type[] containerTypes = [ 
        //    typeof(KeyedRandomizationTag), typeof(KeyedRegion), typeof(ReadOnlyRegion), typeof(KeyedPath), 
        //    typeof(ReadOnlyPath), typeof(KeyedLocation), typeof(KeyedItem), typeof(KeyedOption) 
        //];
        //Type[] inlinedTypes = [ 
        //    typeof(RandomizationTagDefinition), typeof(ReadOnlyRegion), typeof(Region), typeof(ReadOnlyPath), 
        //    typeof(Path), typeof(Location), typeof(Item), typeof(OptionBase) 
        //];
        //settings.Converters.Add(new InlineConverter(containerTypes, inlinedTypes));
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

        string FormatEntry<TID, TItem>(KeyValuePair<TID, TagStorage<TID, TItem>.TagEntry> entry) where TID : struct, ITagID
            => $"\"{entry.Key.ToString()}\","
             + $"\"{entry.Value.Name}\","
             + $"\"{entry.Value.Definition.Description}\","
             + $"\"{entry.Value.Definition.Parent.ToString()}\","
             + $"\"{(entry.Value.Definition.OtherParents == null ? "" : string.Join(", ", entry.Value.Definition.OtherParents.Select(p => p.ToString())))}\"";

        Game.Data data = GetProcessedGameData();
        IEnumerable<string> text = Enumerable.Repeat("\"ID\",\"NAME\",\"DESCRIPTION\",\"PARENT\",\"OTHER_PARENTS\"", 1)
            .Concat(data.Regions.GetAllEntries().Select(FormatEntry))
            .Concat(data.Locations.GetAllEntries().Select(FormatEntry))
            .Concat(data.Items.GetAllEntries().Select(FormatEntry));
        
        File.WriteAllLines(filename, text);
        FeatureLogger.Success($"Tags saved to: {filename}");
    }

    /// <summary>
    /// Struct used to help serialize tags for JSON (for hierarchal viewing)
    /// </summary>
    [DataContract]
    private struct JsonTag<TID> where TID : struct, ITagID
    {
        public JsonTag(TID id, string name, string description, TID[]? otherParents, List<JsonTag<TID>>? children)
        {
            ID = id;
            Name = name;
            Description = description;
            OtherParents = otherParents;
            Children = children;
        }

        [DataMember(Name = "id")] public TID ID { get; private init; }
        [DataMember(Name = "name")] public string Name { get; private init; }
        [DataMember(Name = "description")] public string Description { get; private init; }
        [DataMember(Name = "other_parents", EmitDefaultValue = false)] public TID[]? OtherParents { get; private init; }
        private List<JsonTag<TID>>? m_children;
        [DataMember(Name = "children", EmitDefaultValue = false)] public List<JsonTag<TID>>? Children
        { 
            get => m_children; 
            private init => m_children = (value?.Count ?? 0) == 0 ? null : value; 
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

        List<JsonTag<TID>> MakeJsonList<TID, TItem>(IReadOnlyDictionary<TID, TagStorage<TID, TItem>.TagEntry> storage) where TID : struct, ITagID
        {
            Dictionary<TID, List<TID>> tagsByParent = new();
            foreach (var entry in storage)
            {
                foreach (var parent in entry.Value.Definition.AllParents)
                {
                    if (tagsByParent.TryGetValue(parent, out var children))
                        children.Add(entry.Key);
                    else
                        tagsByParent.Add(parent, new List<TID>() { entry.Key });
                }
            }

            // Helper to create hierarchal structure
            List<JsonTag<TID>> MakeJsonRecursive(TID parentTag)
            {
                return (tagsByParent.GetValueOrDefault(parentTag, null!) ?? Enumerable.Empty<TID>())
                    .Select(id => KeyValuePair.Create(id, storage[id]))
                    .Select(t => new JsonTag<TID>(
                        t.Key, 
                        t.Value.Name, 
                        t.Value.Definition.Description, 
                        t.Value.Definition.OtherParents == null ? null : t.Value.Definition.AllParents.Where(p => !p.Equals(parentTag)).ToArray(),
                        MakeJsonRecursive(t.Key)
                    )).ToList();
            }

            return MakeJsonRecursive(new TID());
        }

        // Output to file as JSON
        var obj = new
        {
            Regions = MakeJsonList(data.Regions.GetAllEntries()),
            Locations = MakeJsonList(data.Locations.GetAllEntries()),
            Items = MakeJsonList(data.Items.GetAllEntries()),
        };
        JsonSerializerSettings settings = new() { Formatting = Formatting.Indented };
        settings.Converters.Add(new IdConverter());
        string json = JsonConvert.SerializeObject(obj, settings);
        File.WriteAllText(filename, json);
        FeatureLogger.Success($"Tags saved to: {filename}");
    }

    /// <summary>
    /// Region data used during graph traversal to perform updates and do checks
    /// </summary>
    private struct ExtendedRegionData
    {
        public ExtendedRegionData() { }
        public ExtendedRegionData(ExtendedRegionData source) 
        {
            IsReachable = source.IsReachable;
            UsedItemCounts = source.UsedItemCounts;
            UsedCategoryCounts = source.UsedCategoryCounts;
        }
        public bool IsReachable = false;
        public SortedList<ItemID, uint>? UsedItemCounts = null;
        public SortedList<ItemID, uint>? UsedCategoryCounts = null;
    }

    /// <summary>
    /// Performs a graph traversal on the provided Game.Data, optionally performing processing / modifications along the way
    /// </summary>
    /// <param name="gameData">The Game.Data data to traverse</param>
    /// <param name="doProcessing">If true, overwrite the region reachability. Also calculates and add direct path requirements if necessary</param>
    /// <param name="logDebugInfo">If true and the Game.Data is not beatable, log info describing the stuck state to help debug why it's considered unbeatable</param>
    /// <returns>True if the Game.Data can be fully traversed (all goal items reachable), false otherwise</returns>
    /// <remarks>All floating items are collected immediately with the assumption they'll be placed somewhere reachable</remarks>
    public static bool DoGraphTraversal(Game.Data gameData, bool doProcessing = false, bool logDebugInfo = true)
    {
        // Count of which items have been obtained by exact item count
        Dictionary<ItemID, uint> itemCounts = new();

        // Count of which items have been obtained by exact category
        Dictionary<ItemID, uint> categoryCounts = new();

        // Extended region data used for performing processing
        List<ExtendedRegionData> regionData = Enumerable.Repeat<ExtendedRegionData>(new(), gameData.Regions.GetAllEntries().Count).ToList();

        // Paths queued for checking
        List<PathID> queuedPaths = new();

        // If processing, we can reset all regions' reachability
        if (doProcessing)
        {
            foreach (var entry in gameData.Regions.GetAllEntries())
                gameData.SetRegionReachable(entry.Key, false);
        }

        // Collect an item by ID
        void collectItem(ItemID id)
        {
            if (id.IsNull) 
                return;

            Item? item = gameData.Items.LookUpValue(id);
            if (item == null)
            {
                FeatureLogger.Error($"Attempted to collect {id} during traversal, but this is not an actual item! Item name: {gameData.Items.LookUpName(id)}");
                return;
            }
            if (!item.RandData.IsProgression) return; // Only progression items can be considered for path reqs

            // One copy goes into item counts, and another copy into cateogry + one for each parent recursively
            itemCounts[id] = itemCounts.GetValueOrDefault(id, 0u) + 1u;
            void collectRecursive(ItemID id)
            {
                if (id.IsNull) return;
                categoryCounts[id] = categoryCounts.GetValueOrDefault(id, 0u) + 1u;
                foreach (var parent in gameData.Items.LookUpDefinition(id).AllParents)
                    collectRecursive(parent);
            }
            collectRecursive(id);
        }

        // Abstractions for getting / setting reachability
        bool getReachable(RegionID id) => doProcessing ? gameData.Regions.LookUpValue(id).Reachable : regionData[id.AsIndex].IsReachable;
        void setReachable(RegionID id) { if (doProcessing) gameData.SetRegionReachable(id, true); else regionData[id.AsIndex] = new(regionData[id.AsIndex]) { IsReachable = true }; }

        // Item helpers
        uint itemCount(ItemID item) => itemCounts.GetValueOrDefault(item, 0u);
        uint catsCount(ItemID item) => categoryCounts.GetValueOrDefault(item, 0u);

        uint usedItemCount(RegionID region, ItemID item) => gameData.IsComplete ? 0u : regionData[region.AsIndex].UsedItemCounts?.GetValueOrDefault(item, 0u) ?? 0u;
        uint usedCatsCount(RegionID region, ItemID item) => gameData.IsComplete ? 0u : regionData[region.AsIndex].UsedCategoryCounts?.GetValueOrDefault(item, 0u) ?? 0u;

        // Floating items are auto-collected - we assumed they'll be randomized somewhere they are logically guaranteed reachable
        // For simplicity, we just collect all items
        foreach (var pair in gameData.GetAllFloatingItems())
            collectItem(pair.Item2);

        // Starting state
        setReachable(gameData.Region_Menu);
        Region startRegion = gameData.Regions.LookUpValue(gameData.Region_Menu);

        // Collecting items in the starting region
        foreach (Location? location in startRegion.ConnectedLocations.Select(gameData.Locations.LookUpValueChecked))
        {
            if ((location?.OwningRegionIDs.Count) == 1 && !location!.ItemID.IsNull) 
                collectItem(location.ItemID);
        }

        queuedPaths.AddRange(startRegion.ConnectedPaths);

        // Traversal iterations
        int newCount = 1; // Number of regions found
        while (newCount > 0)
        {
            newCount = 0;
            for (int i = 0; i < queuedPaths.Count; i++)
            {
                // Whether it's worth checking this path
                Path path = gameData.LookUpPath(queuedPaths[i]);
                if (getReachable(path.EndingRegion))
                {
                    queuedPaths.RemoveAt(i--);
                    continue;
                }

                // Checks if a specific req item for the current path can be satisfied
                bool checkTraversable(Path.RequiredItem req, uint reqCount)
                {
                    if (req.Type == Path.RequiredItem.eType.None) return true;
                    else if (req.Type == Path.RequiredItem.eType.Blocked) return false;

                    // Fetch counts from relevant dicts
                    uint availableCount, usedCount;
                    (availableCount, usedCount) = req.Type switch
                    {
                        Path.RequiredItem.eType.Item         => (itemCount(req.Target), usedItemCount(path.StartingRegion, req.Target)),
                        Path.RequiredItem.eType.ItemConsumed => (itemCount(req.Target), usedItemCount(path.StartingRegion, req.Target)),
                        Path.RequiredItem.eType.Category     => (catsCount(req.Target), usedCatsCount(path.StartingRegion, req.Target)),
                        _ => throw new NotSupportedException($"Unexpected path requirement type: {(int)req.Type}")
                    };

                    if (availableCount < (reqCount + usedCount))
                        return false; // Insufficient items to pass

                    // Perform processing if relevant / necessary
                    if (!gameData.IsComplete)
                    {
                        // Updating used counts
                        ExtendedRegionData newData = new(regionData[path.StartingRegion.AsIndex]);
                        if (req.Type == Path.RequiredItem.eType.ItemConsumed)
                        {
                            // Update both dicts with the consumed counts
                            newData.UsedItemCounts = newData.UsedItemCounts == null ? new(1) : new(newData.UsedItemCounts);
                            newData.UsedItemCounts[req.Target] = usedCount + reqCount;

                            var usedCatsDict = newData.UsedCategoryCounts = newData.UsedCategoryCounts == null ? new() : new(newData.UsedCategoryCounts);
                            void updateUsedRecursive(ItemID id)
                            {
                                if (id.IsNull) return;
                                usedCatsDict[id] = usedCatsDict.GetValueOrDefault(id, 0u) + reqCount;
                                foreach (var parent in gameData.Items.LookUpDefinition(id).AllParents)
                                    updateUsedRecursive(parent);
                            }
                            updateUsedRecursive(req.Target);
                        }
                        regionData[path.EndingRegion.AsIndex] = newData;

                        // Update the path's req count directly
                        if (doProcessing)
                            gameData.SetPathReqCount(queuedPaths[i], usedCount + reqCount);
                    }
                    return true;
                }

                if (checkTraversable(path.AlternateItem, 1u) || checkTraversable(path.ReqItem, path.ReqCount))
                {
                    setReachable(path.EndingRegion);
                    ++newCount;
                    Region endingRegion = gameData.Regions.LookUpValue(path.EndingRegion);

                    // Collect all locations newly available because of this region
                    foreach (var loc in endingRegion.ConnectedLocations.Select(gameData.Locations.LookUpValueChecked))
                    {
                        if (loc!.OwningRegionIDs.Any(id => !getReachable(id))) continue;
                        collectItem(loc.ItemID);
                    }

                    // Queue all new paths available because of this region
                    queuedPaths.AddRange(endingRegion.ConnectedPaths);

                    // Finally, remove the queued path
                    queuedPaths.RemoveAt(i--);
                }
            }
        }

        // ----------------------------------------------------------------------------------------

        // We've stopped making progress. Check if we've won!
        // @Todo: Can't use HashSet, we need multiset. Too lazy to implement right now
        Dictionary<ItemID, uint> requiredItems = gameData.Locations.GetAllEntries()
            .Where(pair => pair.Value.Value != null && !pair.Value.Value.ItemID.IsNull)
            .Select(pair => (pair.Value.Value!.OwningRegionIDs.AsEnumerable(), pair.Value.Value.ItemID))
            .Concat(gameData.GetAllFloatingItems().Select(pair => (Enumerable.Repeat(pair.Item1, 1), pair.Item2)))
            .GroupBy(pair => pair.Item2)
            .Where(group => gameData.Items.IsChild(group.Key, gameData.Item_SectorClears))
            .ToDictionary(group => group.Key, group => (uint)group.Count());

        foreach (var pair in itemCounts.Concat(categoryCounts))
        {
            if (requiredItems.TryGetValue(pair.Key, out uint count))
            {
                if (count > pair.Value) requiredItems[pair.Key] = count - pair.Value;
                else requiredItems.Remove(pair.Key);
            }
        }

        if (requiredItems.Count == 0) return true;
        if (!logDebugInfo) return false;

        // "Pretty" formatting for debugging
        FeatureLogger.Error($"Graph traversal failed for game!");

        // ----------------------------------------------------------------------------------------

        ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
        ConsoleManager.ConsoleStream.WriteLine($"\n    Missing Item{(requiredItems.Count > 1 ? "s" : "")} Required for Completion:");

        ConsoleManager.SetConsoleColor(ConsoleColor.White);
        foreach (var item in requiredItems)
            ConsoleManager.ConsoleStream.WriteLine($"  - {item.Value:00}x {gameData.Items.LookUpName(item.Key)}");

        // ----------------------------------------------------------------------------------------

        ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
        ConsoleManager.ConsoleStream.WriteLine("\n    Regions:");

        bool printed = false;
        foreach (var pair in gameData.Regions.GetAllEntries())
        {
            bool reachable = getReachable(pair.Key);
            if (reachable) ConsoleManager.SetConsoleColor(ConsoleColor.Green);
            else ConsoleManager.SetConsoleColor(ConsoleColor.Red);
            ConsoleManager.ConsoleStream.WriteLine($"  {(reachable ? "[ Reachable ]" : "[Unreachable]")} [{pair.Key.ID:000}] {pair.Value.Name}");
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
            printed = true;
            Path path = gameData.LookUpPath(pathID);
            ConsoleManager.ConsoleStream.WriteLine();
            ConsoleManager.ConsoleStream.WriteLine($"  Name:  {path.Name ?? "None"}");
            ConsoleManager.SetConsoleColor(ConsoleColor.Green);
            ConsoleManager.ConsoleStream.WriteLine($"  Start: [{path.StartingRegion.ID:000}] {gameData.Regions.LookUpName(path.StartingRegion)}");
            ConsoleManager.SetConsoleColor(ConsoleColor.Red);
            ConsoleManager.ConsoleStream.WriteLine($"  End:   [{path.EndingRegion.ID:000}] {gameData.Regions.LookUpName(path.EndingRegion)}");
            ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
            if (path.ReqItem.Type == Path.RequiredItem.eType.None)
                ConsoleManager.ConsoleStream.WriteLine($"  No required item");
            else if (path.ReqItem.Type == Path.RequiredItem.eType.Item || path.ReqItem.Type == Path.RequiredItem.eType.ItemConsumed)
                ConsoleManager.ConsoleStream.WriteLine($"  Item:  {usedItemCount(path.StartingRegion, path.ReqItem.Target) + path.ReqCount:000}x {gameData.Items.LookUpName(path.ReqItem.Target)}");
            else if (path.ReqItem.Type == Path.RequiredItem.eType.Category)
                ConsoleManager.ConsoleStream.WriteLine($"  Cats:  {usedCatsCount(path.StartingRegion, path.ReqItem.Target) + path.ReqCount:000}x {gameData.Items.LookUpName(path.ReqItem.Target)}");
            if (path.AlternateItem.Type != Path.RequiredItem.eType.Blocked)
                if (path.AlternateItem.Type == Path.RequiredItem.eType.None)
                    ConsoleManager.ConsoleStream.WriteLine($"  Alt:   Not blocked");
                else
                    ConsoleManager.ConsoleStream.WriteLine($"  Alt:   001x {gameData.Items.LookUpName(path.AlternateItem.Target)}");

            bool printed2 = false;
            ConsoleManager.ConsoleStream.WriteLine("\n  Potential unfound items which unblock:");
            foreach (var entry in gameData.Locations.GetAllEntries())
            {
                if (entry.Value.Value == null) continue;
                if (entry.Value.Value.ItemID.IsNull) continue;
                if (entry.Value.Value.OwningRegionIDs.All(getReachable)) continue; // Item was already collected

                bool meetsMain = path.ReqItem.Type switch
                {
                    Path.RequiredItem.eType.None => true,
                    Path.RequiredItem.eType.Blocked => false,
                    Path.RequiredItem.eType.Item => path.ReqItem.Target.Equals(entry.Value.Value.ItemID),
                    Path.RequiredItem.eType.ItemConsumed => path.ReqItem.Target.Equals(entry.Value.Value.ItemID),
                    Path.RequiredItem.eType.Category => gameData.Items.IsChild(entry.Value.Value.ItemID, path.ReqItem.Target),
                    _ => throw new NotSupportedException("Unexpected path req type!"),
                };

                bool meetsAlt = path.AlternateItem.Type switch
                {
                    Path.RequiredItem.eType.None => true,
                    Path.RequiredItem.eType.Blocked => false,
                    Path.RequiredItem.eType.Item => path.AlternateItem.Target.Equals(entry.Value.Value.ItemID),
                    Path.RequiredItem.eType.ItemConsumed => path.AlternateItem.Target.Equals(entry.Value.Value.ItemID),
                    Path.RequiredItem.eType.Category => gameData.Items.IsChild(entry.Value.Value.ItemID, path.AlternateItem.Target),
                    _ => throw new NotSupportedException("Unexpected path req type!"),
                };

                if (meetsMain || meetsAlt)
                {
                    printed2 = true;
                    ConsoleManager.ConsoleStream.WriteLine();
                    if (meetsMain && meetsAlt) ConsoleManager.ConsoleStream.WriteLine($"    Reqs: Main, Alt");
                    else if (meetsMain) ConsoleManager.ConsoleStream.WriteLine($"    Reqs: Main");
                    else if (meetsAlt) ConsoleManager.ConsoleStream.WriteLine($"    Reqs: Alt");
                    ConsoleManager.ConsoleStream.WriteLine($"    Item: [{entry.Value.Value.ItemID.ID}] {gameData.Items.LookUpName(entry.Value.Value.ItemID)}");
                    ConsoleManager.ConsoleStream.WriteLine($"    Loc:  [{entry.Key.ID}] {gameData.Locations.LookUpName(entry.Key)}");

                    ConsoleManager.ConsoleStream.WriteLine($"    Regions:");
                    if (entry.Value.Value.OwningRegionIDs.Count == 0)
                    {
                        ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                        ConsoleManager.ConsoleStream.WriteLine("    LOCATION HAS NO REGIONS AND CANNOT BE DISCOVERED");
                    }
                    else foreach (var i in entry.Value.Value.OwningRegionIDs)
                    {
                        bool reachable = getReachable(i);
                        if (reachable) ConsoleManager.SetConsoleColor(ConsoleColor.Green);
                        else ConsoleManager.SetConsoleColor(ConsoleColor.Red);
                        ConsoleManager.ConsoleStream.WriteLine($"     {(reachable ? "[ Reachable ]" : "[Unreachable]")} [{i.ID:000}] {gameData.Regions.LookUpName(i)}");
                    }
                    ConsoleManager.SetConsoleColor(ConsoleColor.Yellow);
                }
            }
            if (!printed2) ConsoleManager.ConsoleStream.WriteLine("\n    NO ITEMS FOUND");
        }
        if (!printed) ConsoleManager.ConsoleStream.WriteLine($"\n  NO BLOCKED PATHS FOUND");

        ConsoleManager.ConsoleStream.WriteLine();
        return false;
    }
}
