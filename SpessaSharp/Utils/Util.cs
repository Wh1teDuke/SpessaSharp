using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SpessaSharp.Utils;

internal static class Util
{
    public static readonly Encoding Utf8 =
        Encoding.GetEncoding("UTF-8");
    
    public static string ToIsoString(DateTime dt) =>
        dt.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture);
    
    // Needed because
    // Invalid date: "sábado 26 setembro 2020, 16:40:14". Replacing with the current date!
    private static readonly FrozenDictionary<string, string> TranslationPortuguese =
        new Dictionary<string, string>
        {
            // Weekdays map (Portuguese to English)
            {"domingo", "Sunday"},
            {"segunda-feira", "Monday"},
            {"terça-feira", "Tuesday"},
            {"quarta-feira", "Wednesday"},
            {"quinta-feira", "Thursday"},
            {"sexta-feira", "Friday"},
            {"sábado", "Saturday"},

            // Months map (Portuguese to English)
            {"janeiro", "January"},
            {"fevereiro", "February"},
            {"março", "March"},
            {"abril", "April"},
            {"maio", "May"},
            {"junho", "June"},
            {"julho", "July"},
            {"agosto", "August"},
            {"setembro", "September"},
            {"outubro", "October"},
            {"novembro", "November"},
            {"dezembro", "December"},
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string>[] Translations = [
        TranslationPortuguese];
    
    private static DateTime? TryTranslate(string dateString) 
    {
        // Translating
        foreach (var translation in Translations)
        {
            var translated = dateString;
            foreach (var (src, english) in translation) 
                translated = Regex.Replace(
                    translated, src, english, RegexOptions.IgnoreCase);

            if (DateTime.TryParse(
                    translated, 
                    CultureInfo.InvariantCulture, 
                    DateTimeStyles.AssumeLocal, 
                    out var dt))
                return dt;
        }

        return null;
    }
    
    private static DateTime? TryDotted(string dateString) 
    {
        var match = RegexExt.CleanDD_MM_YYYY().Match(dateString);
        if (!match.Success) return null; 

        var day = int.Parse(match.Groups[1].Value);
        var month = int.Parse(match.Groups[2].Value);
        var year = int.Parse(match.Groups[3].Value);

        try { return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Local); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
    
    private static DateTime? TryAWE(string dateString) 
    {
        // Regex to match a "DD  MM YY" (testcase: AWE32-MIDI-Conversions, sbk conversion so possibly SFEDT used that)
        // Also "DD MM YY" (without double space)
        var match = RegexExt.CleanDDMMYY().Match(dateString);
        if (!match.Success) return null; 

        var day = match.Groups[1].Value;
        var month = int.Parse(match.Groups[2].Value) + 1; // Seems 0 indexed for some reason
        var year = match.Groups[3].Value;

        // Format like string to let date decide if 2000 or 1900
        try
        {
            return DateTime.Parse(
                $"{month}/{day}/{year}", 
                CultureInfo.InvariantCulture, 
                DateTimeStyles.AssumeLocal);
        }
        catch (FormatException) { return null; }
    }
    
    private static DateTime? TryYear(string dateString) 
    {
        var match = RegexExt.Date4Numbers().Match(dateString);

        try
        {
            var yearStr = match.Groups[0].Value;
            return new DateTime(int.Parse(yearStr), 1, 1);
        }
        catch (FormatException) { return null; }
    }
    
    public static DateTime ParseDateString(string dateString) 
    {
        // Trim the date. Testcase: " 4  0  97"
        dateString = dateString.Trim();
        if (dateString.Length == 0) return DateTime.Now;

        // Remove "st" , "nd" , "rd",  "th", etc.
        var filtered = RegexExt.CleanDate2().Replace(dateString, "$1");
        filtered = RegexExt.CleanDate1().Replace(filtered, " ");

        if (DateTime.TryParse(
                filtered, 
                CultureInfo.InvariantCulture, 
                DateTimeStyles.AssumeUniversal, 
                out var dt))
            return dt;
        
        return TryTranslate(dateString) 
               ?? TryDotted(dateString) 
               ?? TryAWE(dateString) 
               ?? TryYear(dateString) 
               ?? DateTime.Now;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ArraySegment<byte> SliceThenInc(
        ref ArraySegment<byte> seg, int len)
    {
        var ret = seg[..len];
        seg = seg[len..];
        return ret;
    }

    /// <summary>Reads bytes as an ASCII string. This version works with any numeric array.</summary>
    /// <param name="data">The array to read from.</param>
    /// <param name="bytes">The amount of bytes to read.</param>
    /// <returns>The string.</returns>
    public static ArraySegment<byte> ReadBinaryString(
        ref ArraySegment<byte> data, int bytes)
    {
        var res = ReadBinaryString(data[..bytes]);
        data = data[bytes..];
        return res;
    }

    public static string ToString(ReadOnlySpan<byte> data) =>
        SpessaUtil.MidiEncoding.GetString(data);
    
    public static ArraySegment<byte> ReadBinaryString(ArraySegment<byte> data)
    {
        var idx = data.AsSpan().IndexOf((byte)0);
        return data[.. (idx == -1 ? data.Count : idx)];
    }
    
    public static ReadOnlySpan<byte> ReadBinaryString(ReadOnlySpan<byte> data)
    {
        var idx = data.IndexOf((byte)0);
        return data[.. (idx == -1 ? data.Length : idx)];
    }
    
    /// <summary>Gets ASCII bytes from string.</summary>
    /// <param name="str">The string.</param>
    /// <param name="addZero">Adds a zero terminator at the end.</param>
    /// <param name="ensureEven">Ensures even byte count.</param>
    /// <returns>The binary data.</returns>
    public static byte[] GetStringBytes(
        string str, bool addZero = false, bool ensureEven = false)
    {
        var len = str.Length;
        if (addZero) len++;
        if (ensureEven && len % 2 != 0) len++;
        var arr = new byte[len];
        WriteBinaryString(arr, str);
        return arr;
    }
    
    /// <summary>Writes ASCII bytes into a specified array.</summary>
    /// <param name="outArray">The target array. Modified <b>in-place</b></param>
    /// <param name="str">The string.</param>
    /// <param name="padLength">pad with zeros if the string is shorter</param>
    public static void WriteBinaryString(
        ref ArraySegment<byte> outArray, 
        ReadOnlySpan<char> str, 
        int padLength = 0) =>
            outArray = WriteBinaryString(outArray, str, padLength);
    
    public static ArraySegment<byte> WriteBinaryString(
        ArraySegment<byte> outArray, ReadOnlySpan<char> str, int padLength = 0)
    {
        if (padLength > 0 && str.Length > padLength)
            str = str[..padLength];

        var i = 0;
        for (; i < str.Length; i++)
            outArray[i] = (byte)str[i];

        // Pad with zeros if needed
        if (padLength > str.Length)
            for (var _ = 0; _ < padLength - str.Length; _++)
                outArray[i++] = 0;

        return outArray[i..];
    }
    
    public static bool EqualsIgnoreCase(
        ReadOnlySpan<byte> str, params ReadOnlySpan<string> list)
    {
        foreach (ref readonly var str2 in list)
            if (Ascii.EqualsIgnoreCase(str, str2)) return true;
        return false;
    }
    
    public static bool Equals(
        ReadOnlySpan<byte> str, params ReadOnlySpan<string> list)
    {
        foreach (ref readonly var str2 in list)
            if (Ascii.Equals(str, str2)) return true;
        return false;
    }
    
    public static int ReadBigEndian(ReadOnlySpan<byte> data)
    {
        if (data.Length == 4)
            return BinaryPrimitives.ReadInt32BigEndian(data);
        if (data.Length == 2)
            return BinaryPrimitives.ReadUInt16BigEndian(data);
        
        var r = 0;
        foreach (var t in data) r = (r << 8) | t;
        return r;
    }
    
    public static int ReadBigEndian(
        ref ArraySegment<byte> data, int bytes) =>
            ReadBigEndian(SliceThenInc(ref data, bytes));
    
    public static int ReadLittleEndian(ReadOnlySpan<byte> data)
    {
        if (data.Length == 4)
            return BinaryPrimitives.ReadInt32LittleEndian(data);
        if (data.Length == 2)
            return BinaryPrimitives.ReadUInt16LittleEndian(data);
        
        var r = 0;
        for (var i = 0; i < data.Length; i++)
            r |= data[i] << (i * 8);
        return r;
    }

    /// <summary>Reads the number as little endian from a span.</summary>
    /// <param name="data">The span to read from.</param>
    /// <returns>The number</returns>
    public static long ReadLittleEndian64(ReadOnlySpan<byte> data) => 
        data.Length == 8 
            ? BinaryPrimitives.ReadInt64LittleEndian(data) 
            : ReadLittleEndian(data);

    public static int ReadLittleEndian(
        ref ArraySegment<byte> data, int bytes) =>
            ReadLittleEndian(SliceThenInc(ref data, bytes));
    
    /// <summary>Reads the number as little endian from a span.</summary>
    /// <param name="data">The span to read from.</param>
    /// <param name="bytes">The number of bytes to read.</param>
    /// <returns>The number</returns>
    public static long ReadLittleEndian64(
        ref ArraySegment<byte> data, int bytes) =>
        ReadLittleEndian64(SliceThenInc(ref data, bytes));
    
    /// <summary>Reads two bytes as a signed short.</summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short SignedInt16(byte a, byte b) => 
        unchecked((short)((b << 8) | a));

    /// <summary>Reads two bytes as a signed char.</summary>
    /// <param name="a"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SignedInt8(byte a) => unchecked((sbyte)a);

    /// <summary>Writes a number as Big endian.</summary>
    /// <param name="data">The number to write.</param>
    /// <param name="n">The amount of bytes to use. Excess bytes will be set to zero.</param>
    /// <returns>The Big endian representation of the number.</returns>
    public static Span<byte> WriteBigEndian(Span<byte> data, int n)
    {
        for (var i = data.Length - 1; i >= 0; i--)
        {
            data[i] = (byte)(n & 0xff);
            n >>= 8;
        }
        return data;
    }
    
    /// <summary>Writes a number as little endian seems to also work for negative numbers so yay?</summary>
    /// <param name="dataArray">The Span to write to.</param>
    /// <param name="number">The number to write.</param>
    /// <param name="byteTarget">The amount of bytes to use. Excess bytes will be set to zero.</param>
    public static void WriteLittleEndian(
        ref Span<byte> dataArray, int number, int byteTarget) 
    {
        WriteLittleEndian(dataArray[..byteTarget], number);
        dataArray = dataArray[byteTarget..];
    }
    
    public static void WriteLittleEndian(
        ref ArraySegment<byte> dataArray, int number, int byteTarget) 
    {
        WriteLittleEndian(dataArray[..byteTarget], number);
        dataArray = dataArray[byteTarget..];
    }

    private static void WriteLittleEndian(Span<byte> data, int n)
    {
        if (data.Length == 4)
            BinaryPrimitives.WriteInt32LittleEndian(data, n);
        else if (data.Length == 2)
            BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)n);
        else
            for (var i = 0; i < data.Length; i++)
                data[i] = (byte)((n >> (i * 8)) & 0xff);
    }
    
    /// <summary>Writes a WORD (SHORT). 16 bits</summary>
    /// <param name="dataArray"></param>
    /// <param name="word"></param>
    public static void WriteWord(ref ArraySegment<byte> dataArray, short word)
    {
        BinaryPrimitives.WriteInt16LittleEndian(dataArray, word);
        dataArray = dataArray[2..];
    }
    
    /// <summary>Writes a WORD (SHORT). 16 bits</summary>
    /// <param name="dataArray"></param>
    /// <param name="word"></param>
    public static void WriteWord(ref Span<byte> dataArray, short word)
    {
        BinaryPrimitives.WriteInt16LittleEndian(dataArray, word);
        dataArray = dataArray[2..];
    }
    
    /// <summary>Writes a DWORD (INT). 32 bits</summary>
    /// <param name="dataArray"></param>
    /// <param name="dword"></param>
    public static void WriteDword(ref ArraySegment<byte> dataArray, int dword)
    {
        BinaryPrimitives.WriteInt32LittleEndian(dataArray, dword);
        dataArray = dataArray[4..];
    }
    
    /// <summary>Writes a DWORD (INT). 32 bits</summary>
    /// <param name="dataArray"></param>
    /// <param name="dword"></param>
    public static void WriteDword(ref Span<byte> dataArray, int dword)
    {
        BinaryPrimitives.WriteInt32LittleEndian(dataArray, dword);
        dataArray = dataArray[4..];
    }
    
    /// <summary>Writes a QWORD (LONG)* 64 bits.</summary>
    /// <param name="dataArray"></param>
    /// <param name="qword"></param>
    public static void WriteQword(ref ArraySegment<byte> dataArray, long qword)
    {
        BinaryPrimitives.WriteInt64LittleEndian(dataArray, qword);
        dataArray = dataArray[8..];
    }

    /// <summary>Writes a QWORD (LONG)* 64 bits.</summary>
    /// <param name="dataArray"></param>
    /// <param name="qword"></param>
    public static void WriteQword(ref Span<byte> dataArray, long qword)
    {
        BinaryPrimitives.WriteInt64LittleEndian(dataArray, qword);
        dataArray = dataArray[8..];
    }

    /// <summary>Reads VLQ from a MIDI byte array.</summary>
    /// <param name="data">The segment to read from.</param>
    /// <returns>The number.</returns>
    public static int ReadVLengthQuantity(ref ArraySegment<byte> data)
    {
        var d = data;
        var result = 0;
        while (d.Count > 0)
        {
            var b = d[0];
            d = d[1..];
            // Extract the first 7 bytes
            result = (result << 7) | (b & 127);
            // If the last byte isn't 1, stop reading
            if (b >> 7 != 1) break;
        }

        data = d;
        return result;
    }
    
    /// <summary>Writes a VLQ from a number to a byte array.</summary>
    /// <param name="number">The number to write.</param>
    /// <param name="bytes">The VLQ representation of the number.</param>
    public static void WriteVLengthQuantity(
        int number, ref ArraySegment<byte> bytes)
    {
        Debug.Assert(bytes.Count == 4);
        
        // Add the first byte
        var i = 4;
        bytes[--i] = (byte)(number & 127);
        number >>= 7;

        // Continue processing the remaining bytes
        while (number > 0)
        {
            bytes[--i] = (byte)((number & 127) | 128);
            number >>= 7;
        }

        bytes = bytes[i ..];
    }

    public static string ToHexString(ReadOnlySpan<byte> arr)
    {
        var hex = new StringBuilder(arr.Length * 2);
        foreach (var b in arr) hex.Append($"{b:x2}");
        return hex.ToString();
    }
    
    public static bool Any<T>(ReadOnlySpan<T> span, Func<T, bool> match)
    {
        foreach (ref readonly var s in span)
            if (match(s)) return true;
        return false;
    }
    
    public static bool Any<T, A1>(
        ReadOnlySpan<T> span, A1 arg1, Func<T, A1, bool> match)
    {
        foreach (ref readonly var s in span)
            if (match(s, arg1)) return true;
        return false;
    }
    
    public static int? IndexOf<T, A1>(
        ReadOnlySpan<T> span, A1 arg1, Func<T, A1, bool> match)
    {
        for (var i = 0; i < span.Length; i++)
            if (match(span[i], arg1)) return i;
        return null;
    }
    
    public static bool TryFind<T>(
        ReadOnlySpan<T> span, 
        Func<T, bool> match, 
        [NotNullWhen(true)] out T? result) where T : notnull
    {
        foreach (ref readonly var s in span)
        {
            if (!match(s)) continue;
            result = s;
            return true;
        }
        result = default;
        return false;
    }
    
    public static bool TryFind<T, A1>(
        ReadOnlySpan<T> span, 
        A1 arg1,
        Func<T, A1, bool> match,
        [NotNullWhen(true)] out T? result) where T : notnull
    {
        foreach (ref readonly var s in span)
        {
            if (!match(s, arg1)) continue;
            result = s;
            return true;
        }
        result = default;
        return false;
    }

    public static int Sum<T>(ReadOnlySpan<T> span, Func<T, int> sum)
    {
        var res = 0;
        foreach (ref readonly var s in span)
            res += sum(s);
        return res;
    }

    public static T[] Filter<T>(T[] vals, ISet<T> filter)
    {
        var l = vals.ToList();
        l.RemoveAll(filter.Contains);
        return l.ToArray();
    }
    
    public static ReadOnlySpan<char> SafeSlice(
        string str, int? start = null, int? end = null)
    {
        var span = str.AsSpan();
        if (span.IsEmpty || start >= span.Length) return "";
        
        var s = Math.Clamp(start ?? 0,           0,     span.Length - 1);
        var e = Math.Clamp(end   ?? span.Length, s + 1, span.Length);
        
        return span[s..e];
    }
    
    public static int Round(double d) =>
        // js round
        (int)Math.Round(d, MidpointRounding.AwayFromZero);
    
    public static ArraySegment<T> Rent<T>(int s) =>
        new(ArrayPool<T>.Shared.Rent(s), 0, s);

    public static void Grow<T>(ref ArraySegment<T> seg, int s)
    {
        Debug.Assert(seg.Count < s);
        
        if (seg.Array is {} array && array.Length - seg.Offset >= s)
        {
            seg = new ArraySegment<T>(array, seg.Offset, s);
            return;
        }
        
        var newSeg = Rent<T>(s);
        seg.AsSpan().CopyTo(newSeg);
        Return(seg);
        seg = newSeg;
    }

    public static void Return<T>(ArraySegment<T> seg)
    {
        if (seg.Array is not { } array) return;
        var clear = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        ArrayPool<T>.Shared.Return(array, clear);
    }

    public static T[] Sorted<T>(List<T> list, Comparer<T> comparer) =>
        // Stable sort
        list.OrderBy(p => p, comparer).ToArray();
    
    public static void Sort<T>(List<T> list, Comparer<T> comparer)
    {
        var sorted = Sorted(list, comparer);
        list.Clear();
        list.AddRange(sorted);
    }
    
    public static void Sort<T>(List<T> list, Func<T, T, int> comparer) =>
        Sort(list, Comparer<T>.Create((a, b) => comparer(a, b)));
    
    public static T[] Sorted<T>(List<T> list, Func<T, T, int> comparer) =>
        Sorted(list, Comparer<T>.Create((a, b) => comparer(a, b)));
}

