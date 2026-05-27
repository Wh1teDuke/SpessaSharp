using System.Diagnostics;
using SpessaSharp.SoundBank.Utils;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.SoundFont;

internal sealed class SFSample: BasicSample
{
    public const int SF3_BIT_FLIT = 0x10;
    
    /// <summary>Linked sample index for retrieving linked samples in sf2</summary>
    public int LinkedSampleIndex { get; internal set; }

    /// <summary>The sliced sample from the smpl chunk.</summary>
    private ArraySegment<byte>? _s16leData;
    private readonly int _startByteOffset;
    private readonly int _endByteOffset;
    private int _sampleID;
    
    /// <summary> Creates a sample</summary>
    /// <param name="name"></param>
    /// <param name="startIndex"></param>
    /// <param name="endIndex"></param>
    /// <param name="loopStartIndex"></param>
    /// <param name="loopEndIndex"></param>
    /// <param name="rate"></param>
    /// <param name="pitch"></param>
    /// <param name="pitchCorrection"></param>
    /// <param name="linkedSampleIndex"></param>
    /// <param name="type"></param>
    /// <param name="sampleData"></param>
    /// <param name="index">Initial sample index when loading the sfont. Used for SF2Pack support</param>
    private SFSample(
        string name,
        int startIndex,
        int endIndex,
        int loopStartIndex,
        int loopEndIndex,
        
        int rate,
        int pitch,
        int pitchCorrection, 
        int linkedSampleIndex,
        int type,
        
        SampleChunk sampleData,
        int index) : base(
        name, rate, pitch, pitchCorrection, Type.Null, -1, -1)
    {
        // Read sf3
        // https://github.com/FluidSynth/fluidsynth/wiki/SoundFont3Format
        var compressed = (type & SF3_BIT_FLIT) > 0;
        // Remove the compression flag
        var sampleType = type & ~SF3_BIT_FLIT;
        SType = TypeOf(sampleType);
        
        var smplStart = sampleData.Start;
        
        var rStartIndex     = unchecked((uint)startIndex);
        var rEndIndex       = unchecked((uint)endIndex);
        var rLoopStartIndex = unchecked((uint)loopStartIndex);
        var rLoopEndIndex   = unchecked((uint)loopEndIndex);
        
        var rLoopStart  = rLoopStartIndex - rStartIndex / 2;
        var rLoopEnd    = rLoopEndIndex - rStartIndex / 2;
        
        sampleData.EnsureChunkSize(smplStart + rEndIndex);
        
        // in bytes
        // SpessaSharp: Fix Loop Position relative to array chunk.
        _startByteOffset = sampleData.FixOffset(rStartIndex);
        _endByteOffset = sampleData.FixOffset(rEndIndex);

        LoopStart   = (int)rLoopStart;
        LoopEnd     = (int)rLoopEnd;

        DataOverridden = false;
        _sampleID = index;

        if (name == "EOS") return;
        
        // Three data types in:
        // SF2 (s16le)
        // SF3 (vorbis)
        // SF2Pack (entire smpl vorbis)
        if (!sampleData.IsFloat)
        {
            if (compressed)
            {
                // Correct loop points
                LoopStart   += _startByteOffset / 2;
                LoopEnd     += _startByteOffset / 2;
                
                // Copy the compressed data, it can be preserved during writing
                SetCompressedData(sampleData.SliceByte(
                    start: rStartIndex / 2 + smplStart,
                    end: rEndIndex     / 2 + smplStart));
            }
            else
            {
                // Regular sf2 s16le
                _s16leData = sampleData.SliceByte(
                    start:  smplStart + rStartIndex,
                    end:    smplStart + rEndIndex);
            }
        }
        else
        {
            // Float32 array from SF2pack, copy directly
            var seg = sampleData.SliceFloat(
                start: rStartIndex / 2,
                end: rEndIndex     / 2);
            SetAudioData(seg, rate);
        }

        LinkedSampleIndex = linkedSampleIndex;
    }

    public void GetLinkedSample(List<SFSample> samples)
    {
        if (LinkedSample != null || !IsLinked)
            return;

        if (LinkedSampleIndex < samples.Count && LinkedSampleIndex >= 0)
        {
            var linked = samples[LinkedSampleIndex];
            // Check for corrupted files (like FluidR3_GM.sf2 that link EVERYTHING to a single sample)
            if (linked.LinkedSample != null)
            {
                Debug.WriteLine(
                    $"Invalid linked sample for {Name}: {
                        linked.Name} is already linked to {
                            linked.LinkedSample.Name}");
                UnlinkSample();
            }
            else
                SetLinkedSample(linked, SType);
        }
        else
        {
            // Log as info because it's common and not really dangerous
            Debug.WriteLine(
                $"Invalid linked sample for {Name}. Setting to mono.");

            UnlinkSample();
        }
    }

