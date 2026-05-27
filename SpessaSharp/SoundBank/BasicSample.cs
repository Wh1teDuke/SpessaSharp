using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank;

public class BasicSample
{
    /// <summary>Should be reasonable for most cases</summary>
    private const int RESAMPLE_RATE = 48_000;

    public enum Type
    {
        /// <summary>EOS</summary>
        Null,
        Mono, Right, Left, Linked, RomMono, RomRight, RomLeft, RomLinked
    }

    private static readonly int[] TypeVal =
    [0/*EOS*/, 1, 2, 4, 8, 32_769, 32_770, 32_772, 32_776,];
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ValueOf(Type type) => TypeVal[(int)type];

    public static Type TypeOf(int val)
    {
        var idx = TypeVal.IndexOf(val);
        ArgumentOutOfRangeException
            .ThrowIfNegative(idx, "BasicSample.Type is invalid.");
        return (Type)idx;
    }

    /// <summary>The sample's name.</summary>
    public string Name;
    /// <summary>Sample rate in Hz.</summary>
    public int Rate;
    /// <summary>Original pitch of the sample as a MIDI note number.</summary>
    public int OriginalKey;
    /// <summary>Pitch correction, in cents. Can be negative.</summary>
    public int PitchCorrection;
    /// <summary>Linked sample, unused if mono.</summary>
    public BasicSample? LinkedSample;
    /// <summary>The type of the sample.</summary>
    public Type SType;
    /// <summary>
    /// The sample's loop start index, inclusive.
    /// In sample data points, relative to the start of the sample.
    /// <br/>
    /// Minimum allowed value is 0.
    /// </summary>
    public int LoopStart;
    /// <summary>
    /// The sample's loop end index, exclusive.
    /// In sample data points, relative to the start of the sample.
    /// <br/>
    /// Maximum allowed value is the sample data length.
    /// </summary>
    public int LoopEnd;
    /// <summary>Sample's linked instruments (the instruments that use it) note that duplicates are allowed since one instrument can use the same sample multiple times.</summary>
    public readonly List<BasicInstrument> LinkedTo = [];
    /// <summary>Indicates if the data was overridden, so it cannot be copied back unchanged.</summary>
    protected bool DataOverridden = true;
    /// <summary>The compressed sample data if the sample has been compressed.</summary>
    protected ArraySegment<byte>? CompressedData;
    /// <summary>The sample's audio data.</summary>
    protected ArraySegment<float>? AudioData;

    /// <summary>The basic representation of a sample</summary>
    /// <param name="name">The sample's name.</param>
    /// <param name="rate">The sample's rate in Hz.</param>
    /// <param name="originalKey">The sample's pitch as a MIDI note number.</param>
    /// <param name="pitchCorrection">The sample's pitch correction in cents.</param>
    /// <param name="type">The sample's type, an enum that can indicate SF3.</param>
    /// <param name="loopStart">The sample's loop start relative to the sample start in sample points.</param>
    /// <param name="loopEnd">The sample's loop end relative to the sample start in sample points.</param>
    public BasicSample(
        string name,
        int rate,
        int originalKey,
        int pitchCorrection,
        Type type,
        int loopStart,
        int loopEnd)
    {
        Name = name;
        Rate = rate;
        OriginalKey = originalKey;
        PitchCorrection = pitchCorrection;
        SType = type;
        LoopStart = loopStart;
        LoopEnd = loopEnd;
    }
    
    /// <summary>Indicates if the sample is compressed using vorbis SF3.</summary>
    public bool IsCompressed => CompressedData != null;
    
    /// <summary>If the sample is linked to another sample.</summary>
    public bool IsLinked => SType is Type.Left or Type.Right or Type.Linked;
    
    /// <summary>The sample's use count</summary>
    public int UseCount => LinkedTo.Count;
    
