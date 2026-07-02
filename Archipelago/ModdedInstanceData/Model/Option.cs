using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// Helper namespace for common option values, consts, etc
/// </summary>
public static class Option
{
    /// <summary>
    /// The types of supported options
    /// </summary>
    public enum eType
    {
        // ====================================================================
        // Inputs

        Toggle,
        Choice,
        Range,

        // ====================================================================
        // Operations

        ToBool,
        Not,
        Or,
        And,
        Equals,
        DoesNotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        Conditional,
        Negate,
        Reciprocal,
        Add,
        Subtract,
        Multiply,
        Divide,
        LinearMap,

        // ====================================================================
        // Effects

        AddToSet,
        AddCountToDict,
        AddAllToDict,

        // ====================================================================
        // Special

        RegionTagOption,
        LocationTagOption,
        ItemTagOption,
    }

    /// <summary>
    /// Target of a 'set' operation (ie HashSet, not the verb 'set')
    /// </summary>
    public enum eSetTarget
    { 
        RegionWhitelist,
        RegionBlacklist,
        LocationWhitelist,
        LocationBlacklist,
        ItemWhitelist,
        ItemBlacklist
    }

    /// <summary>
    /// Target of a `dict` operation
    /// </summary>
    public enum eDictTarget
    {
        GoalItems,
        StartInventory,
        StartVouchers,
        EarlyItems,
        LocalItems,
        NonLocalItems,
        ItemHints,
        LocationHints,
        PriorityLocations,
        ExcludeLocations,
    }

    /// <summary>
    /// A default category for options you may use
    /// </summary>
    public const string DEFAULT_OPTION_CATEGORY = "Game Options";

    /// <summary>
    /// A warning message which should be appended to the description of any input
    /// which increases the number of early items.
    /// </summary>
    public const string EARLY_WARNING_SUFFIX =
        "\nNote: GTFO has a limited amount of space for early items."
        + " Adding too many early items many result in FILL errors!";

