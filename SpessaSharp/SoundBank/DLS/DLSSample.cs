using SpessaSharp.SoundBank.Utils;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.DLS;

internal sealed class DLSSample: BasicSample
{
    private static readonly (int PCM, int ALAW) W_FORMAT_TAG = (
        PCM: 0x01, ALAW: 0x6);
    
    private readonly int _wFormatTag;
    private readonly int _bytesPerSample;
    
    public int WFormatTag => _wFormatTag;
    public int BytesPerSample => _bytesPerSample;
    
    /// <summary>Sample's raw dat abefore decoding it, for faster writing</summary>
    private readonly ArraySegment<byte>? _rawData;
    
    /// <summary></summary>
    /// <param name="name"></param>
    /// <param name="rate"></param>
    /// <param name="pitch"></param>
    /// <param name="pitchCorrection"></param>
    /// <param name="loopStart">Sample data points</param>
    /// <param name="loopEnd">Sample data points</param>
    /// <param name="dataChunk"></param>
    /// <param name="wFormatTag"></param>
    /// <param name="bytesPerSample"></param>
    public DLSSample(
        string name,
        int rate,
        int pitch,
        int pitchCorrection,
        int loopStart,
        int loopEnd,
        RIFFChunk dataChunk,
        int wFormatTag,
        int bytesPerSample): base(
            name, rate, pitch, pitchCorrection,
            Type.Mono, loopStart, loopEnd)
    {
        DataOverridden = false;
        _rawData = dataChunk.Data;
        _wFormatTag = wFormatTag;
        _bytesPerSample = bytesPerSample;
    }

    public override ArraySegment<float> GetAudioData()
    {
        if (_rawData is not {} rawData)
            return ArraySegment<float>.Empty;

        if (AudioData is null)
        {
            if (_wFormatTag == W_FORMAT_TAG.PCM)
                SetAudioData(ReadPCM(rawData, _bytesPerSample), Rate);
            else if (_wFormatTag == W_FORMAT_TAG.ALAW)
                SetAudioData(ReadALAW(rawData, _bytesPerSample), Rate);
            else
                throw SpessaException.Invalid(
                    $"Failed to decode sample. Unknown wFormatTag: {
                        _wFormatTag}");
        }
        
        return AudioData ?? ArraySegment<float>.Empty;
    }

    public override ArraySegment<byte> GetRawData(bool allowVorbis)
    {
        if (DataOverridden || IsCompressed)
            return base.GetRawData(allowVorbis);
        
        if (_wFormatTag == W_FORMAT_TAG.PCM && _bytesPerSample == 2)
            // Copy straight away
            return _rawData!.Value;

        return EncodeS16LE();
    }

    private static ArraySegment<float> ReadPCM(
        ArraySegment<byte> data, int bytesPerSample)
    {
        // Max value for the sample --v
        var maxSampleValue = (int)Math.Pow(2, bytesPerSample * 8 - 1);
        var maxUnsigned = (int)Math.Pow(2, bytesPerSample * 8);

        var normalizationFactor = 0;
        var isUnsigned = false;

        if (bytesPerSample == 1)
        {
            normalizationFactor = 255; // For 8-bit normalize from 0-255
            isUnsigned = true;
        }
        else
            normalizationFactor = maxSampleValue; // For 16-bit normalize from -32,768 to 32,767
        
        var sampleLen = data.Count / bytesPerSample;
        var sampleData = SharedSampleBuffer.New(sampleLen);

        if (bytesPerSample == 2)
        {
            // Special optimized case for s16 (most common)
            AudioUtil.ConvertPCM16ToFloat32(data, sampleData, sampleLen);
            return sampleData;
        }

        if (isUnsigned)
        {
            foreach (ref var s in sampleData.AsSpan())
            {
                // Read
                var sample = (float)Util.ReadLittleEndian(
                    ref data, bytesPerSample);
                // Turn into signed
                // Normalize unsigned 8-bit sample
                s = sample / normalizationFactor - .5f;
            }
        }
        else
        {
            foreach (ref var s in sampleData.AsSpan())
            {
                // Read
                var sample = (float)Util.ReadLittleEndian(
                    ref data, bytesPerSample);
                // Normalize signed sample
                if (sample >= maxSampleValue)
                    sample -= maxUnsigned;
                s = sample / normalizationFactor;
            }
        }

        return sampleData;
    }

    private static ArraySegment<float> ReadALAW(
        ArraySegment<byte> data, int bytesPerSample)
    {
        var sampleLen = data.Count / bytesPerSample;
        var sampleData = SharedSampleBuffer.New(sampleLen);
        var sampleSpan = sampleData.AsSpan();

        for (var i = 0; i < sampleLen; i++)
        {
            // Read
            var input = Util.ReadLittleEndian(ref data, bytesPerSample);
            
            // https://en.wikipedia.org/wiki/G.711#A-law
            // Re-toggle toggled bits
            var sample = input ^ 0x55;
            
            // Remove sign bit
            sample &= 0x7f;
            
            // Extract exponent
            var exponent = sample >> 4;
            // Extract mantissa
            var mantissa = sample & 0xf;
            if (exponent > 0)
                mantissa += 16; // Add leading '1', if exponent > 0

            mantissa = (mantissa << 4) + 0x8;
            if (exponent > 1) mantissa <<= exponent - 1;

            var s16sample = input > 127 ? mantissa : -mantissa;
            
            // Convert to float
            sampleSpan[i] = s16sample / 32_768f;
        }
        
        return sampleData;
    }
}