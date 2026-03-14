
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

    // Method to retrieve terminal data from the identifying log
    public static Terminal.Data? RetrieveDataFromLog(LG_ComputerTerminal terminal)
    {
        int entry = terminal.m_localLogs.FindEntry(IdentifyingLogName);
        if (entry < 0)
        {
            Zone.Data zone = Zone.Data.FromZone(terminal.SpawnNode.m_zone);
            FeatureLogger.Warning($"Failed to find identifying log from terminal in zone: {zone.ZoneName}");
            return null;
        }

        var pair = terminal.m_localLogs.entries[entry];
        TextDataBlock? text = TextDataBlock.GetBlock(pair.value.FileContent.Id);
        IdentifyingLogTextDataBlock? injectedText = text?.TryCast<IdentifyingLogTextDataBlock>();
        if (injectedText == null)
        {
            Zone.Data zone = Zone.Data.FromZone(terminal.SpawnNode.m_zone);
            FeatureLogger.Warning($"Failed to retrieving identifying data from log for terminal in zone: {zone.ZoneName}");
            return null;
        }

        return injectedText.TerminalData;
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
        public Terminal.Data TerminalData { get; set; } = null!;
    }

    // When processing terminals, add a custom log which helps us identify it during gameplay
    [Terminal.Callback]
    public static void AddIdentifyingLog(Terminal.Data data)
    {
        if ((data.TerminalLocalLogs.Count > 0) && (data.TerminalLocalLogs.Any(l => l.FileName == IdentifyingLogName)))
            return;

        string logTextBlockName = $"{data.TerminalName} Identifying Log";
        TextDataBlock log = TextDataBlock.GetBlock(logTextBlockName);
        if (log == null)
        {
            log = new IdentifyingLogTextDataBlock()
            {
                internalEnabled = true,
                name = logTextBlockName,
                SkipLocalization = true,
                MachineTranslation = true,
                English = $"[{data.TerminalIndex}] {data.TerminalName}",
                Description = "",
                CharacterMetaData = 1,
                ImportVersion = 1,
                ExportVersion = 1,
            };
            log.French = log.Italian = log.German = log.Spanish = log.Russian = log.Portuguese_Brazil
                = log.Polish = log.Japanese = log.Korean = log.Chinese_Traditional = log.Chinese_Simplified
                = new()
                {
                    Translation = log.English,
                    ShouldTranslate = false
                };
            TextDataBlock.AddBlock(log);
        }

        var myLog = log.Cast<IdentifyingLogTextDataBlock>();
        myLog.TerminalData = data; // Overwrite if fetching an existing log (reprocessing data)

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
