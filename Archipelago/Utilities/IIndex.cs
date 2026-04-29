
namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// Anything implementing this interface can be converted to/from a zero-based list index.
/// Use with care.
/// </summary>
public interface IIndex
{
    public int AsIndex { get; init; }
}
