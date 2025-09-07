
using BepInEx;
using Dissonance;
using Gear;
using PlayFab.AdminModels;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReTFO.ThermalOverlay.Config;

/// <summary>
/// Class used to manage / load configs for the ThermalOverlay plugin
/// </summary>
public class ConfigManager
{
    // If gear names are manually set by a different mod, this is where we store them
    private IReadOnlyDictionary<uint, string>? _offlineGearNames = null;

    // A list of gear items that can be set to thermal, including the IDs used to identify them
    // By default, this system uses GearIDRange's Checksum attribute for identification
    // If you're a mod writer consuming this mod, use Plugin's ChangeIDMode to modify this list
    public IReadOnlyDictionary<uint, string> OfflineGearNames
    {
        get 
        {  
            return _offlineGearNames ?? 
                GameData.PlayerOfflineGearDataBlock.GetAllBlocks()
                .Select(b => new GearIDRange(b.GearJSON))
                .ToDictionary(
                    r => r.GetChecksum(), 
                    r => $"{r.PublicGearName}" // I'm not sure how to get the category type
                );
        }
        internal set { _offlineGearNames = new SortedList<uint, string>(value.ToImmutableSortedDictionary()); }
    }

    // User configs aren't loaded until they're needed
    private UserConfigs? _userConfigs = null;

    // Configs controlled via bepinex's config file
    internal UserConfigs UserConfigs
    {
        get { return _userConfigs ??= JIT_Init(); }
        private set { _userConfigs = value; }
    }

    // Just-in-Time init, runs all configs at the last possible moment and only once absolutely necessary
    private UserConfigs JIT_Init()
    {
        string configFilePath = Path.Combine(Path.GetDirectoryName(Paths.PluginPath) ?? "", "GameData", "ThermalOverlay", "ThermalOverlayConfig.json");
        if (!File.Exists(configFilePath))
        {
            string defaultConfigPath = Path.Combine(Paths.PluginPath, "ThermalOverlay", "ThermalOverlayConfig.json");
            if (File.Exists(defaultConfigPath))
            {
                File.WriteAllBytes(configFilePath, File.ReadAllBytes(defaultConfigPath));
                LoadConfigFile(configFilePath);
            }
            else Plugin.Get().Log.LogError("The default config file is missing. Automatic configs will be used. Please reinstall ThermalOverlay");
        }
        else LoadConfigFile(configFilePath);
        return new UserConfigs(this);
    }

    // Try to get the user configs. These aren't loaded until absolutely necessary,
    //  because that allows loading plugins to make changes as needed before the config
    //  file descriptions get populated.
    public bool TryGetUserConfigs([NotNullWhen(true)] out UserConfigs? userConfigs)
    {
        userConfigs = _userConfigs;
        return userConfigs != null;
    }

    // IDs that are enabled programmatically
    private HashSet<uint> _requestedIDs = new HashSet<uint>();

    // Request an ID be enabled for thermal conversion
    public void EnableThermalIDs(uint id)
        => _requestedIDs.Append(id);

    // Request IDs be enabled for thermal conversion
    public void EnableThermalIDs(IEnumerable<uint> ids)
        => _requestedIDs.UnionWith(ids);

    // Returns true if an ID is enabled, false otherwise
    public bool IsIDEnabled(uint id)
    {
        if (UserConfigs.EnableEverything) return true;
        else if (UserConfigs.EnabledGear.Contains(id)) return true;
        else if (_requestedIDs.Contains(id))
        {
            if (UserConfigs.AllowExternalConversions) return true;
            else Plugin.Get().Log.LogWarning($"Extnernal conversion blocked due to config on ID {id}");
        }
        return false;
    }

    // Data used to convert weapons to their thermal variants
    private SortedList<uint, ThermalConfig> _itemConfigs = new();

    // Get a thermal config
    public ThermalConfig? GetConfig(uint id)
        => _itemConfigs.GetValueOrDefault(id);

    // Loads configs from file. Filename is the full file path
    public void LoadConfigFile(string filename)
    {
        if (TryDeserializeConfigFile(filename, out var file))
            LoadConfigFile(file);
    }

    public bool TryDeserializeConfigFile(string filename, [NotNullWhen(true)] out ConfigFile? file)
    {
        file = null;
        if (!File.Exists(filename))
        {
            Plugin.Get().Log.LogWarning($"Config file not found: \"{filename}\"");
            return false;
        }
        string fileText = File.ReadAllText(filename);

        JsonSerializerOptions options = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyFields = true,
            IgnoreReadOnlyProperties = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        file = JsonSerializer.Deserialize<ConfigFile>(fileText, options);

        if (file == null || (file.ConfigEntries?.Count ?? 0) == 0)
        {
            Plugin.Get().Log.LogError($"Config file could not be deserialized: \"{filename}\"");
            return false;
        }
        return true;
    }

    // Loads a ConfigFile object. See the string overload for automatically reading one from file
    public void LoadConfigFile(ConfigFile file)
    {
        if (file.ConfigEntries == null)
        {
            Plugin.Get().Log.LogWarning($"Skipping config file because it contains no configs");
            return;
        }

        file.ConfigEntries.ForEach(LoadConfigEntry);
    }

    // Loads a ConfigFileEntry; essentially, just adds the entry to the item list
    public void LoadConfigEntry(ConfigFileEntry entry)
    {
        if (entry.Config == null)
        {
            Plugin.Get().Log.LogWarning($"Skipping config entry \"{entry.ConfigName ?? "Unnamed Config"}\" because it has no configuration data");
            return;
        }

        if (entry.IDs == null || entry.IDs.Count == 0)
        {
            Plugin.Get().Log.LogWarning($"Skipping config entry \"{entry.ConfigName ?? "Unnamed Config"}\" because it has been provided with no IDs");
            return;
        }

        foreach (uint id in entry.IDs)
        {
            if (_itemConfigs.ContainsKey(id))
                Plugin.Get().Log.LogWarning($"Overwriting thermal config for item {id} with name \"{entry.ConfigName ?? "Unnamed Config"}\"");
            _itemConfigs[id] = entry.Config;
        }
    }

}
