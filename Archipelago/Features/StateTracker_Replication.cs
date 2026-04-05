using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ReTFO.Archipelago.FeaturesAPI;
using ReTFO.Archipelago.Utilities;
using SNetwork;
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
    /// Some helper methods for my serialization.
    /// These are specific to StateTracker and will issue warnings and perform specific behvaiour with regards to that.
    /// </summary>
    public static class SerializationHelpers
    {
        /// <summary>
        /// Get the byte size of an array length.
        /// Currently defaults to an encoded long, but I may optimize it later.
        /// </summary>
        /// <param name="value">The value to calculate the size for</param>
        /// <returns>The size, in bytes</returns>
        public static int GetLengthSize(int value)
            => Get7BitEncodedSize(value, false);

        /// <summary>
        /// Write an array length.
        /// Currently defaults to an encoded long, but I may optimize it later.
        /// </summary>
        /// <param name="bytes">The byte array to write to</param>
        /// <param name="index">Index to write out. Will be moved to the next unwritten byte</param>
        /// <param name="value">The value to write</param>
        public static void WriteLength(Il2CppStructArray<byte> bytes, ref int index, int value)
            => Write7BitEncodedLong(bytes, ref index, value);

        /// <summary>
        /// Read an array length from bytes.
        /// </summary>
        /// <param name="bytes">The bytes to read from</param>
        /// <param name="index">The index to read the length at. Will be moved to the next unread byte</param>
        /// <returns>The read length</returns>
        public static int ReadLength(Il2CppStructArray<byte> bytes, ref int index)
            => checked((int)Read7BitEncodedLong(bytes, ref index));

        /// <summary>
        /// Calculate the number of bytes this long will need after being encoded
        /// </summary>
        /// <param name="value">The value to calculate</param>
        /// <param name="skipZeros">If true, return zero for inputs with zero (and consider them erroneous)</param>
        /// <returns>
        /// The predicted size, in bytes, from 0 to 9. 
        /// 0 is returned if the input is 0 as a special case for StateTracker.
        /// </returns>
        public static int Get7BitEncodedSize(long value, bool skipZeros=true)
            => Get7BitEncodedSize(unchecked((ulong)value));

        /// <inheritdoc cref="Get7BitEncodedSize(long)"/>
        public static int Get7BitEncodedSize(ulong value, bool skipZeros = false)
        {
            // This is the most convenient place to perform debug checks
          #if DEBUG
            if (skipZeros && value == 0)
                FeatureLogger.Error("Attempting to send 0 over the network; not allowed!");
            else if (value > 0xFE000000000000)
                FeatureLogger.Warning("Networking a value greater than allowed; this may cause issues");
          #endif

            if ((value & 0xFF00000000000000) != 0) return 9;
            if ((value & 0xFE000000000000) != 0) return 8;
            if ((value & 0x1FC0000000000) != 0) return 7;
            if ((value & 0x3F800000000) != 0) return 6;
            if ((value & 0x7F0000000) != 0) return 5;
            if ((value & 0xFE00000) != 0) return 4;
            if ((value & 0x1FC000) != 0) return 3;
            if ((value & 0x3F80) != 0) return 2;
            if ((value & 0x7F) != 0) return 1;
            return 0; // We skip zeros
        }

        /// <summary>
        /// Get the size this helper will use to encode the provided enumeration of long values
        /// </summary>
        /// <param name="values">The values to encode</param>
        /// <returns>The calculated encoded size, in bytes</returns>
        public static int Get7BitEncodedArraySize(ICollection<long> values)
            => Get7BitEncodedArraySize(values.Cast<ulong>(), values.Count);

        /// <inheritdoc cref="Get7BitEncodedArraySize(ICollection{long})"/>
        public static int Get7BitEncodedArraySize(ICollection<ulong> values)
            => Get7BitEncodedArraySize(values, values.Count);

        /// <inheritdoc cref="Get7BitEncodedArraySize(ICollection{long})"/>
        /// <param name="count">Number of items in the enumeration</param>
        public static int Get7BitEncodedArraySize(IEnumerable<long> values, int count)
            => Get7BitEncodedArraySize(values.Cast<ulong>(), count);

        /// <inheritdoc cref="Get7BitEncodedArraySize(ICollection{long})"/>
        public static int Get7BitEncodedArraySize(IEnumerable<ulong> values, int count)
            => Get7BitEncodedSize(count) + values.Sum(i => Get7BitEncodedSize(i));

        /// <summary>
        /// Get the size this helper will use to encode multiple enumerations of long values as one array
        /// </summary>
        /// <param name="values">The values which would be encoded</param>
        /// <returns>The calculated encoded size, in bytes</returns>
        public static int Get7BitEncodedMultiArraySize(ICollection<ICollection<long>> values)
            => Get7BitEncodedMultiArraySize(values.Cast<ICollection<ulong>>(), values.Count, values.Sum(arr => arr.Count));

        /// <inheritdoc cref="Get7BitEncodedMultiArraySize(ICollection{ICollection{long}})"/>
        public static int Get7BitEncodedMultiArraySize(ICollection<ICollection<ulong>> values)
            => Get7BitEncodedMultiArraySize(values, values.Count, values.Sum(arr => arr.Count));

        /// <inheritdoc cref="Get7BitEncodedMultiArraySize(IEnumerable{ICollection{long}})"/>
        /// <param name="arrCount">Number of arrays being passed</param>
        /// <param name="valueCount">Number of values being passed (sum of counts in arrays)</param>
        public static int Get7BitEncodedMultiArraySize(IEnumerable<IEnumerable<long>> values, int arrCount, int valueCount)
            => Get7BitEncodedMultiArraySize(values.Cast<IEnumerable<ulong>>(), arrCount, valueCount);

        /// <inheritdoc cref="Get7BitEncodedMultiArraySize(IEnumerable{IEnumerable{long}}, int, int)"/>
        public static int Get7BitEncodedMultiArraySize(IEnumerable<IEnumerable<ulong>> values, int arrCount, int valueCount)
        {
            arrCount = arrCount == 0 ? 0 : arrCount - 1;
            return GetLengthSize(valueCount + arrCount) + arrCount + values.Sum(arr => arr.Sum(i => Get7BitEncodedSize(i)));
        }

        /// <summary>
        /// Write a 7-bit encoded long value to an array, and move the index to the next open spot
        /// </summary>
        /// <param name="arr">The value to write</param>
        /// <param name="index">The index to write at, returned as the next blank index</param>
        /// <param name="value">The value to write</param>
        public static void Write7BitEncodedLong(Il2CppStructArray<byte> bytes, ref int index, long value)
            => Write7BitEncodedLong(bytes, ref index, unchecked((ulong)value));

        /// <inheritdoc cref="Write7BitEncodedLong(Il2CppStructArray{byte}, ref int, long)"/>
        public static void Write7BitEncodedLong(Il2CppStructArray<byte> bytes, ref int index, ulong value)
        {
            // This loop intentially runs at least once, in case the original value is 0
            for (int i = 0; i < 9; i++)
            {
                bytes[index++] = unchecked((byte)((value & 0x7F) | 0x80));
                value >>= 7;
                if (value == 0) break;
            }

            // If the value set its most significant bit, we keep the continuation bit (as a special case which will be handled when reading).
            // Otherwise, we clear it.
            if (value == 0)
                bytes[index - 1] = bytes[index - 1] &= 0x7F; // Clears the continuation bit
        }

        /// <summary>
        /// Reads a 7-bit encoded long from the array starting at the provided index.
        /// Will move the index to the next unread spot.
        /// </summary>
        /// <param name="bytes">The array to read from</param>
        /// <param name="index">The index to read at. Will be modified.</param>
        /// <returns>The unencoded long value</returns>
        public static long Read7BitEncodedLong(Il2CppStructArray<byte> bytes, ref int index)
            => unchecked((long)Read7BitEncodedULong(bytes, ref index));

        /// <inheritdoc cref="Read7BitEncodedLong(Il2CppStructArray{byte}, ref int)"/>
        public static ulong Read7BitEncodedULong(Il2CppStructArray<byte> bytes, ref int index)
        {
            ulong value = 0;
            byte b;
            for (int offset = 0; offset < 56; offset += 7)
            {
                // Read in first 7 bits. If the continuation bit is not set, return immediately
                b = bytes[index++];
                value |= (((ulong)(b & 0x7F)) << offset);
                if ((0x80 & b) == 0) return value;
            }

            // Special case read where we keep all bits
            return value | ((ulong)(bytes[index++]) << 56);
        }

        /// <summary>
        /// Write a collection of values as a 7bit encoded array to the provided byte array.
        /// Index will be moved to the next open spot after the array.
        /// </summary>
        /// <param name="bytes">The array to write to</param>
        /// <param name="index">The index to write the array at</param>
        /// <param name="values">The values to write</param>
        public static void Write7BitEncodedArray(Il2CppStructArray<byte> bytes, ref int index, ICollection<long> values)
            => Write7BitEncodedArray(bytes, ref index, values, values.Count);

        /// <inheritdoc cref="Write7BitEncodedArray(Il2CppStructArray{byte}, ref int, ICollection{long})"/>
        public static void Write7BitEncodedArray(Il2CppStructArray<byte> bytes, ref int index, ICollection<ulong> values)
            => Write7BitEncodedArray(bytes, ref index, values, values.Count);

        /// <inheritdoc cref="Write7BitEncodedArray(Il2CppStructArray{byte}, ref int, ICollection{long})"/>
        /// <param name="count">The count of how many values will be written. This will not be checked!</param>
        public static unsafe void Write7BitEncodedArray(Il2CppStructArray<byte> bytes, ref int index, IEnumerable<long> values, int count)
            => Write7BitEncodedArray(bytes, ref index, values.Cast<ulong>(), count);

        /// <inheritdoc cref="Write7BitEncodedArray(Il2CppStructArray{byte}, ref int, IEnumerable{long}, int)"/>
        public static unsafe void Write7BitEncodedArray(Il2CppStructArray<byte> bytes, ref int index, IEnumerable<ulong> values, int count)
        {
            byte* pBytes = (byte*)IntPtr.Add(bytes.Pointer, 4 * IntPtr.Size).ToPointer();
            WriteLength(bytes, ref index, count);
            foreach (var value in values)
                Write7BitEncodedLong(bytes, ref index, value);
        }

        /// <summary>
        /// Read an array of 7bit encoded long values from raw bytes.
        /// </summary>
        /// <param name="bytes">The bytes to read from</param>
        /// <param name="index">The index to start reading at</param>
        /// <returns>The decoded bytes</returns>
        public static unsafe long[] Read7BitEncodedArray(Il2CppStructArray<byte> bytes, ref int index)
        {
            byte* pBytes = (byte*)IntPtr.Add(bytes.Pointer, 4 * IntPtr.Size).ToPointer();
            int size = ReadLength(bytes, ref index);
            long[] result = new long[size];
            for (int i = 0; i < size; i++) result[i] = Read7BitEncodedLong(bytes, ref index);
            return result;
        }

        /// <inheritdoc cref="Read7BitEncodedArray(Il2CppStructArray{byte}, ref int)"/>
        public static unsafe ulong[] Read7BitEncodedUArray(Il2CppStructArray<byte> bytes, ref int index)
        {
            byte* pBytes = (byte*)IntPtr.Add(bytes.Pointer, 4 * IntPtr.Size).ToPointer();
            int size = ReadLength(bytes, ref index);
            ulong[] result = new ulong[size];
            for (int i = 0; i < size; i++) result[i] = Read7BitEncodedULong(bytes, ref index);
            return result;
        }

        /// <summary>
        /// Write multiple arrays of values to the byte array, separated using 0 as a sentinel value
        /// </summary>
        /// <param name="bytes">The array to write to</param>
        /// <param name="index">The index to write at. Will be moved to to next open spot when done writing</param>
        /// <param name="values">The values to write</param>
        /// <param name="valuesCount">The total number of encoded values in the `values` param</param>
        public static void Write7BitEncodedMultiArray(Il2CppStructArray<byte> bytes, ref int index, ICollection<IEnumerable<long>> values, int valuesCount)
            => Write7BitEncodedMultiArray(bytes, ref index, values.Cast<IEnumerable<ulong>>(), values.Count, valuesCount);

        /// <inheritdoc cref="Write7BitEncodedMultiArray(Il2CppStructArray{byte}, ref int, ICollection{IEnumerable{long}})"/>
        public static void Write7BitEncodedMultiArray(Il2CppStructArray<byte> bytes, ref int index, ICollection<IEnumerable<ulong>> values, int valuesCount)
            => Write7BitEncodedMultiArray(bytes, ref index, values, values.Count, valuesCount);

        /// <inheritdoc cref="Write7BitEncodedMultiArray(Il2CppStructArray{byte}, ref int, ICollection{IEnumerable{long}})"/>
        /// <param name="arrCount">The number of arrays being written</param>
        public static void Write7BitEncodedMultiArray(Il2CppStructArray<byte> bytes, ref int index, IEnumerable<IEnumerable<long>> values, int arrCount, int valuesCount)
            => Write7BitEncodedMultiArray(bytes, ref index, values.Cast<IEnumerable<ulong>>(), arrCount, valuesCount);

        /// <inheritdoc cref="Write7BitEncodedMultiArray(Il2CppStructArray{byte}, ref int, IEnumerable{IEnumerable{long}}, int)"/>
        public static void Write7BitEncodedMultiArray(Il2CppStructArray<byte> bytes, ref int index, IEnumerable<IEnumerable<ulong>> values, int arrCount, int valuesCount)
            => Write7BitEncodedArray(bytes, ref index, values.SelectMany(v => v.Prepend(0u)).Skip(1), valuesCount + arrCount - 1);

        /// <summary>
        /// Wrapper for a span since span is ref only
        /// </summary>
        /// <typeparam name="T">The type contained by the span</typeparam>
        public struct Spannable<T>
        {
            public T[] Values {get; init; }
            public int Start {get; init; }
            public int Length { get; init; }

            public Spannable(T[] values, int start, int length)
            {
                Values = values;
                Start = start;
                Length = length;
            }

            public Span<T> AsSpan() => new Span<T>(Values, Start, Length);
        }

        /// <summary>
        /// Read multiple arrays of values which are separated using 0 as a sentinel value
        /// </summary>
        /// <param name="bytes">The bytes containing the multiarray</param>
        /// <param name="index">The index to start reading at</param>
        /// <returns>The resulting multiarray</returns>
        public static Spannable<long>[] Read7BitEncodedMultiArray(Il2CppStructArray<byte> bytes, ref int index)
        {
            long[] values = Read7BitEncodedArray(bytes, ref index);
            Spannable<long>[] spans = new Spannable<long>[1 + values.Count(i => i == 0)];

            int spanIndex = 0;
            int start = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == 0)
                {
                    spans[spanIndex++] = new(values, start, i - start);
                    start = i + 1;
                }
            }

            return spans;
        }

        /// <summary>
        /// Read multiple arrays of values which are separated using 0 as a sentinel value
        /// </summary>
        /// <param name="bytes">The bytes containing the multiarray</param>
        /// <param name="index">The index to start reading at</param>
        /// <returns>The resulting multiarray</returns>
        public static Spannable<ulong>[] Read7BitEncodedMultiUArray(Il2CppStructArray<byte> bytes, ref int index)
        {
            ulong[] values = Read7BitEncodedUArray(bytes, ref index);
            Spannable<ulong>[] spans = new Spannable<ulong>[1 + values.Count(i => i == 0)];

            int spanIndex = 0;
            int start = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == 0)
                {
                    spans[spanIndex++] = new(values, start, i - start);
                    start = i + 1;
                }
            }

            return spans;
        }

        /// <summary>
        /// Calculate the number of bytes needed to serialize strings
        /// </summary>
        /// <param name="strings">The strings which would be serialized</param>
        /// <returns>The size, in bytes</returns>
        public static int CalcStringArraySize(ICollection<string> strings)
            => CalcStringArraySize(strings, strings.Count);

        /// <inheritdoc cref="CalcStringArraySize(ICollection{string})"/>
        /// <param name="arrSize">Number of strings in the array</param>
        public static int CalcStringArraySize(IEnumerable<string> strings, int arrSize)
        {
          #if DEBUG
            if (strings.Any(s => s.Contains('\0')))
                FeatureLogger.Error("Serializing a string with '\\0' inside of it!");
          #endif

            return GetLengthSize(arrSize)
                + strings.Sum(s => s.Length * sizeof(char) + sizeof(char));
        }

        /// <summary>
        /// Write strings to a byte array at the specified index
        /// </summary>
        /// <param name="bytes">The byte array to write to</param>
        /// <param name="index">The index to write at. Will be moved to the next unwritten byte</param>
        /// <param name="strings">The strings to write</param>
        public static unsafe void WriteStringArray(Il2CppStructArray<byte> bytes, ref int index, IEnumerable<string> strings)
        {
            WriteLength(bytes, ref index, strings.Count());

            byte* pBytes = (byte*)IntPtr.Add(bytes.Pointer, IntPtr.Size * 4).ToPointer();
            int count = 0;
            foreach (var s in strings)
            {
                s.AsSpan().CopyTo(new Span<char>(pBytes + index, (bytes.Length - index) / sizeof(char)));
                index += sizeof(char) * s.Length;
                *(char*)(pBytes + index) = '\0';
                index += sizeof(char);
                ++count;
            }
        }

        /// <summary>
        /// Reads a string array from a byte array
        /// </summary>
        /// <param name="bytes">The array to read from</param>
        /// <param name="index">The index to start reading from. Will be moved to the next unread byte</param>
        /// <returns>The deserialized string array</returns>
        public static unsafe string[] ReadStringArray(Il2CppStructArray<byte> bytes, ref int index)
        {
            byte* pBytes = (byte*)IntPtr.Add(bytes.Pointer, IntPtr.Size * 4).ToPointer();
            int count = ReadLength(bytes, ref index);

            string[] result = new string[count];
            for (count = 0; count < result.Length; count++)
            {
                int start = index;
                while (*(char*)(pBytes + index) != '\0')
                    index += sizeof(char);
                result[count] = new Span<char>((char*)(pBytes + start), (index - start) / sizeof(char)).ToString();
                index += sizeof(char);
            }
            return result;
        }
    }

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
        /// Randomization categories that have been enabled
        /// </summary>
        public string[] RandCategories;

        /// <summary>
        /// Calulcate the size of this struct, when serialized, in bytes
        /// </summary>
        /// <returns>The size of this struct, when serialized, in bytes</returns>
        public int CalcByteSize()
            => SerializationHelpers.Get7BitEncodedSize(RootSeed)
            + (new string[2][] { ExpeditionNames, RandCategories }).Sum(SerializationHelpers.CalcStringArraySize);

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
            SerializationHelpers.WriteStringArray(bytes, ref index, RandCategories);
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
            result.RandCategories = SerializationHelpers.ReadStringArray(bytes, ref index);
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
            => SerializationHelpers.Get7BitEncodedMultiArraySize(new long[2][] { ItemIds, ItemsInTerminalSystem });

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
            m_replicator = SNet_Replication.AddManagerReplicator(new IReplicatorSupplier(this.Pointer));
            SNet_SyncManager.RegisterCaptureCallback(new ICaptureCallbackObject(this.Pointer));

            SNet_Replicator replicator = m_replicator.Cast<SNet_Replicator>();

            IntPtr ptr;

            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveInitState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_initStatePacket = replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveGeneralState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_generalStatePacket = replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

            ptr = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethod(
                this.ObjectClass, false, nameof(OnReceiveRecallState), typeof(void).FullName!, new string[] { typeof(Il2CppStructArray<byte>).FullName!, typeof(SNet_PacketBufferBytes.BufferData).FullName! }
            );
            m_recallStatePacket = replicator.CreatePacketBytes(new Il2CppSystem.Action<Il2CppStructArray<byte>, SNet_PacketBufferBytes.BufferData>(this, ptr));

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
        public IReplicator GetReplicator() => m_replicator;
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
        private IReplicator m_replicator = null!;
        private StateTracker m_owner = null!;

        // Packets
        // Note that we have to reuse existing generic instantiations for which AOT code
        //  exists; we'll reinterpret the bytes before forwarding calls to StateTracker
        private SNet_PacketBufferBytes m_initStatePacket = null!;
        private SNet_PacketBufferBytes m_generalStatePacket = null!;
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
        /// Callback fro rececing init state from the network
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
                    SNet_SendQuality.Reliable_WithBuffering,
                    (int)SNet_ChannelType.SessionOrderCritical
                );
            }
            else
            {
                m_initStatePacket.Send(
                    m_owner.MakeInitState().ToBytes(),
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendQuality.Reliable_WithBuffering,
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
                    SNet_SendQuality.Reliable_WithBuffering,
                    (int)SNet_ChannelType.SessionOrderCritical
                );
            }
            else
            {
                m_generalStatePacket.Send(
                    m_owner.MakeGeneralState().ToBytes(),
                    SNet_PacketBufferBytes.GetBufferDataBytes(new SNet_PacketBufferBytes.BufferData(1, 0)),
                    SNet_SendQuality.Reliable_WithBuffering,
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
    }

    /// <summary>
    /// The state replicator for this StateTracker
    /// </summary>
    ArchipelagoStateReplicator? m_stateReplicator = null;

    private class UpdateStateEnumerator : IEnumerator
    {
        private StateTracker m_owner;

        public UpdateStateEnumerator(StateTracker owner)
            => m_owner = owner;

        public object Current => new UnityEngine.WaitForSecondsRealtime(60f);
        
        public bool MoveNext()
        {
            if (m_owner.CurrentState.IsConnected || m_owner.CurrentState.IsFakeConnected)
                m_owner.m_stateReplicator!.SendGeneral();
            return true;
        }

        public void Reset() { }
    }

    /// <summary>
    /// Ensure replication is running for this StateTracker
    /// </summary>
    public void SetupReplication()
    {
        if (m_stateReplicator != null) return;
        m_stateReplicator ??= new(this);

        // Seems fitting to put this on SNet, though it probably doesn't matter
        SNet.Current.StartCoroutine(new Il2CppEnumerator(new UpdateStateEnumerator(this)));
    }

    /// <summary>
    /// Wrap the current state of the StateTracker into a struct used for replication.
    /// This 
    /// </summary>
    /// <returns>The current state for the StateTracker</returns>
    public pArchipelagoInitState MakeInitState()
    {
        // Might optimize this later
        return new pArchipelagoInitState()
        {
            RootSeed = RootSeed,
            ExpeditionNames = ExpeditionNames.ToArray(),
            RandCategories = RandomizationCategories.ToArray(),
        };
    }

    /// <summary>
    /// Wrap the current state of the StateTracker into a struct used for recalls.
    /// </summary>
    /// <returns></returns>
    public pArchipelagoGeneralState MakeGeneralState()
    {
        // Might optimize this later
        return new pArchipelagoGeneralState()
        {
            ItemIds = ItemCounts.SelectMany(pair => Enumerable.Repeat(pair.Key.ID, pair.Value)).ToArray(),
            ItemsInTerminalSystem = ItemsInTerminalSystem.Select(pair => pair.Item1.ID).ToArray(),
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
        if (!(CurrentState.IsConnected || CurrentState.IsClientConnected)) return;
        
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
        if (!CurrentState.IsDisconnected)
        {
            FeatureLogger.Warning("Received init state while connected. Ignoring!");
            return;
        }

        RootSeed = state.RootSeed;
        ExpeditionNames = state.ExpeditionNames.ToList();
        RandomizationCategories = state.RandCategories.ToHashSet();
        CurrentState = eState.ClientConnect;

        PostConnectCommon();
    }

    /// <summary>
    /// Receive a general state, expected periodically to ensure good sync
    /// </summary>
    /// <param name="state">The state being received</param>
    /// <param name="isRecall">If the state is the reuslt of a recall</param>
    public void ReceiveGeneralState(pArchipelagoGeneralState state, bool isRecall)
    {
        if (!CurrentState.IsClientConnected)
        {
            FeatureLogger.Warning("Ignoring GeneralState packet because this is not a client!");
            return;
        }

        var gameData = MidManager.GetProcessedGameData();

        foreach (var id in state.ItemsInTerminalSystem)
            AddItemToTerminal(gameData.LookupItem(id));

        // Reading state can never lower item counts
        var newItemCounts = state.ItemIds.GroupBy(i => i)
            .ToDictionary(g => gameData.LookupItem(g.Key), g => g.Count());
        foreach (var key in ItemCounts.Keys.Union(newItemCounts.Keys)) 
        {
            if (isRecall)
            {
                int count = ItemCounts[key];
                for (int i = newItemCounts[key]; i < count; i++)
                    key.OnItemObtained(this, 0, null);
            }
            else
            {
                int count = newItemCounts[key];
                for (int i = ItemCounts[key]; i < count; i++)
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
                CollectItem(interaction.Value);
                break;

            case pArchipelagoInteraction.eType.AddItemToTerminal:
                AddItemToTerminal(interaction.Value);
                break;

            default:
                FeatureLogger.Error($"Received unknown interaction type: {interaction.Type}");
                break;
        }
    }

    /// <summary>
    /// Patch which sends init packet when players join the lobby
    /// </summary>
    [ArchivePatch(typeof(SNet_LobbyManager), nameof(SNet_LobbyManager.PlayerJoinedLobby))]
    public static class SNet_LobbyManager__PlayerJoinedLobby__Patch
    {
        public static void Prefix(SNet_Player player)
        {
            StateTracker stateTracker = StateTracker.Get();
            if (stateTracker.CurrentState.IsConnected || stateTracker.CurrentState.IsFakeConnected)
                stateTracker.m_stateReplicator!.SendInit(player);
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