internal static partial class RegexExt
{
    // Regex to match DD.MM.YYYY format
    [GeneratedRegex(@"^(\d{2})\.(\d{2})\.(\d{4})$")]
    public static partial Regex CleanDD_MM_YYYY();
    
    // Match exactly 4 numbers
    [GeneratedRegex(@"\b\d{4}\b")]
    public static partial Regex Date4Numbers();
    
    // Regex to match a "DD  MM YY" (testcase: AWE32-MIDI-Conversions, sbk conversion so possibly SFEDT used that)
    // Also "DD MM YY" (without double space)
    [GeneratedRegex(@"^(\d{1,2})\s{1,2}(\d{1,2})\s{1,2}(\d{2})$")]
    public static partial Regex CleanDDMMYY();
    
    // Remove "st" , "nd" , "rd",  "th", etc.
    [GeneratedRegex(@"\s+at\s+", RegexOptions.IgnoreCase, "en-US")]
    public static partial Regex CleanDate1();
    [GeneratedRegex(@"\b(\d+)(?:st|nd|rd|th)\b", RegexOptions.IgnoreCase, "en-US")]
    public static partial Regex CleanDate2();
    
    // RMIDI Meta
    [GeneratedRegex("/@T|@A/g")]
    public static partial Regex RmidiMeta();
}