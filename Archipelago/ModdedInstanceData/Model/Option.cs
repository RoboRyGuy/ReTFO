using Il2CppSystem.Xml.Serialization;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Represents an option in the Archipelago YAML
/// </summary>
[DataContract]
public abstract class Option
{
    public enum eType
    {
        /// <summary>
        /// An option that is either true or false
        /// </summary>
        Toggle,

        /// <summary>
        /// An option with a list of defined possible choices
        /// </summary>
        Selection,
    }

    /// <summary>
    /// Which tag list, set, or dict is being targetted
    /// </summary>
    public enum eTarget
    {
        /// <summary>
        /// Add tags to the whitelist set
        /// </summary>
        Whitelist,

        /// <summary>
        /// Add tags to the blacklist set
        /// </summary>
        Blacklist,

        /// <summary>
        /// Add tags to the start inventory dict.
        /// </summary>
        StartItems,

        /// <summary>
        /// Add tags to the early item dict.
        /// Note that only randomized items can be declared as early items.
        /// </summary>
        EarlyItems,

        /// <summary>
        /// Add a tag to the local item set
        /// </summary>
        LocalItems,

        /// <summary>
        /// Add a tag to the non-local item set
        /// </summary>
        NonLocalItems,

        /// <summary>
        /// Add a tag to the start item hint 
        /// </summary>
        StartItemHints,

        /// <summary>
        /// Add a tag to the start location hints
        /// </summary>
        StartLocationHints,

        /// <summary>
        /// Add a location to the exclude locations override list
        /// </summary>
        CustomExcludeLocations,

        /// <summary>
        /// Add a location to the priority locations override list
        /// </summary>
        CustomPriorityLocations,

        /// <summary>
        /// Add a tag to the goal items tag set
        /// </summary>
        GoalItems,

        // TODO: Item links and plando
    }

    const string TrueOptionName = "True";
    const string FalseOptionName = "False";

    public Option(eType type, string category, string description)
    {
        Type = type;
        Category = category;
        Description = description;
    }

    /// <summary>
    /// The type of option to be used
    /// </summary>
    [DataMember]
    public eType Type { get; init; }

    /// <summary>
    /// A string category to sort the option under
    /// </summary>
    [DataMember]
    public string Category { get; init; }

    /// <summary>
    /// A brief description of this option and how it works
    /// </summary>
    [DataMember]
    public string Description { get; init; }

    /// <summary>
    /// The possible choices for this option and what the choice corresponds to.
    /// If using a toggle, prefer using TrueChoice and FalseChoice.
    /// </summary>
    [DataMember]
    public SortedList<string, SortedList<eTarget, List<RandomizationTag>>> Choices { get; init; } = new(2);

    /// <summary>
    /// Gets the effect that is applied if this is a toggle option set to True
    /// </summary>
    public SortedList<eTarget, List<RandomizationTag>> TrueEffect
    {
        get
        {
            if (Type != eType.Toggle) 
                throw new ArgumentException($"Cannot get {nameof(TrueEffect)} on a non-toggle option!");
            return Choices[TrueOptionName];
        }
    }

    /// <summary>
    /// Gets the effect that is applied if this is a toggle option set to False
    /// </summary>
    public SortedList<eTarget, List<RandomizationTag>> FalseEffect
    {
        get
        {
            if (Type != eType.Toggle) 
                throw new ArgumentException($"Cannot get {nameof(FalseEffect)} on a non-toggle option!");
            return Choices[FalseOptionName];
        }
    }

}
