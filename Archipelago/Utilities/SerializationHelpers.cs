
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ReTFO.Archipelago.FeaturesAPI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.Utilities;

/// <summary>
/// Various serialization helpers for SNetwork network serialization in Il2Cpp.
/// </summary>
public static class SerializationHelpers
{
    /// <summary>
    /// Get the byte size of an array length.
    /// Currently defaults to an encoded long, but I may optimize it later.
    /// </summary>
    /// <param name="value">The value to calculate the size for</param>
    /// <returns>The size, in bytes</returns>
    public static int CalcLengthSize(int value)
        => value == 0 ? 1 : Calc7BitEncodedSize(value); // Check bypasses debug code in Calc7BitEncodedSize

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
    public static int Calc7BitEncodedSize(long value)
        => Calc7BitEncodedSize(unchecked((ulong)value));

    /// <inheritdoc cref="Calc7BitEncodedSize(long)"/>
    public static int Calc7BitEncodedSize(ulong value)
    {
        // This is the most convenient place to perform debug checks
#if DEBUG
        if (value == 0)
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
        //if ((value & 0x7F) != 0) return 1;
        return 1; 
    }

    /// <summary>
    /// Get the size this helper will use to encode the provided enumeration of long values
    /// </summary>
    /// <param name="values">The values to encode</param>
    /// <returns>The calculated encoded size, in bytes</returns>
    public static int Calc7BitEncodedArraySize(ICollection<long> values)
        => Calc7BitEncodedArraySize(values.Cast<ulong>(), values.Count);

    /// <inheritdoc cref="Calc7BitEncodedArraySize(ICollection{long})"/>
    public static int Calc7BitEncodedArraySize(ICollection<ulong> values)
        => Calc7BitEncodedArraySize(values, values.Count);

    /// <inheritdoc cref="Calc7BitEncodedArraySize(ICollection{long})"/>
    /// <param name="count">Number of items in the enumeration</param>
    public static int Calc7BitEncodedArraySize(IEnumerable<long> values, int count)
        => Calc7BitEncodedArraySize(values.Cast<ulong>(), count);

    /// <inheritdoc cref="Calc7BitEncodedArraySize(ICollection{long})"/>
    public static int Calc7BitEncodedArraySize(IEnumerable<ulong> values, int count)
        => Calc7BitEncodedSize(count) + values.Sum(i => Calc7BitEncodedSize(i));

    /// <summary>
    /// Get the size this helper will use to encode multiple enumerations of long values as one array
    /// </summary>
    /// <param name="values">The values which would be encoded</param>
    /// <returns>The calculated encoded size, in bytes</returns>
    public static int Calc7BitEncodedMultiArraySize(ICollection<ICollection<long>> values)
        => Calc7BitEncodedMultiArraySize(values.Cast<ICollection<ulong>>(), values.Count, values.Sum(arr => arr.Count));

    /// <inheritdoc cref="Calc7BitEncodedMultiArraySize(ICollection{ICollection{long}})"/>
    public static int Calc7BitEncodedMultiArraySize(ICollection<ICollection<ulong>> values)
        => Calc7BitEncodedMultiArraySize(values, values.Count, values.Sum(arr => arr.Count));

    /// <inheritdoc cref="Calc7BitEncodedMultiArraySize(IEnumerable{ICollection{long}})"/>
    /// <param name="arrCount">Number of arrays being passed</param>
    /// <param name="valueCount">Number of values being passed (sum of counts in arrays)</param>
    public static int Calc7BitEncodedMultiArraySize(IEnumerable<IEnumerable<long>> values, int arrCount, int valueCount)
        => Calc7BitEncodedMultiArraySize(values.Cast<IEnumerable<ulong>>(), arrCount, valueCount);

    /// <inheritdoc cref="Calc7BitEncodedMultiArraySize(IEnumerable{IEnumerable{long}}, int, int)"/>
    public static int Calc7BitEncodedMultiArraySize(IEnumerable<IEnumerable<ulong>> values, int arrCount, int valueCount)
    {
        arrCount = arrCount == 0 ? 0 : arrCount - 1;
        return CalcLengthSize(valueCount + arrCount) + arrCount + values.Sum(arr => arr.Sum(i => Calc7BitEncodedSize(i)));
    }

    /// <summary>
    /// Write a 7-bit encoded long value to an array, and move the index to the next open spot
    /// </summary>
    /// <param name="bytes">The array to write to</param>
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
    public static long[] Read7BitEncodedArray(Il2CppStructArray<byte> bytes, ref int index)
    {
        int size = ReadLength(bytes, ref index);
        long[] result = new long[size];
        for (int i = 0; i < size; i++) result[i] = Read7BitEncodedLong(bytes, ref index);
        return result;
    }

    /// <inheritdoc cref="Read7BitEncodedArray(Il2CppStructArray{byte}, ref int)"/>
    public static ulong[] Read7BitEncodedUArray(Il2CppStructArray<byte> bytes, ref int index)
    {
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
        public T[] Values { get; init; }
        public int Start { get; init; }
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
        spans[spanIndex] = new(values, start, values.Length - start);

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

        return CalcLengthSize(arrSize)
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
