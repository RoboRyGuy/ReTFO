using System;
using TheArchive.Core;
using TheArchive.Core.Attributes;
using TheArchive.Core.Localization;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago;

[ArchiveModule(Plugin.GUID, Plugin.Name, Plugin.Version)]
public class ArchipelagoArchiveModule : IArchiveModule
{
    // Archive mdule implementation
    public ILocalizationService LocalizationService { get; set; } = null!;
    private IArchiveLogger? m_logger = null;
    public IArchiveLogger Logger
    {
        get => m_logger ?? throw new NullReferenceException("Module logger was not initalized!");
        set => m_logger = value;
    }
    public void Init() 
    {
        if (Plugin.TryGet(out var plugin))
            plugin.ArchiveModule = this;
        else
            Logger.Warning("Failed to find Plugin and set ArchiveModule!");
    }
}
