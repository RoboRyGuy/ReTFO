using Clonesoft.Json;
using GameData;
using ReTFO.Archipelago.Features.ObjectiveHandlers;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace ReTFO.Archipelago.ModdedInstanceData;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using static DebugDraw3D;

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
    protected Dictionary<Type, Processor> m_processorLookup { get; init; } = new();
    protected Game.Data? m_gameData { get; set; } = null;
    protected Game.Processor m_gameProcessor { get; set; } = new();
    protected Dictionary<string, string?> m_namedHashes { get; init; } = new() 
    { 
        { "xddj0OpT14z2lmo9dBfHD-BSASJtIvF7kAXMww7F0pM=", null } // Vanilla game hash. Null is reserved for vanilla
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

    [DataContract]
    private struct JsonKeyValue<T, U>
    {
        public JsonKeyValue(T id, U value)
        {
            ID = id;
            Value = value;
        }

        [DataMember(Name = "id")]
        public T ID { get; init; }

        [DataMember(Name = "value")]
        public U Value { get; init; }
    }

    private static JsonKeyValue<T, U> MakeJsonKeyValue<T, U>(KeyValuePair<T, U> source)
        => new(source.Key, source.Value);

    [DataContract]
    private struct FloatingItem
    {
        public FloatingItem(RegionID region, ItemID item)
        {
            Region = region;
            Item = item;
        }

        [DataMember(Name = "region")]
        public RegionID Region { get; init; }

        [DataMember(Name = "item")]
        public ItemID Item { get; init; }

        public static FloatingItem Make((RegionID, ItemID) pair)
            => new(pair.Item1, pair.Item2);
    }

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

        DoGraphTraversal(gameData);

        var dumpData = new
        {
            name = gameData.Name,
            version = Version.Parse(Plugin.Version),
            regions = gameData.Regions.GetAllEntries().Select(MakeJsonKeyValue),
            locations = gameData.Locations.GetAllEntries().Select(MakeJsonKeyValue),
            items = gameData.Items.GetAllEntries().Select(MakeJsonKeyValue),
            paths = gameData.GetAllPaths().Select(MakeJsonKeyValue),
            floating_items = gameData.GetAllFloatingItems().Select(FloatingItem.Make),
            options = gameData.GetAllOptions().Select(MakeJsonKeyValue),
        };

        JsonSerializerSettings settings = new() { Formatting = Formatting.Indented };
        settings.Converters.Add(new Clonesoft.Json.Converters.StringEnumConverter());
        settings.Converters.Add(new SimplifiedListConverter<uint>(20));   // Compress long lists of uints (unpacked IDs) for readability
        settings.Converters.Add(new SimplifiedListConverter<long>(15));   // Compress long lists of longs (option values) for readability
        settings.Converters.Add(new SimplifiedListConverter<string>(15)); // Compress long lists of strings (Expedition Names) for readability
        settings.Converters.Add(new IdConverter());                       // Convert IDs to longs
        Type[] containerTypes = [
            typeof(JsonKeyValue<RegionID, TagStorage<RegionID, Region>.TagEntry>),
            typeof(JsonKeyValue<LocationID, TagStorage<LocationID, Location>.TagEntry>),
            typeof(JsonKeyValue<ItemID, TagStorage<ItemID, Item>.TagEntry>),
            typeof(JsonKeyValue<PathID, Path>),
            typeof(JsonKeyValue<OptionID, OptionBase>),
            typeof(TagStorage<RegionID, Region>.TagEntry),
            typeof(TagStorage<LocationID, Location>.TagEntry),
            typeof(TagStorage<ItemID, Item>.TagEntry),
            typeof(Location),
            typeof(Item),
        ];
        Type[] inlinedTypes = [ 
            typeof(TagStorage<RegionID, Region>.TagEntry),
            typeof(TagStorage<LocationID, Location>.TagEntry),
            typeof(TagStorage<ItemID, Item>.TagEntry),
            typeof(TagDefinition<RegionID>),
            typeof(TagDefinition<LocationID>),
            typeof(TagDefinition<ItemID>),
            typeof(Path),
            typeof(OptionBase),
            typeof(Region),
            typeof(ItemData),
            typeof(LocationData),
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
    /// Represents a set of choices that can be made during a graph traversal.
    /// This is functionally similar to python's frozenset, being optionally hashable by value.
    /// </summary>
    public class GraphChoiceSet : HashSet<PathID>
    {
        /// <summary>
        /// Standard constructor
        /// </summary>
        public GraphChoiceSet()
            : base()
        {
            ChoicesHash = 0;
        }

        /// <summary>
        /// Standard constructor
        /// </summary>
        public GraphChoiceSet(IEnumerable<PathID> choices)
            : base(choices)
        {
            ChoicesHash = 0;
            foreach (PathID id in choices)
                ChoicesHash ^= id.GetHashCode();
        }

        /// <summary>
        /// Compares two states by equality, with a minor shortcut based on the set's hash
        /// </summary>
        public class ByChoicesComparer : EqualityComparer<GraphChoiceSet>
        {
            private static IEqualityComparer<HashSet<PathID>> baseComparer = CreateSetComparer();

            public override bool Equals(GraphChoiceSet? x, GraphChoiceSet? y)
                => x == null ? y == null : y != null && x.ChoicesHash == y.ChoicesHash && baseComparer.Equals(x, y);

            public override int GetHashCode([DisallowNull] GraphChoiceSet obj)
                => obj.ChoicesHash;
        }

        /// <summary>
        /// Hash code for the choices
        /// </summary>
        public readonly int ChoicesHash;

        public override string ToString()
            => $"({Count}) {string.Join(", ", this)}";
    }

    /// <summary>
    /// Helper used to iterate through an item's children or parents recursively
    /// </summary>
    private class ItemHIerarchyIterator
    {
        /// <summary>
        /// Standard constructor
        /// </summary>
        public ItemHIerarchyIterator(Game.Data data)
        {
            Data = data;
            ChildrenByParent = data.Items.GetAllEntries()
                .SelectMany(e => e.Value.Definition.AllParents.Select(p => (Parent: p, Child: e.Key)))
                .ToLookup(pair => pair.Parent, pair => pair.Child);
        }

        /// <summary>
        /// Game data for the items being iterated through
        /// </summary>
        private Game.Data Data;

        /// <summary>
        /// Used to quickly find children of an parent
        /// </summary>
        private ILookup<ItemID, ItemID> ChildrenByParent;

        /// <summary>
        /// Predicate used by recursion functions
        /// </summary>
        Func<ItemID, bool> Predicate = null!;

        /// <summary>
        /// Gets all children of the provided item recursively, including the inputted item
        /// </summary>
        public IEnumerable<ItemID> GetAllChildren(ItemID child, Func<ItemID, bool> predicate)
        {
            Predicate = predicate;
            IEnumerable<ItemID> result = GetAllChildrenRecursive(child);
            return result;
        }

        /// <summary>
        /// Recursive helper for the <see cref="GetAllChildren" /> function
        /// </summary>
        private IEnumerable<ItemID> GetAllChildrenRecursive(ItemID item)
            => !item.IsNull && Predicate!.Invoke(item)
            ? (ChildrenByParent.Contains(item) ? ChildrenByParent[item].SelectMany(GetAllChildrenRecursive) : Enumerable.Empty<ItemID>()).Prepend(item)
            : Enumerable.Empty<ItemID>();

        /// <summary>
        /// Gets all parents of the provided item recursively.
        /// Predicate will be called on all items and, if false, will skip that item and its parents
        /// </summary>
        public IEnumerable<ItemID> GetAllParents(ItemID child, Func<ItemID, bool> predicate)
        {
            Predicate = predicate;
            IEnumerable<ItemID> result = GetAllParentsRecursive(child);
            return result;
        }

        /// <summary>
        /// Recursive helper for the <see cref="GetAllParents" /> function
        /// </summary>
        private IEnumerable<ItemID> GetAllParentsRecursive(ItemID item)
            => !item.IsNull && Predicate!.Invoke(item)
            ? Data.Items.LookUpDefinition(item).AllParents.SelectMany(GetAllParentsRecursive).Prepend(item)
            : Enumerable.Empty<ItemID>();
    }

    /// <summary>
    /// Checks if the provided game data has choices and, if not, creates them
    /// </summary>
    public void TryComputeChoices(Game.Data data)
    {
        if (data.Choices != null) return;

        FeatureLogger.Notice("Beginning choice parsing");
        List<ChoiceState> results = new();

        Stack<GraphChoiceSet> queuedChoices = new(); // Could also be a queue, but a stack ensures depth-first traversals, which I find more intuitive
        HashSet<GraphChoiceSet> createdChoices = new(new GraphChoiceSet.ByChoicesComparer()); // Tracks all created states to avoid duplicate work

        // "Root" choice used to start all processing
        GraphChoiceSet rootChoice = new();
        queuedChoices.Push(rootChoice);
        createdChoices.Add(rootChoice);

        // Creating reverse lookups
        ILookup<ItemID, PathID> pathByItem = data.GetAllPaths()
            .SelectMany(p => p.Value.Reqs.Select(r => (ID: p.Key, Req: r)))
            .Where(pair => pair.Req.Type != Path.eType.None)
            .Where(pair => (pair.Req.Type & Path.eType.IsCategory) == Path.eType.None)
            .ToLookup(pair => pair.Req.Target, pair => pair.ID);

        ILookup<ItemID, PathID> pathByCategory = data.GetAllPaths()
            .SelectMany(p => p.Value.Reqs.Select(r => (ID: p.Key, Req: r)))
            .Where(pair => pair.Req.Type != Path.eType.None)
            .Where(pair => (pair.Req.Type & Path.eType.IsCategory) != Path.eType.None)
            .ToLookup(pair => pair.Req.Target, pair => pair.ID);

        ILookup<ItemID, Location> locationByItem = data.Locations.GetAllValuesNonNull()
            .Where(loc => !loc.Value.ItemID.IsNull)
            .ToLookup(loc => loc.Value.ItemID, loc => loc.Value);

        ILookup<RegionID, PathID> reversePaths = data.GetAllPaths()
            .ToLookup(pair => pair.Value.EndingRegion, pair => pair.Key);

        ItemHIerarchyIterator itemIterator = new(data);

        // For each sub tree, identify relevant items, regions, paths, and locations
        // Then create combined trees by region and process those as well in a similar fashion
        HashSet<RegionID> checkedRegions = new();
        HashSet<ItemID> checkedItems = new();
        HashSet<ItemID> checkedCats = new();
        Queue<PathID> queuedPaths = new();
        HashSet<PathID> checkedPaths = new();

        while (queuedChoices.TryPop(out GraphChoiceSet? choice))
        {
            FeatureLogger.Notice($"Processing choice: {choice}");

            // Guarantee these are cleared
            checkedRegions.Clear();
            queuedPaths.Clear();
            checkedItems.Clear();
            checkedCats.Clear();
            checkedPaths.Clear();

            // Initial paths we'll explore
            IEnumerable<PathID> initialForwardPaths = choice.Any() ? choice
                : data.Regions.LookUpValue(data.Region_Menu).ConnectedPaths; // Root choice
            foreach (var pathId in initialForwardPaths)
                queuedPaths.Enqueue(pathId);

            // Explore all currently available paths in the forward direction
            while (queuedPaths.TryDequeue(out PathID pathID))
            {
                if (!checkedPaths.Add(pathID)) 
                    continue; // We've already processed this path

                Path path = data.LookUpPath(pathID);
                if (path.Reqs.IsConsume && !choice.Contains(pathID))
                    continue; // Not traversable

                if (!checkedRegions.Add(path.EndingRegion))
                    continue; // We've already processed the ending region
                Region region = data.Regions.LookUpValue(path.EndingRegion);

                // Add new paths
                foreach (var p in region.ConnectedPaths)
                    queuedPaths.Enqueue(p);

                // Check new locations and identify paths which benefit from their items
                foreach (LocationID l in region.ConnectedLocations)
                {
                    Location loc = data.Locations.LookUpValueChecked(l);

                    if (loc.ItemID.IsNull) continue;
                    if (data.Items.LookUpValueChecked(loc.ItemID).RandData.IsRandomLike) continue;
                    if (!checkedItems.Add(loc.ItemID)) continue;

                    if (pathByItem.Contains(loc.ItemID))
                    {
                        foreach (PathID p in pathByItem[loc.ItemID])
                            queuedPaths.Enqueue(p);
                    }

                    foreach (ItemID item in itemIterator.GetAllParents(loc.ItemID, checkedCats.Add))
                    {
                        if (pathByCategory.Contains(item))
                        {
                            foreach (PathID p in pathByCategory[item])
                                queuedPaths.Enqueue(p);
                        }
                    }
                }
            }

            // Build reverse direction in a similar manner
            foreach (PathID pathID in checkedPaths)
                queuedPaths.Enqueue(pathID);

            checkedRegions.Clear();
            checkedItems.Clear();
            checkedPaths.Clear();

            while (queuedPaths.TryDequeue(out PathID pathID))
            {
                if (!checkedPaths.Add(pathID)) continue;
                Path path = data.LookUpPath(pathID);

                // Check if we need to add new reverse paths
                if (checkedRegions.Add(path.StartingRegion) && reversePaths.Contains(path.StartingRegion))
                {
                    foreach (PathID p in reversePaths[path.StartingRegion])
                        queuedPaths.Enqueue(p);
                }

                // Check if we need to evaluate other regions which contain items useful for this path
                if (path.Reqs.IsNone) continue;
                foreach (var req in path.Reqs)
                {
                    // On top of the nuance of category vs item, we also skip reqs which are randomlike
                    IEnumerable<Location> locs;
                    if ((req.Type & Path.eType.IsCategory) == Path.eType.None)
                    {
                        if (checkedItems.Add(req.Target) && !data.Items.LookUpValueChecked(req.Target).RandData.IsRandomLike)
                            locs = locationByItem.Contains(req.Target) ? locationByItem[req.Target] : Enumerable.Empty<Location>();
                        else
                            locs = Enumerable.Empty<Location>();
                    }
                    else
                    {
                        locs = itemIterator.GetAllChildren(req.Target, checkedItems.Add)
                            .Where(i => !(data.Items.LookUpValue(i)?.RandData.IsRandomLike ?? true))
                            .Where(locationByItem.Contains)
                            .SelectMany(i => locationByItem[i]);
                    }

                    foreach (RegionID r in locs.SelectMany(l => l.OwningRegionIDs))
                    {
                        if (checkedRegions.Add(r) && reversePaths.Contains(r))
                        {
                            foreach (var p in reversePaths[r])
                                queuedPaths.Enqueue(p);
                        }
                    }
                }
            }

            // Create new "pruned" set of regions from the reachable regions
            // This is made by forward traversing while checking against relevant paths,
            //  and results in unreachable regions being pruned from the collection
            // This is also the best time to find relevant superchoices, so we do that too
            checkedRegions.Clear();
            queuedPaths.Clear();

            checkedRegions.Add(data.Region_Menu);
            foreach (var p in data.Regions.LookUpValue(data.Region_Menu).ConnectedPaths)
                queuedPaths.Enqueue(p);

            while (queuedPaths.TryDequeue(out PathID pathID))
            {
                if (!checkedPaths.Contains(pathID)) continue;

                Path path = data.LookUpPath(pathID);
                if (path.Reqs.IsConsume && !choice.Contains(pathID))
                {   // Create a new superchoice from the combination of these two choices
                    GraphChoiceSet newChoice = new(choice.Append(pathID));
                    if (createdChoices.Add(newChoice))
                        queuedChoices.Push(newChoice);
                    continue;
                }

                // Check if we need to both traversing this path
                if (!checkedRegions.Add(path.EndingRegion)) continue;
                foreach (PathID p in data.Regions.LookUpValue(path.EndingRegion).ConnectedPaths)
                    queuedPaths.Enqueue(p);
            }

            // Sort, then compress the region set
            RegionID[] sorted = new RegionID[checkedRegions.Count];
            int count = 0;
            foreach (RegionID id in checkedRegions) sorted[count++] = id;
            Array.Sort(sorted);

            RegionID min = sorted[0];
            RegionID max = sorted[0];

            List<RegionID> solos = new();
            List<(RegionID, RegionID)> ranges = new();

            foreach (RegionID id in sorted.Skip(1))
            {
                if (id.ID == (max.ID + 1u))
                    max = id;
                else if (min.ID < max.ID)
                {
                    ranges.Add((min, max));
                    min = max = id;
                }
                else
                {
                    solos.Add(max);
                    min = max = id;
                }
            }

            if (min.ID < max.ID)
                ranges.Add((min, max));
            else
                solos.Add(max);

            // Finally, adding the choice to the results
            results.Add(new()
            {
                ChoicePaths = choice.ToArray(),
                Regions = solos.ToArray(),
                RegionRanges = ranges.ToArray(),
            });

            if (choice.Count == 1 && choice.Contains(new PathID() { ID = 3328 }))
            {
                var debugExplored = checkedRegions.Select(data.Regions.LookUpName).ToList();
                var debugItems = checkedItems.Select(data.Items.LookUpName).ToList();
                int i = 0;
            }
        }

        // Push computed choices to the game data
        data.Choices = results.ToArray();
    }

    /// <summary>
    /// Used during graph traversal to track the traversal progress of a particular choice
    /// </summary>
    public readonly struct TraversalState
    {
        /// <summary>
        /// Standard constructor
        /// </summary>
        public TraversalState(GraphChoiceSet choice)
        {
            Choice = choice;
            Regions = new();
            ItemCounts = new();
            CategoryCounts = new();
            Paths = new();
        }

        public readonly GraphChoiceSet Choice;

        /// <summary>
        /// Regions found / explored by this state
        /// </summary>
        public readonly HashSet<RegionID> Regions;

        /// <summary>
        /// Non-randomlike items found by this state
        /// </summary>
        public readonly Dictionary<ItemID, int> ItemCounts;

        /// <summary>
        /// Category counts for the items found in this state
        /// </summary>
        public readonly Dictionary<ItemID, int> CategoryCounts;

        /// <summary>
        /// Paths available to this state which have not yet been traversed
        /// </summary>
        public readonly List<PathID> Paths;
    }

    /// <summary>
    /// Helper for updating categories recursively
    /// </summary>
    private class CategoryUpdater
    {
        /// <summary>
        /// Standard constructor
        /// </summary>
        public CategoryUpdater(Game.Data data)
            => Data = data;

        /// <summary>
        /// Game data used to identify parent tags of items
        /// </summary>
        public readonly Game.Data Data;

        /// <summary>
        /// Tags seen during the current recursive update process.
        /// Note that this is necessary due to the diamond problem.
        /// </summary>
        private readonly HashSet<ItemID> SeenTags = new();

        /// <summary>
        /// The dictionary currently being updated
        /// </summary>
        private Dictionary<ItemID, int> Target = null!;

        /// <summary>
        /// Increment the relevant category counts in the provided target for the provided item by 1
        /// </summary>
        /// <param name="target">The dictionary to update</param>
        /// <param name="item">The item being collected</param>
        /// <param name="count">The amount to update by</param>
        public void Update(Dictionary<ItemID, int> target, ItemID item, int count = 1)
        {
            Target = target;
            SeenTags.Clear();
            SeenTags.Add(new());
            UpdateRecursive(item, count);
        }

        /// <summary>
        /// Recursive helper for <see cref="Update"/>
        /// </summary>
        private void UpdateRecursive(ItemID item, int count)
        {
            if (!SeenTags.Add(item)) return;
            Target[item] = Target.GetValueOrDefault(item, 0) + count;
            foreach (var parent in Data.Items.LookUpDefinition(item).AllParents)
                UpdateRecursive(parent, count);
        }
    }

    /// <summary>
    /// Attempts a graph traversal of the provided game data.
    /// This will update the reachability of all regions and ensure the game is beatable.
    /// </summary>
    public void DoGraphTraversal(Game.Data data)
    {
        // Unpack choices
        FeatureLogger.Notice("Prepping graph traversal");
        TryComputeChoices(data);

        var comparer = new GraphChoiceSet.ByChoicesComparer(); // Micro-optimization :)
        Dictionary<GraphChoiceSet, HashSet<RegionID>> definedChoices = new(comparer);
        foreach (var choice in data.Choices!)
        {
            HashSet<RegionID> regions = new(2 * choice.RegionRanges.Length + choice.Regions.Length);
            foreach (RegionID r in choice.Regions) regions.Add(r);
            foreach (var pair in choice.RegionRanges)
            {
                for (uint id = pair.Item1.ID; id <= pair.Item2.ID; id++)
                    regions.Add(new RegionID() { ID = id });
            }
            definedChoices.Add(new GraphChoiceSet(choice.ChoicePaths), regions);
        }

        // Set up the global state
        bool[] globalReachedRegion = new bool[data.Regions.Count];
        HashSet<LocationID> discoveredLocations = new();
        Dictionary<ItemID, int> globalItemCounts = new();
        Dictionary<ItemID, int> globalCategoryCounts = new();
        CategoryUpdater catUpdater = new(data);

        foreach (var pair in data.GetAllFloatingItems())
        {
            globalItemCounts[pair.Item2] = globalItemCounts.GetValueOrDefault(pair.Item2, 0) + 1;
            catUpdater.Update(globalCategoryCounts, pair.Item2);
        }

        HashSet<GraphChoiceSet> seenStates = new(comparer);
        List<TraversalState> states = new() { new(new GraphChoiceSet()) };
        seenStates.Add(states[0].Choice);

        // Iterate through all reachable states until we discover what we're looking for
        FeatureLogger.Notice("Beginning graph traversal");
        bool madeProgress;
        do
        {
            madeProgress = false;

            for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
            {
                TraversalState state = states[stateIndex];
                HashSet<RegionID> stateRegions = definedChoices[state.Choice];

                // Init the state because it's brand new
                if (!state.Regions.Any())
                {
                    state.Regions.Add(data.Region_Menu);
                    Region region = data.Regions.LookUpValue(data.Region_Menu);
                    state.Paths.AddRange(region.ConnectedPaths);

                    foreach (LocationID locID in region.ConnectedLocations)
                    {
                        Location loc = data.Locations.LookUpValueChecked(locID);
                        if (loc.ItemID.IsNull) continue;
                        if (loc.OwningRegionIDs.Count > 1) continue;

                        Item item = data.Items.LookUpValueChecked(loc.ItemID);
                        if (!item.RandData.IsProgression) continue;

                        if (item.RandData.IsRandomLike)
                        {   // Randomlike items are shared between states, and so should only be discovered once
                            if (discoveredLocations.Add(locID))
                            {
                                globalItemCounts[loc.ItemID] = globalItemCounts.GetValueOrDefault(loc.ItemID, 0) + 1;
                                catUpdater.Update(globalCategoryCounts, loc.ItemID);
                            }
                        }
                        else
                        {
                            state.ItemCounts[loc.ItemID] = state.ItemCounts.GetValueOrDefault(loc.ItemID, 0) + 1;
                            catUpdater.Update(state.CategoryCounts, loc.ItemID);
                        }
                    }
                }

                // Traversal!
                int lastRegionCount;
                do
                {
                    lastRegionCount = state.Regions.Count;
                    for (int pathIndex = 0; pathIndex < state.Paths.Count; pathIndex++)
                    {
                        PathID pathID = state.Paths[pathIndex];
                        Path path = data.LookUpPath(pathID);

                        // Check if this path is still relevant
                        if (state.Regions.Contains(path.EndingRegion))
                        {
                            state.Paths.RemoveAt(pathIndex--);
                            continue;
                        }

                        if (state.Choice.Count == 1 && state.Choice.First().ID == 3328u && pathID.ID == 3336u) { }

                        bool isTraversable = true, isConsume = false;
                        if (!path.Reqs.IsNone)
                        {
                            if (state.Choice.Contains(pathID))
                            {
                                if (pathID.ID == 1991u) { }
                                foreach (var req in path.Reqs)
                                {
                                    if ((req.Type & Path.eType.IsConsumed) != Path.eType.None) continue; // Price has already been paid
                                    isTraversable = isTraversable
                                        && (((req.Type & Path.eType.IsCategory) != Path.eType.None) || ((globalItemCounts.GetValueOrDefault(req.Target, 0) + state.ItemCounts.GetValueOrDefault(req.Target, 0)) >= req.Count))
                                        && (((req.Type & Path.eType.IsCategory) == Path.eType.None) || ((globalCategoryCounts.GetValueOrDefault(req.Target, 0) + state.CategoryCounts.GetValueOrDefault(req.Target, 0)) >= req.Count))
                                    ;
                                }
                            }
                            else
                            {
                                foreach (var req in path.Reqs)
                                {
                                    isConsume = isConsume || ((req.Type & Path.eType.IsConsumed) != Path.eType.None);
                                    isTraversable = isTraversable
                                        && (((req.Type & Path.eType.IsCategory) != Path.eType.None) || ((globalItemCounts.GetValueOrDefault(req.Target, 0) + state.ItemCounts.GetValueOrDefault(req.Target, 0)) >= req.Count))
                                        && (((req.Type & Path.eType.IsCategory) == Path.eType.None) || ((globalCategoryCounts.GetValueOrDefault(req.Target, 0) + state.CategoryCounts.GetValueOrDefault(req.Target, 0)) >= req.Count))
                                    ;
                                }
                            }
                        }

                        // Traversing the path
                        if (!isTraversable) continue;
                        state.Paths.RemoveAt(pathIndex--);
                        madeProgress = true;

                        // Check if this introduces a new state
                        if (isConsume)
                        {   // Calc new choice, init and add if necessary
                            GraphChoiceSet newChoice = new(state.Choice.Append(pathID));
                            if (definedChoices.ContainsKey(newChoice) && seenStates.Add(newChoice))
                            {
                                TraversalState newState = new(newChoice);
                                foreach (var req in newChoice.SelectMany(p => data.LookUpPath(p).Reqs))
                                {
                                    if ((req.Type & Path.eType.IsConsumed) != Path.eType.None)
                                       catUpdater.Update(newState.CategoryCounts, req.Target, -(int)req.Count);
                                }
                                states.Add(newState);
                            }
                            continue;
                        }

                        // Add the ending region to the traversed regions
                        if (!stateRegions.Contains(path.EndingRegion))
                            continue;
                        if (state.Regions.Add(path.EndingRegion))
                            globalReachedRegion[path.EndingRegion.AsIndex] = true;
                        else
                            continue;

                        Region region = data.Regions.LookUpValue(path.EndingRegion);
                        state.Paths.AddRange(region.ConnectedPaths);

                        // Discovering locations + items
                        foreach (var locID in region.ConnectedLocations)
                        {
                            Location loc = data.Locations.LookUpValueChecked(locID);
                            if (loc.ItemID.IsNull) continue;
                            if (!loc.OwningRegionIDs.All(state.Regions.Contains)) continue;

                            Item item = data.Items.LookUpValueChecked(loc.ItemID);
                            if (!item.RandData.IsProgression) continue;

                            if (item.RandData.IsRandomLike)
                            {   // Randomlike items are shared between states, and so should only be discovered once
                                if (discoveredLocations.Add(locID))
                                {
                                    globalItemCounts[loc.ItemID] = globalItemCounts.GetValueOrDefault(loc.ItemID, 0) + 1;
                                    catUpdater.Update(globalCategoryCounts, loc.ItemID);
                                }
                            }
                            else
                            {
                                state.ItemCounts[loc.ItemID] = state.ItemCounts.GetValueOrDefault(loc.ItemID, 0) + 1;
                                catUpdater.Update(state.CategoryCounts, loc.ItemID);
                            }
                        }
                    }
                } while (state.Regions.Count > lastRegionCount);
            }

        } while (madeProgress);

        // Pushing reachability to data
        FeatureLogger.Debug("Pushing new reachability to MID data");
        foreach (var id in data.Regions.GetAllIDs())
            data.SetRegionReachable(id, globalReachedRegion[id.AsIndex]);

        FeatureLogger.Debug("Checking if all win locations were reachable during traversal");
        HashSet<ItemID> winCats = [data.Item_SectorClears, data.Item_PEClears];
        bool allFound = true;
        foreach (var pair in data.Locations.GetAllValuesNonNull())
        {
            if (data.Items.IsChild(pair.Value.ItemID, winCats) && !discoveredLocations.Contains(pair.Key))
            {
                FeatureLogger.Error($"Failed to find win location {data.Locations.LookUpName(pair.Key)} during traversal");
                allFound = false;
            }
        }
        if (allFound)
            FeatureLogger.Success("All sector clears found!");
        else
            FeatureLogger.Error("Failed to find at least one sector clear");

        //{
        //    PathID[][] keys = states.Select(s => s.Choice.ToArray()).ToArray();
        //    foreach (var subarr in keys) 
        //        Array.Sort(subarr);
        //    Array.Sort(keys, (x, y) =>
        //    {
        //        foreach (var pair in x.Zip(y))
        //        {
        //            int compare = pair.First.CompareTo(pair.Second);
        //            if (compare != 0) return compare;
        //        }
        //        return x.Length.CompareTo(y.Length);
        //    });
        //    foreach (var key in keys)
        //        FeatureLogger.Debug($"Found state: ({key.Length}) {string.Join(", ", key)}");
        //}
        //
        //var debugChoice = new GraphChoiceSet([new PathID() { ID = 53u }]);
        //var debugState = states.FirstOrDefault(s => comparer.Equals(s.Choice, debugChoice), states[0]);
        //var debugRegions = debugState.Regions.Select(data.Regions.LookUpName).ToList();
        //var debugItems = debugState.ItemCounts.ToDictionary(pair => data.Items.LookUpName(pair.Key), pair => pair.Value);
        //var debugPaths = debugState.Paths.Select(p => data.LookUpPath(p).Name ?? $"{data.Regions.LookUpName(data.LookUpPath(p).StartingRegion)} => {data.Regions.LookUpName(data.LookUpPath(p).EndingRegion)}").ToList();
        //var debugStateRegions = definedChoices[debugChoice].Select(data.Regions.LookUpName).ToList();
        //var debugGlobalItems = globalItemCounts.ToDictionary(pair => data.Items.LookUpName(pair.Key), pair => pair.Value);

        FeatureLogger.Notice("Graph traversal completed. See log for details");
    }

}
