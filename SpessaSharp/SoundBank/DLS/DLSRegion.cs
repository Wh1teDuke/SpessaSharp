using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.DLS;

internal struct DLSRegion
{
    public readonly DLSArticulation Articulation = new();

    /// <summary>Specifies the key range for this region.</summary>
    public (int Min, int Max) KeyRange = (0, 127);
    /// <summary> Specifies the velocity range for this region. </summary>
    public (int Min, int Max) VelRange = (0, 127);

    /// <summary>
    /// Specifies the key group for a drum instrument. Key group values allow multiple regions
    /// within a drum instrument to belong to the same "key group." If a synthesis engine is
    /// instructed to play a note with a key group setting and any other notes are currently playing
    /// with this same key group, then the synthesis engine should turn off all notes with the same
    /// key group value as soon as possible.
    /// </summary>
    public int KeyGroup;
    
    /// <summary> Specifies flag options for the synthesis of this region. </summary>
    public int FusOptions;

    /// <summary>
    /// Indicates the layer of this region for editing purposes. This field facilitates the
    /// organization of overlapping regions into layers for display to the user of a DLS sound editor.
    /// For example, if a piano sound and a string section are overlapped to create a piano/string pad,
    /// all the regions of the piano might be labeled as layer 1, and all the regions of the string
    /// section might be labeled as layer 2
    /// </summary>
    public int usLayer;
    
    public readonly WaveSample WaveSample;
    public readonly WaveLink WaveLink;

    private DLSRegion(WaveLink waveLink, WaveSample waveSample)
    {
        WaveSample  = waveSample;
        WaveLink    = waveLink;
    }

    public static DLSRegion Copy(DLSRegion input)
    {
        var output = new DLSRegion(input.WaveLink, input.WaveSample)
        {
            KeyGroup    = input.KeyGroup,
            KeyRange    = input.KeyRange,
            VelRange    = input.VelRange,
            usLayer     = input.usLayer,
            FusOptions  = input.FusOptions,
        };

        output.Articulation.CopyFrom(input.Articulation);
        
        return output;
    }

    public static DLSRegion? Read(
        ReadOnlySpan<DownloadableSoundsSample> samples,
        RIFFChunk chunk)
    {
        var regionChunks = DLSVerifier.VerifyAndReadList(
            chunk,
            new RIFFChunk.DLSFourCC("rgn "),
            new RIFFChunk.DLSFourCC("rgn2"));
        
        // Wsmp: wave sample chunk
        var wsIndex = regionChunks.FindIndex(c => c.Header == "wsmp");
        WaveSample? waveSample = wsIndex != -1
            ? WaveSample.Read(regionChunks[wsIndex])
            : null;
        
        // Wlnk: wave link chunk
        var wlIndex = regionChunks.FindIndex(c => c.Header == "wlnk");
        if (wlIndex == -1)
        {
            // No wave link means no sample. What? Why is it even here then?
            Debug.WriteLine(
                "[WARN] Invalid DLS region: missing 'wlnk' chunk! Discarding...");
            return null;
        }

        var waveLink = WaveLink.Read(regionChunks[wlIndex]);
        
        // Region header
        var regionHeader = regionChunks.FindIndex(c => c.Header == "rgnh");
        if (regionHeader == -1)
        {
            Debug.WriteLine(
                "[WARN] Invalid DLS region: missing 'rgnh' chunk! Discarding...");
            return null;
        }

        if (waveLink.TableIndex < 0 || waveLink.TableIndex >= samples.Length)
        {
            DLSVerifier.ParsingError(
                $"Invalid sample index: {waveLink.TableIndex
                }. Samples available: {samples.Length}");
        }

        var sample = samples[waveLink.TableIndex];
        waveSample ??= sample.WaveSample;

        var region = new DLSRegion(waveLink, waveSample.Value);
        
        // Key range
        var rhData = regionChunks[regionHeader].Data;

        var keyMin = Util.ReadLittleEndian(ref rhData, 2);
        var keyMax = Util.ReadLittleEndian(ref rhData, 2);
        // Vel range
        var velMin = Util.ReadLittleEndian(ref rhData, 2);
        var velMax = Util.ReadLittleEndian(ref rhData, 2);
        
        // A fix for not cool files
        // Cannot do the same to key zones sadly
        if (velMin == 0 && velMax == 0)
        {
            velMax = 127;
            velMin = 0;
        }

        region.KeyRange = (keyMin, keyMax);
        region.VelRange = (velMin, velMax);
        
        // FusOptions
        region.FusOptions = Util.ReadLittleEndian(ref rhData, 2);
        // KeyGroup: essentially exclusive class
        region.KeyGroup = Util.ReadLittleEndian(ref rhData, 2);
        
        // UsLayer
        if (rhData.Count >= 2)
            region.usLayer = Util.ReadLittleEndian(ref rhData, 2);
        
        region.Articulation.Read(
            CollectionsMarshal.AsSpan(regionChunks));
        return region;
    }

