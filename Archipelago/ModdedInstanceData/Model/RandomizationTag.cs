using ReTFO.Archipelago.Utilities;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// A RandomizationTag used for randomization purposes
/// </summary>
[DataContract]
public struct RandomizationTag : INullable, IId, IIndex, IComparable<RandomizationTag>, IEquatable<RandomizationTag>
{
    /// <summary>
    /// Constructs a null RandomizationTag
    /// </summary>
    public RandomizationTag() { }

    /// <summary>
    /// ID of the tag
    /// </summary>
    [DataMember(Name = "value")] 
    private readonly long m_value = 0;

    public bool IsNull => m_value == 0;
    public long AsId { get => m_value; init => m_value = value; }
    public int AsIndex { get => checked((int)m_value) - 1; init => m_value = value + 1; }
    public int CompareTo(RandomizationTag other) => m_value.CompareTo(other.m_value);
    public bool Equals(RandomizationTag other) => m_value.Equals(other.m_value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RandomizationTag tag && Equals(tag);
    public override int GetHashCode() => m_value.GetHashCode();
    public override string ToString() => $"TagID: {m_value}";
}

/// <summary>
/// A definition for a randomization tag; metadata associated with a tag
/// </summary>
[DataContract]
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
    [DataMember(Name = "name")]
    public string Name { get; private init; }

    /// <summary>
    /// A description of what this tag controls.
    /// </summary>
    [DataMember(Name = "description")]
    public string Description { get; private init; }

    /// <summary>
    /// The parent of this tag, if any
    /// </summary>
    [DataMember(Name = "parent")]
    public RandomizationTag Parent { get; private init; }
}

[DataContract]
public struct KeyedRandomizationTag
{
    /// <summary>
    /// Create a deafult, null KeyedItem
    /// </summary>
    public KeyedRandomizationTag()
    {
        ID = new();
        Definition = new(); // Todo: Class default item?
    }

    /// <summary>
    /// Create a keyed item with the given item and ID
    /// </summary>
    public KeyedRandomizationTag(RandomizationTag id, RandomizationTagDefinition definition)
    {
        ID = id;
        Definition = definition;
    }

    /// <summary>
    /// Unique ID of the Item. IDs range from 1 to 2^53-1.
    /// </summary>
    [DataMember(Name = "id")] public readonly RandomizationTag ID;

    /// <summary>
    /// The Item object with the given ID
    /// </summary>
    [DataMember(Name = "definition")] public readonly RandomizationTagDefinition Definition;
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