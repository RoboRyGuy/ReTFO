using System.Runtime.Serialization;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

// ================================================================================================
// Separate file holding the various implemented option operations

/// <summary>
/// The base for unary operations (which have a singular input)
/// </summary>
[DataContract]
public abstract class OptionUnaryOperation : OptionOperation
{
    public OptionUnaryOperation(OptionParameter param) => Param = param;

    /// <summary>
    /// The first parameter in this operation
    /// </summary>
    [DataMember(Name = "param")]
    public OptionParameter Param { get; init; }
}

/// <summary>
/// If the input is zero, outputs zero; otherwise, outputs 1
/// </summary>
public class OptionToBoolOperation : OptionUnaryOperation
{
    public OptionToBoolOperation(OptionParameter param) : base(param) { }

    public override Option.eType Type => Option.eType.ToBool;
}

/// <summary>
/// Outputs the bool opposite of the input
/// </summary>
public class OptionNotOperation : OptionUnaryOperation
{
    public OptionNotOperation(OptionParameter param) : base(param) { }

    public override Option.eType Type => Option.eType.Not;
}

/// <summary>
/// Multiplies the input by -1
/// </summary>
public class OptionNegateOperation : OptionUnaryOperation
{
    public OptionNegateOperation(OptionParameter param) : base(param) { }

    public override Option.eType Type => Option.eType.Negate;
}

/// <summary>
/// Divides 1 by the input
/// </summary>
public class OptionReciprocalOperation : OptionUnaryOperation
{
    public OptionReciprocalOperation(OptionParameter param) : base(param) { }

    public override Option.eType Type => Option.eType.Reciprocal;
}

/// <summary>
/// The base for binary operations (which have two inputs, a left and right input)
/// </summary>
[DataContract]
public abstract class OptionBinaryOperation : OptionOperation
{
    public OptionBinaryOperation(OptionParameter lParam, OptionParameter rParam)
    {
        LParam = lParam;
        RParam = rParam;
    }

    /// <summary>
    /// The "left" parameter, ie 'a' in 'a * b = c'
    /// </summary>
    [DataMember(Name = "l_param")]
    public OptionParameter LParam { get; init; }

    /// <summary>
    /// The "right" parameter, ie 'b' in 'a * b = c'
    /// </summary>
    [DataMember(Name = "r_param")]
    public OptionParameter RParam { get; init; }
}

/// <summary>
/// An or operation; outputs the first value if it's true, else the second value
/// </summary>
public class OptionOrOperation : OptionBinaryOperation
{
    public OptionOrOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.Or;
}

/// <summary>
/// An and operation; outputs the second value only if both values are true
/// </summary>
public class OptionAndOperation : OptionBinaryOperation
{
    public OptionAndOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.And;
}

/// <summary>
/// Tests whether LParam is equal to RParam
/// </summary>
public class OptionEqualsOperation : OptionBinaryOperation
{
    public OptionEqualsOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.Equals;
}

/// <summary>
/// Tests whether LParam is not equal to RParam
/// </summary>
public class OptionDoesNotEqualOperation : OptionBinaryOperation
{
    public OptionDoesNotEqualOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.DoesNotEqual;
}

/// <summary>
/// Tests whether LParam is less than RParam
/// </summary>
public class OptionLessThanOperation : OptionBinaryOperation
{
    public OptionLessThanOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.LessThan;
}

/// <summary>
/// Tests whether LParam is less than or equal to RParam
/// </summary>
public class OptionLessThanOrEqualOperation : OptionBinaryOperation
{
    public OptionLessThanOrEqualOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.LessThanOrEqual;
}

/// <summary>
/// Tests whether LParam is greater than RParam
/// </summary>
public class OptionGreaterThanOperation : OptionBinaryOperation
{
    public OptionGreaterThanOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.GreaterThan;
}

/// <summary>
/// Tests whether LParam is greater than or equal to RParam
/// </summary>
public class OptionGreaterThanOrEqualOperation : OptionBinaryOperation
{
    public OptionGreaterThanOrEqualOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.GreaterThanOrEqual;
}

/// <summary>
/// An add operation
/// </summary>
public class OptionAddOperation : OptionBinaryOperation
{
    public OptionAddOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.Add;
}

/// <summary>
/// A subtract operation; subtracts RParam from LParam
/// </summary>
public class OptionSubtractOperation : OptionBinaryOperation
{
    public OptionSubtractOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.Subtract;
}

/// <summary>
/// A multiply operation
/// </summary>
public class OptionMultiplyOperation : OptionBinaryOperation
{
    public OptionMultiplyOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.Multiply;
}

/// <summary>
/// A divide operation; divides LParam by RParam
/// </summary>
public class OptionDivideOperation : OptionBinaryOperation
{
    public OptionDivideOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.Divide;
}

/// <summary>
/// Gets the value of the specified bit, 0-inexed from least significant to most; outputs either one or zero
/// </summary>
public class OptionGetBitOperation : OptionBinaryOperation
{
    public OptionGetBitOperation(OptionParameter lParam, OptionParameter rParam) : base(lParam, rParam) { }

    public override Option.eType Type => Option.eType.GetBit;
}

/// <summary>
/// The base for ternary operations (which have 3 inputs: a, b, and c)
/// </summary>
public abstract class OptionTernaryOperation : OptionOperation
{
    public OptionTernaryOperation(OptionParameter aParam, OptionParameter bParam, OptionParameter cParam)
    {
        AParam = aParam;
        BParam = bParam;
        CParam = cParam;
    }

    /// <summary>
    /// The first parameter; its use varies
    /// </summary>
    [DataMember(Name = "a_param")]
    public OptionParameter AParam { get; init; }

    /// <summary>
    /// The second parameter; its use varies
    /// </summary>
    [DataMember(Name = "b_param")]
    public OptionParameter BParam { get; init; }

    /// <summary>
    /// The third parameter; its use varies
    /// </summary>
    [DataMember(Name = "c_param")]
    public OptionParameter CParam { get; init; }
}

/// <summary>
/// A ternary operation which evaluates A as a bool; if A is true, outputs B, else outputs C.
/// This is equivalent to the ternary conditional: `A ? B : C`
/// </summary>
public class OptionConditionalOperation : OptionTernaryOperation
{
    public OptionConditionalOperation(OptionParameter aParam, OptionParameter bParam, OptionParameter cParam)
        : base(aParam, bParam, cParam) { }

    public override Option.eType Type => Option.eType.Conditional;
}

/// <summary>
/// Using the inputs A, B, and C, outputs A * B + C
/// </summary>
public class OptionLinearMapOperation : OptionTernaryOperation
{
    public OptionLinearMapOperation(OptionParameter aParam, OptionParameter bParam, OptionParameter cParam)
        : base(aParam, bParam, cParam) { }

    public override Option.eType Type => Option.eType.LinearMap;
}