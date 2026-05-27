using System.Diagnostics;
using System.Text;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Read;


/// <summary> Parses a midi file for the sequencer, including things like marker or CC 2/4 loop detection, copyright detection, etc. </summary>
internal static class ReaderMidi
{
    private static readonly int[] DataBytesAmount = [
        2, // 0x8 Note off
        2, // 0x9 Note on
        2, // 0xa Note at
        2, // 0xb Cc change
        1, // 0xc Pg change
        1, // 0xd Channel after touch
        2, // 0xe Pitch Wheel
    ];
    
    private readonly record struct Chunk(
        ArraySegment<byte> Type, int Size, ArraySegment<byte> Data);
    
    /// <summary>
    /// Loads a Standard MIDI File (SMF) from given binary data
    /// </summary>
    /// <param name="outputMIDI">The BasicMIDI instance to populate with the parsed MIDI data.</param>
    /// <param name="smfFileBinary">The ArrayBuffer containing the SMF file data.</param>
    /// <param name="fileName">The optional name of the file, will be used if the MIDI file does not have a name.</param>
    /// <remarks>
    /// This function reads the MIDI file format, extracts the header and track chunks, and populates the BasicMIDI instance with the parsed data.
    /// It supports Standard MIDI Files (SMF), RIFF MIDI (RMIDI), and Extensible Music Format (XMF).
    /// It also handles embedded soundbanks in RMIDI files.
    /// If the file is an RMIDI file, it will extract the embedded soundbank and store
    /// it in the `embeddedSoundFont` property of the BasicMIDI instance.
    /// If the file is an XMF file, it will parse the XMF structure and extract the MIDI data.
    /// </remarks>
    /// <exception cref="Exception"></exception>
    public static void Load(
        Midi outputMIDI, ArraySegment<byte> smfFileBinary, string? fileName)
    {
        Debug.WriteLine("Parsing MIDI File...");

        outputMIDI.FileName = fileName;
        
        var headerChunk = ReadChunk(ref smfFileBinary);
        if (headerChunk.Size != 6)
            throw SpessaException.ParsingMidi(
                $"Invalid MIDI header chunk size! Expected 6, got {headerChunk.Size}");
        
        // Format
        var hcData = headerChunk.Data;
        
        var fID = Util.ReadBigEndian(ref hcData, 2);
        outputMIDI.Format = (MidiFormat)fID;
        // Tracks count
        var trackCount = Util.ReadBigEndian(ref hcData, 2);
        // Time Division
        outputMIDI.TimeDivision = Util.ReadBigEndian(ref hcData, 2);
        // Read all the tracks
        for (var i = 0; i < trackCount; i++)
        {
            var track = new Track();
            var trackChunk = ReadChunk(ref smfFileBinary);

            if (!Ascii.Equals(trackChunk.Type, "MTrk"))
                throw SpessaException.ParsingMidi(
                    $"Invalid track header! Expected 'MTrk', got '{trackChunk.Type}'");
            
            // MIDI running byte
            byte? runningByte = null;
            var totalTicks = 0;
            
            // Format 2 plays sequentially
            if (outputMIDI.Format == MidiFormat.m2 && i > 0) 
                totalTicks += outputMIDI.Tracks[i - 1].Events[^1].Ticks;
            
            // Loop until we reach the end of track
            var tcData = trackChunk.Data;
            while (tcData.Count != 0)
            {
                totalTicks += Util.ReadVLengthQuantity(ref tcData);
                // Check if the status byte is valid (IE. larger than 127)
                var statusByteCheck = tcData[0];
                byte? statusByte = null;
                
                // If we have a running byte and the status byte isn't valid
                if (runningByte != null && statusByteCheck < 0x80)
                    statusByte = runningByte;

                else
                {
                    if (statusByteCheck < 0x80)
                        // If we don't have a running byte and the status byte isn't valid, it's an error.
                        throw SpessaException.ParsingMidi(
                            $"Unexpected byte with no running byte. ({statusByteCheck})");

                    // If the status byte is valid, use that
                    statusByte = statusByteCheck;
                    tcData = tcData[1..];
                }

                var dataSize = 0;
                // Determine the message's length
                if (
                    // First note off (note off on channel 0)
                    statusByte >= MidiMessage.Type.NoteOff.ID() &&
                    // Lower than sysex (pitch wheel on channel 15)
                    statusByte < MidiMessage.Type.SystemExclusive.ID()) 
                {
                    // Voice message
                    // Gets the midi message length
                    dataSize = DataBytesAmount[(int)(statusByte >> 4) - 0x8];
                    // Save the status byte
                    runningByte = statusByte;
                } 
                else if (statusByte == MidiMessage.Type.SystemExclusive.ID())
                {
                    // Sysex
                    dataSize = Util.ReadVLengthQuantity(ref tcData);
                } 
                else if (statusByte == 0xff) 
                {
                    // Meta message (the next is the actual status byte)
                    statusByte = tcData[0];
                    tcData = tcData[1..];
                    dataSize = Util.ReadVLengthQuantity(ref tcData);
                } 
                else 
                {
                    // System common/realtime (no length)
                    dataSize = 0;
                }
                
                // Put the event data into the array
                var eventData = tcData[..dataSize];
                track.EventList.Add(new MidiMessage(
                    totalTicks, statusByte.Value, eventData));
                
                // Advance the track chunk
                tcData = tcData[dataSize..];
            }

            trackChunk = trackChunk with { Data = tcData };
            
            outputMIDI.Tracks.Add(track);
            Debug.WriteLine(
                $"Parsed {outputMIDI.Tracks.Count} / {outputMIDI.Tracks.Count}");
        }
        
        Debug.WriteLine("All tracks parsed correctly!");
        // Parse the events (no need to sort as they are already sorted by the SMF specification)
        outputMIDI.Flush(false);

        return;

        Chunk ReadChunk(ref ArraySegment<byte> fileByteArray)
        {
            var type = Util.ReadBinaryString(ref fileByteArray, 4);
            var size = Util.ReadBigEndian(ref fileByteArray, 4);
            var data = fileByteArray[.. size];
            fileByteArray = fileByteArray[size..];
            
            return new Chunk(type, size, data);
        }
    }
}