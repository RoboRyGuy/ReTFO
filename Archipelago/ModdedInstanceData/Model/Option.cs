using ReTFO.Archipelago.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

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

        /// <summary>
        /// An input which is a toggle
        /// </summary>
        Toggle,

        /// <summary>
        /// An input which is a selection of one or more values
        /// </summary>
        Choice,

        /// <summary>
        /// An input which is a value range with a custom min/max value
        /// </summary>
        Range,

        // ====================================================================
        // Operations

        /// <summary>
        /// Operation which outputs either 1 if the input is nonzero else zero
        /// </summary>
        ToBool,

        /// <summary>
        /// Treats the input as either False (zero) or True (not zero), and flips it.
        /// The output is either 1 for true or 0 for false.
        /// </summary>
        Not,

        /// <summary>
        /// Treats the inputs as either False (zero) or True (not zero) and ORs them together.
        /// The output is LParam if LParam is true, otherwise the output is RParam
        /// </summary>
        Or,

        /// <summary>
        /// Treats the inputs as either False (zero) or True (not zero) and ANDs them together.
        /// The output is RParam if true, 0 otherwise.
        /// </summary>
        And,

        /// <summary>
        /// Tests whether LParam is exactly equivalent to RParam.
        /// Outputs 1 if it is, outputs 0 otherwise.
        /// </summary>
        Equals,

        /// <summary>
        /// Tests whether LParam is exactly equivalent to RParam
        /// Outputs 0 if it is, outputs 1 otherwise.
        /// </summary>
        DoesNotEqual,

        /// <summary>
        /// Tests whether LParam is less than RParam
        /// Outputs 1 if it is, outputs 0 otherwise.
        /// </summary>
        LessThan,

        /// <summary>
        /// Tests whether LParam is less than or equal to RParam
        /// Outputs 1 if it is, outputs 0 otherwise.
        /// </summary>
        LessThanOrEqual,

        /// <summary>
        /// Tests whether LParam is greater than to RParam
        /// Outputs 1 if it is, outputs 0 otherwise.
        /// </summary>
        GreaterThan,

        /// <summary>
        /// Tests whether LParam is greater than or equal to RParam
        /// Outputs 1 if it is, outputs 0 otherwise.
        /// </summary>
        GreaterThanOrEqual,

        /// <summary>
        /// With inputs A, B, C; treats input A as either False (zero) or True (not zero).
        /// The outputs is B if A is True, otherwise C.
        /// </summary>
        Conditional,

        /// <summary>
        /// Operation which produces the negation of one input (-x)
        /// </summary>
        Negate,

        /// <summary>
        /// Operation which produces the reciprocal of one input (1/x)
        /// </summary>
        Reciprocal,

        /// <summary>
        /// An operator which adds two numbers together
        /// </summary>
        Add,

        /// <summary>
        /// An operator which subtracts RParam from LParam
        /// </summary>
        Subtract,

        /// <summary>
        /// An operator which multiplies two numbers together
        /// </summary>
        Multiply,

        /// <summary>
        /// An operator which divides LParam by RParam
        /// </summary>
        Divide,

        /// <summary>
        /// With inputs A, B, C; outputs the result of linear mapping (output = a * b + c).
        /// This is equivalent to the `mad` function in HLSL
        /// </summary>
        LinearMap,

        // ====================================================================
        // Effects

        /// <summary>
        /// Adds a single tag to a specified set
        /// </summary>
        AddToSet,

        /// <summary>
        /// Adds a count to a tag count for a particular tag
        /// </summary>
        AddCount,

        // ====================================================================
        // Special

        /// <summary>
        /// Creates a simple choice input field allowing users to whitelist, blacklist,
        ///  or ignore a specific tag. 
        /// </summary>
        WhiteOrBlacklist,
    }

    /// <summary>
    /// The set or count to target in a tag effect
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
        /// Add tags to the start vouchers dict.
        /// </summary>
        StartVouchers,

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
    /// A default category for options you may use
    /// </summary>
    public const string DEFAULT_OPTION_CATEGORY = "Miscellaneous Options";

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
}

/// <summary>
/// Simple wrapper around a long to help identify it as an OptionID, usable
///  for looking up an OptionID instance in GameData.
/// </summary>
[DataContract]
public struct OptionID : INullable, IId, IIndex, IComparable<OptionID>, IEquatable<OptionID>
{
    public OptionID() { }
    [DataMember(Name = "value")]
    private readonly long m_value = 0;

    public bool IsNull => m_value == 0;
    public long AsId { get => m_value; init => m_value = value; }
    public int AsIndex { get => checked((int)m_value) - 1; init => m_value = value + 1; }
    public int CompareTo(OptionID other) => m_value.CompareTo(other.m_value);
    public bool Equals(OptionID other) => m_value.Equals(other.m_value);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ItemID id && Equals(id);
    public override int GetHashCode() => m_value.GetHashCode();
    public override string ToString() => $"OptionID: {m_value}";
}

/// <summary>
/// An option with an ID associated with it
/// </summary>
[DataContract]
public struct KeyedOption : INullable
{
    /// <summary>
    /// Create a default, null KeyedOption
    /// </summary>
    public KeyedOption()
    {
        ID = new();
        Option = null!;
    }

    /// <summary>
    /// Create a keyed option with the given option and ID
    /// </summary>
    public KeyedOption(OptionID id, OptionBase option)
    {
        ID = id;
        Option = option;
    }

