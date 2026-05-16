using Archipelago.MultiClient.Net.MessageLog.Messages;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Text;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Members;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using AP = Archipelago.MultiClient.Net;

namespace ReTFO.Archipelago.Features;

using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;
using ReTFO.Archipelago.Utilities;
using SNetwork;

[EnableFeatureByDefault]
public class ChatHandler : ArchipelagoFeature
{
    public override string Name => "Chat Manager";
    public override string Description
        => "Handles chat messages to/from Archipelago";
    public override FeatureGroup Group => FeatureGroups.Archipelago;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        Plugin plugin = Plugin.Get();
        if (plugin.StateTracker != null)
        {
            plugin.StateTracker.OnStateChange += OnStateChanged;
            if (plugin.StateTracker.ApSession != null)
                plugin.StateTracker.ApSession.MessageLog.OnMessageReceived += OnMessageReceived;
        }
        else
            plugin.LateSetup += OnLateSetup;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        StateTracker st = StateTracker.Get();
        st.OnStateChange -= OnStateChanged;
        if (st.ApSession != null)
            st.ApSession.MessageLog.OnMessageReceived -= OnMessageReceived;
    }

    [FeatureConfig]
    public static Settings Config { get; set; } = null!;

    public class Settings
    {
        [FSDisplayName("Hide Non-Randomized Checks")]
        [FSDescription(
            "If true, prevent messages for unrandomized from appearing locally, including messages for items"
            +" which cannot be randomized."
        )]
        public bool DoHideTrivialChecks { get; set; } = true;

        [FSDisplayName("Shorten Received Item Messages")]
        [FSDescription("If true, show a shortened version of all \"X received Y\" messages")]
        public bool DoShortenReceivedItemMessages { get; set; } = true;

        [FSDisplayName("Show Only My Received Items")]
        [FSDescription("If true, only show \"X received Y\" messages if you are the recipient")]
        public bool DoShowOnlyMyRecievedItemMessages { get; set; } = false;
    }

    protected void OnLateSetup(SNet_Replicator replicator)
    {
        Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData> action = this.OnSNetPacketReceived;

        m_packet = replicator.CreatePacketBytes(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>>(action)
        );
        StateTracker.Get().OnStateChange += OnStateChanged;
    }

    protected void OnStateChanged(StateTracker stateTracker)
    {
        if (stateTracker.ApSession != null)
            stateTracker.ApSession.MessageLog.OnMessageReceived += OnMessageReceived;
    }

    private Queue<string> m_messages = new();
    public IEnumerable<string> Messages => m_messages;
    private SNet_PacketBufferBytes? m_packet = null;

    /// <summary>
    /// Constructs a hex color string with one digit per color
    /// </summary>
    public static string ColorToHex(AP.Models.Color color)
        => $"#{(color.R >> 4).ToString("X1")}{(color.G >> 4).ToString("X1")}{(color.B >> 4).ToString("X1")}";

    /// <summary>
    /// Receive a message from SNet. Intended solely for proxy clients.
    /// </summary>
    protected void OnSNetPacketReceived(Il2CppStructArray<byte> bytes, SNet_PacketBufferBytes.BufferData data)
    {
        StateTracker stateTracker = StateTracker.Get();
        if (stateTracker.ApSession != null) return;

        int index = 0;
        string? message = SerializationHelpers.ReadString(bytes, ref index);
        if (message == null) return;

        StateTracker.LogForLobby(message, true);
    }

    /// <summary>
    /// Receive messages from AP and format before presenting
    /// </summary>
    protected void OnMessageReceived(LogMessage message)
    {
        if (message is PlayerSpecificLogMessage pm && pm.IsActivePlayer) return;
        if (message is ItemSendLogMessage itemSendMessage)
        {
            if (Config.DoHideTrivialChecks)
            {
                LocationID locId = new() { AsId = itemSendMessage.Item.LocationId };
                Game.Data data = StateTracker.Get().MidManager.GetProcessedGameData();
                if (!data.LookupLocation(locId).RandMode.IsRandomized) return;
            }

            if (Config.DoShowOnlyMyRecievedItemMessages)
            {
                if (itemSendMessage.Item.Player.Slot != StateTracker.Get().ApSession!.Players.ActivePlayer.Slot)
                    return;
            }

            //if (Config.DoShortenReceivedItemMessages)
            //{
            //    StringBuilder itemSendMessageBuilder = new();
            //    if (itemSendMessage.Item.Player.Slot == StateTracker.Get().ApSession!.Players.ActivePlayer.Slot)
            //        itemSendMessageBuilder.Append($"<#{ColorToHex(AP.Colors.BuiltInPalettes.Dark.)}")
            //}

        }

        StringBuilder output = new();
        foreach (var part in message.Parts)
        {
            output.Append($"<{ColorToHex(part.Color)}>");
            switch (part.Type)
            {
                case AP.MessageLog.Parts.MessagePartType.Text:
                    output.Append(part.Text);
                    break;

                case AP.MessageLog.Parts.MessagePartType.Player:
                case AP.MessageLog.Parts.MessagePartType.Item:
                case AP.MessageLog.Parts.MessagePartType.Location:
                case AP.MessageLog.Parts.MessagePartType.Entrance:
                    output.Append("<i>");
                    output.Append(part.Text);
                    output.Append("</i>");
                    break;

                case AP.MessageLog.Parts.MessagePartType.HintStatus:
                    output.Append("<b>");
                    output.Append(part.Text);
                    output.Append("</b>");
                    break;
            }
        }
        output.Append("</color>");
        m_messages.Enqueue(output.ToString());

        // GTFO only shows 15 or so lines at a time anyway
        while (m_messages.Count > 15) m_messages.Dequeue();
    }

    /// <summary>
    /// Marshal messages to the main thread. Also send over SNet if necessary
    /// </summary>
    public override void Update()
    {
        while (m_messages.TryDequeue(out string? message))
        {
            StateTracker.LogForLobby(message, true);
            if (SNet.IsMaster)
            {
                Il2CppStructArray<byte> bytes = new(SerializationHelpers.CalcStringSize(message));
                int index = 0;
                SerializationHelpers.WriteString(bytes, ref index, message);
                m_packet?.Send(
                    bytes, 
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendGroup.PlayersInSessionHub,
                    SNet_SendQuality.Reliable,
                    (int)SNet_ChannelType.GameNonCritical
                );
            }
        }
    }

    /// <summary>
    /// Attempts to disable or at least lessen chat restrictions
    /// </summary>
    public static void TryRemoveChatLimitations()
    {
        if (PlayerChatManager.Current != null)
        {
            //PlayerChatManager.CHAT_MESSAGE_MAX_LENGTH = 255;
            PlayerChatManager.Current.m_maxLen = 255;
            PlayerChatManager.Current.m_forbiddenChars = Array.Empty<int>();
        }
    }

    [ArchivePatch(typeof(PlayerChatManager), nameof(PlayerChatManager.WantToSentTextMessage))]
    public static class PlayerChatManager__WantToSentTextMessage__Patch
    {
        public static void Postfix(Player.PlayerAgent? fromPlayer, string? message, Player.PlayerAgent? toPlayer)
        {
            if (message == null) return;
            if (toPlayer != null) return; // Sending a direct message (somehow?)
            StateTracker st = StateTracker.Get();
            if (st.ApSession == null) return;
            if (!(fromPlayer?.IsLocallyOwned ?? true))
                st.ApSession!.Say($"{fromPlayer.Owner.NickName}: {message}");
            else
                st.ApSession!.Say(message);
        }
    }

    [ArchivePatch(typeof(PlayerChatManager), nameof(PlayerChatManager.Setup))]
    public static class PlayerChatManager__Setup__Patch
    {
        public static void Postfix() => TryRemoveChatLimitations();
    }

}
