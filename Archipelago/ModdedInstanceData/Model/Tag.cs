using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// The definition of a tag; all elements of a tag other than its name
/// </summary>
[DataContract]
public readonly record struct TagDefinition<TID>
    where TID : struct, ITagID
{
    /// <summary>
    /// Short description of the tag and what it matches
    /// </summary>
    [DataMember(Name = "description")]
    public string Description { get; private init; }

    /// <summary>
    /// Parent of the tag
    /// </summary>
    public TID Parent { get; private init; }

    /// <summary>
    /// Additional parents for the tag, relevant
    /// </summary>
    public TID[]? OtherParents { get; private init; }

    /// <summary>
    /// Helper to get enumeration of all parents
    /// </summary>
    [DataMember(Name = "parents")]
    public IEnumerable<TID> AllParents => (OtherParents ?? Enumerable.Empty<TID>()).Prepend(Parent);

    public TagDefinition(string description, TID parent)
    {
        Description = description;
        Parent = parent;
        OtherParents = null;
    }

    public TagDefinition(string description, TID parent, params TID[] otherParents)
    {
        Description = description;
        Parent = parent;
        OtherParents = otherParents;
    }
}

/// <summary>
/// A generic tag; has a name, description, and one or more parents
/// </summary>
public readonly record struct Tag<TID> where TID : struct, ITagID
{
    /// <summary>
    /// Name of the tag
    /// </summary>
    public string Name { get; private init; }

    /// <summary>
    /// Definition of the tag
    /// </summary>
    public TagDefinition<TID> Definition { get; private init; }

    public Tag(string name, TagDefinition<TID> definition)
    {
        Name = name;
        Definition = definition;
    }

    public Tag(string name, string description, TID parent)
    {
        Name = name;
        Definition = new(description, parent);
    }

    public Tag(string name, string description, TID parent, params TID[] otherParents)
    {
        Name = name;
        Definition = new(description, parent, otherParents);
    }
}

/// <summary>
/// Interface used by IDs used for tags
/// </summary>
public interface ITagID: INullable, IIndex, IEquatable<ITagID>, IComparable<ITagID>
{
    /// <summary>
    /// Get the underlying ID value represented by this ID
    /// </summary>
    public uint ID { get; init; }

    /// <inheritdoc cref="INullable.IsNull"/>
    bool INullable.IsNull => ID == 0;

    /// <inheritdoc cref="IIndex.AsIndex"/>
    int IIndex.AsIndex { get => checked((int)ID - 1); init => ID = unchecked((uint)value + 1u); }

    /// <inheritdoc cref="IEquatable{ITagID}.Equals(ITagID?)"/>
    bool IEquatable<ITagID>.Equals(ITagID? other) => other == null ? false : ID == other.ID;

    /// <inheritdoc cref="IComparable{ITagID}.CompareTo(ITagID?)"/>
    int IComparable<ITagID>.CompareTo(ITagID? other) => ID.CompareTo(other?.ID ?? 0);
}
