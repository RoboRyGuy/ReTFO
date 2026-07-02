
using GameData;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using LevelGeneration;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using System;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.Terminals;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

// Ensures terminals have an "identfying log", which is used to determine which
//  terminal placement each spawned terminal is at runtime
[InjectToIl2Cpp, EnableFeatureByDefault, AutomatedFeature]
public class IdentifyingLogHandler : ArchipelagoFeature
{
    public override string Name => "Identifying Log Handler";
    public override string Description => "Adds identifying logs to terminals. Used by other handlers to identify the terminal during play";
    public override FeatureGroup Group => FeatureGroups.TerminalHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    /// <summary>
    /// Because reactor terminals are a special case, we have a special way to track them
    /// </summary>
    public struct IdentifyTerminalResult
    {
        public IdentifyTerminalResult(bool isReactor, Terminal.Data? data)
        {
            IsReactorTerminal = isReactor;
            Data = data;
        }
        public bool IsReactorTerminal;
        public Terminal.Data? Data;
    }

    // Method to retrieve terminal data from the identifying log
    public static IdentifyTerminalResult RetrieveDataFromLog(LG_ComputerTerminal terminal)
    {
        if (terminal.ConnectedReactor != null)
            return new IdentifyTerminalResult(true, null);

        int entry = terminal.m_localLogs.FindEntry(IdentifyingLogName);
        if (entry < 0)
        {
            Zone.Data? zone = terminal.SpawnNode?.m_zone == null ? null : Zone.Data.GetFromZone(terminal.SpawnNode.m_zone);
            FeatureLogger.Warning($"Failed to find identifying log from {terminal.ItemKey} in {zone?.ZoneName ?? "ZONE_NULL"}");
            return new IdentifyTerminalResult(false, null);
        }

        var pair = terminal.m_localLogs.entries[entry];
        TextDataBlock? text = TextDataBlock.GetBlock(pair.value.FileContent.Id);
        IdentifyingLogTextDataBlock? injectedText = text?.TryCast<IdentifyingLogTextDataBlock>();
        if (injectedText == null)
        {
            Zone.Data zone = Zone.Data.GetFromZone(terminal.SpawnNode.m_zone);
            FeatureLogger.Warning($"Failed to retrieving identifying data from log for terminal in zone: {zone.ZoneName}");
            return new IdentifyTerminalResult(false, null);
        }

        return new IdentifyTerminalResult(false, new(StateTracker.Get().GameData, injectedText.TerminalRegion));
    }

    // Name of the log used to identify the terminal
    private const string IdentifyingLogName = "TERMINAL_NAME.LOG";

    // Extends the TextDataBlock class to include terminal data as a member
    [InjectToIl2Cpp]
    private class IdentifyingLogTextDataBlock : TextDataBlock
    {
        public IdentifyingLogTextDataBlock() : base(ClassInjector.DerivedConstructorPointer<IdentifyingLogTextDataBlock>())
            => ClassInjector.DerivedConstructorBody(this);

        public IdentifyingLogTextDataBlock(IntPtr ptr) : base(ptr) { }

        [HideFromIl2Cpp]
        public RegionID TerminalRegion { get; init; } = new();
    }

    // When processing terminals, add a custom log which helps us identify it during gameplay
    [Terminal.Callback]
    public void AddIdentifyingLog(Terminal.Data data)
    {
        if ((data.TerminalLocalLogs.Count > 0) && (data.TerminalLocalLogs.Any(l => l.FileName == IdentifyingLogName)))
            return;

        string logTextBlockName = $"{data.TerminalName} Identifying Log";
        TextDataBlock log = TextDataBlock.GetBlock(logTextBlockName);
        if (log == null)
        {
            LanguageData helper = new()
            {
                Translation = $"[{data.TerminalIndex}] {data.TerminalName}",
                ShouldTranslate = false
            };

            log = new IdentifyingLogTextDataBlock()
            {
                internalEnabled = true,
                name = logTextBlockName,
                SkipLocalization = true,
                MachineTranslation = true,
                English = helper.Translation,
                Description = "",
                CharacterMetaData = 1,
                ImportVersion = 1,
                ExportVersion = 1,
                TerminalRegion = data.Region_Terminal,
                French = helper,
                Italian = helper,
                German = helper,
                Spanish = helper,
                Russian = helper,
                Portuguese_Brazil = helper,
                Polish = helper,
                Japanese = helper,
                Korean = helper,
                Chinese_Traditional = helper,
                Chinese_Simplified = helper,
            };
            TextDataBlock.AddBlock(log);
        }
        else
        {
            FeatureLogger.Warning("Attmpted to set identifying log twice!");
        }

        data.TerminalLocalLogs.Capacity = data.TerminalLocalLogs.Count + 1; // Ensure exact sizing
        data.TerminalLocalLogs.Insert(0, new()
        {
            FileName = IdentifyingLogName,
            FileContentOriginalLanguage = global::Localization.Language.English,
            FileContent = new()
            {
                Id = log.persistentID,
                OldId = 0,
                UntranslatedText = log.English
            },
            AttachedAudioByteSize = 0,
            AttachedAudioFile = 0,
            PlayerDialogToTriggerAfterAudio = 0,
        });
    }

}
