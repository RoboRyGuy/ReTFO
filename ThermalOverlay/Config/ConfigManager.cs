
using BepInEx;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ReTFO.ThermalOverlay.Config;

/// <summary>
/// Class used to manage / load configs for the ThermalOverlay plugin
/// </summary>
public class ConfigManager
{
    // String comparer which ignores case
    public class CaselessComparer : IEqualityComparer<string>
    {
        public virtual bool Equals(string? x, string? y)
            => string.Compare(x, y, true) == 0;

        public virtual int GetHashCode(string? x)
            => x?.ToLower().GetHashCode() ?? 0;
    }

    // If item names are manually set by a different mod, this is where we store them
    private ICollection<string>? _itemNames = null;

    // A list of item names that can be set to thermal. See Plugin.SetItemNames
    public ICollection<string> ItemNames
    {
        get 
        {
            return _itemNames ??
                GameData.PlayerOfflineGearDataBlock.GetAllBlocks()
                .Select(b => Regex.Match(b.GearJSON, "\"Name\":\"([^\"]*)\""))
                .Where(m => m.Success)
                .Select(m => m.Captures.First().Value)
                .ToList();
        }
        internal set { _itemNames = value; }
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
        string configFilePath = Path.Combine(Path.GetDirectoryName(Paths.PluginPath) ?? ".", "GameData", "ThermalOverlay", "ThermalOverlayConfig.json");
        if (!File.Exists(configFilePath))
        {
            string thisDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            string defaultConfigPath = Path.Combine(thisDirectory, "DoNotEdit.json");
            if (File.Exists(defaultConfigPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFilePath) ?? ".");
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
    private HashSet<string> _requestedItems = new(new CaselessComparer());

    // Request an ID be enabled for thermal conversion
    public void EnableThermalItem(string item)
        => _requestedItems.Add(item);

    // Request IDs be enabled for thermal conversion
    public void EnableThermalItems(IEnumerable<string> items)
        => _requestedItems.UnionWith(items);

    // Returns true if an ID is enabled, false otherwise
    public bool IsIDEnabled(string itemName)
    {
        if (UserConfigs.EnableEverything) return true;
        else if (UserConfigs.EnabledGear.Contains(itemName)) return true;
        else if (_requestedItems.Contains(itemName))
        {
            if (UserConfigs.AllowExternalConversions) return true;
            else Plugin.Get().Log.LogWarning($"Extnernal conversion blocked due to config on ID {itemName}");
        }
        return false;
    }

    // Data used to convert weapons to their thermal variants
    private SortedList<string, ThermalConfig> _itemConfigs = new();

    // Get a thermal config
    public ThermalConfig? GetConfig(string itemName)
        => _itemConfigs.GetValueOrDefault(itemName);

    // Loads configs from file. Filename is the full file path
    public void LoadConfigFile(string filename)
    {
        if (TryDeserializeConfigFile(filename, out var file))
            LoadConfigFile(file);
    }

    public static bool TryDeserializeConfigFile(string filename, [NotNullWhen(true)] out ConfigFile? file)
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
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new Vector2_JsonConverter());
        options.Converters.Add(new Vector3_JsonConverter());
        options.Converters.Add(new Vector4_JsonConverter());
        options.Converters.Add(new Quaternion_JsonConverter());
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

        if (entry.Items == null || entry.Items.Count == 0)
        {
            Plugin.Get().Log.LogWarning($"Skipping config entry \"{entry.ConfigName ?? "Unnamed Config"}\" because it has been provided with no Items");
            return;
        }

        foreach (string item in entry.Items)
        {
            if (_itemConfigs.ContainsKey(item))
                Plugin.Get().Log.LogWarning($"Overwriting thermal config for item \"{item}\" with config named \"{entry.ConfigName ?? "Unnamed Config"}\"");
            _itemConfigs[item] = entry.Config;
        }
    }

}
