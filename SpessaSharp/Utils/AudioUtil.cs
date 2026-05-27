using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SpessaSharp.Utils;

public static class AudioUtil
{
    /// <summary> </summary>
    /// <param name="Title">The song's title.</param>
    /// <param name="Artist">The song's artist.</param>
    /// <param name="Album">The song's album.</param>
    /// <param name="Genre">The song's genre.</param>
    public readonly record struct WaveMetadata(
        string? Title, string? Artist, string? Album, string? Genre);

    /// <summary>
    /// This will find the max sample point and set it to 1, and scale others with it. Recommended
    /// </summary>
    /// <param name="NormalizeAudio">The loop start and end points in seconds. Undefined if no loop should be written.</param>
    /// <param name="Loop"></param>
    /// <param name="Metadata">The metadata to write into the file.</param>
    public readonly record struct WaveWriteOptions(
        bool NormalizeAudio,
        (
            // The start point in seconds.
            float Start,
            // The end point in seconds.
            float End)? Loop,
        WaveMetadata? Metadata)
    {
        public static readonly WaveWriteOptions Default = new(
            NormalizeAudio: true,
            Loop: null,
            Metadata: null);
    }
    
    /// <summary> Writes an audio into a valid WAV file. </summary>
    /// <param name="audioData">The audio data channels.</param>
    /// <param name="sampleRate">The sample rate, in Hertz.</param>
    /// <param name="options">Additional options for writing the file.</param>
    /// <returns>The binary file.</returns>
    public static byte[] ToWav(
        ReadOnlySpan<ArraySegment<float>> audioData,
        int sampleRate,
        WaveWriteOptions? options = null)
    {
        var fullOptions = options ?? WaveWriteOptions.Default;
        
        var length = audioData[0].Count;
        var numChannels = audioData.Length;
        const int bytesPerSample = 2; // 16-bit PCM
        
        // Prepare INFO chunk
        var infoChunk = ArraySegment<byte>.Empty;
        if (fullOptions.Metadata is { } infoOn)
        {
            var infoChunks = new List<ArraySegment<byte>>();
            infoChunks.Add(RIFFChunk.Write(
                new RIFFChunk.FourCC("ICMT"), 
                "Created with SpessaSharp"u8,
                true));
            
            if (infoOn.Artist is {} artist)
                infoChunks.Add(RIFFChunk.Write(
                    new RIFFChunk.FourCC("IART"), 
                    Util.Utf8.GetBytes(artist),
                    true));
            
            if (infoOn.Album is {} album)
                infoChunks.Add(RIFFChunk.Write(
                    new RIFFChunk.FourCC("IPRD"), 
                    Util.Utf8.GetBytes(album),
                    true));
            
            if (infoOn.Genre is {} genre)
                infoChunks.Add(RIFFChunk.Write(
                    new RIFFChunk.FourCC("IGNR"), 
                    Util.Utf8.GetBytes(genre),
                    true));
            
            if (infoOn.Title is {} title)
                infoChunks.Add(RIFFChunk.Write(
                    new RIFFChunk.FourCC("INAM"), 
                    Util.Utf8.GetBytes(title),
                    true));

            infoChunk = RIFFChunk.WriteParts(
                new RIFFChunk.FourCC("INFO"), 
                CollectionsMarshal.AsSpan(infoChunks), 
                true);
        }
        
        // Prepare CUE chunk
        var cueChunk = ArraySegment<byte>.Empty;
        if (fullOptions.Loop is {} loop) 
        {
            var loopStartSamples = (int)float.Floor(loop.Start * sampleRate);
            var loopEndSamples = (int)float.Floor(loop.End * sampleRate);

            var cueStart    = new byte[24];
            var cueSpanS    = (ArraySegment<byte>)cueStart;
            
            Util.WriteLittleEndian(ref cueSpanS, 0, 4);  // DwIdentifier
            Util.WriteLittleEndian(ref cueSpanS, 0, 4);  // DwPosition
            Util.WriteBinaryString(ref cueSpanS, "data"); // Cue point ID
            Util.WriteLittleEndian(ref cueSpanS, 0, 4);  // ChunkStart, always 0
            Util.WriteLittleEndian(ref cueSpanS, 0, 4);  // BlockStart, always 0
            Util.WriteLittleEndian(ref cueSpanS, loopStartSamples, 4); // SampleOffset

            var cueEnd = new byte[24];
            var cueSpanE = (ArraySegment<byte>)cueEnd;
            Util.WriteLittleEndian(ref cueSpanE, 1, 4); // DwIdentifier
            Util.WriteLittleEndian(ref cueSpanE, 0, 4); // DwPosition
            Util.WriteBinaryString(ref cueSpanE, "data"); // Cue point ID
            Util.WriteLittleEndian(ref cueSpanE, 0, 4); // ChunkStart, always 0
            Util.WriteLittleEndian(ref cueSpanE, 0, 4); // BlockStart, always 0
            Util.WriteLittleEndian(ref cueSpanE, loopEndSamples, 4); // SampleOffset

            cueChunk = RIFFChunk.WriteParts(
                new RIFFChunk.FourCC("cue "), [
                new byte [] {2, 0, 0, 0}, // Cue points count
                cueStart,
                cueEnd]);
        }
        
        // Prepare the header
        const int headerSize = 44;
        var dataSize = length * numChannels * bytesPerSample; // 16-bit per channel
        var fileSize =
            headerSize + dataSize + 
            infoChunk.Count + cueChunk.Count - 8; // Total file size minus the first 8 bytes

        var header = new byte[headerSize];
        var h = header.AsSpan();
        
        // 'RIFF'
        "RIFF"u8.CopyTo(h);

        // File length
        ((ReadOnlySpan<byte>)[
            (byte)(fileSize & 0xff),
            (byte)((fileSize >> 8) & 0xff),
            (byte)((fileSize >> 16) & 0xff),
            (byte)((fileSize >> 24) & 0xff)
        ]).CopyTo(h[4..]);
        
        // 'WAVE'
        "WAVE"u8.CopyTo(h[8..]);
        // 'fmt '
        "fmt "u8.CopyTo(h[12..]);
        // Fmt chunk length
        h[16] = 16; // 16 for PCM
        // Audio format (PCM)
        h[20] = 1;
        // Number of channels (2)
        ((ReadOnlySpan<byte>)[
            (byte)(numChannels & 255),
            (byte)(numChannels >> 8)]).CopyTo(
            h[22..]);
        
        // Sample rate
        ((ReadOnlySpan<byte>)[
            (byte)(sampleRate & 0xff),
            (byte)((sampleRate >> 8) & 0xff),
            (byte)((sampleRate >> 16) & 0xff),
            (byte)((sampleRate >> 24) & 0xff)]).CopyTo(
            h[24..]);
        
        // Byte rate (sample rate * block align)
        var byteRate = sampleRate * numChannels * bytesPerSample; // 16-bit per channel
        ((ReadOnlySpan<byte>)[
            (byte)(byteRate & 0xff),
            (byte)((byteRate >> 8) & 0xff),
            (byte)((byteRate >> 16) & 0xff),
            (byte)((byteRate >> 24) & 0xff)
        ]).CopyTo(h[28..]);

        // Block align (channels * bytes per sample)
        h[32] = (byte)(numChannels * bytesPerSample); // N channels * 16-bit per channel / 8
 
        // Bits per sample
        h[34] = 16; // 16-bit
        
        // Data chunk identifier 'data'
        "data"u8.CopyTo(h[36..]);

        // Data chunk length
        ((ReadOnlySpan<byte>) [
            (byte)(dataSize & 0xff),
            (byte)((dataSize >> 8) & 0xff),
            (byte)((dataSize >> 16) & 0xff),
            (byte)((dataSize >> 24) & 0xff)
        ]).CopyTo(h[40..]);

        var wavData = new byte[fileSize + 8];
        var offset = headerSize;
        header.AsSpan().CopyTo(wavData);
        
        // Interleave audio data (combine channels)
        var multiplier = (float)short.MaxValue;
        if (fullOptions.NormalizeAudio) 
        {
            // Find min and max values to prevent clipping when converting to 16 bits
            var numSamples = audioData[0].Count;
            var maxAbsValue = 0f;

            for (var ch = 0; ch < numChannels; ch++) 
            {
                var data = audioData[ch];
                for (var i = 0; i < numSamples; i++) 
                {
                    var sample = Math.Abs(data[i]);
                    if (sample > maxAbsValue) maxAbsValue = sample;
                }
            }

            multiplier = maxAbsValue > 0 
                ? short.MaxValue / maxAbsValue : 1;
        }
        
        for (var i = 0; i < length; i++) 
        {
            // Interleave both channels
            foreach (var d in audioData) 
            {
                var sample = (short)Math.Min(
                    short.MaxValue,
                    Math.Max(short.MinValue, d[i] * multiplier));

                // Convert to 16-bit
                wavData[offset++] = (byte)(sample & 0xff);
                wavData[offset++] = (byte)((sample >> 8) & 0xff);
            }
        }

        if (!infoChunk.AsSpan().IsEmpty) 
        {
            infoChunk.CopyTo(wavData, offset);
            offset += infoChunk.Count;
        }

        if (!cueChunk.AsSpan().IsEmpty)
            cueChunk.CopyTo(wavData, offset);

        return wavData;
    }
    
