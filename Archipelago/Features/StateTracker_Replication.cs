using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Xml.Schema;
using JetBrains.Annotations;
using ReTFO.Archipelago.FeaturesAPI;
using SNetwork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using pArtifactInventoryState = BoosterImplants.pArtifactInventoryState;

namespace ReTFO.Archipelago.Features;

// Tracks Archipelago state.
// This file is dedicated to the SNetwork integration for StateTracker
[InjectToIl2Cpp]
public partial class StateTracker : ArchipelagoFeature
{
    /// <summary>
    /// Struct used to update Archipelago's state (including during recalls)
    /// </summary>
    public struct pArchipelagoState
    {
        /// <summary>
        /// IDs for regions which have been found
        /// </summary>
        public long[] RegionIDs;

        /// <summary>
        /// IDs for locations which have been checked
        /// </summary>
        public long[] LocationIDs;

        /// <summary>
        /// IDs for items which have been obtained; may contain repeating IDs
        /// </summary>
        public long[] ItemIds;
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
            CheckLocation,
        }

        /// <summary>
        /// The type of interaction
        /// </summary>
        [FieldOffset(0)]
        public eType type;

        /// <summary>
        /// A singular long value associated with the interaction, if applicable
        /// </summary>
        [FieldOffset(4)]
        public long value;
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
            m_replicator = SNet_Replication.AddManagerReplicator(new IReplicatorSupplier(this.Pointer));
            SNet_SyncManager.RegisterCaptureCallback(new ICaptureCallbackObject(this.Pointer));

            SNet_Replicator replicator = m_replicator.Cast<SNet_Replicator>();