    /// <summary>
    /// Adds line breaks to long descriptions so they fit within a specific character width limit
    /// </summary>
    /// <param name="source">The text to format</param>
    /// <param name="maxLen">The max width of the text</param>
    /// <returns></returns>
    public static string AddLineBreaks(string source, int maxLen = 80)
    {
        StringBuilder output = new(source.Length);
        int lastSpace = -1;
        int lastLineBreak = 0;
        bool isQuoteOpen = false;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\"') isQuoteOpen = !isQuoteOpen;
            else if (c == ' ' && !isQuoteOpen) lastSpace = i;
            else if (c == '\n')
            {
                lastLineBreak = i;
                lastSpace = -1;
            }
            output.Append(c);

            int currentLineLength = i - lastLineBreak;
            if (
                (isQuoteOpen && (currentLineLength > (maxLen + 10)) && (lastSpace != -1))
                || (!isQuoteOpen && (currentLineLength > maxLen) && (lastSpace != -1))
            )
            {
                output[lastSpace] = '\n';
                lastLineBreak = lastSpace;
                lastSpace = -1;
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Make a sort key for a specific ID
    /// </summary>
    /// <param name="data">The game data used to create the ID</param>
    /// <param name="id">The ID to make a sort key for</param>
    /// <returns>The sort key</returns>
    public static uint[] MakeSortKey(Game.Data data, RegionID id)
        => IdToUint(data.Regions.MakeChain(id));

    /// <inheritdoc cref="MakeSortKey(Game.Data, RegionID)"/>
    public static uint[] MakeSortKey(Game.Data data, LocationID id)
        => IdToUint(data.Locations.MakeChain(id));

    /// <inheritdoc cref="MakeSortKey(Game.Data, RegionID)"/>
    public static uint[] MakeSortKey(Game.Data data, ItemID id)
        => IdToUint(data.Items.MakeChain(id));

    /// <summary>
    /// Helper which converts an array of IDs to their plain uint values
    /// </summary>
    /// <typeparam name="TID">The type of ID to convert</typeparam>
    /// <param name="ids">The array of IDs</param>
    /// <returns>A new array with the IDs as uints</returns>
    private static uint[] IdToUint<TID>(TID[] ids) where TID : struct, ITagID
    {
        uint[] result = new uint[ids.Length];
        MemoryMarshal.Cast<TID, uint>(ids.AsSpan()).CopyTo(result);
        return result;
    }
}

/// <summary>
/// Simple wrapper around a long to help identify it as an OptionID, usable
///  for looking up an OptionID instance in GameData.
/// </summary>
[DataContract]
public struct OptionID : ITagID, IEquatable<OptionID>, IComparable<OptionID>
{
    [DataMember(Name = "id")]
    public uint ID { get; init; }

    public bool IsNull => ID == 0;
    public int AsIndex { get => checked((int)ID - 1); init => ID = unchecked((uint)value + 1u); }
    public bool Equals(OptionID other) => ID == other.ID;
    public int CompareTo(OptionID other) => ID.CompareTo(other.ID);
    public override string ToString() => $"OptionID {ID}";
}

/// <summary>
/// A parameter in an option, which identifies how to get an input value
/// </summary>
[DataContract]
public struct OptionParameter
{
    /// <summary>
    /// The type of parameter
    /// </summary>
    public enum eType
    {
        /// <summary>
        /// Specifies the contained value should be used as-is
        /// </summary>
        Constant,

        /// <summary>
        /// Specifies this is a constant and that constant comes from a RegionID
        /// </summary>
        RegionID,

        /// <summary>
        /// Specifies this is a constant and that constant comes from a LocationID
        /// </summary>
        LocationID,

        /// <summary>
        /// Specifies this is a constant and that constant comes from a ItemID
        /// </summary>
        ItemID,

        /// <summary>
        /// Specifies the contained value is an OptionID, and to use the output from that option as the value
        /// </summary>
        OptionID,
    }

    /// <summary>
    /// The type of parameter this represents
    /// </summary>
    [DataMember(Name = "type")]
    public eType Type { get; init; }

    /// <summary>
    /// The value to use in this parameter
    /// </summary>
    [DataMember(Name = "value")]
    public long Value { get; init; }

    /// <summary>
    /// Create a new option parameter targeting a constant value
    /// </summary>
    public static implicit operator OptionParameter(long value)
        => new OptionParameter() { Type = eType.Constant, Value = value };

    /// <summary>
    /// Create a new option parameter targeting a RegionID
    /// </summary>
    public static implicit operator OptionParameter(RegionID id)
        => new OptionParameter() { Type = eType.RegionID, Value = id.ID };

    /// <summary>
    /// Create a new option parameter targeting a LocationID
    /// </summary>
    public static implicit operator OptionParameter(LocationID id)
        => new OptionParameter() { Type = eType.LocationID, Value = id.ID };

    /// <summary>
    /// Create a new option parameter targeting an ItemID
    /// </summary>
    public static implicit operator OptionParameter(ItemID id)
        => new OptionParameter() { Type = eType.ItemID, Value = id.ID };

    /// <summary>
    /// Create a new option parameter targeting an option by ID
    /// </summary>
    public static implicit operator OptionParameter(OptionID id)
        => new OptionParameter() { Type = eType.OptionID, Value = id.ID, };
}

/// <summary>
/// Shared base for all option classes
/// </summary>
[DataContract]
public abstract class OptionBase
{
    /// <summary>
    /// The instance of option this class is
    /// </summary>
    [DataMember(Name = "type")]
    public abstract Option.eType Type { get; }
}

/// <summary>
/// The base for input options
/// </summary>
[DataContract]
public abstract class OptionInput : OptionBase
{
    public OptionInput(
        string displayName, string description, string category,
        uint[] categorySort, long defaultValue, OptionID condition
    ) {
        DisplayName = displayName;
        Description = description;
        Category = category;
        CategorySort = categorySort;
        DefaultValue = defaultValue;
        Condition = condition;
    }

    /// <summary>
    /// The name to present to the user for this input
    /// </summary>
    [DataMember(Name = "display_name")]
    public string DisplayName { get; init; }

    /// <summary>
    /// The string name to use for this input
    /// </summary>
    [DataMember(Name = "description")]
    public string Description { 
        get => m_description; 
        init => m_description = Option.AddLineBreaks(value); 
    }
    private string m_description = null!; // Initialized by required property `Description`

    /// <summary>
    /// The category to sort this input under
    /// </summary>
    [DataMember(Name = "category")]
    public string Category { get; init; }

    /// <summary>
    /// A set of integers used to sort in a category.
    /// This sorts similar to strings: 
    ///  1. The first int is checked, and the lower of the two goes first.
    ///  2. If both match, the second int is checked, and the lower of those two goes first.
    ///  3. If all ints match, sorts based on insertion order (the sort is stable).
    /// </summary>
    [DataMember(Name = "category_sort")]
    public uint[] CategorySort { get; init; }

    /// <summary>
    /// The default value to use for this input
    /// </summary>
    [DataMember(Name = "default_value")]
    public long DefaultValue { get; init; }

    /// <summary>
    /// If non-null, specifies an option which must evaluate to non-zero for this input to be visible.
    /// Note that this only affects visibility in the WebWorld (when I eventually create it), and does
    ///  not prevent this input from being used in any way.
    /// </summary>
    [DataMember(Name = "condition")]
    public OptionID Condition { get; init; }
}

/// <summary>
/// An option input with either a True or False value.
/// The resulting value of this option will be 0 for False or 1 for True
/// </summary>
[DataContract]
public class OptionToggle : OptionInput
{
    public OptionToggle(
        string displayName, string description, string category,
        uint[] categorySort, long defaultValue, OptionID condition
    ) : base(displayName, description, category, categorySort, defaultValue, condition) { }

    public override Option.eType Type => Option.eType.Toggle;
}

/// <summary>
/// An option input based on a choice. The user may choose a value by name from a list of choices
/// </summary>
[DataContract]
public class OptionChoice : OptionInput
{
    public OptionChoice(
        string displayName, string description, string category,
        uint[] categorySort, long defaultValue, OptionID condition,
        List<string> choiceNames, List<long> choiceValues
    ) : base(displayName, description, category, categorySort, defaultValue, condition) 
    {
        ChoiceNames = choiceNames;
        ChoiceValues = choiceValues;
    }

    public override Option.eType Type => Option.eType.Choice;

    /// <summary>
    /// Names for the choices available for selection
    /// </summary>
    [DataMember(Name = "choice_names")]
    public List<string> ChoiceNames { get; init; } = new();

    /// <summary>
    /// The values to associate with the above choices, matched by index
    /// </summary>
    [DataMember(Name = "choice_values")]
    public List<long> ChoiceValues { get; init; } = new();
}

/// <summary>
/// An option input based on a range. The user may enter a value within that range
/// </summary>
[DataContract]
public class OptionRange : OptionInput
{
    public OptionRange(
        string displayName, string description, string category,
        uint[] categorySort, long defaultValue, OptionID condition,
        float min, float max
    ) : base(displayName, description, category, categorySort, defaultValue, condition) 
    {
        Min = min;
        Max = max;
    }

    public override Option.eType Type => Option.eType.Range;

    /// <summary>
    /// The min value of the range
    /// </summary>
    [DataMember(Name = "min")]
    public float Min { get; init; }

    /// <summary>
    /// The max value of the range
    /// </summary>
    [DataMember(Name = "max")]
    public float Max { get; init; }
}

/// <summary>
/// The base for an option operation, which manipulates values prior to triggering an option effect
/// See OptionOperators.cs for all the implementations of this class
/// </summary>
[DataContract]
public abstract class OptionOperation : OptionBase { }

/// <summary>
/// Base class for options which produce an effect once computed
/// </summary>
[DataContract]
public abstract class OptionEffect : OptionBase 
{
    public OptionEffect(OptionID condition)
        => Condition = condition;

    /// <summary>
    /// If non-null, specifies an option which must evaluate to non-zero for this effect to apply.
    /// If the effect evaluates to zero, the effect is ignored/discarded.
    /// </summary>
    [DataMember(Name = "condition")]
    public OptionID Condition { get; init; } = new();
}

/// <summary>
/// An option effect which adds a tag to a target set
/// </summary>
[DataContract]
public class OptionAddToSet : OptionEffect
{
    public OptionAddToSet(OptionID condition, Option.eSetTarget target, OptionParameter tag)
        :   base(condition)
    {
        Target = target;
        Tag = tag;
    }

    public override Option.eType Type => Option.eType.AddToSet;

    /// <summary>
    /// The set to add a tag to
    /// </summary>
    [DataMember(Name = "target")]
    public Option.eSetTarget Target { get; init; }

    /// <summary>
    /// The ID to add to the set; if an option ID, its output will be converted to the relevant ID type
    /// </summary>
    [DataMember(Name = "tag")]
    public OptionParameter Tag { get; init; }
}

/// <summary>
/// An option effect which adds a count to the specified tag key in a tag count
/// </summary>
[DataContract]
public class OptionAddCount : OptionEffect
{
    public OptionAddCount(OptionID condition, Option.eDictTarget target, OptionParameter tag, OptionParameter count)
        : base(condition)
    {
        Target = target;
        Tag = tag;
        Count = count;
    }

    public override Option.eType Type => Option.eType.AddCountToDict;

    /// <summary>
    /// The dict to add to
    /// </summary>
    [DataMember(Name = "target")]
    public Option.eDictTarget Target { get; init; }

    /// <summary>
    /// The ID to use as a key; if an option ID, its output will be converted to the relevant ID type
    /// </summary>
    [DataMember(Name = "tag")]
    public OptionParameter Tag { get; init; }

    /// <summary>
    /// The count to add to the ID in the specified target
    /// </summary>
    [DataMember(Name = "count")]
    public OptionParameter Count { get; init; }
}

/// <summary>
/// An option effect which sets a dict key's value to a special value indicating 'all'
/// </summary>
[DataContract]
public class OptionAddAll : OptionEffect
{
    public OptionAddAll(OptionID condition, Option.eDictTarget target, OptionParameter tag)
        : base(condition)
    {
        Target = target;
        Tag = tag;
    }

    public override Option.eType Type => Option.eType.AddAllToDict;

    /// <summary>
    /// The dict to add to
    /// </summary>
    [DataMember(Name = "target")]
    public Option.eDictTarget Target { get; init; }

    /// <summary>
    /// The ID to use as a key; if an option ID, its output will be converted to the relevant ID type
    /// </summary>
    [DataMember(Name = "tag")]
    public OptionParameter Tag { get; init; }
}

/// <summary>
/// A special option which creates a choice field allowing users to whitelist,
///  blacklist, or ignore a tag. This also applies the effect.
/// Option values: 0 = Whitelisted, 1 = None, 2 = Blacklist
/// </summary>
[DataContract]
public abstract class OptionTagOption : OptionInput
{
    public OptionTagOption(
        string displayName, string description, string category,
        uint[] categorySort, long defaultValue, OptionID condition,
        OptionParameter tag
    ) : base(displayName, description, category, categorySort, defaultValue, condition)
    {
        Tag = tag;
    }

    /// <summary>
    /// Suffix which should be placed on descriptions for this option which explains the choices
    /// </summary>
    public const string DESC_SUFFIX = ""
        + "\nWhitelist: Enables for all unless blacklisted elsewhere."
        + "\nBlacklist: Disables for all."
        + "\nNone: Defer to other relevant setting(s). If no other relevant setting is set, defaults to blacklisted.";

    /// <summary>
    /// The tag to add to either the whitelist or the blacklist.
    /// </summary>
    [DataMember(Name = "tag")]
    public OptionParameter Tag { get; init; }
}

/// <inheritdoc cref="OptionTagOption"/>
[DataContract]
public class OptionRegionTagOption : OptionTagOption
{
    public OptionRegionTagOption(
        string displayName, string description, string category,
        uint[] categorySort, long defaultValue, OptionID condition,
        OptionParameter tag
    ) : base(displayName, description, category, categorySort, defaultValue, condition, tag) { }

    public override Option.eType Type => Option.eType.RegionTagOption;
}

/// <inheritdoc cref="OptionTagOption"/>
[DataContract]
public class OptionLocationTagOption : OptionTagOption
{
    public OptionLocationTagOption(
        string displayName, string description, string category,
        uint[] categorySort, long defaultValue, OptionID condition,
        OptionParameter tag
    ) : base(displayName, description, category, categorySort, defaultValue, condition, tag) { }

    public override Option.eType Type => Option.eType.LocationTagOption;
}

/// <inheritdoc cref="OptionTagOption"/>
[DataContract]
public class OptionItemTagOption : OptionTagOption
{
    public OptionItemTagOption(
        string displayName, string description, string category,
        uint[] categorySort, long defaultValue, OptionID condition,
        OptionParameter tag
    ) : base(displayName, description, category, categorySort, defaultValue, condition, tag) { }

    public override Option.eType Type => Option.eType.ItemTagOption;
}