    // https://github.com/ModernMube/OwnAudioSharp/blob/master/OwnAudioEngine/Ownaudio.Core/Common/SimdAudioConverter.cs
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ConvertPCM16ToFloat32(
        ReadOnlySpan<byte> source, Span<float> dest, int sampleCount)
    {
        const float scale16Bit = 1.0f / 32_768.0f;
        
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            sampleCount, dest.Length, "Destination buffer too small");
        ArgumentOutOfRangeException.ThrowIfLessThan(
            source.Length, sampleCount * sizeof(short), 
            "Source buffer too small");

        var samples = MemoryMarshal.Cast<byte, short>(source);
        var i = 0;

        if (Sse41.IsSupported && sampleCount >= 4)
        {
            var scale = Vector128.Create(scale16Bit);
            var simdEnd = sampleCount - 3;

            for (; i < simdEnd; i += 4)
            {
                var shortVec = Vector128.Create(
                    samples[i], samples[i + 1], samples[i + 2], samples[i + 3],
                    0, 0, 0, 0);
                
                var intVec = Sse41.ConvertToVector128Int32(shortVec);
                var floatVec = Sse2.ConvertToVector128Single(intVec);
                var result = Sse.Multiply(floatVec, scale);
                result.CopyTo(dest.Slice(i, 4));
            }
        }
        else if (Sse2.IsSupported && sampleCount >= 4)
        {
            var scale = Vector128.Create(scale16Bit);
            var simdEnd = sampleCount - 3;

            for (; i < simdEnd; i += 4)
            {
                var intVec = Vector128.Create(
                    samples[i], samples[i + 1], samples[i + 2], samples[i + 3]);

                var floatVec = Sse2.ConvertToVector128Single(intVec);
                var result = Sse.Multiply(floatVec, scale);
                result.CopyTo(dest.Slice(i, 4));
            }
        }