    /// <summary>
    /// Unique ID of the OptionBase
    /// </summary>
    [DataMember(Name = "id")] public readonly OptionID ID;

    /// <summary>
    /// The OptionBase object with the given ID
    /// </summary>
    [DataMember(Name = "option")] public readonly OptionBase Option;

    public bool IsNull => ID.IsNull;
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
        /// Specifies the contained value is an OptionID, and to use the output from that option as the value
        /// </summary>
        Option,
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
    /// Create a new option parameter targeting a tag. This is internally the same as a constant
    /// </summary>
    public static implicit operator OptionParameter(RandomizationTag tag)
        => new OptionParameter() { Type = eType.Constant, Value = tag.AsId };

    /// <summary>
    /// Create a new option parameter targeting a tag. This is internally the same as a constant
    /// </summary>
    public static implicit operator OptionParameter(TagResolver tag)
        => tag.SelfResolve();

    /// <summary>
    /// Create a new option parameter targeting an option by ID
    /// </summary>
    public static implicit operator OptionParameter(OptionID id)
        => new OptionParameter() { Type = eType.Option, Value = id.AsId, };
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
    /// <summary>
    /// The name to present to the user for this input
    /// </summary>
    [DataMember(Name = "display_name")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// The string name to use for this input
    /// </summary>
    [DataMember(Name = "description")]
    public required string Description { 
        get => m_description; 
        init => m_description = Option.AddLineBreaks(value); 
    }
    private string m_description = null!; // Initialized by required property `Description`

    /// <summary>
    /// The category to sort this input under
    /// </summary>
    [DataMember(Name = "category")]
    public required string Category { get; init; }

    /// <summary>
    /// The default value to use for this input
    /// </summary>
    [DataMember(Name = "default_value")]
    public required long DefaultValue { get; init; }

    /// <summary>
    /// If non-null, specifies an option which must evaluate to non-zero for this input to be visible.
    /// Note that this only affects visibility in the WebWorld (when I eventually create it), and does
    ///  not prevent this input from being used in any way.
    /// </summary>
    [DataMember(Name = "condition")]
    public required OptionID Condition { get; init; }
}

/// <summary>
/// An option input with either a True or False value.
/// The resulting value of this option will be 0 for False or 1 for True
/// </summary>
[DataContract]
public class OptionToggle : OptionInput
{
    public override Option.eType Type => Option.eType.Toggle;
}

/// <summary>
/// An option input based on a choice. The user may choose a value by name from a list of choices
/// </summary>
[DataContract]
public class OptionChoice : OptionInput
{
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
    public override Option.eType Type => Option.eType.Range;

    /// <summary>
    /// The min value of the range
    /// </summary>
    [DataMember(Name = "min")]
    public required float Min { get; init; }

    /// <summary>
    /// The max value of the range
    /// </summary>
    [DataMember(Name = "max")]
    public required float Max { get; init; }
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
    /// <summary>
    /// If non-null, specifies an option which must evaluate to non-zero for this effect to apply.
    /// If the effect evaluates to zero, the effect is ignored/discarded.
    /// </summary>
    [DataMember(Name = "condition")]
    public required OptionID Condition { get; init; } = new();
}

/// <summary>
/// An option effect which adds a tag to a target set
/// </summary>
[DataContract]
public class OptionAddToSet : OptionEffect
{
    public override Option.eType Type => Option.eType.AddToSet;

    /// <summary>
    /// The target which is affected; read the enum for details.
    /// Note that attempting to target a non-set target will throw an error.
    /// </summary>
    [DataMember(Name = "target")]
    public required Option.eTarget Target { get; init; }

    /// <summary>
    /// This option parameter will be interpretted as a tag ID and added to the target set
    /// </summary>
    [DataMember(Name = "tag")]
    public required OptionParameter Tag { get; init; }
}

/// <summary>
/// An option effect which adds a count to the specified tag key in a tag count
/// </summary>
[DataContract]
public class OptionAddCount : OptionEffect
{
    public override Option.eType Type => Option.eType.AddCount;

    /// <summary>
    /// The target which is affected; read the enum for details.
    /// Note that attempting to target a non-dict target will throw an error.
    /// </summary>
    [DataMember(Name = "target")]
    public required Option.eTarget Target { get; init; }

    /// <summary>
    /// This option parametere will be interpretted as a tag ID and used as the key for the target count
    /// </summary>
    [DataMember(Name = "tag")]
    public required OptionParameter Tag { get; init; }

    /// <summary>
    /// The count to add to the key in the specified target
    /// </summary>
    [DataMember(Name = "count")]
    public required OptionParameter Count { get; init; }
}

/// <summary>
/// A special option which creates an input field allowing users to whitelist,
///  blacklist, or ignore a tag. This also applies the effect.
/// Option values: 0 = Whitelisted, 1 = None, 2 = Blacklist
/// </summary>
[DataContract]
public class OptionWhiteOrBlacklist : OptionInput
{
    public override Option.eType Type => Option.eType.WhiteOrBlacklist;

    public const string DESC_SUFFIX = ""
        + "\nWhitelist: Enables for all unless blacklisted elsewhere"
        + "\nBlacklist: Disables for all."
        + "\nNone: Defer to other settings. If no other setting is relevant, defaults to blacklisted.";

    /// <summary>
    /// The tag to add to either the whitelist or the blacklist.
    /// </summary>
    [DataMember(Name = "tag")]
    public required OptionParameter Tag { get; init; }
}
