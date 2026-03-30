
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

/// <summary>
/// Various pieces of data used to determine if/how items and locations are randomized,
///  and to determine other behaviours relating to when randomization does / does not occur
/// </summary>
public class RandomizationData
{
    private enum eRandomizationFlags
    {
        TypeMask       = (0x01 << 4) - 1,
        DoNotRandomize = 0,
        Progression    = 0x01 << 1,
        Useful         = 0x01 << 2,
        Filler         = 0x01 << 3,
        Trap           = 0x01 << 4,
        AutoDiscover   = 0x01 << 5,
        RandomLike     = 0x01 << 6,
        UncollectOnRandomized = 0x01 << 7,
    }

    private eRandomizationFlags m_flags = 0;
    private bool getFlag(eRandomizationFlags flag) => (m_flags & flag) != 0;
    private eRandomizationFlags setFlag(eRandomizationFlags flag, bool value) =>
        value
        ? m_flags |= flag
        : m_flags &= ~flag;

    /// <summary>
    /// True if not randomized into progression, useful, filler, or trap categories.
    /// This is the preferred way to disallow randomization on an item or location.
    /// Items randomized in this way are considered event items and are labelled "progression" for pathing purposes.
    /// </summary>
    public bool IsNoneRandomization
    {
        get => (m_flags & eRandomizationFlags.TypeMask) == 0;
    }

    /// <summary>
    /// This location typically contains a progression item.
    /// This item is required to progress the game, and can be used to calculate path progression.
    /// </summary>
    public bool IsProgression
    {
        get => getFlag(eRandomizationFlags.Progression);
        set => setFlag(eRandomizationFlags.Progression, value);
    }

    /// <summary>
    /// This location is not a random or misc location nor a progression location.
    /// This item is considered particularly useful.
    /// </summary>
    public bool IsUseful
    {
        get => getFlag(eRandomizationFlags.Useful);
        set => setFlag(eRandomizationFlags.Useful, value);
    }

    /// <summary>
    /// This location is missable, random, or misc.
    /// This item is a minor item, perhaps one that can be obtained multiple times.
    /// </summary>
    public bool IsFiller
    {
        get => getFlag(eRandomizationFlags.Filler);
        set => setFlag(eRandomizationFlags.Filler, value);
    }

    /// <summary>
    /// This location is actively deterimental to check; it typically would cause a negative result.
    /// This item is actively negative and deterimental to player progress.
    /// </summary>
    public bool IsTrap
    {
        get => getFlag(eRandomizationFlags.Trap);
        set => setFlag(eRandomizationFlags.Trap, value);
    }

    /// <summary>
    /// No effect on locations.
    /// This item is "random-like", meaning it will act as a randomized item even if not randomized.
    /// </summary>
    public bool IsRandomLike
    {
        get => getFlag(eRandomizationFlags.RandomLike);
        set => setFlag(eRandomizationFlags.RandomLike, value);
    }

    /// <summary>
    /// This location will be immediately discovered when all its regions are discovered.
    /// No effect on items.
    /// </summary>
    public bool AutoDiscover
    {
        get => getFlag(eRandomizationFlags.AutoDiscover);
        set => setFlag(eRandomizationFlags.AutoDiscover, value);
    }

    /// <summary>
    /// No effect on locations.
    /// UncollectItem will be called at session start if this item is randomized.
    /// This is particularly useful for FloatingItems.
    /// </summary>
    public bool DoUncollectOnRandom
    {
        get => getFlag(eRandomizationFlags.UncollectOnRandomized);
        set => setFlag(eRandomizationFlags.UncollectOnRandomized, value);
    }

    /// <summary>
    /// Categories for randomization, which will be compared with those defined in the YAML during generation.
    /// If a location matches a randomization category, it (and its item) will not randomize.
    /// If an item matches a randomization category, it will try to randomize.
    /// </summary>
    public SortedSet<string> Categories { get; set; } = new();

}
