
using LevelGeneration;
using System;
using System.Collections.Generic;

namespace ReTFO.Archipelago.ModdedInstanceData.Model;

using ReTFO.Archipelago.Features;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

/* Represents an item in GTFO. Some examples include:
 *  - Colored keys and bulkhead keys
 *  - "Extraction Reachable" (an event item)
 *  - Objective items - ie generators or a central gen cluster
 * Items must have a name, ID, and handler. The handler will grant players the item when
 *  it is received from Archipelago
 */
public abstract class Item
{
    public Item(string name, eRandomizationType type, List<string> randomizationCategories)
    {
        Name = name;
        Categories = new(0);
        Type = type;
        RandomizationCategories = randomizationCategories;
    }

    public Item(string name, string category, eRandomizationType type, List<string> randomizationCategories)
    {
        Name = name;
        Categories = new(1) { category };
        Type = type;
        RandomizationCategories = randomizationCategories;
    }

    public Item(string name, List<string> categories, eRandomizationType type, List<string> randomizationCategories)
    {
        Name = name;
        Categories = categories;
        Type = type;
        RandomizationCategories = randomizationCategories;
    }

    // Name of this item
    public string Name { get; set; }

    // Categories for this item, if any
    public List<string> Categories { get; init; }

    // Id of this item, set when it's registered
    public long ID { get; set; } = -1L;

    // Type of item
    public eRandomizationType Type { get; set; }

    // Randomizaztion categories; which filters can be used to randomize this item
    public List<string> RandomizationCategories { get; set; }

    // Helper for visualizing in debugger
    public override string ToString() => Name;

    // === Virtuals for handling Item being obtained ==========================

    // Called immediately when the item is obtained
    public virtual void OnItemObtained(StateTracker stateTracker) { }

    // Called immediately when the item is lost - Items can only be lost by a call to "uncollect"
    // Also called if the item is not assigned to a location when the apSession starts - ie to lock out 
    public virtual void OnItemLost(StateTracker stateTracker) { }

    // Called just after loading into an expedition if this item has been previously obtained
    public virtual void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data) { }

    /* Called when the player retrieves the item from the terminal item system (only if placed in the system)
     *  stateTracker - Current StateTracker
     *  terminal     - The terminal the item was claimed from. Useful for custom textual output
     *   return - Expected to return an enumerable of actions. The enumerable will be immediately enumerated.
     *            Each action in the enumerable will be executed in order, pausing when the terminal is processing.
     *  Remarks:
     *   The intended use of the output is two provide 2 actions; the first add terminal outputs (ie trigger a wait),
     *    and the second will give the item. In this way, the second action only triggers if the player allows the
     *    terminal to "do work" (lingers near it), and only gives the item once the terminal is done.
     *   Also of note, items are removed from the terminal system after all queued items provide their actions
     */
    public virtual IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        => throw new NotImplementedException();

}
