using ReTFO.Archipelago.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// A struct used to store a large quantity of tags
/// </summary>
[DataContract]
public struct TagStorage<TID, TItem>
    where TID : struct, ITagID
{
    public TagStorage() { }
    static TagStorage()
    {
        // String value types are not supported
        // TagEntry allows implicit casting to string and TItem; if TItem == string,
        //  this becomes unsupported
        if (typeof(TItem) == typeof(string))
            throw new NotSupportedException($"Tag storage TItem constraint cannot be of type {typeof(string).FullName}");
    }

    /// <summary>
    /// A single tag entry in the list of tags
    /// </summary>
    [DataContract]
    public readonly record struct TagEntry
    {
        public TagEntry(string name, TagDefinition<TID> definition, [AllowNull] TItem value)
        {
            Name = name;
            Definition = definition;
            Value = value;
        }

        public TagEntry(TagEntry source)
        {
            Name = source.Name;
            Definition = source.Definition;
            Value = source.Value!;
        }

        /// <summary>
        /// The name of the entry
        /// </summary>
        [DataMember(Name = "name")]
        public string Name { get; init; }

        /// <summary>
        /// The definition of the tag for the entry
        /// </summary>
        [DataMember(Name = "definition")]
        public TagDefinition<TID> Definition { get; init; }

        /// <summary>
        /// The value stored in the entry, if any
        /// </summary>
        [AllowNull, MaybeNull, DataMember(Name = "value")]
        public TItem Value { get; init; }
    }

    /// <summary>
    /// Adapts this tag storage to some common dictionary interfaces
    /// </summary>
    private class Adapter : IReadOnlyDictionary<TID, TagEntry>, IReadOnlyDictionary<TID, TItem>
    {
        public Adapter(TagStorage<TID, TItem> source) => m_storage = source;
        private readonly TagStorage<TID, TItem> m_storage;

        private const string AMIGUOUS_ENUMERATOR_ERROR = "Please use the type-safe implementation of GenEnumerator; the non-type-safe version is ambiguous";

        private class Enumerator : IEnumerator<KeyValuePair<TID, TagEntry>>, IEnumerator<KeyValuePair<TID, TItem>>
        {
            public Enumerator(TagStorage<TID, TItem> source) => m_storage = source;
            private TagStorage<TID, TItem> m_storage;
            private uint m_index = 0;

            KeyValuePair<TID, TagEntry> IEnumerator<KeyValuePair<TID, TagEntry>>.Current
            {
                get
                {
                    TID id = new() { ID = m_index };
                    return KeyValuePair.Create(id, m_storage.LookUpEntry(id));
                }
            }

            KeyValuePair<TID, TItem> IEnumerator<KeyValuePair<TID, TItem>>.Current
            {
                get
                {
                    TID id = new() { ID = m_index };
                    return KeyValuePair.Create(id, m_storage.LookUpValue(id))!;
                }
            }

            public void Dispose() { }
            public bool MoveNext() => ++m_index <= m_storage.Count;
            public void Reset() => m_index = 0;

            [Obsolete(AMIGUOUS_ENUMERATOR_ERROR)]
            object IEnumerator.Current => throw new NotSupportedException(AMIGUOUS_ENUMERATOR_ERROR);
        }

        // Common
        public IEnumerable<TID> Keys => m_storage.GetAllIDs();
        public int Count => m_storage.Count;
        public bool ContainsKey(TID key) => key.ID > 0 && key.ID <= m_storage.Count;
        [Obsolete(AMIGUOUS_ENUMERATOR_ERROR)]
        IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException(AMIGUOUS_ENUMERATOR_ERROR);

        // For specifically TagEntry dict
        IEnumerable<TagEntry> IReadOnlyDictionary<TID, TagEntry>.Values => m_storage.m_entries;
        TagEntry IReadOnlyDictionary<TID, TagEntry>.this[TID key] => m_storage.LookUpEntry(key);
        public bool TryGetValue(TID key, out TagEntry value) => (ContainsKey(key) ? (true, value = m_storage.LookUpEntry(key)) : (false, value = default)).Item1;
        IEnumerator<KeyValuePair<TID, TagEntry>> IEnumerable<KeyValuePair<TID, TagEntry>>.GetEnumerator() => new Enumerator(m_storage);

        // For specifically TItem dict
        IEnumerable<TItem> IReadOnlyDictionary<TID, TItem>.Values => m_storage.m_entries.Select(e => e.Value)!;
        TItem IReadOnlyDictionary<TID, TItem>.this[TID key] => m_storage.LookUpValue(key)!;
        public bool TryGetValue(TID key, out TItem value) => (ContainsKey(key) ? (true, value = m_storage.LookUpValue(key)!) : (false, value = default!)).Item1;
        IEnumerator<KeyValuePair<TID, TItem>> IEnumerable<KeyValuePair<TID, TItem>>.GetEnumerator() => new Enumerator(m_storage);
    }

    /// <summary>
    /// A lookup to find a tag by name in the tag list
    /// </summary>
    private readonly Dictionary<string, uint> m_lookup = new();

    /// <summary>
    /// The list of stored tags
    /// </summary>
    [DataMember(Name = "entries")]
    private readonly List<TagEntry> m_entries = new();

    /// <summary>
    /// Shortcut to get a tag entry or set a tag entry's value.
    /// This will not change a tag entry's name or definition.
    /// </summary>
    public TagEntry this[TID id]
    {
        get => new(LookUpEntry(id));
    }

    /// <summary>
    /// How many entries are in the collection
    /// </summary>
    public int Count => m_entries.Count;

    /// <summary>
    /// Enumerator for all IDs in this tag storage
    /// </summary>
    public IEnumerable<TID> GetAllIDs() => Enumerable.Range(1, Count).Select(i => new TID() { ID = unchecked((uint)i) });

    /// <summary>
    /// Gets a readonly view of all entries in this storage
    /// </summary>
    public IReadOnlyDictionary<TID, TagEntry> GetAllEntries() => new Adapter(this);

    /// <summary>
    /// Gets a readonly view of all the values in this storage
    /// </summary>
    public IReadOnlyDictionary<TID, TItem> GetAllValues() => new Adapter(this);

    /// <summary>
    /// Creates a new tag entry; throws on fail
    /// </summary>
    /// <param name="name">The tag's name</param>
    /// <param name="definition">The tag's definition</param>
    /// <param name="item">The item to store with the tag, if any</param>
    /// <returns>The ID of the created tag entry</returns>
    /// <exception cref="ArgumentException">Thrown if a tag with the provided name already exists</exception>
    public TID Create(string name, TagDefinition<TID> definition, [AllowNull] TItem item = default!)
    {
        foreach (TID parent in definition.AllParents)
            if (parent.ID > Count) throw new ArgumentOutOfRangeException(
                $"Cannot register tag \"{name}\" with parent {parent}; no such parent is currently defined!"
                +"\nTag storage requires all created tags use only existing parent tags to ensure graphs are acyclic and well-defined."
            );

        TID id = new() { AsIndex = m_entries.Count };
        m_lookup.Add(name, id.ID);
        m_entries.Add(new(name, definition, item));
        return id;
    }

    /// <summary>
    /// Look up or create a tag entry, optionally providing the value for that entry
    /// </summary>
    /// <param name="name">The tag's name</param>
    /// <param name="definitionFactory">The factory for creating the tag's definition</param>
    /// <param name="item">The item to store with the tag, if any</param>
    /// <returns>The ID of the created tag entry</returns>
    public TID LookUpOrCreate(string name, Func<TagDefinition<TID>> definitionFactory, [AllowNull] TItem item = default!)
    {
        if (TryLookUpID(name, out var id)) return id;
        else return Create(name, definitionFactory.Invoke(), item);
    }

    /// <summary>
    /// Look up or create a tag entry, optionally providing the value for that entry
    /// </summary>
    /// <param name="name">The tag's name</param>
    /// <param name="definitionFactory">The factory for creating the tag's definition</param>
    /// <param name="item">The item to store with the tag, if any</param>
    /// <param name="result">The ID which is either found or created</param>
    /// <returns>True if the ID is new, false otherwise</returns>
    public bool TryLookUpOrCreate(out TID result, string name, Func<TagDefinition<TID>> definitionFactory, [AllowNull] TItem item = default!)
    {
        if (TryLookUpID(name, out result)) return false;
        result = Create(name, definitionFactory.Invoke(), item);
        return true;
    }

    /// <summary>
    /// Look up or create a tag entry, optionally providing a value generator for that entry
    /// </summary>
    /// <param name="name">The tag's name</param>
    /// <param name="definitionFactory">The factory for creating the tag's definition</param>
    /// <param name="valueFactory">The factory used to create the value, if necessary</param>
    /// <returns>The ID of the created tag entry</returns>
    public TID LookUpOrCreate(string name, Func<TagDefinition<TID>> definitionFactory, Func<TItem> valueFactory)
    {
        if (TryLookUpID(name, out var id)) return id;
        else return Create(name, definitionFactory.Invoke(), valueFactory.Invoke());
    }

    /// <summary>
    /// Look up or create a tag entry, optionally providing a value generator for that entry
    /// </summary>
    /// <param name="name">The tag's name</param>
    /// <param name="definitionFactory">The factory for creating the tag's definition</param>
    /// <param name="valueFactory">The factory used to create the value, if necessary</param>
    /// <returns>The ID of the created tag entry</returns>
    public bool TryLookUpOrCreate(out TID result, string name, Func<TagDefinition<TID>> definitionFactory, Func<TItem> valueFactory)
    {
        if (TryLookUpID(name, out result)) return false;
        result = Create(name, definitionFactory.Invoke(), valueFactory.Invoke());
        return true;
    }

    /// <summary>
    /// Look up or create a tag entry, optionally providing the value for that entry
    /// </summary>
    /// <param name="data">The data used to create the tag definition</param>
    /// <param name="name">The tag's name</param>
    /// <param name="definitionFactory">The factory for creating the tag's definition</param>
    /// <param name="item">The item to store with the tag, if any</param>
    /// <returns>The ID of the created tag entry</returns>
    public TID LookUpOrCreate<TData>(TData data, string name, Func<TData, TagDefinition<TID>> definitionFactory, [AllowNull] TItem item = default!)
    {
        if (TryLookUpID(name, out var id)) return id;
        else return Create(name, definitionFactory.Invoke(data), item);
    }

    /// <summary>
    /// Look up or create a tag entry, optionally providing the value for that entry
    /// </summary>
    /// <param name="data">The data used to create the tag definition</param>
    /// <param name="name">The tag's name</param>
    /// <param name="definitionFactory">The factory for creating the tag's definition</param>
    /// <param name="item">The item to store with the tag, if any</param>
    /// <returns>The ID of the created tag entry</returns>
    public bool TryLookUpOrCreate<TData>(out TID result, TData data, string name, Func<TData, TagDefinition<TID>> definitionFactory, [AllowNull] TItem item = default!)
    {
        if (TryLookUpID(name, out result)) return false;
        result = Create(name, definitionFactory.Invoke(data), item);
        return true;
    }

    /// <summary>
    /// Look up or create a tag entry, optionally providing a value generator for that entry
    /// </summary>
    /// <param name="data">The data used to create the tag definition</param>
    /// <param name="name">The tag's name</param>
    /// <param name="definitionFactory">The factory for creating the tag's definition, if necessary</param>
    /// <param name="valueFactory">The factory for creating the tag's value, if necessary</param>
    /// <returns>The ID of the created tag entry</returns>
    public TID LookUpOrCreate<TData>(TData data, string name, Func<TData, TagDefinition<TID>> definitionFactory, Func<TData, TItem> valueFactory) where TData : Game.Data
    {
        if (TryLookUpID(name, out var id)) return id;
        else return Create(name, definitionFactory.Invoke(data), valueFactory.Invoke(data));
    }

    /// <summary>
    /// Look up or create a tag entry, optionally providing a value generator for that entry
    /// </summary>
    /// <param name="data">The data used to create the tag definition</param>
    /// <param name="name">The tag's name</param>
    /// <param name="definitionFactory">The factory for creating the tag's definition, if necessary</param>
    /// <param name="valueFactory">The factory for creating the tag's value, if necessary</param>
    /// <returns>The ID of the created tag entry</returns>
    public bool TryLookUpOrCreate<TData>(out TID result, TData data, string name, Func<TData, TagDefinition<TID>> definitionFactory, Func<TData, TItem> valueFactory) where TData : Game.Data
    {
        if (TryLookUpID(name, out result)) return false;
        result = Create(name, definitionFactory.Invoke(data), valueFactory.Invoke(data));
        return true;
    }

    /// <summary>
    /// Attempt to look up an existing tag by name
    /// </summary>
    /// <param name="name">The name of the tag to look up</param>
    /// <param name="id">The found id, or a null ID if failed</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool TryLookUpID(string name, out TID id)
        => (m_lookup.TryGetValue(name, out uint value) ? (true, id = new() { ID = value }) : (false, id = default)).Item1;

    /// <summary>
    /// Look up the entry associated with a particular tag ID
    /// </summary>
    /// <param name="id">The ID to look up. Must not be null</param>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">Thrown if the ID is null</exception>
    public TagEntry LookUpEntry(TID id)
    {
        if (id.IsNull)
            throw new NullReferenceException("Attempted to look up tag entry associated with null ID");
        return m_entries[id.AsIndex];
    }

    /// <summary>
    /// Shorthand to look up the name for an entry
    /// </summary>
    public string LookUpName(TID id) => LookUpEntry(id).Name;

    /// <summary>
    /// Shorthand to look up the definition for an entry
    /// </summary>
    public TagDefinition<TID> LookUpDefinition(TID id) => LookUpEntry(id).Definition;

    /// <summary>
    /// Shorthand to look up the value for an entry
    /// </summary>
    [return: MaybeNull]
    public TItem LookUpValue(TID id) => LookUpEntry(id).Value;

    /// <summary>
    /// Shorthand to look up a value for an entry.
    /// Throws if the value is null.
    /// </summary>
    [return: NotNull]
    public TItem LookUpValueChecked(TID id) => LookUpEntry(id).Value
        ?? throw new NullReferenceException($"Attempted to look up value but it was null: {id}");

    /// <summary>
    /// Set the value of an entry
    /// </summary>
    public void SetValue(TID id, [AllowNull] TItem item) 
        => m_entries[id.AsIndex] = new(LookUpEntry(id)) { Value = item };

    /// <summary>
    /// Returns true if the provided ID is well-defined for this TagStorage.
    /// </summary>
    public bool ContainsID(TID id)
        => !(id.IsNull || id.AsIndex >= m_entries.Count);

    /// <summary>
    /// Tests if a single child tag is a child of a single parent tag
    /// </summary>
    /// <param name="child">The child to test</param>
    /// <param name="parent">The parent to test</param>
    /// <returns>True if child is parent or is a child or indirect child of parent, false otherwise</returns>
    public bool IsChild(TID child, TID parent)
    {
        if (child.IsNull) return false;
        if (parent.Equals(child)) return true;
        TagDefinition<TID> def = LookUpDefinition(child);
        if (IsChild(def.Parent, parent)) return true;
        if (def.OtherParents != null)
            for (int i = 0; i < def.OtherParents.Length; i++)
                if (IsChild(def.OtherParents[i], parent)) return true;
        return false;
    }

    /// <summary>
    /// Tests if a single ID is in or is a direct or indirect child of any ID in the collection. 
    /// Ideally, the collection is a HashSet.
    /// </summary>
    /// <param name="child">The ID to test</param>
    /// <param name="parents">The parents to test against</param>
    /// <returns>True if child is a child of parents, false otherwise</returns>
    public bool IsChild(TID child, IReadOnlyCollection<TID> parents)
    {
        if (child.IsNull) return false;
        if (parents.Contains(child)) return true;
        TagDefinition<TID> def = LookUpDefinition(child);
        if (IsChild(def.Parent, parents)) return true;
        if (def.OtherParents != null)
            for (int i = 0; i < def.OtherParents.Length; i++)
                if (IsChild(def.OtherParents[i], parents)) return true;
        return false;
    }

    /// <summary>
    /// Returns a list of IDs for the main parent chain. The first item is the non-null root,
    ///  and each subsequent item is a child of the previous. The last item is the input id
    /// </summary>
    /// <param name="id">The ID to make a chain for.</param>
    /// <returns>The parent chain</returns>
    /// <remarks>
    /// If `id` is null, the returned chained will be an empty array.
    /// </remarks>
    public TID[] MakeChain(TID id) => MakeChain_Helper(id, 0);

    /// <summary>
    /// Helper method for MakeChain to enable recursion
    /// </summary>
    /// <param name="id">ID to make a chain for</param>
    /// <param name="depth">Current depth in the chain</param>
    /// <returns>The partially-constructed chain (dependent on depth)</returns>
    private TID[] MakeChain_Helper(TID id, int depth)
    {
        if (id.IsNull) 
            return new TID[depth];
        else
        {
            TID[] result = MakeChain_Helper(LookUpDefinition(id).Parent, ++depth);
            result[result.Length - depth] = id;
            return result;
        }
    }

    /// <summary>
    /// Returns a set containing the input IDs and all their direct and indirect parent IDs
    /// </summary>
    public HashSet<TID> GetAllParents(IEnumerable<TID> ids)
    {
        GetAllParents_Helper helper = new(this);
        foreach (TID id in ids) helper.Process(id);
        return helper.Result;
    }

    /// <summary>
    /// Small helper which could've been a lambda :)
    /// </summary>
    private class GetAllParents_Helper
    {
        public HashSet<TID> Result;
        readonly TagStorage<TID, TItem> m_storage;
        public GetAllParents_Helper(TagStorage<TID, TItem> storage)
        {
            m_storage = storage;
            Result = new();
        }

        public void Process(TID id)
        {
            if (id.IsNull) return;
            if (!Result.Add(id)) return;
            foreach (var parent in m_storage.LookUpDefinition(id).AllParents)
                Process(parent);
        }
    }

    /// <summary>
    /// Trim excess entries from internal storage
    /// </summary>
    public void TrimExcess()
    {
        m_entries.TrimExcess();
        m_lookup.TrimExcess();
    }
}

/// <summary>
/// Extension method(s) for TagStorage with more-constrained type parameters
/// </summary>
public static class TagStorageExtensions
{
    /// <summary>
    /// Extension helper used to get only tag entries with non-null values
    /// </summary>
    public static IEnumerable<KeyValuePair<TID, TItem>> GetAllValuesNonNull<TID, TItem>(this TagStorage<TID, TItem> self)
        where TID : struct, ITagID
        where TItem : class
    {
        return self.GetAllValues().Where(p => p.Value != null);
    }
}