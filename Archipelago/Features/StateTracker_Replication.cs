using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.Utilities;
using SNetwork;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using TheArchive.Core.Attributes.Feature.Patches;
using pArtifactInventoryState = BoosterImplants.pArtifactInventoryState;

namespace ReTFO.Archipelago.Features;

// Tracks Archipelago state.
// This file is dedicated to the SNetwork integration for StateTracker
[InjectToIl2Cpp]
public partial class StateTracker : ArchipelagoFeature
{
    /// <summary>
    /// Struct used to replicate Archipelago's init (non-changing) state
    /// </summary>
    public struct pArchipelagoInitState
    {
        /// <summary>
        /// Root randomization seed
        /// </summary>
        public long RootSeed;

        /// <summary>
        /// Names of expeditions being run
        /// </summary>
        public string[] ExpeditionNames;

        /// <summary>
        /// Whitelist tags
        /// </summary>
        public long[] WhitelistTags;

        /// <summary>
        /// Blacklist tags
        /// </summary>
        public long[] BlacklistTags;

        /// <summary>
        /// Calulcate the size of this struct, when serialized, in bytes
        /// </summary>
        /// <returns>The size of this struct, when serialized, in bytes</returns>
        public int CalcByteSize()
            => SerializationHelpers.Calc7BitEncodedSize(RootSeed == 0 ? 1 : RootSeed)
            + SerializationHelpers.CalcStringArraySize(ExpeditionNames)
            + SerializationHelpers.Calc7BitEncodedMultiArraySize(new long[2][] { WhitelistTags, BlacklistTags });

        /// <summary>
        /// Convert this struct to bytes
        /// </summary>
        /// <param name="offset">Number of empty bytes to leave at the start of the array</param>
        /// <returns>The new array of serialized bytes</returns>
        public Il2CppStructArray<byte> ToBytes(int offset = 0)
        {
            Il2CppStructArray<byte> bytes = new(CalcByteSize() + offset);
            WriteToBytes(bytes, ref offset);
            return bytes;
        }

        /// <summary>
        /// Write this struct to a byte array at the provided index
        /// </summary>
        /// <param name="bytes">The byte array to write to</param>
        /// <param name="index">The index to write at. This will be moved to the next empty byte</param>
        public void WriteToBytes(Il2CppStructArray<byte> bytes, ref int index)
        {
            SerializationHelpers.Write7BitEncodedLong(bytes, ref index, RootSeed);
            SerializationHelpers.WriteStringArray(bytes, ref index, ExpeditionNames);
            SerializationHelpers.Write7BitEncodedMultiArray(bytes, ref index, new long[2][] { WhitelistTags, BlacklistTags }, WhitelistTags.Length + BlacklistTags.Length);
        }

        /// <summary>
        /// Read an archipelago state from raw bytes at the given index.
        /// </summary>
        /// <param name="bytes">The bytes to read from</param>
        /// <param name="index">The index to read at. This will be moved to next unread index</param>
        /// <returns>The read state</returns>
        public static pArchipelagoInitState ReadFromBytes(Il2CppStructArray<byte> bytes, ref int index)
        {
            pArchipelagoInitState result = new();
            result.RootSeed = SerializationHelpers.Read7BitEncodedLong(bytes, ref index);
            result.ExpeditionNames = SerializationHelpers.ReadStringArray(bytes, ref index);
            
            var spans = SerializationHelpers.Read7BitEncodedMultiArray(bytes, ref index);
            result.WhitelistTags = new long[spans[0].Length];
            spans[0].AsSpan().CopyTo(result.WhitelistTags.AsSpan());
            result.BlacklistTags = new long[spans[1].Length];
            spans[1].AsSpan().CopyTo(result.BlacklistTags.AsSpan());
            
            return result;
        }

