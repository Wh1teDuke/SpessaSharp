#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj

using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Write;
using SpessaSharp.SoundBank;

// Process arguments
if (args.Length != 3)
{
    // Also: ./RMidWriter.cs <sf2/dls input path> <mid input path> <rmi output path>
    Console.WriteLine(
        "Usage: dotnet RMidWriter.cs -- <sf2/dls input path> <mid input path> <rmi output path>");
    return;
}

var sfPath = args[0];
var midPath = args[1];
var outPath = args[2];

// Load bank and MIDI
var bank = SoundBank.From(new FileInfo(sfPath));
var midi = Midi.From(new FileInfo(midPath));
Console.WriteLine("Loaded bank and MIDI!");

// Trim sf2 for midi
bank.Trim(midi);

// Write rmid
var rmidi = midi.WriteRMIDI(
    bank.WriteSF2(), 
    new WriterRMidi.Options { SoundBank = bank });
File.WriteAllBytes(outPath, rmidi);
Console.WriteLine($"File written to {outPath}");