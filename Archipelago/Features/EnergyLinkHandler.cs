using Archipelago.MultiClient.Net.Models;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using SNetwork;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using TheArchive.Core.Attributes.Feature;
using TheArchive.Core.Attributes.Feature.Members;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.Features;

[EnableFeatureByDefault, InjectToIl2Cpp]
public class EnergyLinkHandler : ArchipelagoFeature
{
    public override string Name => "Energy Link";
    public override string Description
        => "Handles energy link. This handler controls all energy in/out, but does not add or remove any by itself.";
    public override FeatureGroup Group => FeatureGroups.Archipelago;
    private static IArchiveLogger? m_featureLogger = null;
    public static new IArchiveLogger FeatureLogger
    {
        get => m_featureLogger ?? Plugin.Get().Logger;
        set => m_featureLogger = value;
    }

    [FeatureConfig]
    public static Settings Config { get; set; } = null!;

    public class Settings
    {
        [FSDisplayName("Output Conversion Rate")]
        [FSDescription("When sending energy to your team, how much is sent? Higher values increase the amount sent.")]
        public float OutputConversionRate 
        { 
            get => m_output; 
            set => m_output = MathF.Max(value, 0f); 
        }
        private float m_output = 0.75f;

        [FSDisplayName("Input Consumption Rate")]
        [FSDescription("When consuming energy from the team, how much is consumed? Higher values increase the amount consumed.")]
        public float InputConsumptionRate 
        { 
            get => m_input; 
            set => m_input = MathF.Max(value, 0f); 
        }
        private float m_input = 1f;
    }

    /// <summary>
    /// Wraps replication; is injected so we can register native callbacks for replication
    /// </summary>
    [InjectToIl2Cpp]
    private class EnergyReplicator : Il2CppSystem.Object
    {
        /// <summary>
        /// Energy packet. This datatype is used both for both requests and responses 
        ///  If requesting:
        ///   - Amount is the amount desired
        ///   - If cancel is true, cancel the request if there was not enough. Otherwise, proceed anyway
        ///  If responding:
        ///   - Amount is the amount obtained (or, if cancelled, the amount which is now refunded)
        ///   - If cancel is true, the request was cancelled. Otherwise, it succeeded
        ///  FromPlayer is always from whoever sent the packet.
        /// </summary>
        public struct pEnergyPacket
        {
            /// <summary>
            /// Type of packet
            /// </summary>
            public enum eType : byte
            {
                /// <summary>
                /// A request to fetch the current energy
                /// </summary>
                Get = 0,

                /// <summary>
                /// A request to add to the energy amount
                /// </summary>
                Add = 1,

                /// <summary>
                /// A request to take energy
                /// </summary>
                Take = 2,

                /// <summary>
                /// A request to take energy which shouldn't be cancelled
                /// </summary>
                Take_NoCancel = 3,

                /// <summary>
                /// The get request was successful
                /// </summary>
                Get_Success = 4,

                /// <summary>
                /// The get request failed
                /// </summary>
                Get_Fail = 5,

                /// <summary>
                /// The take request was successful
                /// </summary>
                Take_Success = 6,

                /// <summary>
                /// The take request failed
                /// </summary>
                Take_Fail = 7,
            }

            public pEnergyPacket(eType type, BigInteger amount, ulong playerLookup)
            {
                Type = type;
                Amount = amount;
                PlayerLookup = playerLookup;
            }

            public readonly eType Type;
            public readonly BigInteger Amount;
            public readonly ulong PlayerLookup;

            /// <summary>
            /// Calc the compressed size of this instance, in bytes
            /// </summary>
            /// <returns></returns>
            public int CalcByteSize()
            {
                int size = Amount.GetByteCount();
                return sizeof(byte)
                    + SerializationHelpers.CalcLengthSize(size) + (size * sizeof(byte))
                    + SerializationHelpers.Calc7BitEncodedSize(PlayerLookup);
            }

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
                byte[] value = Amount.ToByteArray();
                bytes[index++] = (byte)Type;
                SerializationHelpers.WriteLength(bytes, ref index, value.Length);
                for (int i = 0; i < value.Length; i++)
                    bytes[index++] = value[i];
                SerializationHelpers.Write7BitEncodedLong(bytes, ref index, PlayerLookup);
            }