        /// <inheritdoc cref="ReadFromBytes(Il2CppStructArray{byte}), ref int"/>
        public static pArchipelagoInitState ReadFromBytes(Il2CppStructArray<byte> bytes)
        {
            int offset = 0;
            return ReadFromBytes(bytes, ref offset);
        }
        
    }

    /// <summary>
    /// Struct used for general Archipelago replication and recalls
    /// </summary>
    public struct pArchipelagoGeneralState
    {
        /// <summary>
        /// List of all items collected
        /// </summary>
        public long[] ItemIds;

        /// <summary>
        /// List of items stored in the terminal system (not including their code names)
        /// </summary>
        public long[] ItemsInTerminalSystem;

        /// <summary>
        /// Calulcate the size of this struct, when serialized, in bytes
        /// </summary>
        /// <returns>The size of this struct, when serialized, in bytes</returns>
        public int CalcByteSize()
            => SerializationHelpers.Calc7BitEncodedMultiArraySize(new long[2][] { ItemIds, ItemsInTerminalSystem });

        /// <summary>
        /// Convert this state to a byte array
        /// </summary>
        /// <param name="offset">Number of blank bytes to leave at the beginning of the array</param>
        /// <returns>The requested byte array</returns>
        public Il2CppStructArray<byte> ToBytes(int offset = 0)
        {
            Il2CppStructArray<byte> bytes = new(offset + CalcByteSize());
            WriteToBytes(bytes, ref offset);
            return bytes;
        }

        /// <summary>
        /// Write this struct to an existing byte array at the provided index.
        /// </summary>
        /// <param name="bytes">The bytes array to write to</param>
        /// <param name="index">The index to write to. This will be moved to the next unwritten byte</param>
        public void WriteToBytes(Il2CppStructArray<byte> bytes, ref int index)
        {
            long[][] arrs = new long[2][] { ItemIds, ItemsInTerminalSystem };
            SerializationHelpers.Write7BitEncodedMultiArray(bytes, ref index, arrs, arrs.Sum(i => i.Length));
        }

        /// <summary>
        /// Read a byte array and convert it to a recall state
        /// </summary>
        /// <param name="bytes">The bytes to read from</param>
        /// <param name="index">The index to start reading at. Will be moved to the next unread spot after reading.</param>
        /// <returns>The deserialized struct</returns>
        public static pArchipelagoGeneralState FromBytes(Il2CppStructArray<byte> bytes, ref int index)
        {
            var arrs = SerializationHelpers.Read7BitEncodedMultiArray(bytes, ref index);
            var result = new pArchipelagoGeneralState()
            {
                ItemIds = new long[arrs[0].Length],
                ItemsInTerminalSystem = new long[arrs[1].Length],
            };

            arrs[0].AsSpan().CopyTo(result.ItemIds.AsSpan());
            arrs[1].AsSpan().CopyTo(result.ItemsInTerminalSystem.AsSpan());
            return result;
        }

        /// <inheritdoc cref="FromBytes(Il2CppStructArray{byte}, ReferenceEqualityComparer int)"/>
        public static pArchipelagoGeneralState FromBytes(Il2CppStructArray<byte> bytes)
        {
            int offset = 0;
            return FromBytes(bytes, ref offset);
        }
    }

    /// <summary>
    /// Struct used to send/receive interactions, IE locations being checked or items being received.
    /// Intentionally the same size as pArtifactInventoryState
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct pArchipelagoInteraction
    {
        public enum eType : uint
        {
            CollectItem,
            AddItemToTerminal,
        }

        /// <summary>
        /// Create new pArchipelagoInteraction with the provided values
        /// </summary>
        public pArchipelagoInteraction(eType type, long value)
        {
            Type = type;
            Value = value;
        }

        /// <summary>
        /// The type of interaction
        /// </summary>
        [FieldOffset(0)]
        public eType Type;

        /// <summary>
        /// A singular long value associated with the interaction, if applicable
        /// </summary>
        [FieldOffset(4)]
        public long Value;

        /// <summary>
        /// Create an interaction from the networked type used for this type
        /// </summary>
        /// <param name="bytes">The net type received from the network</param>
        /// <returns>The converted struct</returns>
        public static unsafe pArchipelagoInteraction FromBytes(pArtifactInventoryState bytes)
            => *(pArchipelagoInteraction*)&bytes;

        public unsafe pArtifactInventoryState ToBytes()
        {
            // c# won't let me create a pointer to this :(
            pArchipelagoInteraction state = this;
            return *(pArtifactInventoryState*)&state;
        }
    }

