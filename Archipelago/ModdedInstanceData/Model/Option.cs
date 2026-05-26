using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Represents an option in the Archipelago YAML
/// </summary>
[DataContract]
public class Option
{
    /// <summary>
    /// The types of supported options
    /// </summary>
    public enum eType
    {
        /// <summary>
        /// An option that is either true or false, and is false by default
        /// </summary>
        Toggle,

        /// <summary>
        /// An option that is either true or false, and is true by default
        /// </summary>
        DefaultOnToggle,

        /// <summary>
        /// An option with a list of defined possible choices
        /// </summary>
        Choice,
    }

    /// <summary>
    /// Which tag list, set, or dict is being targetted
    /// </summary>
    public enum eTarget
    {
        /// <summary>
        /// Add tags to the whitelist set, enabling randomization for items and locations
        /// </summary>
        Whitelist,

        /// <summary>
        /// Add tags to the blacklist set, blocking randomization for items and locations
        /// </summary>
        Blacklist,

        /// <summary>
        /// Add tags to the start inventory dict.
        /// </summary>
        StartInventory,

        /// <summary>
        /// Add tags to the early item dict.
        /// Note that only randomized items can be declared as early items.
        /// </summary>
        EarlyItems,

        /// <summary>
        /// Add a tag to the local item set, forcing all instances of that tag
        ///  and its children to be local items.
        /// </summary>
        LocalItems,

        /// <summary>
        /// Add a tag to the non-local item set, forcing all instances of that
        ///  tag and its children to be non-local items
        /// </summary>
        NonLocalItems,

        /// <summary>
        /// Add a tag to the start hints dict, granting a hint for one child 
        ///  tag (item or location) for each entry in the dict
        /// </summary>
        StartHints,

        /// <summary>
        /// Add a location to the exclude locations override list,
        ///  setting all child locations as excluded locations
        /// </summary>
        CustomExcludeLocations,

        /// <summary>
        /// Add tags to the priority location tag set, setting all
        ///  child locations as priority locations.
        /// </summary>
        CustomPriorityLocations,

        /// <summary>
        /// Add a tag to the goal items blacklist tag set, allowing players to 
        ///  skip particular goal items
        /// </summary>
        GoalBlacklist,

        // TODO: Item links and plando
    }

    /// <summary>
    /// For the toggle option, the name of the 'True' choice
    /// </summary>
    const string TrueOptionName = "True";

    /// <summary>
    /// For the toggle option, the name of the 'False' choice
    /// </summary>
    const string FalseOptionName = "False";

    /// <summary>
    /// A default category for options you may use
    /// </summary>
    public const string DefaultCategory = "GTFO Options";

    /// <summary>
    /// Construct a new option of the provided type.
    /// </summary>
    /// <param name="type">The option's type or "mode"</param>
    /// <param name="name">The option's display name</param>
    /// <param name="category">The category this option lives under</param>
    /// <param name="description">A description of the option. This supports python reStructured text formatting.</param>
    public Option(eType type, string name, string category, string description)
    {
        Type = type;
        Name = name;
        Category = category;
        Description = description;

        if (type == eType.Toggle || type == eType.DefaultOnToggle)
        {
            Choices[TrueOptionName] = new();
            Choices[FalseOptionName] = new();
        }

        DefaultValue = string.Empty;
        if (type == eType.Toggle) DefaultValue = FalseOptionName;
        else if (type == eType.DefaultOnToggle) DefaultValue = TrueOptionName;
    }

    /// <summary>
    /// The type of option to be used
    /// </summary>
    [DataMember]
    public eType Type { get; init; }

    /// <summary>
    /// The name of the option as displayed to the user
    /// </summary>
    [DataMember]
    public string Name { get; init; }

    /// <summary>
    /// A string category to sort the option under
    /// </summary>
    [DataMember]
    public string Category { get; init; }

    /// <summary>
    /// A brief description of this option and how it works.
    /// This description supports python reStructured text formatting.
    /// </summary>
    [DataMember]
    public string Description { get; init; }

    /// <summary>
    /// The default value for the option
    /// </summary>
    [DataMember]
    public string DefaultValue { get; init; }

    /// <summary>
    /// The possible choices for this option and what the choice corresponds to.
    /// If using a toggle, prefer using TrueChoice and FalseChoice.
    /// </summary>
    [DataMember]
    public SortedList<string, SortedList<eTarget, List<RandomizationTag>>> Choices { get; init; } = new(2);

    /// <summary>
    /// Gets the effect list that is applied if this is a toggle option set to True
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
    /// Gets the effect list that is applied if this is a toggle option set to False
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