    public static DLSRegion FromSFZone(
        BasicInstrument.Zone zone,
        ReadOnlySpan<BasicSample> samples)
    {
        var waveSample = WaveSample.FromSFZone(zone);
        var waveLink = WaveLink.FromSFZone(samples, zone);
        var region = new DLSRegion(waveLink, waveSample);
        
        // Assign ranges
        region.KeyRange.Min = Math.Max(zone.Basic.KeyRange.Min, 0);
        region.KeyRange.Max = zone.Basic.KeyRange.Max;
        
        region.VelRange.Min = Math.Max(zone.Basic.VelRange.Min, 0);
        region.VelRange.Max = zone.Basic.VelRange.Max;
        
        // KeyGroup (exclusive class)
        region.KeyGroup = zone.Basic.GetGenerator(
            Generator.Type.ExclusiveClass, 0);
        region.Articulation.FromSFZone(zone);
        
        return region;
    }

    public ArraySegment<byte> Write()
    {
        // in that order!
        var chunks = (ReadOnlySpan<ArraySegment<byte>>)[
            WriteHeader(),
            WaveSample.Write(),
            WaveLink.Write(),
            Articulation.Write(),
        ];

        return RIFFChunk.WriteParts(new RIFFChunk.FourCC("rgn2"), chunks, true);
    }

    public BasicInstrument.Zone ToSFZone(
        BasicInstrument instrument,
        ReadOnlySpan<BasicSample> samples)
    {
        if (WaveLink.TableIndex < 0 || WaveLink.TableIndex >= samples.Length)
            DLSVerifier.ParsingError(
                $"Invalid sample index: {WaveLink.TableIndex}");

        var sample = samples[WaveLink.TableIndex];
        var zone = instrument.CreateZone(sample);
        zone.Basic.KeyRange = KeyRange;
        zone.Basic.VelRange = VelRange;
        
        // If the zones are default (0-127), set to -1 as "not set"
        if (KeyRange is { Max: 127, Min: 0 })
            zone.Basic.KeyRange.Min = -1;
        if (VelRange is { Max: 127, Min: 0 })
            zone.Basic.VelRange.Min = -1;
        
        // KeyGroup: essentially exclusive class
        if (KeyGroup != 0)
            zone.Basic.SetGenerator(
                Generator.Type.ExclusiveClass, KeyGroup);
        
        WaveSample.ToSFZone(zone.Basic, sample);
        Articulation.ToSFZone(zone.Basic);
        
        // Remove generators with default values
        zone.Basic.Generators.RemoveAll(Generator.HasDefaultValue);

        return zone;
    }

    private ArraySegment<byte> WriteHeader()
    {
        // Region header
        var rgnhSeg = (Span<byte>)stackalloc byte[14];
        var buff = rgnhSeg;
        
        // KeyRange
        Util.WriteWord(ref rgnhSeg, (short)Math.Max(KeyRange.Min, 0));
        Util.WriteWord(ref rgnhSeg, (short)KeyRange.Max);
        // VelRange
        Util.WriteWord(ref rgnhSeg, (short)Math.Max(VelRange.Min, 0));
        Util.WriteWord(ref rgnhSeg, (short)VelRange.Max);
        // FusOptions
        Util.WriteWord(ref rgnhSeg, (short)FusOptions);
        // KeyGroup (exclusive class)
        Util.WriteWord(ref rgnhSeg, (short)KeyGroup);
        // UsLayer
        Util.WriteWord(ref rgnhSeg, (short)usLayer);

        return RIFFChunk.Write(new RIFFChunk.FourCC("rgnh"), buff);
    }
}