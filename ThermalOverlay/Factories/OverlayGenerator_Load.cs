
using Gear;
using ReTFO.ThermalOverlay.Interfaces;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Loads an existing sight from the game for use as an overlay. Accepts one parameters, the persistent ID of the sight to load (uint).
/// Use with caution, as this just triggers the load; the chosen handler must always prioritize sight conversion over overlay
///  generation for this to work correctly, otherwise you'll get an infinite loop.
/// Note: This loads in the requested part, but will often causes visual artifacts (most guns without scopes weren't intended
///  to have one, and the game does not like it when you add one haphazardly like this)
/// Load(sightPersistentID)
/// </summary>
public class OverlayGenerator_Load : IOverlayGenerator
{
    public const string Name = "Load";

    public virtual bool GenerateOverlay(string? thisName, ConversionContext context)
    {
        uint id;
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length == 0)
        {
            context.Log.LogError("OverlayGenerator_Load expected a sight's persistent ID in its parameters, but instead got nothing");
            return false;
        }
        else
        {
            string item = parameters[0];
            if (!uint.TryParse(item, out id))
            {
                context.Log.LogError($"OverlayGenerator_Load failed to parse uint from string \"{item}\"");
                return false;
            }
        }

        GearIDRange range = new();
        range.SetCompID(eGearComponent.SightPart, id);
        context.Item.GearPartHolder.SpawnGearPartsAsynch(range);
        context.Log.LogDebug($"OverlayGenerator_Load successfully triggered async load of sight \"{id}\"");
        return false;
    }
}
