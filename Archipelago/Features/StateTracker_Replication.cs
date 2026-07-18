using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Player;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using SNetwork;
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using TheArchive.Core.Attributes.Feature.Patches;
using pArtifactInventoryState = BoosterImplants.pArtifactInventoryState;

namespace ReTFO.Archipelago.Features;

using ReTFO.Archipelago.ModdedInstanceData.Model;
using ReTFO.Archipelago.ModdedInstanceData.Processors;

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
        /// Name of the Archipelago game
        /// </summary>
        public string? GameName;

        /// <summary>
        /// Root randomization seed
        /// </summary>
        public long RootSeed;

        /// <summary>
        /// Whitelist tags
        /// </summary>
        public uint[] RegionWhitelist;

        /// <summary>
        /// Blacklist tags
        /// </summary>
        public uint[] RegionBlacklist;

        /// <summary>
        /// Blacklist tags
        /// </summary>
        public uint[] LocationWhitelist;

        /// <summary>
        /// Blacklist tags
        /// </summary>
        public uint[] LocationBlacklist;

        /// <summary>
        /// Blacklist tags
        /// </summary>
        public uint[] ItemWhitelist;

        /// <summary>
        /// Blacklist tags
        /// </summary>
        public uint[] ItemBlacklist;

        /// <summary>
        /// Helper to access all int arrays for serialization
        /// </summary>
        private uint[][] MultiArray => [
            RegionWhitelist,
            RegionBlacklist,
            LocationWhitelist,
            LocationBlacklist,
            ItemWhitelist,
            ItemBlacklist,
        ];

        /// <summary>
        /// Calculate the size of this struct, when serialized, in bytes
        /// </summary>
        /// <returns>The size of this struct, when serialized, in bytes</returns>
        public int CalcByteSize()
            => SerializationHelpers.CalcStringSize(GameName)
            + SerializationHelpers.Calc7BitEncodedSize(RootSeed == 0 ? 1 : RootSeed)
            + SerializationHelpers.Calc7BitEncodedMultiArraySize(MultiArray);

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
            SerializationHelpers.WriteString(bytes, ref index, GameName);
            SerializationHelpers.Write7BitEncodedLong(bytes, ref index, RootSeed);
            SerializationHelpers.Write7BitEncodedMultiArray(bytes, ref index, MultiArray);
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
            result.GameName = SerializationHelpers.ReadString(bytes, ref index);
            result.RootSeed = SerializationHelpers.Read7BitEncodedLong(bytes, ref index);
            
            var spans = SerializationHelpers.Read7BitEncodedMultiUIntArray(bytes, ref index);
            if (spans.Length != result.MultiArray.Length)
                throw new NotSupportedException("Received incorrect number of int arrays for AP State from network");

            result.RegionWhitelist =   new uint[spans[0].Length]; spans[0].CopyTo(result.RegionWhitelist);
            result.RegionBlacklist =   new uint[spans[1].Length]; spans[1].CopyTo(result.RegionBlacklist);
            result.LocationWhitelist = new uint[spans[0].Length]; spans[2].CopyTo(result.LocationWhitelist);
            result.LocationBlacklist = new uint[spans[1].Length]; spans[3].CopyTo(result.LocationBlacklist);
            result.ItemWhitelist =     new uint[spans[0].Length]; spans[4].CopyTo(result.ItemWhitelist);
            result.ItemBlacklist =     new uint[spans[1].Length]; spans[5].CopyTo(result.ItemBlacklist);

            return result;
        }

        /// <inheritdoc cref="FromBytes(Il2CppStructArray{byte}), ref int"/>
        public static pArchipelagoInitState FromBytes(Il2CppStructArray<byte> bytes)
        {
            int offset = 0;
            return ReadFromBytes(bytes, ref offset);
        }
        
    }

    /// <summary>
    /// Struct used to send scouting info to clients
    /// </summary>
    public struct pArchipelagoScoutingUpdate
    {
        /// <summary>
        /// Pairs of strings, in the order PlayerName, GameName, PlayerName, etc...
        /// One entry exists per slot.
        /// </summary>
        public string[] SlotLookup;

        /// <summary>
        /// List of the locations scouted
        /// </summary>
        public uint[] LocationIDs;

        /// <summary>
        /// The index of the item's slot in the lookup contained in this struct
        /// </summary>
        public uint[] SlotIds;

        /// <summary>
        /// The display name for the item in each location
        /// </summary>
        public string[] ItemDisplayNames;

        /// <summary>
        /// Helper to get multi array for networking
        /// </summary>
        private uint[][] MultiArray => [ LocationIDs, SlotIds ];

        /// <summary>
        /// Calculate the size of this struct, when serialized, in bytes
        /// </summary>
        /// <returns>The size of this struct, when serialized, in bytes</returns>
        public int CalcByteSize()
            => SerializationHelpers.CalcStringArraySize(SlotLookup)
            + SerializationHelpers.Calc7BitEncodedMultiArraySize(MultiArray)
            + SerializationHelpers.CalcStringArraySize(ItemDisplayNames);

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
            SerializationHelpers.WriteStringArray(bytes, ref index, SlotLookup);
            SerializationHelpers.Write7BitEncodedMultiArray(bytes, ref index, MultiArray);
            SerializationHelpers.WriteStringArray(bytes, ref index, ItemDisplayNames);
        }

        /// <summary>
        /// Read a byte array and convert it to a scouting update
        /// </summary>
        /// <param name="bytes">The bytes to read from</param>
        /// <param name="index">The index to start reading at. Will be moved to the next unread spot after reading.</param>
        /// <returns>The deserialized struct</returns>
        public static pArchipelagoScoutingUpdate FromBytes(Il2CppStructArray<byte> bytes, ref int index)
        {
            pArchipelagoScoutingUpdate result;
            result.SlotLookup = SerializationHelpers.ReadStringArray(bytes, ref index);
            
            var spans = SerializationHelpers.Read7BitEncodedMultiUIntArray(bytes, ref index);
            result.LocationIDs = new uint[spans[0].Length]; spans[0].CopyTo(result.LocationIDs);
            result.SlotIds = new uint[spans[1].Length]; spans[1].CopyTo(result.SlotIds);

            result.ItemDisplayNames = SerializationHelpers.ReadStringArray(bytes, ref index);
            return result;
        }

        /// <inheritdoc cref="FromBytes(Il2CppStructArray{byte}, ReferenceEqualityComparer int)"/>
        public static pArchipelagoScoutingUpdate FromBytes(Il2CppStructArray<byte> bytes)
        {
            int offset = 0;
            return FromBytes(bytes, ref offset);
        }

    }

    /// <summary>
    /// Struct used for general Archipelago replication and recalls
    /// </summary>
    public struct pArchipelagoGeneralState
    {
        /// <summary>
        /// List of all locations checked
        /// </summary>
        public uint[] FoundLocations;

        /// <summary>
        /// List of all locations marked as trash
        /// </summary>
        public uint[] TrashedLocations;

        /// <summary>
        /// List of all items collected
        /// </summary>
        public uint[] ItemIds;

        /// <summary>
        /// List of items stored in the terminal system (not including their code names)
        /// </summary>
        public uint[] ItemsInTerminalSystem;

        /// <summary>
        /// Helper to get multi array for networking
        /// </summary>
        private uint[][] MultiArray => [ FoundLocations, TrashedLocations, ItemIds, ItemsInTerminalSystem ];

        /// <summary>
        /// Calculate the size of this struct, when serialized, in bytes
        /// </summary>
        /// <returns>The size of this struct, when serialized, in bytes</returns>
        public int CalcByteSize()
            => SerializationHelpers.Calc7BitEncodedMultiArraySize(MultiArray);

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
            SerializationHelpers.Write7BitEncodedMultiArray(bytes, ref index, MultiArray);
        }

        /// <summary>
        /// Read a byte array and convert it to a recall state
        /// </summary>
        /// <param name="bytes">The bytes to read from</param>
        /// <param name="index">The index to start reading at. Will be moved to the next unread spot after reading.</param>
        /// <returns>The deserialized struct</returns>
        public static pArchipelagoGeneralState FromBytes(Il2CppStructArray<byte> bytes, ref int index)
        {
            var arrs = SerializationHelpers.Read7BitEncodedMultiUIntArray(bytes, ref index);
            var result = new pArchipelagoGeneralState();

            result.FoundLocations        = new uint[arrs[0].Length]; arrs[0].CopyTo(result.FoundLocations);
            result.TrashedLocations      = new uint[arrs[1].Length]; arrs[1].CopyTo(result.TrashedLocations);
            result.ItemIds               = new uint[arrs[2].Length]; arrs[2].CopyTo(result.ItemIds);
            result.ItemsInTerminalSystem = new uint[arrs[3].Length]; arrs[3].CopyTo(result.ItemsInTerminalSystem);

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
    [StructLayout(LayoutKind.Explicit, Size = 3 * sizeof(int))]
    public struct pArchipelagoInteraction
    {
        public enum eType : ushort
        {
            CheckRegion,
            CheckLocation,
            ScoutLocation,
            MarkTrash,
            EmptyTrash,
            CollectItem,
        }

        /// <summary>
        /// Create new pArchipelagoInteraction with the provided values
        /// </summary>
        public pArchipelagoInteraction(eType type, uint value = 0, ushort count = 0)
        {
            Type = type;
            Value = value;
            Count = count;
        }

        /// <summary>
        /// The type of interaction
        /// </summary>
        [FieldOffset(0)]
        public eType Type;

        /// <summary>
        /// A short value associated with the interaction, if applicable
        /// </summary>
        [FieldOffset(sizeof(ushort))]
        public ushort Count;

        /// <summary>
        /// A singular long value associated with the interaction, if applicable
        /// </summary>
        [FieldOffset(2 * sizeof(ushort))]
        public uint Value;

        public pArtifactInventoryState ToBytes()
        {
            byte[] bytes = new byte[sizeof(int) * 3];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0, sizeof(ushort)), (ushort)Type);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(sizeof(ushort), sizeof(ushort)), Count);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(2 * sizeof(ushort), sizeof(long)), Value);
            return MemoryMarshal.Read<pArtifactInventoryState>(bytes);
        }

        /// <summary>
        /// Create an interaction from the networked type used for this type
        /// </summary>
        /// <param name="bytes">The net type received from the network</param>
        /// <returns>The converted struct</returns>
        public static pArchipelagoInteraction FromBytes(pArtifactInventoryState input)
        {
            pArchipelagoInteraction result;
            var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref input, 1)).ToArray();
            result.Type = (eType)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, sizeof(ushort)));
            result.Count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(sizeof(ushort), sizeof(ushort)));
            result.Value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(2 * sizeof(ushort), sizeof(long)));
            return result;
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

            // Init
            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveInitState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_initStatePacket = m_replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

            // Scouting Update
            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveScoutingUpdate), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_scoutingUpdatePacket = m_replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

            // General state
            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveGeneralState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_generalStatePacket = m_replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

            // General state (recall)
            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveRecallState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_recallStatePacket = m_replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

            // Interaction
            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveInteraction), typeof(void).FullName!, new string[] { typeof(pArtifactInventoryState).FullName! }
            );
            m_interactionPacket = m_replicator.CreatePacket(new Il2CppSystem.Action<pArtifactInventoryState>(this, ptr));

            // Logging
            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveLog), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_logPacket = m_replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));
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
        private SNet_PacketBufferBytes m_scoutingUpdatePacket = null!;
        private SNet_PacketBufferBytes m_generalStatePacket = null!;
        private SNet_PacketBufferBytes m_recallStatePacket = null!;
        private SNet_Packet<pArtifactInventoryState> m_interactionPacket = null!;
        private SNet_PacketBufferBytes m_logPacket = null!;

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
            pArchipelagoInitState state = pArchipelagoInitState.FromBytes(bytes);
            m_owner.ReceiveInitState(state);
        }

        /// <summary>
        /// Callback fro receiving init state from the network
        /// </summary>
        /// <param name="bytes">Scouting update as bytes</param>
        /// <param name="data">Data about the buffer being received</param>
        private void OnReceiveScoutingUpdate(Il2CppStructArray<byte> bytes, SNet_PacketBufferBytes.BufferData data)
        {
            pArchipelagoScoutingUpdate update = pArchipelagoScoutingUpdate.FromBytes(bytes);
            m_owner.ReceiveScoutingUpdate(update);
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
            pArchipelagoInteraction interaction = pArchipelagoInteraction.FromBytes(bytes);
            m_owner.ReceiveInteraction(interaction);
        }

        /// <summary>
        /// Receive a log message from SNet and log it to the screen
        /// </summary>
        /// <param name="bytes">The message being received, as bytes</param>
        /// <param name="data">Data about the buffer being received</param>
        private void OnReceiveLog(Il2CppStructArray<byte> bytes, SNet_PacketBufferBytes.BufferData data)
        {
            int index = 0;
            string? message = SerializationHelpers.ReadString(bytes, ref index);
            if (message != null) StateTracker.LogForLobby(message, true);
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
        /// Immediately send an init packet
        /// </summary>
        public void SendScouting(pArchipelagoScoutingUpdate? update = null, SNet_Player? player = null)
        {
            update ??= m_owner.MakeScoutingUpdate();
            if (update.Value.LocationIDs.Length == 0)
                return; // No need to send an empty packet

            if (player == null)
            {
                m_scoutingUpdatePacket.Send(
                    update.Value.ToBytes(),
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendGroup.PlayersInSessionHub,
                    SNet_SendQuality.Reliable,
                    (int)SNet_ChannelType.SessionOrderCritical
                );
            }
            else
            {
                m_scoutingUpdatePacket.Send(
                    update.Value.ToBytes(),
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
            m_interactionPacket.Send(interaction.ToBytes(), SNet_ChannelType.SessionOrderCritical, SNet_SendQuality.Reliable_WithBuffering);
        }

        /// <summary>
        /// Send a log
        /// </summary>
        /// <param name="message">The log to send</param>
        public void SendLog(string message, SNet_Player? player = null)
        {
            Il2CppStructArray<byte> bytes = new(SerializationHelpers.CalcStringSize(message));
            int index = 0;
            SerializationHelpers.WriteString(bytes, ref index, message);

            if (player == null)
            {
                m_logPacket.Send(
                    bytes,
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendGroup.PlayersInSessionHub,
                    SNet_SendQuality.Reliable,
                    (int)SNet_ChannelType.GameNonCritical
                );
            }
            else
            {
                m_logPacket.Send(
                    bytes,
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendQuality.Reliable,
                    (int)SNet_ChannelType.GameNonCritical,
                    player
                );
            }
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

        // Enforce a 60 second delay between each state sync
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
    public pArchipelagoInitState MakeInitState()
    {
        pArchipelagoInitState result = new()
        {
            GameName = MidManager.GetProcessedGameData().Name,
            RootSeed = RootSeed,
        };

        int i;
        i = 0; result.RegionWhitelist   = new uint[m_regionWhitelist.Count];   foreach (var id in m_regionWhitelist)   result.RegionWhitelist[i++]   = id.ID;
        i = 0; result.RegionBlacklist   = new uint[m_regionBlacklist.Count];   foreach (var id in m_regionBlacklist)   result.RegionBlacklist[i++]   = id.ID;
        i = 0; result.LocationWhitelist = new uint[m_locationWhitelist.Count]; foreach (var id in m_locationWhitelist) result.LocationWhitelist[i++] = id.ID;
        i = 0; result.LocationBlacklist = new uint[m_locationBlacklist.Count]; foreach (var id in m_locationBlacklist) result.LocationBlacklist[i++] = id.ID;
        i = 0; result.ItemWhitelist     = new uint[m_itemWhitelist.Count];     foreach (var id in m_itemWhitelist)     result.ItemWhitelist[i++]     = id.ID;
        i = 0; result.ItemBlacklist     = new uint[m_itemBlacklist.Count];     foreach (var id in m_itemBlacklist)     result.ItemBlacklist[i++]     = id.ID;

        return result;
    }

    /// <summary>
    /// Wrap the current state of the StateTracker into a struct used for recalls and replication
    /// </summary>
    public pArchipelagoGeneralState MakeGeneralState()
    {
        // Might optimize this later
        pArchipelagoGeneralState result;

        int i, j;
        i = 0; result.FoundLocations        = new uint[m_foundLocations.Count];        foreach (var id in m_foundLocations)        result.FoundLocations[i++]        = id.ID;
        i = 0; result.TrashedLocations      = new uint[m_trashedLocations.Count];      foreach (var id in m_trashedLocations)      result.TrashedLocations[i++]      = id.ID;
        i = 0; result.ItemsInTerminalSystem = new uint[ItemsInTerminalSystem.Count]; foreach (var id in ItemsInTerminalSystem) result.ItemsInTerminalSystem[i++] = id.Item1.ID;
        
        i = 0; 
        result.ItemIds = new uint[CollectedItemCounts.Sum(pair => pair.Value)];
        foreach (var pair in CollectedItemCounts)
        {
            for (j = 0; j < pair.Value; j++)
                result.ItemIds[i++] = pair.Key.ID;
        };

        return result;
    }

    /// <summary>
    /// Make a scouting update for all scouted items
    /// </summary>
    public pArchipelagoScoutingUpdate MakeScoutingUpdate()
        => FormatScoutingUpdate(MidManager.GetProcessedGameData().Locations.GetAllEntries().Where(l => l.Value.Value?.ScoutedItemName != null).Select(i => i.Key));

    /// <summary>
    /// Make a scouting update for a particular collection of items
    /// </summary>
    /// <param name="locations">The locations to create a scouting update for</param>
    public pArchipelagoScoutingUpdate FormatScoutingUpdate(IEnumerable<LocationID> locations)
    {
        if (ApSession == null) 
            throw new NullReferenceException("Cannot format scouting update; no AP session");
        pArchipelagoScoutingUpdate result;

        Dictionary<string, int> playerIdToLookup = new();
        result.SlotLookup = new string[ApSession!.Players.AllPlayers.Count() * 2];
        int count = 0;

        foreach (var player in ApSession.Players.AllPlayers)
        {
            result.SlotLookup[2 * count] = player.Name;
            result.SlotLookup[2 * count + 1] = player.Game;
            playerIdToLookup.Add(player.Name, count++);
        }

        Game.Data data = MidManager.GetProcessedGameData();
        result.LocationIDs = locations.Select(id => id.ID).ToArray();
        result.SlotIds = new uint[result.LocationIDs.Length];
        result.ItemDisplayNames = new string[result.LocationIDs.Length];

        for (int i = 0; i < result.LocationIDs.Length; i++)
        {
            LocationID id = new() { ID = result.LocationIDs[i] };
            Location location = data.Locations.LookUpValueChecked(id);
            result.SlotIds[i] = checked((uint)playerIdToLookup[location.ScoutedPlayerName!]);
            result.ItemDisplayNames[i] = location.ScoutedItemName 
                ?? throw new NullReferenceException("Expected scouted item name, got null!");
        }

        return result;
    }

    /// <summary>
    /// Attempt to send an interaction as a packet to all clients
    /// </summary>
    /// <param name="type">The type of interaction to send</param>
    /// <param name="value">An optional value associated with the interaction type</param>
    protected void SendInteraction(pArchipelagoInteraction.eType type, uint value = 0, ushort count = 0)
    {
        // If recalling, we'll redo several interactions (ie adding items to terminal)
        if (SNet.Capture.IsRecalling) return;

        m_stateReplicator!.SendInteraction(new pArchipelagoInteraction(type, value: value, count: count));
    }

    /// <summary>
    /// Receive an init state, expected when first joining a lobby
    /// </summary>
    /// <param name="state">The init state being received</param>
    public void ReceiveInitState(pArchipelagoInitState state) => ClientConnect(state);

    /// <summary>
    /// Receive scouting information from SNet
    /// </summary>
    /// <param name="update"></param>
    public void ReceiveScoutingUpdate(pArchipelagoScoutingUpdate update)
    {
        if (ApSession != null)
        {
            FeatureLogger.Debug("Ignoring scouting update; we are connected to AP!");
            return;
        }

        FeatureLogger.Debug("Received a scouting update.");
        Game.Data data = MidManager.GetProcessedGameData();

        for (int i = 0; i < update.LocationIDs.Length; i++)
        {
            LocationID id = new() { ID = update.LocationIDs[i] };
            Location loc = data.Locations.LookUpValueChecked(id);
            long slot = update.SlotIds[i];
            loc.ScoutedItemName = update.ItemDisplayNames[i];
            loc.ScoutedPlayerName = update.SlotLookup[2 * slot];
            loc.ScoutedGameName = update.SlotLookup[2 * slot + 1];
        }
    }

    /// <summary>
    /// Receive a general state, expected periodically to ensure good sync
    /// </summary>
    /// <param name="state">The state being received</param>
    /// <param name="isRecall">If the state is the result of a recall</param>
    public void ReceiveGeneralState(pArchipelagoGeneralState state, bool isRecall)
    {
        if (!isRecall)
        {
            if (ApSession != null)
            {
                FeatureLogger.Debug("Ignoring GeneralState packet because we're connected to AP");
                return;
            }

            if (CurrentState == eState.FakeConnect)
            {
                FeatureLogger.Debug("Ignoring GeneralState packet because we're using FakeConnect");
                return;
            }
        }

        var gameData = MidManager.GetProcessedGameData();

        // We only add to checked locations - no need to notify
        m_foundLocations.UnionWith(state.FoundLocations.Select(id => new LocationID() { ID = id }));
        m_trashedLocations.UnionWith(state.TrashedLocations.Select(id => new LocationID() { ID = id }));

        // Reset terminal items
        ItemsInTerminalSystem.Clear();
        foreach (var id in state.ItemsInTerminalSystem)
            AddItemToTerminal(new ItemID() { ID = id });

        var newItemCounts = state.ItemIds.GroupBy(i => i)
            .ToDictionary(g => new ItemID() { ID = g.Key }, g => g.Count());

        // Sync item counts
        foreach (var key in m_collectedItemCounts.Keys.Union(newItemCounts.Keys)) 
        {
            int count = m_collectedItemCounts.GetValueOrDefault(key, 0);
            int newCount = newItemCounts.GetValueOrDefault(key, 0);

            if (isRecall)
            {   // Re-do OnObtained item events since items persist past checkpoints
                for (int i = newCount; i < count; i++)
                    gameData.Items.LookUpValueChecked(key).OnItemObtained(this, new LocationID(), null, key);
            }
            else
            {   // Check for items we're missing and try to obtain them
                for (int i = count; i < newCount; i++)
                    CollectItem(key, skipInteraction: true);
            }
        }
    }

    /// <summary>
    /// Receive and handle an interaction from the network
    /// </summary>
    /// <param name="interaction">The interaction to receive and handle</param>
    public void ReceiveInteraction(pArchipelagoInteraction interaction)
    {
        PlayerAgent? sendingAgent = null;
        if (SNet.Replication.TryGetLastSender(out SNet_Player sender))
            sendingAgent = sender.PlayerAgent?.TryCast<PlayerAgent>();

        switch (interaction.Type)
        {
            case pArchipelagoInteraction.eType.CollectItem:
                // If we're authoritative on items, we ignore client notifications
                if (ApSession != null) return;
                if (CurrentState == eState.FakeConnect) return;

                // Note we intentionally truncate the actual item count as a form of lazy rollover support
                ItemID itemId = new() { ID = interaction.Value };
                int actualItemCount = (ushort)m_collectedItemCounts.GetValueOrDefault(itemId, 0);
                while (actualItemCount++ < interaction.Count)
                    CollectItem(itemId, skipInteraction: true);
                break;

            case pArchipelagoInteraction.eType.CheckLocation:
                LocationID locationId = new() { ID = interaction.Value };
                NotifyFoundLocation(locationId, sendingAgent, skipInteraction: true);
                break;

            case pArchipelagoInteraction.eType.ScoutLocation:
                LocationID scoutId = new() { ID = interaction.Value };
                ScoutLocations(Enumerable.Repeat(scoutId, 1));
                break;

            case pArchipelagoInteraction.eType.MarkTrash:
                LocationID trashId = new() { ID = interaction.Value };
                MarkAsTrash([ trashId ], sendingAgent, skipInteraction: true);
                break;

            case pArchipelagoInteraction.eType.EmptyTrash:
                m_trashedLocations.Clear();
                break;

            case pArchipelagoInteraction.eType.CheckRegion:
                RegionID regionId = new() { ID = interaction.Value };
                NotifyFoundRegion(regionId, sendingAgent, skipInteraction: true);
                break;

            default:
                FeatureLogger.Error($"Received unknown interaction type: {interaction.Type}");
                break;
        }
    }

    /// <summary>
    /// Patch which sends init packet when players join the lobby
    /// </summary>
    [ArchivePatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.AddPlayerToSession))]
    public static class SNet_SessionHub__AddPlayerToSession__Patch
    {
        public static void Prefix(SNet_SessionHub __instance, SNet_Player player)
        {
            if (!SNet.IsMaster || player.IsBot) return;
            if (SNet.SessionHub.PlayersInSession.Any(p => p.Pointer == player.Pointer)) return;

            FeatureLogger.Notice($"New player is joining session; sending init packet: {player.NickName}");
            StateTracker.Get().m_stateReplicator?.SendInit(player);
        }
    }

    /// <summary>
    /// Sends general sync packets when a player joins the lobby
    /// </summary>
    [ArchivePatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.OnJoinedLobby))]
    public static class SNet_SessionHub__OnJoinedLobby__Patch
    {
        public static void Postfix(SNet_SessionHub __instance, SNet_Player player)
        {
            if (!SNet.IsMaster || player.IsBot) return;
            StateTracker st = StateTracker.Get();
            if (st.ApSession == null) return;
    
            FeatureLogger.Notice($"Adding new player to session; sending sync packets: {player.NickName}");
            st.m_stateReplicator?.SendGeneral(player);
            st.m_stateReplicator?.SendScouting(player: player);
        }
    }

    /// <summary>
    /// Cached master answer; used to determine if we're trying to connect as a client
    /// </summary>
    pMasterAnswer m_cachedMasterAnswer { get; set; } = new() { answer = pMasterSessionAnswerType.LeaveLobby };

    /// <summary>
    /// Prevent us from joining a lobby before we've finished the client setup process
    /// </summary>
    [ArchivePatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.OnMasterSessionAnswer))]
    public static class SNet_SessionHub__OnMasterSessionAnswer__Patch
    {
        public static bool Prefix(SNet_SessionHub __instance, pMasterAnswer data)
        {
            StateTracker stateTracker = StateTracker.Get();
            if (stateTracker.m_cachedMasterAnswer.answer == pMasterSessionAnswerType.AllowedToJoinHub)
            {
                FeatureLogger.Notice("Client connection allowed!");
                stateTracker.m_cachedMasterAnswer = new() { answer = pMasterSessionAnswerType.LeaveLobby };
                return true;
            }

            FeatureLogger.Notice("Received master answer; caching and blocking for now");
            stateTracker.m_cachedMasterAnswer = data;
            return false;
        }
    }

    /// <summary>
    /// If we're connected to AP, we can opt in to be the new migration master.
    /// Otherwise, refuse.
    /// This still needs plenty of debugging
    /// </summary>
    [ArchivePatch(typeof(SNet_MasterManager), nameof(SNet_MasterManager.ThisTheNewMasterFoundDuringMigration))]
    public static class SNet_MasterManager__SearchForMigrationMaster__Patch
    {
        public static bool Prefix(SNet_Player masterPlayer)
        {
            if (masterPlayer.Pointer != SNet.LocalPlayer.Pointer) return true;
            return StateTracker.Get().ApSession != null;
        }
    }
}