    /// <summary>Get raw data for writing the file, either a compressed bit stream or signed 16-bit little endian PCM data.</summary>
    /// <param name="allowVorbis">If vorbis file data is allowed.</param>
    /// <returns>Either <b>s16le</b> or <b>vorbis</b> data.</returns>
    public virtual ArraySegment<byte> GetRawData(bool allowVorbis)
    {
        if (IsCompressed && allowVorbis && !DataOverridden)
            return CompressedData!.Value;
        return EncodeS16LE();
    }
    
    /// <summary>Resamples the audio data to a given sample rate</summary>
    /// <param name="newSampleRate">The new sample rate</param>
    public void ResampleData(int newSampleRate)
    {
        var audioData = GetAudioData();
        var ratio = newSampleRate / (float)Rate;
        var resampled = new float[
            (int)float.Floor(audioData.Count * ratio)];

        for (var i = 0; i < resampled.Length; i++)
            resampled[i] = audioData[(int)float.Floor(i * (1 / ratio))];
        
        audioData = resampled;
        Rate = newSampleRate;

        // Adjust loop points
        LoopStart = (int)float.Floor(LoopStart * ratio);
        LoopEnd = (int)float.Floor(LoopEnd * ratio);
        AudioData = audioData;
    }
    
    /// <summary>Compresses the audio data</summary>
    /// <param name="encode">The compression function to use when compressing</param>
    public void CompressSample(SoundBank.Encode encode)
    {
        // No need to compress
        if (IsCompressed) return;

        // Compress, always mono!
        try
        {
            // If the sample rate is too low or too high, resample
            var audioData = GetAudioData();
            if (Rate is < 8_000 or > 96_000) 
            {
                ResampleData(RESAMPLE_RATE);
                audioData = GetAudioData();
            }
            var compressed = encode(audioData, Rate);
            SetCompressedData(compressed);
        } 
        catch (Exception error) 
        {
            Debug.WriteLine(
                $"Failed to compress {Name}, {error.Message
                }. Leaving as uncompressed");
            CompressedData = null;
        }
    }
    
    /// <summary>Sets the sample type and unlinks if needed.</summary>
    /// <param name="type">The type to set it to.</param>
    /// <exception cref="InvalidOperationException">Type unsupported</exception>
    public void Set(Type type) 
    {
        SType = type;

        if (!IsLinked) 
        {
            // Unlink the other sample
            if (LinkedSample != null)
            {
                Debug.Assert(LinkedSample.LinkedSample == this);
                LinkedSample.LinkedSample = null;
                LinkedSample.SType = type;
            }

            LinkedSample = null;
        }

        if ((TypeVal[(int)type] & 0x80_00) > 0)
            throw new InvalidOperationException(
                "ROM samples are not supported.");
    }

    /// <summary>Unlinks the sample from its stereo link if it has any.</summary>
    public void UnlinkSample() => Set(Type.Mono);
    
    /// <summary>Links a stereo sample.</summary>
    /// <param name="sample">The sample to link to.</param>
    /// <param name="type">Either <b>left</b>, <b>right</b> or <b>linked</b>.</param>
    /// <exception cref="ArgumentException">Invalid sample type</exception>
    public void SetLinkedSample(BasicSample sample, Type type) 
    {
        // Sanity check
        if (sample.LinkedSample != null)
            throw new ArgumentException($"{sample.Name} is linked to {
                sample.LinkedSample.Name}. Unlink it first.");

        LinkedSample = sample;
        sample.LinkedSample = this;

        switch (type) 
        {
            case Type.Left:
                Set(Type.Left);
                sample.Set(Type.Right);
                break;

            case Type.Right:
                Set(Type.Right);
                sample.Set(Type.Left);
                break;

            case Type.Linked:
                Set(Type.Linked);
                sample.Set(Type.Linked);
                break;

            case Type.Null:
            case Type.Mono:
            case Type.RomMono:
            case Type.RomRight:
            case Type.RomLeft:
            case Type.RomLinked:
            default: throw new ArgumentException(
                $"Invalid sample type: {type}");
        }
    }
    
