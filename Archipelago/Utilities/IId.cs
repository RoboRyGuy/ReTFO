
namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// Anything implementing this interface can be converted to/from a 1-based index.
/// </summary>
public interface IId
{
    public long AsId { get; init; }
}
