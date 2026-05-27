using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Write;

internal static class WriterMidi
{
    /// <summary> Exports the midi as a standard MIDI file. </summary>
    /// <param name="midi">The MIDI to serialize</param>
    /// <returns>The binary file data.</returns>
    /// <exception cref="Exception"></exception>
    public static ArraySegment<byte> Save(Midi midi)
    {
        if (midi.Tracks.Count == 0)
            throw new ArgumentException("MIDI has no tracks!");
        
        var binaryTrackData = new List<byte[]>();
        var buff = new byte[4];
        var asBuff = (ArraySegment<byte>)buff;
        
        using var ms1 = new MemoryStream();

        foreach (var track in midi.Tracks)
        {
            ms1.Seek(0, SeekOrigin.Begin);
            ms1.SetLength(0);

            var currentTick = 0;
            StatusByte? runningByte = null;

            foreach (var (ticks, sb, arraySegment) in track.Events)
            {
                // Ticks stored in MIDI are absolute, but SMF wants relative. Convert them here.
                var deltaTicks = Math.Max(0, ticks - currentTick);

                // EndOfTrack is written automatically.
                if (sb.Is(MidiMessage.Type.EndOfTrack)) 
                {
                    currentTick += deltaTicks;
                    continue;
                }
                
                // Write VLQ
                ArraySegment<byte> seg2 = buff;
                Util.WriteVLengthQuantity(deltaTicks, ref seg2);
                ms1.Write(seg2);
                
                // Determine the message
                if (sb.LE(MidiMessage.Type.SequenceSpecific))
                {
                    // This is a meta-message
                    // Syntax is FF<type><length><data>
                    ms1.WriteByte(0xff);
                    ms1.WriteByte(sb.Byte);

                    ArraySegment<byte> seg = buff;
                    Util.WriteVLengthQuantity(arraySegment.Count, ref seg);
                    ms1.Write(seg);
                    ms1.Write(arraySegment);

                    // RP-001:
                    // Sysex events and meta-events cancel any running status which was in effect.
                    runningByte = null;
                }
                else if (sb.Is(MidiMessage.Type.SystemExclusive))
                {
                    // This is a system exclusive message
                    // Syntax is F0<length><data>
                    ms1.WriteByte(0xf0);

                    ArraySegment<byte> seg = buff;
                    Util.WriteVLengthQuantity(arraySegment.Count, ref seg);
                    ms1.Write(seg);
                    ms1.Write(arraySegment);

                    // RP-001:
                    // Sysex events and meta-events cancel any running status which was in effect.
                    runningByte = null;
                }
                else
                {
                    // This is a midi message
                    if (runningByte != sb) 
                    {
                        // Running byte was not the byte we want. Add the byte here.
                        runningByte = sb;
                        // Add the status byte to the midi
                        ms1.WriteByte(sb.Byte);
                    }
                    // Add the data
                    ms1.Write(arraySegment);
                }

                currentTick += deltaTicks;
            }
            // Write endOfTrack
            ms1.Write([
                0,
                0xff, 
                (byte)MidiMessage.ID(MidiMessage.Type.EndOfTrack),
                0]);
            ms1.Seek(0, SeekOrigin.Begin);
            binaryTrackData.Add(ms1.ToArray());
        }

        // Write the file
        ms1.Seek(0, SeekOrigin.Begin);
        // Write header
        ms1.Write("MThd"u8); // MThd
        ms1.Write(Util.WriteBigEndian(asBuff, 6));
        ms1.WriteByte(0);
        ms1.WriteByte((byte)midi.Format);
        // Num tracks
        ms1.Write(Util.WriteBigEndian(asBuff[..2], midi.Tracks.Count));
        // Time division
        ms1.Write(Util.WriteBigEndian(asBuff[..2], midi.TimeDivision));
        
        // Write tracks
        foreach (var track in binaryTrackData)
        {
            // Write track header
            ms1.Write("MTrk"u8); // MTrk
            ms1.Write(
                Util.WriteBigEndian(buff.AsSpan(0, 4), 
                track.Length));
            ms1.Write(track);
        }

        ms1.Seek(0, SeekOrigin.Begin);
        return ms1.ToArray();
    }
}