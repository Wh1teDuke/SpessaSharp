#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj

using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
// TODO: Update MidiPrinter
// Process arguments
if (args.Length != 2)
{
    // Also: ./AnalyzeMidiFile.cs <sf path> <midi path>
    Console.WriteLine("Usage: dotnet AnalyzeMidiFile.cs -- <sf path> <midi path>");
    return;
}

var indent = 0;
var sbkPath = args[0];
var midiPath = args[1];

var bank = SoundBank.From(new FileInfo(sbkPath));
var midi = Midi.From(new FileInfo(midiPath));
var used = midi.GetUsedProgramsAndKeys(bank);

Group("-- MIDI File Analysis ---");
WriteLine("Name: " + midi.GetName());
WriteLine("Duration: " + midi.Duration);
WriteLine("Time division: " + midi.TimeDivision);
WriteLine("Track count: " + midi.Tracks.Count);
WriteLine("Lyric count: " + midi.Lyrics.Count);
WriteLine("Bank offset: " + midi.BankOffset);

Group("--- Extra Metadata ---");
foreach (var meta in midi.GetExtraMetadata())
    WriteLine(meta);
GroupEnd();
WriteLine("---");

Group("--- RMIDI Metadata ---");
foreach (var key in Enum.GetValues<RMidi.Info.Key>())
    if (midi.GetRMidiInfo(key) is {} data)
        WriteLine($"{key}: {data}");
GroupEnd();
WriteLine("---");

Group("--- Used Programs ---");
foreach (var (preset, keys) in used) 
    WriteLine(
        $"{preset.Patch.ToFullMIDIString(),-30}-> {keys.Count} key combinations detected.");

GroupEnd();
WriteLine("---");

GroupEnd();
WriteLine("---");

return;

void Group(string str)
{
    WriteLine(str);
    indent += 4;
}

void GroupEnd()
{
    Console.WriteLine();
    indent -= 4;
}

void WriteLine(string str) => 
    Console.WriteLine(new string(' ', indent) + str);