    /// <summary>
    /// Custom variation of SNet_StateReplicator<S, I> for StateTracker.
    /// </summary>
    [InjectToIl2Cpp(new Type[] { typeof(IReplicatorSupplier), typeof(ICaptureCallbackObject) })]
    private class ArchipelagoStateReplicator : Il2CppSystem.Object
    {
        public ArchipelagoStateReplicator(IntPtr ptr) : base(ptr) { }
        public ArchipelagoStateReplicator(StateTracker owner) : base(ClassInjector.DerivedConstructorPointer<ArchipelagoStateReplicator>())
        {
            ClassInjector.DerivedConstructorBody(this);
            m_owner = owner;
            m_replicator = SNet_Replication.AddManagerReplicator(new IReplicatorSupplier(this.Pointer)).Cast<SNet_Replicator>();
            SNet_SyncManager.RegisterCaptureCallback(new ICaptureCallbackObject(this.Pointer));

            IntPtr ptr;

            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveInitState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_initStatePacket = m_replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveGeneralState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_generalStatePacket = m_replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveRecallState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_recallStatePacket = m_replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveInteraction), typeof(void).FullName!, new string[] { typeof(pArtifactInventoryState).FullName! }
            );
            m_interactionPacket = m_replicator.CreatePacket(new Il2CppSystem.Action<pArtifactInventoryState>(this, ptr));
        }

        // IReplicatorSupplier
        public UnityEngine.GameObject gameObject => m_gameObject;
        public string name => s_name;
        public IReplicator Replicator => m_replicator.Cast<IReplicator>();
        // END IReplicatorSupplier

        // ICaptureCallbackObject
        public IReplicator GetReplicator() => m_replicator.Cast<IReplicator>();
        public void OnStateCapture()
        {
            // This capture statement doesn't seem to work?
            // Manually creating the packet and adding it to the capture
            Il2CppStructArray<byte> packetBytes = m_owner.MakeGeneralState().ToBytes(6);

            var miscBytes = m_recallStatePacket.Replicator.KeyBytes; // Caching because idk if it remakes it each time
            packetBytes[0] = miscBytes[0];
            packetBytes[1] = miscBytes[1];
            packetBytes[2] = m_recallStatePacket.Index;

            miscBytes = SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0));
            packetBytes[3] = miscBytes[0];
            packetBytes[4] = miscBytes[1];
            packetBytes[5] = miscBytes[2];

            SNet.Capture.PrimedBuffer.GetPass(eCapturePass.SessionPass).Add(packetBytes);
        }
        // END ICaptureCallbackObject

        private const string s_name = "State Tracker Replicator";
        private UnityEngine.GameObject m_gameObject = new(s_name);
        private SNet_Replicator m_replicator = null!;
        private StateTracker m_owner = null!;
        public SNet_Replicator ConcreteReplicator => m_replicator;

        // Packets
        // Note that we have to reuse existing generic instantiations for which AOT code
        //  exists; we'll reinterpret the bytes before forwarding calls to StateTracker
        private SNet_PacketBufferBytes m_initStatePacket = null!;
        private SNet_PacketBufferBytes m_generalStatePacket = null!;
        private SNet_PacketBufferBytes m_recallStatePacket = null!;
        private SNet_Packet<pArtifactInventoryState> m_interactionPacket = null!;

        /// <summary>
        /// Returns replicator information for the replicator backing this state replicator
        /// </summary>
        public pStateReplicatorProvider GetProviderSyncStruct()
        {
            SNetStructs.pReplicator rep = new();
            rep.SetID(Replicator);
            return new pStateReplicatorProvider() { pRep = rep };
        }

        /// <summary>
        /// Callback fro receiving init state from the network
        /// </summary>
        /// <param name="bytes">Init state as bytes</param>
        /// <param name="data">Data about the buffer being received</param>
        private void OnReceiveInitState(Il2CppStructArray<byte> bytes, SNet_PacketBufferBytes.BufferData data)
        {
            pArchipelagoInitState state = pArchipelagoInitState.ReadFromBytes(bytes);
            m_owner.ReceiveInitState(state);
        }

        /// <summary>
        /// Callback for receiving state from the network
        /// </summary>
        /// <param name="bytes">State being received, in bytes</param>
        /// <param name="data">Data about the buffer being received</param>
        private void OnReceiveGeneralState(Il2CppStructArray<byte> bytes, SNet_PacketBufferBytes.BufferData data)
        {
            pArchipelagoGeneralState state = pArchipelagoGeneralState.FromBytes(bytes);
            m_owner.ReceiveGeneralState(state, false);
        }

        /// <summary>
        /// Callback for receiving state during a recall
        /// </summary>
        /// <param name="bytes">The state being received, as bytes</param>
        /// <param name="data">Data about the buffer being received</param>
        private void OnReceiveRecallState(Il2CppStructArray<byte> bytes, SNet_PacketBufferBytes.BufferData data)
        {
            pArchipelagoGeneralState state = pArchipelagoGeneralState.FromBytes(bytes);
            m_owner.ReceiveGeneralState(state, true);
        }

        /// <summary>
        /// Callback for receiving state during an interaction
        /// </summary>
        /// <param name="interaction">The interaction being received</param>
        private void OnReceiveInteraction(pArtifactInventoryState bytes)
        {
            // TODO: Do something with the interaction
            pArchipelagoInteraction interaction = pArchipelagoInteraction.FromBytes(bytes);
            FeatureLogger.Warning("Received interaction, cannot use!");
        }

        /// <summary>
        /// Immediately send an init packet
        /// </summary>
        public void SendInit(SNet_Player? player = null)
        {
            if (player == null)
            {
                m_initStatePacket.Send(
                    m_owner.MakeInitState().ToBytes(),
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendGroup.PlayersInSessionHub,
                    SNet_SendQuality.Reliable,
                    (int)SNet_ChannelType.SessionOrderCritical
                );
            }
            else
            {
                m_initStatePacket.Send(
                    m_owner.MakeInitState().ToBytes(),
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendQuality.Reliable,
                    (int)SNet_ChannelType.SessionOrderCritical,
                    player
                );
            }
        }

        /// <summary>
        /// Immediately send a general sync packet
        /// </summary>
        public void SendGeneral(SNet_Player? player = null)
        {
            if (player == null)
            {
                m_generalStatePacket.Send(
                    m_owner.MakeGeneralState().ToBytes(),
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendGroup.PlayersInSessionHub,
                    SNet_SendQuality.Reliable,
                    (int)SNet_ChannelType.SessionOrderCritical
                );
            }
            else
            {
                m_generalStatePacket.Send(
                    m_owner.MakeGeneralState().ToBytes(),
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendQuality.Reliable,
                    (int)SNet_ChannelType.SessionOrderCritical,
                    player
                );
            }
        }

        /// <summary>
        /// Send an interaction
        /// </summary>
        /// <param name="interaction">The interaction being performed</param>
        public void SendInteraction(pArchipelagoInteraction interaction)
        {
            m_interactionPacket.Send(interaction.ToBytes(), SNet_ChannelType.SessionOrderCritical, SNet_SendQuality.Reliable);
        }
    }

    /// <summary>
    /// The state replicator for this StateTracker
    /// </summary>
    ArchipelagoStateReplicator? m_stateReplicator = null;

    /// <summary>
    /// Simple update enumerator which will be used as a coroutine
    /// </summary>
    private class UpdateStateEnumerator : IEnumerator
    {
        private StateTracker m_owner;

        public UpdateStateEnumerator(StateTracker owner)
            => m_owner = owner;

        public object Current => new UnityEngine.WaitForSecondsRealtime(60f);
        
        public bool MoveNext()
        {
            if (m_owner.CurrentState == eState.HostConnected || m_owner.CurrentState == eState.FakeConnect)
                m_owner.m_stateReplicator!.SendGeneral();
            return true;
        }

        public void Reset() { }
    }

    /// <summary>
    /// Ensure replication is running for this StateTracker.
    /// This is called in <see cref="Patches.ModifyRundownMenuPatch"/>
    /// </summary>
    public void SetupReplication()
    {
        if (m_stateReplicator != null) return;

        m_stateReplicator ??= new(this);
        Plugin.InvokeLateSetup(m_stateReplicator.ConcreteReplicator);

        // Seems fitting to put this on SNet, though it probably doesn't matter
        SNet.Current.StartCoroutine(new Il2CppEnumerator(new UpdateStateEnumerator(this)));
    }

    /// <summary>
    /// Wrap the current state of the StateTracker into a struct used for replication.
    /// </summary>
    /// <returns>The current state for the StateTracker</returns>
    public pArchipelagoInitState MakeInitState()
    {
        // Might optimize this later
        pArchipelagoInitState result = new()
        {
            RootSeed = RootSeed,
            ExpeditionNames = new string[Expeditions.Count],
            WhitelistTags = new long[WhitelistTags.Count],
            BlacklistTags = new long[BlacklistTags.Count],
        };

        int count = 0;
        foreach (var exp in Expeditions)
            result.ExpeditionNames[count++] = exp.ExpeditionName;

        foreach (var tag in WhitelistTags)
            result.WhitelistTags[count++] = tag.AsId;

        count = 0;
        foreach (var tag in BlacklistTags)
            result.BlacklistTags[count++] = tag.AsId;

        return result;
    }

    /// <summary>
    /// Wrap the current state of the StateTracker into a struct used for recalls and replication
    /// </summary>
    /// <returns></returns>
    public pArchipelagoGeneralState MakeGeneralState()
    {
        // Might optimize this later
        return new pArchipelagoGeneralState()
        {
            ItemIds = ActualItemCounts.SelectMany(pair => Enumerable.Repeat(pair.Key.AsId, pair.Value)).ToArray(),
            ItemsInTerminalSystem = ItemsInTerminalSystem.Select(pair => pair.Item1.AsId).ToArray(),
        };
    }

    /// <summary>
    /// Attempt to send an interaction as a packet to all clients
    /// </summary>
    /// <param name="type">The type of interaction to send</param>
    /// <param name="value">An optional value associated with the interaction type</param>
    protected void SendInteraction(pArchipelagoInteraction.eType type, long value = 0)
    {
        // Check if master
        if (!SNet.IsMaster) return;
        
        // If recalling, we'll redo several interactions (ie adding items to terminal)
        if (SNet.Capture.IsRecalling) return;

        m_stateReplicator!.SendInteraction(new pArchipelagoInteraction(type, value));
    }

    /// <summary>
    /// Receive an init state, expected when first joining a lobby
    /// </summary>
    /// <param name="state">The init state being received</param>
    public void ReceiveInitState(pArchipelagoInitState state)
    {
        if (SNet.IsMaster)
        {
            FeatureLogger.Warning("Receive init state, but this is master. Ignoring!");
            return;
        }

        if (CurrentState != eState.ClientConnect)
        {
            FeatureLogger.Warning("Receive init state but wasn't expecting it. Ignoring!");
            return;
        }

        RootSeed = state.RootSeed;
        Expeditions = ExpeditionsFromNames(state.ExpeditionNames);
        WhitelistTags = state.WhitelistTags.Select(i => new RandomizationTag() { AsId = i }).ToHashSet();
        BlacklistTags = state.BlacklistTags.Select(i => new RandomizationTag() { AsId = i }).ToHashSet();
        CurrentState = eState.ClientConnect;

        ConnectCommon();
    }

    /// <summary>
    /// Receive a general state, expected periodically to ensure good sync
    /// </summary>
    /// <param name="state">The state being received</param>
    /// <param name="isRecall">If the state is the reuslt of a recall</param>
    public void ReceiveGeneralState(pArchipelagoGeneralState state, bool isRecall)
    {
        if (SNet.IsMaster)
        {
            FeatureLogger.Warning("Ignoring GeneralState packet because this is master!");
            return;
        }

        var gameData = MidManager.GetProcessedGameData();

        // Reset terminal items
        ItemsInTerminalSystem.Clear();
        foreach (var id in state.ItemsInTerminalSystem)
            AddItemToTerminal(new ItemID() { AsId = id });

        // Reading state can never lower item counts
        var newItemCounts = state.ItemIds.GroupBy(i => i)
            .ToDictionary(g => new ItemID() { AsId = g.Key }, g => g.Count());

        foreach (var key in ActualItemCounts.Keys.Union(newItemCounts.Keys)) 
        {
            int count = ActualItemCounts.GetValueOrDefault(key, 0);
            int newCount = newItemCounts.GetValueOrDefault(key, 0);

            if (isRecall)
            {
                for (int i = newCount; i < count; i++)
                    gameData.LookupItem(key).OnItemObtained(this, new LocationID(), null);
            }
            else
            {
                for (int i = count; i < newCount; i++)
                    CollectItem(key);
            }
        }
    }

    /// <summary>
    /// Receive and handle an interaction from the network
    /// </summary>
    /// <param name="interaction">The interaction to receive and handle</param>
    public void ReceiveInteraction(pArchipelagoInteraction interaction)
    {
        switch (interaction.Type)
        {
            case pArchipelagoInteraction.eType.CollectItem:
                CollectItem(new ItemID() { AsId = interaction.Value });
                break;

            case pArchipelagoInteraction.eType.AddItemToTerminal:
                AddItemToTerminal(new ItemID() { AsId = interaction.Value });
                break;

            default:
                FeatureLogger.Error($"Received unknown interaction type: {interaction.Type}");
                break;
        }
    }

    /// <summary>
    /// Patch which sends init packet when players join the lobby
    /// </summary>
    [ArchivePatch(typeof(SNet_Lobby_STEAM), nameof(SNet_Lobby_STEAM.PlayerJoined), new Type[] { typeof(SNet_Player), typeof(CSteamID) })]
    public static class SNet_Lobby_STEAM__PlayerJoined__Patch
    {
        public static void Prefix(SNet_Player player)
        {
            if ((player?.Pointer ?? IntPtr.Zero) == IntPtr.Zero) return;
            StateTracker stateTracker = StateTracker.Get();
            if (SNet.IsMaster)
            {
                stateTracker.m_stateReplicator!.SendInit(player);
                stateTracker.m_stateReplicator!.SendGeneral(player);
            }
        }
    }
    
    /// <summary>
    /// Patch which ensures migration fails, since we really don't support that
    /// </summary>
    [ArchivePatch(typeof(SNet_MasterManager), nameof(SNet_MasterManager.SearchForMigrationMaster))]
    public static class SNet_MasterManager__SearchForMigrationMaster__Patch
    {
        public static bool Prefix()
        {
            SNet.MigrationMaster = null;
            return false;
        }
    }
}
