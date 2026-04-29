
namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// Interface for structs which can be used to test if a struct is null.
/// The default construction of structs implementing this interface must be null.
/// </summary>
public interface INullable
{
    /// <summary>
    /// True if this struct is null
    /// </summary>
    public bool IsNull { get; }
}