            IntPtr receiveStatePtr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_statePacket = replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, receiveStatePtr));

            IntPtr receiveRecallStatePtr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveRecallState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_recallStatePacket = replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, receiveRecallStatePtr));

            IntPtr receiveInteractionPtr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveInteraction), typeof(void).FullName!, new string[] { typeof(pArtifactInventoryState).FullName! }
            );
            m_interactionPacket = m_replicator.CreatePacket(new Il2CppSystem.Action<pArtifactInventoryState>(this, receiveStatePtr));
        }

        // IReplicatorSupplier
        public UnityEngine.GameObject gameObject => m_gameObject;
        public string name => s_name;
        public IReplicator Replicator => m_replicator.Cast<IReplicator>();
        // END IReplicatorSupplier

        // ICaptureCallbackObject
        public IReplicator GetReplicator() => m_replicator;
        public void OnStateCapture()
        {
            // This capture statement doesn't seem to work?
            // m_recallStatePacket.CaptureToBuffer(ToBytes(m_owner.MakeState()), eCapturePass.SessionPass);

            // Manually creating the packet and adding it to the capture
            Il2CppStructArray<byte> packetBytes = ToBytes(m_owner.MakeState());
            Il2CppStructArray<byte> captureBytes = new(packetBytes.Length + 6);

            // Using an unsafe copy for speed's sake
            unsafe
            {
                byte* pSource = (byte*)IntPtr.Add(packetBytes.Pointer, 4 * IntPtr.Size).ToPointer();
                byte* pDest = (byte*)IntPtr.Add(captureBytes.Pointer, 4 * IntPtr.Size).ToPointer();
                Buffer.MemoryCopy(pSource, pDest + 6, captureBytes.Length - 3, packetBytes.Length);
            }

            var miscBytes = m_recallStatePacket.Replicator.KeyBytes; // Caching because idk if it remakes it each time
            captureBytes[0] = miscBytes[0];
            captureBytes[1] = miscBytes[1];

            captureBytes[2] = m_recallStatePacket.Index;

            miscBytes = SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0));
            captureBytes[3] = miscBytes[0];
            captureBytes[4] = miscBytes[1];
            captureBytes[5] = miscBytes[2];

            SNet.Capture.PrimedBuffer.GetPass(eCapturePass.SessionPass).Add(captureBytes);
        }
        // END ICaptureCallbackObject

        private const string s_name = "State Tracker Replicator";
        private UnityEngine.GameObject m_gameObject = new(s_name);
        private IReplicator m_replicator = null!;
        private StateTracker m_owner = null!;

        // Packets
        // Note that we have to reuse existing generic instantiations for which AOT code
        //  exists; we'll reinterpret the bytes before forwarding calls to StateTracker
        private SNet_PacketBufferBytes m_statePacket = null!;
        private SNet_PacketBufferBytes m_recallStatePacket = null!;
        private SNet_Packet<pArtifactInventoryState> m_interactionPacket = null!;
        private ushort m_currentBufferId = 0;

        /// <summary>
        /// Returns replicator information for the replicator backing this state replicator
        /// </summary>
        public pStateReplicatorProvider GetProviderSyncStruct()
        {
            SNetStructs.pReplicator rep = new();
            rep.SetID(m_replicator);
            return new pStateReplicatorProvider() { pRep = rep };
        }

        /// <summary>
        /// Convert from an Archipelago state to bytes for networking
        /// </summary>
        /// <param name="state">The state to convert</param>
        /// <returns>The converted state</returns>
        private Il2CppStructArray<byte> ToBytes(pArchipelagoState state)
        {
            // We'll be writing the data in 7-bit compressed format, using a 7-bit 0 as a sentinel value (since AP disallows IDs of 0)
            static int calc7BitSize(long value)
            {
                ulong testValue = unchecked((ulong)value);

                // This is the most convenient place to perform debug checks
                if (testValue == 0)
                    FeatureLogger.Error("Attempting to send 0 over the network; not allowed!");
                else if (testValue > 0xFE000000000000)
                    FeatureLogger.Warning("Networking a value greater than allowed; this may cause issues");

                if ((testValue & 0xFF00000000000000) != 0) return 9;
                if ((testValue & 0xFE000000000000) != 0) return 8;
                if ((testValue & 0x1FC0000000000) != 0) return 7;
                if ((testValue & 0x3F800000000) != 0) return 6;
                if ((testValue & 0x7F0000000) != 0) return 5;
                if ((testValue & 0xFE00000) != 0) return 4;
                if ((testValue & 0x1FC000) != 0) return 3;
                if ((testValue & 0x3F80) != 0) return 2;
                if ((testValue & 0x7F) != 0) return 1;
                return 0; // We skip zeros
            }

            static bool notZero(long i) => i != 0;

            int size = state.RegionIDs.Concat(state.LocationIDs).Concat(state.ItemIds).Sum(calc7BitSize) + 2; // 2 sentinel values
            var values = Enumerable.Empty<long>()
                .Concat(state.RegionIDs.Where(notZero))
                .Append(0)
                .Concat(state.LocationIDs.Where(notZero))
                .Append(0)
                .Concat(state.ItemIds.Where(notZero));
            Il2CppStructArray<byte> bytes = new(size);
            int index = 0;
            foreach (long value in values)
            {
                ulong testValue = unchecked((ulong)value);

                // This loop intentially runs at least once, in case the original value is 0
                for (int i = 0; i < 9; i++)
                {
                    bytes[index++] = unchecked((byte)((testValue & 0x7F) | 0x80));
                    testValue >>= 7;
                    if (testValue == 0) break;
                }

                // If the value set its most significant bit, we keep the continuation bit (as a special case which will be handled when reading)
                if (testValue == 0)
                    bytes[index - 1] = bytes[index - 1] &= 0x7F; // Clear the continuation bit
            }

            if (index != size)
                FeatureLogger.Warning("Did not set all bytes while converting IDs to 7-bit format");
            return bytes;
        }

        /// <summary>
        /// Convert from raw bytes to an archipelago state
        /// </summary>
        /// <param name="bytes">The bytes to convert from</param>
        /// <returns>The converted state</returns>
        private pArchipelagoState ToState(Il2CppStructArray<byte> bytes)
        {
            // Regions are assumed to contain region ID 0, which corrseponds to the menu
            // The other two zeros are our sentinel values
            Il2CppStructArray<long>[] arrs = new Il2CppStructArray<long>[3];
            int currentArr = 0;

            // Measuring array sizes
            int currentCount = 0;
            int currentByteCount = 0;
            foreach (byte b in bytes)
            {
                if (b == 0 && currentByteCount == 0)
                {
                    arrs[currentArr++] = new(currentCount);
                    currentCount = 0;
                    continue;
                }

                ++currentByteCount;
                if ((0x80 & b) == 0 || currentByteCount >= 9)
                {
                    ++currentCount;
                    currentByteCount = 0;
                }
            }

            if (currentArr != 2) throw new NotSupportedException("Expected two sentinel values in byte array, got a different count!"); // Idk, I'm not handling this right now
            arrs[currentArr] = new(currentCount);

            // Decompressing and copying data into the arrays
            currentArr = 0;
            currentCount = 0;
            ulong currentValue = 0;
            int index = 0;
            foreach (var b in bytes)
            {
                if (b == 0 && currentCount == 0)
                {
                    if (index < arrs[currentArr].Length)
                        FeatureLogger.Warning($"Failed to fill buffer {currentArr} during network deserialization");

                    index = 0;
                    ++currentArr;
                    continue;
                }

                currentValue |= (((ulong)(b & 0x7F)) << currentCount);
                currentCount += 7;

                if ((0x80 & b) == 0 || currentCount >= 63)
                {
                    arrs[currentArr][index++] = unchecked((long)currentValue);
                    currentValue = 0;
                    currentCount = 0;
                }
            }

            return new pArchipelagoState()
            {
                RegionIDs = arrs[0],
                LocationIDs = arrs[1],
                ItemIds = arrs[2],
            };
        }

        /// <summary>
        /// Convert from an Archipelago interaction to the type we can use for networking
        /// </summary>
        /// <param name="interaction">The interaction to convert</param>
        /// <returns>The converted interaction</returns>
        private unsafe pArtifactInventoryState ToNetInteraction(pArchipelagoInteraction interaction)
        {
            pArtifactInventoryState newInteraction;
                
            // Simple reinterpretation
            newInteraction = *(pArtifactInventoryState*)&interaction;
            return newInteraction;
        }

        /// <summary>
        /// Convert from the type we use for networking to an Archipelago interaction
        /// </summary>
        /// <param name="interaction">The interaction to convert</param>
        /// <returns>The converted interaction</returns>
        private unsafe pArchipelagoInteraction ToInteraction(pArtifactInventoryState interaction)
        {
            pArchipelagoInteraction newInteraction;

            // Simple reinterpretation
            newInteraction = *(pArchipelagoInteraction*)&interaction;
            return newInteraction;
        }

        /// <summary>
        /// Callback for receiving state from the network
        /// </summary>
        /// <param name="bytes">State being received, in bytes</param>
        /// <param name="data">Data about the buffer being received</param>
        private void OnReceiveState(Il2CppStructArray<byte> bytes, SNet_PacketBufferBytes.BufferData data)
        {
            // TODO: Do something with the state
            pArchipelagoState newState = ToState(bytes);
            FeatureLogger.Warning("Received state, cannot use!");
        }

        /// <summary>
        /// Callback for receiving state during a recall
        /// </summary>
        /// <param name="bytes">The state being received, as bytes</param>
        /// <param name="data">Data about the buffer being received</param>
        private void OnReceiveRecallState(Il2CppStructArray<byte> bytes, SNet_PacketBufferBytes.BufferData data)
        {
            // TODO: Do something with the state
            pArchipelagoState state = ToState(bytes);
            FeatureLogger.Warning("Received recall state, cannot use!");
        }

        /// <summary>
        /// Callback for receiving state during an interaction
        /// </summary>
        /// <param name="interaction">The interaction being received</param>
        private void OnReceiveInteraction(pArtifactInventoryState bytes)
        {
            // TODO: Do something with the interaction
            pArchipelagoInteraction interaction = ToInteraction(bytes);
            FeatureLogger.Warning("Received interaction, cannot use!");
        }

        /// <summary>
        /// Update the current state as a result of an interaction. This will sync the interaction
        ///  and set the current state.
        /// </summary>
        /// <param name="state">The new state, after being modified by the interaction</param>
        /// <param name="interaction">The interaction being performed</param>
        public void InteractWithState(pArchipelagoState state, pArchipelagoInteraction interaction)
        {
            m_interactionPacket.Send(ToNetInteraction(interaction), SNet_ChannelType.SessionOrderCritical);
            m_statePacket.Send(
                ToBytes(state),
                SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(m_currentBufferId, 0)),
                SNet_SendGroup.PlayersInSessionHub,
                SNet_SendQuality.Unreliable,
                (int)SNet_ChannelType.SessionOrderCritical
            );
        }

        /// <summary>
        /// Set the current state of the replicator without syncing. Use when receiving network updates to set the internal state.
        /// </summary>
        /// <param name="state">The new state to use</param>
        public void SetStateUnsynced(pArchipelagoState state)
        {

        }
    }

    ArchipelagoStateReplicator? m_stateReplicator = null;

    protected void SetupReplication()
    {
        m_stateReplicator ??= new(this);
        m_stateReplicator.SetStateUnsynced(MakeState());
    }

    /// <summary>
    /// Wrap the current state of the StateTracker into a struct used for replication.
    /// Used during new client syncs and during recalls.
    /// </summary>
    /// <returns>The current state for the StateTracker</returns>
    public pArchipelagoState MakeState()
    {
        return new pArchipelagoState()
        {
            RegionIDs = FoundRegions.ToArray(),
            LocationIDs = FoundLocations.ToArray(),
            ItemIds = CollectedItemCounts.SelectMany(pair => Enumerable.Repeat(pair.Key.ID, pair.Value)).ToArray(),
        };
    }


    // Reference legacy code from my AlternateFireModes mod. Not sure how much of this I'll be copying, if any
    /*
    // Modify a struct injected by Il2CppInterop to be a proper blittable type
    // The type should be a proper, blittable value type. Error checks will throw where issues are
    //  detected, but this is not guaranteed to catch all issues
    public unsafe static void FixValueType(Type type)
    {
        // Il2CppInterop's internal interface for editting classes
        INativeClassStruct klass = UnityVersionHandler.Wrap((Il2CppClass*)Il2CppClassPointerStore.GetNativeClassPointer(type));

        // Making this a value type lets Il2Cpp box it via IL2CPP.il2cpp_value_box
        klass.ValueType = true;

        // This prevents it from trying to garbage collect itself (since it doesn't live on the heap)
        klass.HasFinalize = false;

        // is_blittable isn't exposed for some reason, so we use reflection to manually set it

        // Il2Cpp uses a struct to represent the binary for the class, and contains both the bits to set and the enum of bit locations insde that struct
        // Structs and their interfaces are kept together under the same containing public class
        // So, we get the declaring type and find the underlying struct, and steal the info we need from there
        // GTFO, as a Unity 2019.1 project, uses NativeClassStructHandler_24_4, which uses the struct Il2CppClass_24_4. Look there for details
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        Type klassType = klass.GetType();
        Type containedType = klassType.DeclaringType?.GetNestedTypes(flags).First(t => t.Name.StartsWith("Il2CppClass"))
                         ?? throw new TypeLoadException($"Il2CppClass not found for interface {klassType.FullName}.");

        // Field containing the bits
        int _bitfield0offset = Marshal.OffsetOf(containedType, "_bitfield0").ToInt32();

        // Getting the value from the enum
        Type enumType = containedType.GetNestedType("Bitfield0", flags) ?? throw new TypeLoadException($"Enum not found");
        if (!enumType.IsEnum) throw new ArgumentException($"{enumType.FullName} is not an enum.");
        if (!Enum.IsDefined(enumType, "BIT_is_blittable"))
            throw new ArgumentException($"Value 'BIT_is_blittable' is not defined on enum {enumType.FullName}.");
        object result = Enum.Parse(enumType, "BIT_is_blittable");
        ushort is_blittable_index = Convert.ToUInt16(result);

        // IsBlittable is necessary for this type to be used in ComputeBuffers
        klass.SetBit(_bitfield0offset, is_blittable_index, true);

        // Il2CppInterop only registers fields which use its generics (Il2CppValueField<>, Il2CppReferenceField<>, etc)
        // However, for this to work we need the correct fields. So we're going to manually register those

        // Free the field info Il2CppInterop generated
        Marshal.FreeHGlobal((IntPtr)klass.Fields);

        // Getting the fields
        FieldInfo[]? fieldsToInject = type
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            //.Where(IsFieldEligible) <- This is the filter used by Il2CppInterop which we're now skipping
            .ToArray();
        klass.FieldCount = (ushort)fieldsToInject.Length;
        Il2CppFieldInfo* il2cppFields = (Il2CppFieldInfo*)Marshal.AllocHGlobal(klass.FieldCount * UnityVersionHandler.FieldInfoSize());

        INativeClassStruct baseKlass = UnityVersionHandler.Wrap((Il2CppClass*)Il2CppClassPointerStore<ValueType>.NativeClassPtr);
        int fieldOffset = (int)baseKlass.InstanceSize; // Start at the end of the base type
        for (var i = 0; i < klass.FieldCount; i++)
        {
            // Basic field info
            INativeFieldInfoStruct fieldInfo = UnityVersionHandler.Wrap(il2cppFields + i * UnityVersionHandler.FieldInfoSize());
            fieldInfo.Name = Marshal.StringToCoTaskMemUTF8(fieldsToInject[i].Name);
            fieldInfo.Parent = klass.ClassPointer;
            fieldInfo.Offset = fieldOffset;

            Type fieldType = fieldsToInject[i].FieldType;

            // I'm not entirely sure if this is necessary, but since I'm not handling it I'm checking it
            if (!fieldType.IsValueType || fieldType.IsGenericType) 
                throw new NotImplementedException("Fields of blittable value type must be non-generic value types");

            // Field-specific type information, such as how it's stored
            IntPtr fieldInfoClass = Il2CppClassPointerStore.GetNativeClassPointer(fieldType);
            INativeTypeStruct? sourceFieldType = UnityVersionHandler.Wrap((Il2CppTypeStruct*)IL2CPP.il2cpp_class_get_type(fieldInfoClass));
            if (fieldInfoClass == IntPtr.Zero)
                throw new Exception($"Type {fieldType} in {type}.{fieldsToInject[i].Name} doesn't exist in Il2Cpp");
            
            // If we need to overwrite the field attributes, we create a copy of the field type and use that instead
            //  -> This may be unecessary, but it's what Il2CppInterop does so I'm doing it to.
            //  -> Also, we should be caching and reusing these where possible, but I'm lazy
            FieldAttributes fieldAttributes = fieldsToInject[i].Attributes;
            IntPtr fieldTypePtr;
            if (sourceFieldType.Attrs != (ushort)fieldAttributes)
            {
                INativeTypeStruct duplicatedType = UnityVersionHandler.NewType();
                duplicatedType.Data = sourceFieldType.Data;
                duplicatedType.Attrs = (ushort)fieldAttributes;
                duplicatedType.Type = sourceFieldType.Type;
                duplicatedType.ByRef = sourceFieldType.ByRef;
                duplicatedType.Pinned = sourceFieldType.Pinned;
                fieldTypePtr = duplicatedType.Pointer;
            }
            else
                fieldTypePtr = sourceFieldType.Pointer;
            fieldInfo.Type = (Il2CppTypeStruct*)fieldTypePtr;

            // Updating the field offset
            if (IL2CPP.il2cpp_class_is_valuetype(fieldInfoClass))
            {
                uint _align = 0;
                var fieldSize = IL2CPP.il2cpp_class_value_size(fieldInfoClass, ref _align);
                fieldOffset += fieldSize;
            }
            else
            {
                //fieldOffset += sizeof(Il2CppObject*);
                // This seems unsafe, so I'm gonna throw an exception if this happens
                throw new Exception($"Field {type}.{fieldsToInject[i].Name} is a reference type, which is not supported for making blittable types");
            }
        }
        klass.Fields = il2cppFields;

        // We're not tracking a gcHandle, so we can omit that (fortunately)
        klass.ActualSize = klass.InstanceSize = (uint)(fieldOffset);// + sizeof(InjectedClassData));

        // This ensures Il2Cpp treats this type as a value type (and that it will copy the whole thing when doing calls)
        // This is most notable for array creation, where this will now get its full size per array instead of just 8 bytes per item
        klass.ThisArg.Type = klass.ByValArg.Type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE;
    }
     */

}