        unsafe
        {
            fixed (short* src = samples[i..])
            fixed (float* tgt = dest[i..])
            {
                var s = src;
                var t = tgt;

                for (; i < sampleCount; i++) 
                    *t++ = *s++ * scale16Bit;
            }
        }
    }
    
    
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ConvertFloat32ToPCM16(
        ReadOnlySpan<float> source, Span<short> target)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(
            source.Length,
            target.Length,
            "Source and target must have the same length");

        unsafe
        {
            fixed (float* src = source)
            fixed (short* tgt = target)
            {
                const float max = 32_768f;
                
                var s = src;
                var t = tgt;
                
                var i = 0;
                var len = source.Length;

                while (i + 4 < len)
                {
                    i += 4;
                    *t++ = (short)Math.Clamp(*s++ * max, short.MinValue, short.MaxValue);
                    *t++ = (short)Math.Clamp(*s++ * max, short.MinValue, short.MaxValue); 
                    *t++ = (short)Math.Clamp(*s++ * max, short.MinValue, short.MaxValue); 
                    *t++ = (short)Math.Clamp(*s++ * max, short.MinValue, short.MaxValue); 
                }

                while (i++ < len)
                    *t++ = (short)Math.Clamp(*s++ * max, short.MinValue, short.MaxValue);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Interleave(
        ReadOnlySpan<float> left,
        ReadOnlySpan<float> right,
        Span<short> output)
    {
        Debug.Assert(left.Length == right.Length);
        Debug.Assert(left.Length == output.Length / 2);
        
        var len = left.Length;
        unsafe
        {
            fixed (float* pLeft = left)
            fixed (float* pRight = right)
            fixed (short* pOut = output)
            {
                const float max = 32_768f;
                
                var pL = pLeft;
                var pR = pRight;
                var pO = pOut;
                var i = 0;
                
                while (i + 4 < len)
                {
                    i += 4;

                    *pO++ = (short)Math.Clamp((int)(max * *pL++), short.MinValue, short.MaxValue);
                    *pO++ = (short)Math.Clamp((int)(max * *pR++), short.MinValue, short.MaxValue);
                    //
                    *pO++ = (short)Math.Clamp((int)(max * *pL++), short.MinValue, short.MaxValue);
                    *pO++ = (short)Math.Clamp((int)(max * *pR++), short.MinValue, short.MaxValue);
                    //
                    *pO++ = (short)Math.Clamp((int)(max * *pL++), short.MinValue, short.MaxValue);
                    *pO++ = (short)Math.Clamp((int)(max * *pR++), short.MinValue, short.MaxValue);
                    //
                    *pO++ = (short)Math.Clamp((int)(max * *pL++), short.MinValue, short.MaxValue);
                    *pO++ = (short)Math.Clamp((int)(max * *pR++), short.MinValue, short.MaxValue);
                }
                
                while (i++ < len)
                {
                    *pO++ = (short)Math.Clamp((int)(max * *pL++), short.MinValue, short.MaxValue);
                    *pO++ = (short)Math.Clamp((int)(max * *pR++), short.MinValue, short.MaxValue);
                }
            }
        }
    }
}