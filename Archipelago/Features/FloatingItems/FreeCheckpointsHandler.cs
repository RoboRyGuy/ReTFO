using LevelGeneration;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features.FloatingItems;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

public static class FreeCheckpointsHandler_Tags
{
    extension(Game.Data data)
    {
        public TagResolver Tag_FreeCheckpoints
            => new TagResolver(data, gd => gd.LookupOrCreateTag("Free Checkpoint Items", "Items which trigger an immediate checkpoint when used", gd.Tag_OptionalItems));
    }
}

// Handles the UnlockExpedition item, and the path between menu and the expedition's first zone
[EnableFeatureByDefault, AutomatedFeature]
public class FreeCheckpointsHandler : ArchipelagoFeature
{
    public override string Name => "Unlock Expedition Handler";
    public override string Description
        => "Locks expeditions and adds floating expedition unlock items which unlock them";
    public override FeatureGroup Group => FeatureGroups.FloatingHandlers;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }
    
    private class FreeCheckpointItem : Item
    {
        public FreeCheckpointItem(Expedition.Data expedition)
            : base(MakeTag(expedition), MakeRandData())
        {
            ExpeditionData = expedition;
        }

        public static TagResolver MakeTag(Expedition.Data data)
            => new TagResolver(data, gd => gd.LookupOrCreateTag($"{data.ExpeditionName} Free Checkpoint", "Item which triggers a checkpoint when used", gd.Tag_FreeCheckpoints));

        public static ItemData MakeRandData() => new ItemData() { IsUseful = true };

        public Expedition.Data ExpeditionData { get; set; }

        public override Expedition.Data? RequiredExpedition => ExpeditionData;

        public override void OnItemObtained(StateTracker stateTracker, LocationID sourceLocationId, PlayerAgent? player = null)
        {
            if (ExpeditionData.IsCurrentlyInExpedition())
                stateTracker.AddItemToTerminal(this);
        }

        public override void OnStartExpeditionWithItem(StateTracker stateTracker, Expedition.Data data)
        {
            if (ExpeditionData.IsSameExpedition(data))
                stateTracker.AddItemToTerminal(this);
        }

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal)
        {
            yield return () =>
            {
                terminal.AddLine(TerminalLineType.ProgressWait, "Activating <#0F0>CHECKPOINT</color>", 2.5f);
            };

            yield return () =>
            {
                // Why doesn't it make its own capture? I don't know. This works, though
                SNetwork.SNet.Capture.CaptureGameState(SNetwork.eBufferType.Checkpoint);
                CheckpointManager.StoreCheckpoint(terminal.m_position);
                terminal.AddLine("<#0F0>CHECKPOINT</color> reached!");
            };
        }
    }

    public static KeyedItem GetCheckpointItem(Expedition.Data data)
    {
        if (data.TryLookupItem(FreeCheckpointItem.MakeTag(data), out var item))
            return item;

        Item newItem = new FreeCheckpointItem(data);
        return new(data.AddItem(newItem), newItem);
    }

    [Expedition.Callback]
    public void AddExpeditionUnlock(Expedition.Data data)
    {
        KeyedItem reqItem = GetCheckpointItem(data);
        data.AddFloatingItem(reqItem.ID);
    }

}
