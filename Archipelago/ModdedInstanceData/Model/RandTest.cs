
namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Wraps around a randomization test result, and informs on how an entity is or is not randomized.
/// </summary>
public struct RandTest
{
    public enum eType
    {
        /// <summary>
        /// Default value. An unset test; treated as not randomized
        /// </summary>
        None = 0,

        /// <summary>
        /// This is not randomized, but should still be treated like it is
        /// </summary>
        Randomlike = 1,

        /// <summary>
        /// This is randomized. If a location, the contained item is also randomized
        /// </summary>
        Randomized = 2,

        /// <summary>
        /// This location is not allowed to be randomized due to its tag(s) being blacklisted
        /// </summary>
        LocationBlacklisted = 4,

        /// <summary>
        /// There is no whitelist tag allowing this location to be randomized
        /// </summary>
        LocationNotWhitelisted = 6,

        /// <summary>
        /// This item has a non-null required expedition AND that expedition is not enabled
        /// </summary>
        ItemExpeditionNotRandomized = 8,

        /// <summary>
        /// This item (or the item contained by this location) is not allowed to be randomized due to its tag(s) being blacklisted
        /// </summary>
        ItemBlacklisted = 10,

        /// <summary>
        /// There is no whitelist tag allowing this item (or the item contained by this location) to be randomized
        /// </summary>
        ItemNotWhitelisted = 12,

        /// <summary>
        /// An empty location which was not filled with an item
        /// </summary>
        UnusedEmptyLocation = 14,
    }

    public RandTest() { m_value = eType.None; }
    public RandTest(eType type) { m_value = type; }

    /// <summary>
    /// The packed result type of the randomization test
    /// </summary>
    private readonly eType m_value;

    /// <summary>
    /// The unpacked result type for the test, ignoring the RandomLike bit
    /// </summary>
    public eType Type => m_value & ~eType.Randomlike;

    /// <summary>
    /// True if the randomization type is "Randomized"
    /// </summary>
    public bool IsRandomized => Type == eType.Randomized;

    /// <summary>
    /// True if the randomization type is "Randomlike"
    /// </summary>
    public bool IsRandomLike => (m_value & eType.Randomlike) == eType.Randomlike;

    /// <summary>
    /// True if the randomization is either "Randomized" or "Randomlike"
    /// </summary>
    public bool IsTreatedAsRandom => IsRandomized || IsRandomLike;
}

