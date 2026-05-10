using Archipelago.MultiClient.Net.MessageLog.Messages;
using JetBrains.Annotations;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Patches;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;
using AP = Archipelago.MultiClient.Net;

namespace ReTFO.Archipelago.Features;

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
            plugin.LateSetup += (_) => StateTracker.Get().OnStateChange += OnStateChanged;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        StateTracker st = StateTracker.Get();
        st.OnStateChange -= OnStateChanged;
        if (st.ApSession != null)
            st.ApSession.MessageLog.OnMessageReceived -= OnMessageReceived;
    }

    protected void OnStateChanged(StateTracker stateTracker)
    {
        if (stateTracker.ApSession != null)
            stateTracker.ApSession.MessageLog.OnMessageReceived += OnMessageReceived;
    }

    private Queue<string> m_messages = new();
    public IEnumerable<string> Messages => m_messages;

    /// <summary>
    /// Constructs a hex color string with one digit per color
    /// </summary>
    public static string ColorToHex(AP.Models.Color color)
        => $"#{(color.R >> 4).ToString("X1")}{(color.G >> 4).ToString("X1")}{(color.B >> 4).ToString("X1")}";

    /// <summary>
    /// Receive messages from AP and format before presenting
    /// </summary>
    protected void OnMessageReceived(AP.MessageLog.Messages.LogMessage message)
    {
        if (message is PlayerSpecificLogMessage pm && pm.IsActivePlayer) return;

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
    /// Marshal messages to the main thread
    /// </summary>
    public override void Update()
    {
        while (m_messages.TryDequeue(out string? message))
            StateTracker.LogForPlayer(message);
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
            var func = () =>
            {
                if (message == null) return;
                if (toPlayer != null) return; // Sending a direct message (somehow?)
                StateTracker st = StateTracker.Get();
                if (st.ApSession == null) return;
                if (!(fromPlayer?.IsLocallyOwned ?? true))
                    st.ApSession!.Say($"{fromPlayer.Owner.NickName}: {message}");
                else
                    st.ApSession!.Say(message);
            };
            func();
        }
    }

    [ArchivePatch(typeof(PlayerChatManager), nameof(PlayerChatManager.Setup))]
    public static class PlayerChatManager__Setup__Patch
    {
        public static void Postfix() => TryRemoveChatLimitations();
    }

}
