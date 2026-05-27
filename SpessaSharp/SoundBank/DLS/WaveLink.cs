using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.DLS;

/// <summary></summary>
/// <param name="Channel">
/// Specifies the channel placement of the sample. This is used to place mono sounds within a
/// stereo pair or for multi-track placement. Each bit position within the ulChannel field specifies
/// a channel placement with bit 0 specifying a mono sample or the left channel of a stereo file.</param>
/// <param name="TableIndex">Specifies the 0 based index of the cue entry in the wave pool table.</param>
/// <param name="FusOptions">Specifies flag options for this wave link. All bits not defined must be set to 0.</param>
/// <param name="PhaseGroup">
/// Specifies a group number for samples which are phase locked. All waves in a set of wave
/// links with the same group are phase locked and follow the wave in the group with the
/// F_WAVELINK_PHASE_MASTER flag set. If a wave is not a member of a phase locked
/// group, this value should be set to 0.</param>
internal readonly record struct WaveLink(
    int Channel, int TableIndex, short FusOptions, short PhaseGroup)
{
    public WaveLink(int tableIndex) : this(1, tableIndex, 0, 0)
    {}

    public static WaveLink Read(RIFFChunk chunk)
    {
        var data = chunk.Data;
        // Flags
        var fusOptions = (short)Util.ReadLittleEndian(ref data, 2);
        // Phase group
        var phaseGroup = (short)Util.ReadLittleEndian(ref data, 2);
        // Channel
        var ulChannel = Util.ReadLittleEndian(ref data, 4);
        // Table index
        var ulTableIndex = Util.ReadLittleEndian(ref data, 4);
        
        return new WaveLink(ulChannel, ulTableIndex, fusOptions, phaseGroup);
    }

    public static WaveLink FromSFZone(
        ReadOnlySpan<BasicSample> samples,
        BasicInstrument.Zone zone)
    {
        var index = samples.IndexOf(zone.Sample);
        if (index == -1)
            throw SpessaException.Invalid(
                $"Wave link error: Sample {
                    zone.Sample.Name} does not exist in the sample list");

        var waveLink = new WaveLink(index);

        switch (zone.Sample.SType)
        {
            case BasicSample.Type.Left:
            case BasicSample.Type.Mono:
                // Left (or mono)
                waveLink = waveLink with { Channel = 1 };
                break;
            
            case BasicSample.Type.Right:
                // Right channel
                waveLink = waveLink with { Channel = 1 << 1 };
                break;

            case BasicSample.Type.Null:
            case BasicSample.Type.Linked:
            case BasicSample.Type.RomMono:
            case BasicSample.Type.RomRight:
            case BasicSample.Type.RomLeft:
            case BasicSample.Type.RomLinked:
            default: break;
        }
        
        return waveLink;
    }

    public ArraySegment<byte> Write()
    {
        var data = (Span<byte>)stackalloc byte[12];
        var seg = data;
        
        Util.WriteWord(ref seg, FusOptions);    // FusOptions
        Util.WriteWord(ref seg, PhaseGroup);    // UsPhaseGroup
        Util.WriteDword(ref seg, Channel);      // UlChannel
        Util.WriteDword(ref seg, TableIndex);   // UlTableIndex

        return RIFFChunk.Write(new RIFFChunk.FourCC("wlnk"), data);
    }
}