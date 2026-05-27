using System.Diagnostics;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.DLS;

/// <summary></summary>
/// <param name="Gain">Specifies the gain to be applied to this sample in 32 bit relative gain units. Each unit of gain represents 1/655360 dB.</param>
/// <param name="FineTune">Specifies the tuning offset from the usUnityNote in 16 bit relative pitch. (cents)</param>
/// <param name="Loop">
/// Specifies the number (count) of <b>wavesample-loop</b> records that are contained in the
/// <b>wsmp-ck</b> chunk. The <b>wavesample-loop</b> records are stored immediately following the
/// cSampleLoops data field. One shot sounds will have the cSampleLoops field set to 0.
/// Looped sounds will have the cSampleLoops field set to 1. Values greater than 1 are not yet
/// defined at this time.</param>
/// <param name="FulOptions">Specifies flag options for the digital audio sample. Default to F_WSMP_NO_COMPRESSION, according to all DLS files I have.</param>
/// <param name="UnityNote">Specifies the MIDI note which will replay the sample at original pitch. This value ranges from 0 to 127 (a value of 60 represents Middle C, as defined by the MIDI specification).</param>
internal readonly record struct WaveSample(
    int Gain,
    short FineTune,
    DownloadableSounds.Loop? Loop,
    int FulOptions,
    short UnityNote)
{
    private const int WSMP_SIZE = 20;
    private const int WSMP_LOOP_SIZE = 16;
    
    public static readonly WaveSample Default = new (
        Gain: 0, FineTune: 0, Loop: null, FulOptions: 2, UnityNote: 60);

    public static WaveSample Read(RIFFChunk chunk)
    {
        var data = chunk.Data;
        DLSVerifier.VerifyHeader(chunk, new RIFFChunk.FourCC("wsmp"));
        
        // CbSize
        var cbSize = Util.ReadLittleEndian(ref data, 4);
        if (cbSize != WSMP_SIZE) 
            Debug.WriteLine(
                $"Wsmp cbSize mismatch: got {cbSize}, expected {WSMP_SIZE}");
        
        var unityNote = (short)Util.ReadLittleEndian(ref data, 2);
        // SFineTune
        var fineTune = Util.SignedInt16(data[0], data[1]);
        data = data[2..];
        
        // LGain: Each unit of gain represents 1/655360 dB
        var gain = Util.ReadLittleEndian(ref data, 4);
        var fulOptions = Util.ReadLittleEndian(ref data, 4);
        
        // Read loop count (always one or zero)
        DownloadableSounds.Loop? loop = null;
        var loopsAmount = Util.ReadLittleEndian(ref data, 4);
        if (loopsAmount == 0)
        {
            // No Loop   
        }
        else
        {
            cbSize = Util.ReadLittleEndian(ref data, 4);
            if (cbSize != WSMP_LOOP_SIZE)
            {
                Debug.WriteLine($"CbSize for loop in wsmp mismatch. Expected {
                    WSMP_LOOP_SIZE}, got {cbSize}");
            }
            
            // Loop type: loop normally or loop until release (like soundfont)
            var loopType = (DownloadableSounds.Loop.Type)
                Util.ReadLittleEndian(ref data, 4);
            var loopStart = Util.ReadLittleEndian(ref data, 4);
            var loopLen = Util.ReadLittleEndian(ref data, 4);
            loop = new DownloadableSounds
                .Loop(loopType, loopStart, loopLen);
        }

        return new WaveSample(
            Gain: gain, 
            FineTune: fineTune, 
            Loop: loop, 
            FulOptions: fulOptions, 
            UnityNote: unityNote);
    }

    public static WaveSample From(BasicSample sample)
    {
        var waveSample = Default with 
        {
            Gain        = 0,
            UnityNote   = (short)sample.OriginalKey,
            Loop        = null,
            FineTune    = (short)sample.PitchCorrection,
        };

        if (sample.LoopEnd != 0 || sample.LoopStart != 0)
            waveSample = waveSample with
            {
                Loop = new DownloadableSounds.Loop(
                    DownloadableSounds.Loop.Type.Forward,
                    sample.LoopStart,
                    sample.LoopEnd - sample.LoopStart)
            };
        
        return waveSample;
    }

    public static WaveSample FromSFZone(BasicInstrument.Zone zone)
    {
        var unityNote = zone.Basic.GetGenerator(
            Generator.Type.OverridingRootKey, (short)zone.Sample.OriginalKey);
        
        // A lot of sound banks like to set scale tuning to 0 in drums and keep the key at 60
        // Since we implement scale tuning via a dls articulator and fluid doesn't support these,
        // Change the root key here
        if (zone.Basic.GetGenerator(Generator.Type.ScaleTuning, 100) == 0 &&
            zone.Basic.KeyRange.Max - zone.Basic.KeyRange.Min == 0)
            unityNote = (short)zone.Basic.KeyRange.Min;
        
        /*
         Note: this may slightly change the generators themselves when doing SF -> DLS -> SF, but the tuning remains the same
         Testcase: Helicopter from GeneralUser-GS v2.0.1
         It sets coarse -13 fine 2 which is a total of -1298 cents
         This then gets converted into -12 coarse and -98 tune which is still correct!
        */
        var fineTune =
            (short)(zone.Basic.FineTuning + zone.Sample.PitchCorrection);
        // E-mu attenuation correction
        var attenuationCb = Util.Round(zone.Basic.GetGenerator(
            Generator.Type.InitialAttenuation, 0) * .4);
        
        // Gain is stored as a 32-bit value, shift here
        var gain = -attenuationCb << 16;
        var loopingMode = (Synthesizer.Synthesizer.SampleLoopingMode)zone.Basic
            .GetGenerator(Generator.Type.SampleModes, 0);
        
        // Don't add loops unless needed
        DownloadableSounds.Loop? loop = null;
        
        if (loopingMode != Synthesizer.Synthesizer.SampleLoopingMode.m0)
        {
            // make sure to get offsets
            var loopStart =
                zone.Sample.LoopStart +
                zone.Basic.GetGenerator(
                    Generator.Type.StartLoopAddrsOffset, 0) +
                zone.Basic.GetGenerator(
                    Generator.Type.StartLoopAddrsCoarseOffset, 0) *
                32_768;

            var loopEnd =
                zone.Sample.LoopEnd +
                zone.Basic.GetGenerator(
                    Generator.Type.EndLoopAddrsOffset, 0) +
                zone.Basic.GetGenerator(
                    Generator.Type.EndLoopAddrsCoarseOffset, 0) *
                32_768;

            var dlsLoopType = loopingMode switch
            {
                Synthesizer.Synthesizer.SampleLoopingMode.m3 =>
                    DownloadableSounds.Loop.Type.LoopAndRelease,
                _ => DownloadableSounds.Loop.Type.Forward,
            };
            
            loop = new DownloadableSounds.Loop(
                dlsLoopType, loopStart, loopEnd - loopStart);
        }

        return Default with
        {
            Gain        = gain,
            FineTune    = fineTune,
            Loop        = loop,
            UnityNote   = unityNote,
        };
    }

    /// <summary>Converts the wsmp data into an SF zone.</summary>
    /// <param name="zone"></param>
    /// <param name="sample"></param>
    public void ToSFZone(BasicZone zone, BasicSample sample)
    {
        var loopingMode = Synthesizer.Synthesizer.SampleLoopingMode.m0;
        if (Loop is {} loop)
            loopingMode = 
                loop.LType == DownloadableSounds.Loop.Type.LoopAndRelease
                ? Synthesizer.Synthesizer.SampleLoopingMode.m3
                : Synthesizer.Synthesizer.SampleLoopingMode.m1;
        if (loopingMode != Synthesizer.Synthesizer.SampleLoopingMode.m0)
            zone.SetGenerator(Generator.Type.SampleModes, (int)loopingMode);
        
        // Convert to signed and turn into attenuation (invert)
        var wsmpGain16 = Gain >> 16;
        var wsmpAttenuation = -wsmpGain16;
        
        // Apply the E-MU attenuation correction here
        var wsmpAttenuationCorrected =
            Util.Round(wsmpAttenuation / .4);
        
        if (wsmpAttenuationCorrected != 0)
            zone.SetGenerator(
                Generator.Type.InitialAttenuation, 
                wsmpAttenuationCorrected);
        
        // Correct tuning
        zone.FineTuning = FineTune - sample.PitchCorrection;
        
        // Correct the key if needed
        if (UnityNote != sample.OriginalKey)
            zone.SetGenerator(
                Generator.Type.OverridingRootKey, UnityNote);
        
        // Correct loop if needed
        if (Loop is not var (_, start, len)) return;

        var diffStart = start - sample.LoopStart;
        var loopEnd = start + len;
        var diffEnd = loopEnd - sample.LoopEnd;

        if (diffStart != 0)
            Fix(
                diffStart,
                Generator.Type.StartLoopAddrsOffset,
                Generator.Type.StartLoopAddrsCoarseOffset);

        if (diffEnd != 0)
            Fix(
                diffEnd,
                Generator.Type.EndLoopAddrsOffset,
                Generator.Type.EndLoopAddrsCoarseOffset);
        return;

        void Fix(int val, Generator.Type fT, Generator.Type cT)
        {
            var fine = val % 32_768;
            zone.SetGenerator(fT, fine);
            // Coarse generator uses 32768 samples per step
            var coarse = /*trunc*/val / 32_768;
            if (coarse != 0) zone.SetGenerator(cT, coarse);
        }
    }

    public ArraySegment<byte> Write()
    {
        var loopCount = Loop == null ? 0 : 1;
        var wsmp = (Span<byte>)stackalloc byte[
            WSMP_SIZE + loopCount * WSMP_LOOP_SIZE];
        var buff = wsmp;
        
        // CbSize
        Util.WriteDword(ref wsmp, WSMP_SIZE);

        Util.WriteWord(ref wsmp, UnityNote);
        Util.WriteWord(ref wsmp, FineTune);

        Util.WriteDword(ref wsmp, Gain);
        Util.WriteDword(ref wsmp, FulOptions);
        
        // CSampleLoops
        Util.WriteDword(ref wsmp, loopCount);
        
        if (Loop is {} loop)
        {
            Util.WriteDword(ref wsmp, WSMP_LOOP_SIZE);
            Util.WriteDword(ref wsmp, (int)loop.LType);
            Util.WriteDword(ref wsmp, loop.Start);
            Util.WriteDword(ref wsmp, loop.Len);
        }

        return RIFFChunk.Write(new RIFFChunk.FourCC("wsmp"), buff);
    }
}