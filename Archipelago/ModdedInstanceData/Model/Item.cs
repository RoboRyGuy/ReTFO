
using LevelGeneration;
using System;
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using Player;
using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

/// <summary>
/// Represents an item in GTFO. Some examples include:
/// <list type="bullet">
///   <item>Colored keys and bulkhead keys</item>
///   <item>"Extraction Reachable" (an event item)</item>
///   <item>Objective items - ie generators or a central gen cluster</item>
/// </list>
/// </summary>
public abstract class Item
{
    /// <summary>
    /// Helper for handling string lists using implicit conversions
    /// </summary>
    public struct CategoryList
    {
        public CategoryList() => Categories = new(0);
        public List<string> Categories { get; init; }
        public static implicit operator CategoryList(string category)
            => new CategoryList() { Categories = new(1) { category } };
        public static implicit operator CategoryList(List<string> categories)
            => new CategoryList() { Categories = categories };
        public static implicit operator List<string>(CategoryList categoryList)
            => categoryList.Categories;
    }

    /// <summary>
    /// Construct an abstract item from a name and categories
    /// </summary>
    /// <param name="name">The name of the item</param>
    public Item(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Name of this item. Names must be unique per item.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// ID of this item. This will be assigned when the item is registered.
    /// </summary>
    public long ID { get; set; } = 0L;

    /// <summary>
    /// Helper for visualizing in debugger
    /// </summary>
    /// <returns></returns>
    public override string ToString() => Name;

    // === Virtuals for item categorization and randomization =================

    /// <summary>
    /// Categories used by this item. Only use these categories where absolutely necessary.
    /// Categories are typically used only if you need a bunch of an item, and you need unique data to
    ///  perform OnCollect or similar calls for each item. For an example, see <see cref="Features.Terminals.TerminalPasswordHandler"/>
    /// </summary>
    public virtual List<string> Categories => new(0);

    /// <summary>
    /// Randomization data used to determine when / how the item is randomized.
    /// </summary>
    public abstract RandomizationData RandData { get; }

    // === Virtuals for handling Item being obtained ==========================

    /// <summary>
    /// Called immediately when the item is obtained.
    /// Note that this can be called multiple times if a checkpoint is loaded from before the item being obtained.
    /// </summary>
    /// <param name="stateTracker">The stateTracker for this session</param>
    /// <param name="sourceLocationId">The ID of the location this was found in if found in this lobby.</param>
    /// <param name="player">The player who found the item, if that player is in this lobby (for randomlike items)</param>
    /// <remarks>
    /// The sourceLocationId is supplied during <see cref="StateTracker.eState.FakeConnect"/>, where it can be used for debug.
    /// Otherwise, it is supplied when the item is not randomized but is randomlike.
    /// </remarks>
    public virtual void OnItemObtained(StateTracker stateTracker, long sourceLocationId, PlayerAgent? player) { }

    /// <summary>
    /// Called immediately when the item is lost - Items can only be lost by a call to "uncollect".
    /// </summary>
    /// <param name="stateTracker">The stateTracker for this session</param>
    public virtual void OnItemLost(StateTracker stateTracker) { }

    /// <summary>
    /// Called just after loading into an expedition if this item has been previously obtained
    /// </summary>
    /// <param name="stateTracker">The stateTracker for this session</param>
    /// <param name="data">The expedition being started</param>
    public virtual void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data) { }

    /// <summary>
    /// Called when the player retrieves the item from the terminal item system (only if placed in the system)
    /// </summary>
    /// <param name="stateTracker">Current StateTracker</param>
    /// <param name="terminal">The terminal the item was claimed from. Useful for custom textual output</param>
    /// <returns>
    /// Expected to return an enumerable of actions. The enumerable will be immediately enumerated and placed in a list.
    /// Each action in the enumerable will be executed in order, pausing when the terminal is processing.
    /// </returns>
    /// <remarks>
    /// The intended use of the output is two provide 2 actions; the first adds terminal outputs (ie trigger a wait),
    ///  and the second will give the item. In this way, the second action only triggers if the player allows the
    ///  terminal to "do work" (lingers near it), and only gives the item once the terminal is done.
    /// Also of note, items are removed from the terminal system immdiately after all queued items provide their actions.
    /// </remarks>
    public virtual IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        => throw new NotImplementedException();

}
