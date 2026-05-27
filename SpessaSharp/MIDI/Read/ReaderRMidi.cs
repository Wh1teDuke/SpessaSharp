using System.Diagnostics;
using System.Text;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Read;

internal static class ReaderRMidi
{
    /// <summary> Loads a RIFF MIDI File (RMIDI) from given binary data </summary>
    /// <param name="outputMIDI">The MIDI instance to populate with the parsed MIDI data.</param>
    /// <param name="arraySegment">The ArraySegment containing the file data.</param>
    /// <param name="fileName">The optional name of the file, will be used if the MIDI file does not have a name.</param>
    public static void Load(
        Midi outputMIDI, ArraySegment<byte> arraySegment, string? fileName)
    {
        // https://github.com/spessasus/sf2-rmidi-specification#readme
        // Skip size (we already verified "RIFF" if we're here)
        var binaryData = arraySegment[8..];
        var rmid = Util.ReadBinaryString(ref binaryData, 4);
        if (!Ascii.Equals(rmid, "RMID"))
            throw SpessaException.ParsingMidi(
                $"Invalid RMIDI header! Expected 'RMID', got '{Util.ToString(rmid)}'");
        var riff = RIFFChunk.Read(ref binaryData);
        if (riff.Header != "data")
            throw SpessaException.ParsingMidi(
                $"Invalid RMIDI Chunk header! Expected 'data', got '{riff.Header}'");

        // OutputMIDI is a rmid, load the midi into an array for parsing
        var smfFileBinary = riff.Data;
        
        var isSF2RMIDI = false;
        var foundDbnk = false;
        // Keep loading chunks until we get the "SFBK" header
        while (binaryData.Count > 0)
        {
            var start = binaryData;
            var currentChunk = RIFFChunk.Read(ref binaryData, true);
            var ccData = currentChunk.Data;

            if (currentChunk.Header == "RIFF")
            {
                var type = Util.ReadBinaryString(ref ccData, 4);
                if (Util.EqualsIgnoreCase(type, "sfbk", "sfpk", "dls "))
                {
                    Debug.WriteLine("Found embedded soundbank!");
                    outputMIDI.EmbeddedSoundBank = start[..(8 + currentChunk.Size)];
                }
                else
                    Debug.WriteLine($"Unknown RIFF chunk: '{Util.ToString(type)}'");

                if (Ascii.EqualsIgnoreCase(type, "dls "))
                {
                    // Assume bank offset of 0 by default. If we find any bank selects, then the offset is 1.
                    outputMIDI.IsDLSRMIDI = true;
                }
                else
                    isSF2RMIDI = true;
            }
            else if (currentChunk.Header == "LIST")
            {
                var type = Util.ReadBinaryString(ref ccData, 4);
                if (Ascii.Equals(type,"INFO"))
                {
                    Debug.WriteLine("Found RMIDI INFO chunk!");
                    while (ccData.Count > 0)
                    {
                        var infoChunk = RIFFChunk.Read(ref ccData, true);
                        var infoHeader = infoChunk.Header;
                        var infoData = infoChunk.Data;

                        switch (infoHeader)
                        {
                            default:
                                Debug.WriteLine($"[WARN] Unknown RMIDI Info: {infoHeader}");
                                break;
                            case ("INAM") _:
                                SetInfo(RMidi.Info.Key.Name);
                                break;
                            case ("IALB")_:
                            case ("IPRD")_:
                                // Note that there are two album chunks: IPRD and IALB
                                SetInfo(RMidi.Info.Key.Album);
                                break;
                            case ("ICRT")_:
                            case ("ICRD")_:
                                // Older RMIDIs written by spessasynth erroneously used ICRT instead of ICRD.
                                SetInfo(RMidi.Info.Key.CreationDate);
                                break;
                            case ("IART")_:
                                SetInfo(RMidi.Info.Key.Artist);
                                break;
                            case ("IGNR")_:
                                SetInfo(RMidi.Info.Key.Genre);
                                break;
                            case ("IPIC")_:
                                SetInfo(RMidi.Info.Key.Picture);
                                break;
                            case ("ICOP")_:
                                SetInfo(RMidi.Info.Key.Copyright);
                                break;
                            case ("ICMT")_:
                                SetInfo(RMidi.Info.Key.Comment);
                                break;
                            case ("IENG")_:
                                SetInfo(RMidi.Info.Key.Engineer);
                                break;
                            case ("ISFT")_:
                                SetInfo(RMidi.Info.Key.Software);
                                break;
                            case ("ISBJ")_:
                                SetInfo(RMidi.Info.Key.Subject);
                                break;
                            case ("IENC")_:
                                SetInfo(RMidi.Info.Key.InfoEncoding);
                                break;
                            case ("MENC")_:
                                SetInfo(RMidi.Info.Key.MidiEncoding);
                                break;
                            case ("DBNK")_:
                                outputMIDI.BankOffset = Util.
                                    ReadLittleEndian(infoData[..2]);
                                foundDbnk = true;
                                break;

                            void SetInfo(RMidi.Info.Key key) =>
                                outputMIDI.RmidiInfo[key] = infoData;
                        }
                    }
                }
            }
        }

        if (isSF2RMIDI && !foundDbnk)
            outputMIDI.BankOffset = 1; // Defaults to 1

        if (outputMIDI.IsDLSRMIDI)
        {
            // Assume bank offset of 0 by default. If we find any bank selects, then the offset is 1.
            outputMIDI.BankOffset = 0;
        }
        
        // If no embedded bank, assume 0
        if (outputMIDI.EmbeddedSoundBank == null)
            outputMIDI.BankOffset = 0;
        
        // Send the extracted SMF to the parser
        ReaderMidi.Load(outputMIDI, smfFileBinary, fileName);
    }
}