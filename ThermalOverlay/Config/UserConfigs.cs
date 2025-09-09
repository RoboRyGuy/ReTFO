using BepInEx.Configuration;

namespace ReTFO.ThermalOverlay.Config;

// All configs that can be set by standard users via a BepInEx config file
public class UserConfigs
{
    // Backing field for EnableEverything
    private readonly ConfigEntry<bool> _enableEverything;

    // Enable all thermal configs!
    public bool EnableEverything => _enableEverything.Value;

    // Backing field for AllowPluginLoading
    private readonly ConfigEntry<bool> _allowExternalConversions;

    // Allow other mods to convert items to thermal items
    public bool AllowExternalConversions => _allowExternalConversions.Value;

    // Backing field for EnabledGear
    private readonly ConfigEntry<string> _enabledGear;

    // Stores processed enabledGear value
    private HashSet<string> _enabledGearSet = new(new ConfigManager.CaselessComparer());

    // Hash to ensure enabledGear hasn't changed
    private int _enabledGearHash = 0;

    // List of gear that users want converted to thermals
    public HashSet<string> EnabledGear
    {
        get
        {
            if (_enabledGearHash != _enabledGear.Value.GetHashCode())
            {
                _enabledGearSet.Clear();
                _enabledGearSet = _enabledGear.Value.Split(',').Where(s => s.Length > 0).Select(s => s.Trim()).ToHashSet();
                _enabledGearHash = _enabledGear.Value.GetHashCode();
            }
            return _enabledGearSet;
        }
    }

    // Backing field for MakeGunsCold
    private readonly ConfigEntry<bool> _makeGunsCold;

    // If we should make guns cold; that is, not visible to thermal
    public bool MakeGunsCold => _makeGunsCold.Value;

    internal UserConfigs(ConfigManager manager)
    {
        Plugin plugin = Plugin.Get(); // If this fails, I'd rather know than try and work past it
        BepInEx.Configuration.ConfigFile config = plugin.Config;

        _enableEverything = config.Bind<bool>(
            new ConfigDefinition("Conversion", "Enable Everything!"),
            false,
            new ConfigDescription("Enables all thermal configs, overriding per-item settings below")
        );

        _allowExternalConversions = config.Bind<bool>(
            new ConfigDefinition("Conversion", "Allow External Conversions"),
            true,
            new ConfigDescription("Allow other plugins to convert items to thermal items")
        );

        _enabledGear = config.Bind<string>(
            new ConfigDefinition("Conversion", "Enabled Gear"),
            string.Empty,
            new ConfigDescription(String.Join("\n", Enumerable.Concat(
                Enumerable.Repeat("Which gear items to convert, by PlayerOfflineGear Persistent ID.\nMust be a comma-separated list of item names.\nName reference: ", 1),
                manager.ItemNames.Select(p => $" - {p}")
            )))
        );
        if (!EnableEverything && EnabledGear.Count == 0)
            Plugin.TryGet()?.Log.LogWarning("No weapons have been configured to use thermal overlays; did you forget to configure the mod?");

        _makeGunsCold = config.Bind<bool>(
            new ConfigDefinition("Conversion", "Make Guns Cold"),
            true,
            new ConfigDescription(
                "When thermal overlays are added to guns, they can highlight the gun they're attached to, since "
                + "player weapons are considered warm. This often looks weird. This setting makes local guns cold, "
                + "which prevents the issue while minimally impacting gameplay"
            ));
    }
}