    /// <summary>Loads the audio data and stores it for reuse</summary>
    /// <returns>The audio data</returns>
    /// <exception cref="NullReferenceException">AudioData is null</exception>
    public override ArraySegment<float> GetAudioData()
    {
        if (AudioData != null)
            return AudioData.Value;
        
        // SF2Pack is decoded during load time
        // SF3 is decoded in BasicSample
        if (IsCompressed) return base.GetAudioData();

        if (_s16leData == null)
            throw new NullReferenceException("Unexpected lack of audio data.");
        
        // Start loading data if it is not loaded
        var byteLen = _endByteOffset - _startByteOffset;
        if (byteLen < 1)
        {
            Debug.WriteLine(
                $"Invalid sample '{Name}'! Invalid length: {byteLen}");
            return ArraySegment<float>.Empty;
        }
        
        // SF2
        // Read the sample data
        var audioData = SharedSampleBuffer.New(byteLen / 2);
        // Convert to float
        AudioUtil.ConvertPCM16ToFloat32(
            _s16leData.Value,
            audioData,
            _s16leData.Value.Count / 2);

        AudioData = audioData;
        return audioData;
    }

    public override ArraySegment<byte> GetRawData(bool allowVorbis)
    {
        if (DataOverridden || CompressedData != null)
            // Return vorbis or encode manually
            return base.GetRawData(allowVorbis);
        // Copy the smpl directly
        return _s16leData ?? ArraySegment<byte>.Empty;
    }
    
    /// <summary>Reads the samples from the shdr chunk</summary>
    public static List<SFSample> Read(
        RIFFChunk sampleHeadersChunk,
        SampleChunk smplChunkData,
        bool linkSamples = true)
    {
        var shcData = sampleHeadersChunk.Data;
        var samples = new List<SFSample>();
        var index = 0;

        while (shcData.Count > 0)
        {
            var sample = Read(index, ref shcData, smplChunkData);
            samples.Add(sample);
            index++;
        }
        
        // Remove EOS
        samples.RemoveAt(samples.Count - 1);
        
        // Link samples
        if (linkSamples)
            foreach (var s in samples)
                s.GetLinkedSample(samples);

        return samples;
    }
    
    /// <summary> Reads it into a sample </summary>
    private static SFSample Read(
        int index,
        ref ArraySegment<byte> sampleHeaderData,
        SampleChunk smplArrayData)
    {
        // Read the sample name
        var sampleName = Util.ReadBinaryString(ref sampleHeaderData, 20);
        // Read the sample start index
        var sampleStartIndex = Util.ReadLittleEndian(ref sampleHeaderData, 4) * 2;
        var sampleEndIndex = Util.ReadLittleEndian(ref sampleHeaderData, 4) * 2;
        // Read the sample looping start index
        var sampleLoopStartIndex = Util.ReadLittleEndian(ref sampleHeaderData, 4);
        // Read the sample looping end index
        var sampleLoopEndIndex = Util.ReadLittleEndian(ref sampleHeaderData, 4);
        // Read the sample rate
        var sampleRate = Util.ReadLittleEndian(ref sampleHeaderData, 4);
        
        // Read the original sample pitch
        var samplePitch = sampleHeaderData[0];
        sampleHeaderData = sampleHeaderData[1..];

        if (samplePitch > 127)
        {
            // If it's out of range, then default to 60
            samplePitch = 60;
        }

        // Read the sample pitch correction
        var samplePitchCorrection = Util.SignedInt8(sampleHeaderData[0]);
        sampleHeaderData = sampleHeaderData[1..];
        
        // Read the link to the other channel
        var sampleLink = Util.ReadLittleEndian(ref sampleHeaderData, 2);
        var sampleType = Util.ReadLittleEndian(ref sampleHeaderData, 2);

        return new SFSample(
            Util.ToString(sampleName),
            // uint
            sampleStartIndex,
            sampleEndIndex,
            sampleLoopStartIndex,
            sampleLoopEndIndex,
            //
            sampleRate,
            samplePitch,
            samplePitchCorrection,
            sampleLink,
            sampleType,
            smplArrayData,
            index);
    }
}