    /// <summary>Links the sample to a given instrument</summary>
    /// <param name="instrument">The instrument to link to</param>
    public void LinkTo(BasicInstrument instrument) =>
        LinkedTo.Add(instrument);
    
    /// <summary>Unlinks the sample from a given instrument</summary>
    /// <param name="instrument">The instrument to unlink from</param>
    /// <exception cref="ArgumentException">Instrument not linked to sample</exception>
    public void UnlinkFrom(BasicInstrument instrument) 
    {
        var index = LinkedTo.IndexOf(instrument);
        if (index == -1) 
            throw new ArgumentException(
                $"Instrument {instrument.Name} not linked to {Name}");
        LinkedTo.RemoveAt(index);
    }
    
    /// <summary>
    /// Get the float32 audio data.
    /// Note that this either decodes the compressed data or passes the ready sampleData.
    /// If neither are set then it will throw an error!
    /// </summary>
    /// <returns>The audio data</returns>
    /// <exception cref="InvalidOperationException">Null audio data</exception>
    public virtual ArraySegment<float> GetAudioData()
    {
        if (AudioData != null)
            return AudioData.Value;

        if (!IsCompressed)
            throw new InvalidOperationException(
                "Sample data is null for a BasicSample instance.");

        // SF3
        // If compressed, decode
        AudioData = DecodeVorbis();
        return AudioData.Value;
    }
    
    /// <summary>Replaces the audio data *in-place*.</summary>
    /// <param name="audioData">The new audio data as Float32.</param>
    /// <param name="sampleRate">The new sample rate, in Hertz.</param>
    public void SetAudioData(ArraySegment<float> audioData, int sampleRate) 
    {
        AudioData       = audioData;
        Rate            = sampleRate;
        DataOverridden  = true;
        CompressedData  = null;
    }
    
    /// <summary>Replaces the audio with a compressed data sample and flags the sample as compressed</summary>
    /// <param name="data">The new compressed data</param>
    public void SetCompressedData(ArraySegment<byte> data) 
    {
        AudioData      = null;
        CompressedData = data;
        DataOverridden = false;
    }
    
    /// <summary>Encodes s16le sample</summary>
    /// <returns>The encoded data</returns>
    protected ArraySegment<byte> EncodeS16LE()
    {
        var data = GetAudioData();
        var data8 = new byte[data.Count * 2];
        var data16 = MemoryMarshal.Cast<byte, short>(data8.AsSpan());
        var len = data.Count;

        AudioUtil.ConvertFloat32ToPCM16(data, data16);
        for (var i = 0; i < len; i++)
            data16[i] = (short)Math.Clamp(
                data[i] * 32_768, short.MinValue, short.MaxValue);

        return data8;
    }
    
    /// <summary>Decode binary vorbis into a float32 pcm</summary>
    /// <returns>The decoded audio data</returns>
    /// <exception cref="InvalidOperationException">Null audio data</exception>
    /// <exception cref="NullReferenceException">SoundBank.Vorbis.Decoder is not set</exception>
    private ArraySegment<float> DecodeVorbis()
    {
        if (AudioData != null) return AudioData.Value;

        if (CompressedData == null)
            throw new InvalidOperationException("Compressed data is missing.");

        if (SoundBank.Vorbis.Decoder is not {} decoder)
            throw new NullReferenceException("Vorbis decoder must be supplied");
        
        var res = decoder(CompressedData.Value);
        
        // Clip
        // Because vorbis can go above 1 sometimes
        foreach (ref var sample in res.AsSpan())
            // Magic number is 32,767 / 32,768
            sample = Math.Clamp(sample, -1, .999_969_482_421_875f);
        
        return res;
    }

    public static BasicSample NewEmpty() => 
        new ("", 44_100, 60, 0, Type.Mono, 0, 0);
}