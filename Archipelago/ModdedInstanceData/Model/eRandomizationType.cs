namespace ReTFO.Archipelago.ModdedInstanceData.Model;

// Enum wrapping different randomization types and permissions
// This enum is used by both items and locations
public enum eRandomizationType
{
    None,        // This item or location cannot / currently does not support being randomized
                 // For items, this means the item cannot be given to the player "arbitrarily", ie the item
                 //  must be obtained the same way you would in vanilla
                 // For locations, this means the location will not prevent the player from obtaining
                 //  the normal vanilla item in that location (it cannot be swapped for some "network" item)
    Progression, // This item or location is required to progress normally
    Useful,      // As an item:    This item is useful to have, and will help the player meaningfully
                 // As a location: This location is not required, but it is distinct and/or notable
    Filler,      // This item will have little to no impact, or this location is easy to miss or skip
    Trap,        // This item is actively detrimental. This location should be actively avoided (error scan, for example)
}
