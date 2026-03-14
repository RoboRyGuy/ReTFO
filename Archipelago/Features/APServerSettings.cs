using ReTFO.Archipelago.FeaturesAPI;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Members;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;

namespace ReTFO.Archipelago.Features;

[AutomatedFeature, DisallowInGameToggle, EnableFeatureByDefault]
public class APServerSettings : ArchipelagoFeature
{
    public override string Name => "Server Settings";

    public override FeatureGroup Group => FeatureGroups.Archipelago;

    public override string Description => "Settings for connecting to the Archipelago Server";

    public class SettingsType
    {
        private string MakeHidden(string text)
            => HideConnectionDetails ? new string('*', text.Length) : text;
 
        [FSDisplayName("Use Debug Mode")]
        [FSDescription("Skip connecting to Archipelago and start in debug mode.")]
        public bool UseDebugMode { get; set; } = false;

        [FSDisplayName("Hide Connection Details")]
        [FSDescription("Hide connection details while not editing them. For streamers.")]
        public bool HideConnectionDetails 
        { 
            get => m_hideConnectionDetails; 
            set
            {
                m_hideConnectionDetails = value;
                ArchipelagoFeatureHelper.GetFeature<APServerSettings>().MarkSettingsDirty(this);
            }
        }
        private bool m_hideConnectionDetails = true;

        [FSDisplayName("Server Address")]
        [FSDescription("Address of the server to connect to.\nSupports IPv4 and IPv6 addresses, as well as domain names.")]
        [FSUseDynamicSubmenu]
        public string ServerAddress 
        {
            get => MakeHidden(m_serverAddress);
            set => m_serverAddress = value;
        }
        private string m_serverAddress = "localhost";

        [FSDisplayName("Server Port")]
        [FSDescription("Port of the server\nValid port numbers are in the 16-bit range (0-65535)\nArchipelago defaults to using port 38281")]
        [FSUseDynamicSubmenu]
        public string Port
        {   // This is a string so we can hide the info
            get => MakeHidden(m_port.ToString());
            set
            {
                if (ushort.TryParse(value, out ushort val))
                    m_port = val;
            }
        }
        private ushort m_port = 38281;

        /// <summary>
        /// Helper giving access to the port without needing to parse it as a string
        /// </summary>
        /// <returns>The port</returns>
        public ushort GetPort() => m_port;

        [FSDisplayName("Slot Name")]
        [FSDescription("Slot in the server to try to connect to\nIn simpler terms, your username")]
        [FSUseDynamicSubmenu]
        public string Username 
        {
            get => MakeHidden(m_username);
            set => m_username = value;
        }
        private string m_username = "admin";

        [FSDisplayName("Use Password")]
        [FSDescription("If true, will attempt to authenticate to Archipelago using the below password\nIf false, will try to skip password authentication")]
        [FSUseDynamicSubmenu]
        public bool HasPassword { get; set; } = false;

        [FSDisplayName("Password")]
        [FSDescription("The password to use when connecting to Archipelago")]
        [FSUseDynamicSubmenu]
        public string Password 
        { 
            get => MakeHidden(m_password); 
            set => m_password = value; 
        }
        private string m_password = "Password";
    }

    [FeatureConfig, FSUseDynamicSubmenu]
    public static SettingsType Config { get; set; } = null!;

}
