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
        /// <summary>
        /// Parent tag of all checkpoint items
        /// </summary>
        public ItemID Item_Checkpoints
            => ItemID.From(data, "Checkpoint Items", data => new("Items which trigger a checkpoint", data.Item_All));

        /// <summary>
        /// Parent tag of all floating checkpoitn items
        /// </summary>
        public ItemID Item_FreeCheckpoints
            => ItemID.From(data, "Free Checkpoint Items", data => new("Items which trigger an immediate checkpoint when used", data.Item_Checkpoints));
    }

    extension (Expedition.Data data)
    {
        /// <summary>
        /// A free checkpoint item for a particular expedition
        /// </summary>
        public ItemID Item_FreeCheckpoint_ByExpedition
            => ItemID.From(
                data,
                $"{data.ExpeditionName} Free Checkpoint",
                data => new("Item for a particular expedition which triggers an immediate checkpoint when used", data.Item_FreeCheckpoints),
                new FreeCheckpointsHandler.FreeCheckpointItem(data.Region_Expedition)
            );
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
    
    public class FreeCheckpointItem : TerminalItem
    {
        public FreeCheckpointItem(RegionID expedition)
            : base(MakeRandData())
        {
            ExpeditionRegion = expedition;
        }

        public static ItemData MakeRandData() => new ItemData() { IsUseful = true };

        public RegionID ExpeditionRegion { get; private set; }

        public override RegionID TargetRegion => ExpeditionRegion;

        public override IEnumerable<Action> OnRetrieveFromTerminalSystem(StateTracker stateTracker, LG_ComputerTerminal terminal, ItemID itemId)
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

    [Expedition.Callback]
    public void AddExpeditionUnlock(Expedition.Data data)
    {
        data.AddFloatingItem(data.Region_Expedition, data.Item_FreeCheckpoint_ByExpedition);
    }

}