            /// <summary>
            /// Read an this struct from raw bytes at the given index.
            /// </summary>
            /// <param name="bytes">The bytes to read from</param>
            /// <param name="index">The index to read at. This will be moved to next unread index</param>
            /// <returns>The struct read from the bytes</returns>
            public static pEnergyPacket ReadFromBytes(Il2CppStructArray<byte> bytes, ref int index)
            {
                long size = SerializationHelpers.ReadLength(bytes, ref index);
                eType type = (eType)bytes[index++];
                byte[] value = new byte[size];
                for (int i = 0; i < size; i++)
                    value[i] = bytes[index++];
                ulong playerLookup = SerializationHelpers.Read7BitEncodedULong(bytes, ref index);
                return new pEnergyPacket(type, new BigInteger(value), playerLookup);
            }

            /// <inheritdoc cref="ReadFromBytes(Il2CppStructArray{byte}), ref int"/>
            public static pEnergyPacket ReadFromBytes(Il2CppStructArray<byte> bytes)
            {
                int offset = 0;
                return ReadFromBytes(bytes, ref offset);
            }
        }

        public EnergyReplicator(IntPtr ptr) : base(ptr) { }

        /// <summary>
        /// Creates a new instance of this using baseReplicator to back its packet(s)
        /// </summary>
        public EnergyReplicator(SNet_Replicator replicator)
            : base(ClassInjector.DerivedConstructorPointer<EnergyReplicator>())
        {
            ClassInjector.DerivedConstructorBody(this);

            IntPtr ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceive), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_packet = replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));
        }

        private SNet_PacketBufferBytes? m_packet = null;
        private SortedList<ushort, TaskCompletionSource<BigInteger>> m_responseTasks = new();

        /// <summary>
        /// Send a request. Returns a task which can be used to await the response
        /// </summary>
        /// <param name="type">The type of request being made</param>
        /// <param name="requestAmount">The amount of energy used in the request (for add or take requests)</param>
        /// <returns></returns>
        [HideFromIl2Cpp]
        public Task<BigInteger> SendRequest(pEnergyPacket.eType type, BigInteger requestAmount)
        {
            // Check if we're in a lobby / if master exists
            if (SNet.Master == null || SNet.Master.Pointer == SNet.LocalPlayer.Pointer)
                throw new Exception("Cannot send energy request; master is either null or self!");

            // Set up request packet
            SNetStructs.pPlayer player = new();
            player.SetPlayer(Player.PlayerManager.GetLocalPlayerAgent().Owner);
            pEnergyPacket packet = new(type, requestAmount, player.lookup);

            // Create new response with id
            ushort id = (ushort)(m_responseTasks.Count + 1);
            TaskCompletionSource<BigInteger> task = new();
            while (m_responseTasks.ContainsKey(id)) ++id;
            m_responseTasks.Add(id, task);

            // Send!
            m_packet!.Send(
                packet.ToBytes(),
                SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(id, 0)),
                SNet_SendQuality.Reliable,
                (int)SNet_ChannelType.SessionOrderCritical,
                SNet.Master
            );
            return task.Task;
        }

        /// <summary>
        /// Callback for receiving an energy request from the network
        /// </summary>
        /// <param name="bytes">Init state as bytes</param>
        /// <param name="data">Data about the buffer being received</param>
        public void OnReceive(Il2CppStructArray<byte> bytes, SNet_PacketBufferBytes.BufferData data)
        {
            pEnergyPacket packet = pEnergyPacket.ReadFromBytes(bytes);
            SNetStructs.pPlayer playerStruct = new()
            {
                lookup = packet.PlayerLookup,
                IsBot = false
            };
            if (!playerStruct.GetPlayer(out var player))
            {
                FeatureLogger.Error("Received energy request, but could not identify response player!");
                return;
            }
            ushort requestId = data.bufferID;

            switch (packet.Type)
            {
                case pEnergyPacket.eType.Get:
                    GetCurrentEnergy().ContinueWith(t => SendResponse(
                        player,
                        requestId,
                        t.IsCompletedSuccessfully ? pEnergyPacket.eType.Get_Success : pEnergyPacket.eType.Get_Fail,
                        t.IsCompletedSuccessfully ? t.Result : 0
                    ));
                    break;

                case pEnergyPacket.eType.Add:
                    _ = AddEnergy(packet.Amount);
                    break;

                case pEnergyPacket.eType.Take:
                    RequestEnergy(packet.Amount, true).ContinueWith(t => SendResponse(
                        player,
                        requestId,
                        t.IsCompletedSuccessfully ? pEnergyPacket.eType.Take_Success : pEnergyPacket.eType.Take_Fail,
                        t.IsCompletedSuccessfully ? t.Result : 0
                    ));
                    break;

                case pEnergyPacket.eType.Take_NoCancel:
                    RequestEnergy(packet.Amount, false).ContinueWith(t => SendResponse(
                        player,
                        requestId,
                        t.IsCompletedSuccessfully ? pEnergyPacket.eType.Take_Success : pEnergyPacket.eType.Take_Fail,
                        t.IsCompletedSuccessfully ? t.Result : 0
                    ));
                    break;

                case pEnergyPacket.eType.Get_Success:
                case pEnergyPacket.eType.Take_Success:
                    m_responseTasks[requestId].SetResult(packet.Amount);
                    m_responseTasks.Remove(requestId);
                    break;

                case pEnergyPacket.eType.Get_Fail:
                case pEnergyPacket.eType.Take_Fail:
                    m_responseTasks[requestId].SetCanceled();
                    m_responseTasks.Remove(requestId);
                    break;

                default:
                    throw new NotSupportedException($"Request type {(byte)packet.Type} not supported!");
            }
        }

        [HideFromIl2Cpp]
        public void SendResponse(SNet_Player toPlayer, ushort id, pEnergyPacket.eType type, BigInteger amount)
        {
            // Create the packet
            SNetStructs.pPlayer playerStruct = new();
            playerStruct.SetPlayer(Player.PlayerManager.GetLocalPlayerAgent()?.Owner);
            pEnergyPacket packet = new(type, amount, playerStruct.lookup);

            // Send!
            m_packet!.Send(
                packet.ToBytes(),
                SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(id, 0)),
                SNet_SendQuality.Reliable,
                (int)SNet_ChannelType.SessionOrderCritical,
                SNet.Master
            );
        }
    }

    private EnergyReplicator? m_replicator = null;

    public override void OnEnable()
    {
        base.OnEnable();
        Plugin.Get().LateSetup += (replicator) =>
        {
            m_replicator = new(replicator);
            StateTracker.Get().OnStateChange += (st) => st.ApSession?.DataStorage[$"EnergyLink{st.ApSession.Players.ActivePlayer.Team}"].Initialize(0);
        };
    }

    /// <summary>
    /// Multiply a BigInteger by a float amount with a reasonable amount of accuracy
    /// </summary>
    /// <param name="value">The original BigInteger value</param>
    /// <param name="conversion">The multiply amount</param>
    /// <returns>The resulting BigInteger value, truncated to int</returns>
    private static BigInteger Multiply(BigInteger value, double conversion)
    {
        if (value.Sign < 0) throw new ArgumentException("Cannot use a negative bigint value");
        if (conversion < 0) throw new ArgumentException("Cannot use a negative conversion multiplier");
        byte[] bytes = value.ToByteArray();
        BigInteger result = 0;
        if (Math.Abs(conversion) < 1.0) // It's getting smaller
        {
            result = (BigInteger)(conversion * ((ulong)value));
            for (int i = sizeof(ulong); i < bytes.Length; i++)
                result += (BigInteger)(conversion * (((ulong)bytes[i]) << ((sizeof(ulong) - 1) * 8))) << (8 * i);
        }
        else // It's getting bigger
        {
            for (int i = 0; i < bytes.Length; i++)
                result += (BigInteger)(conversion * ((ulong)bytes[i])) << (8 * i);
        }

        FeatureLogger.Notice($"BigInt multiplication: {value} * {conversion} = {result}");
        return result;
    }

    /// <summary>
    /// Get the current amount of energy available from the shared pool
    /// </summary>
    /// <returns>The amount available</returns>
    public static async Task<BigInteger> GetCurrentEnergy()
    {
        StateTracker stateTracker = StateTracker.Get();
        if (stateTracker.ApSession != null)
        {
            return Multiply(stateTracker.ApSession.DataStorage[$"EnergyLink{stateTracker.ApSession.Players.ActivePlayer.Team}"] + Operation.Max(0), 1f / Config.InputConsumptionRate);
        }
        else if (stateTracker.CurrentState == StateTracker.eState.FakeConnect)
        {
            FeatureLogger.Debug("Due to fake connect, pretending to have a lot of energy");
            return 1_234_567_890_000_000_000;
        }
        else
        {
            var self = ArchipelagoFeatureHelper.GetFeature<EnergyLinkHandler>();
            return await self.m_replicator!.SendRequest(EnergyReplicator.pEnergyPacket.eType.Get, 0);
        }
    }

    /// <summary>
    /// Requests a set amount of energy from the shared pool
    /// </summary>
    /// <param name="amount">The amount of energy desired desired.</param>
    /// <returns>
    /// The amount received. If the task completed successfully, this will be the amount requested
    ///  (modified by input conversion rate). If the task is failed or cancelled, the request was denied.
    /// </returns>
    public static async Task<BigInteger> RequestEnergy(BigInteger amount, bool allowCancel)
    {
        FeatureLogger.Notice($"Processing energy request for amount: {amount}");
        StateTracker stateTracker = StateTracker.Get();
        if (stateTracker.ApSession != null)
        {
            if (amount.Sign < 0)
                throw new ArgumentException("Cannot request less than 0 energy!");

            BigInteger actualAmount = Multiply(amount, 1f / Config.InputConsumptionRate);
            BigInteger result = (stateTracker.ApSession.DataStorage[$"EnergyLink{stateTracker.ApSession.Players.ActivePlayer.Team}"] - actualAmount) + Operation.Max(0);
            stateTracker.ApSession.DataStorage[$"EnergyLink{stateTracker.ApSession.Players.ActivePlayer.Team}"] = result;
            return BigInteger.Max(actualAmount, result);
        }
        else if (stateTracker.CurrentState == StateTracker.eState.FakeConnect)
        {
            if (amount.Sign < 0)
                throw new ArgumentException("Cannot request less than 0 energy!");

            FeatureLogger.Debug("Due to fake connect, approving energy request");
            return amount;
        }
        else
        {
            var feature = ArchipelagoFeatureHelper.GetFeature<EnergyLinkHandler>();
            EnergyReplicator.pEnergyPacket.eType type = allowCancel 
                ? EnergyReplicator.pEnergyPacket.eType.Take 
                : EnergyReplicator.pEnergyPacket.eType.Take_NoCancel;
            return await feature.m_replicator!.SendRequest(type, amount);
        }
    }

    /// <summary>
    /// Add energy to the EnergyLink pool. This should always succeed.
    /// </summary>
    /// <param name="amount">The amount to add</param>
    public static async Task AddEnergy(BigInteger amount)
    {
        FeatureLogger.Notice($"Adding energy: {amount}");
        StateTracker stateTracker = StateTracker.Get();
        if (stateTracker.ApSession != null)
        {
            if (amount.Sign < 0)
                throw new ArgumentException("Cannot add less than 0 energy!");
            stateTracker.ApSession.DataStorage[$"EnergyLink{stateTracker.ApSession.Players.ActivePlayer.Team}"] += Multiply(amount, Config.OutputConversionRate);
        }
        else if (stateTracker.CurrentState == StateTracker.eState.FakeConnect)
        {
            if (amount.Sign < 0)
                throw new ArgumentException("Cannot add less than 0 energy!");
            FeatureLogger.Debug("Due to fake connect, ignoring energy add request");
        }
        else
        {
            var feature = ArchipelagoFeatureHelper.GetFeature<EnergyLinkHandler>();
            await feature.m_replicator!.SendRequest(EnergyReplicator.pEnergyPacket.eType.Add, amount);
        }
    }
}
