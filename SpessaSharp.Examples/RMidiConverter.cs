#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj

using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Write;

// Process arguments
if (args.Length != 1)
{
    // Also: ./RMidiConverter.cs <folder>
    Console.WriteLine(
        "Usage: dotnet RMidiConverter.cs -- <folder>");
    return;
}

var dir = args[0];
Directory.CreateDirectory(Path.Join(dir, "rmid"));

foreach (var path in Directory.EnumerateFiles(dir, "*.mid"))
{
    var file = Path.GetFileName(path);

    var fileBin = File.ReadAllBytes(path);
    var sfFile = new FileInfo(path.Replace(".mid", ".sf2"));

    if (!sfFile.Exists)
    {
        Console.WriteLine($"[WARN] No matching file for {path}! Skipping!");
        continue;
    }
    
    var midi = Midi.From(fileBin);
    var outFile = Path.Join(dir, "rmid", file.Replace(".mid", ".rmi"));
    
    File.WriteAllBytes(outFile, midi.WriteRMIDI(
        File.ReadAllBytes(sfFile.FullName),
        new WriterRMidi.Options { BankOffset = 1, CorrectBankOffset = false }
    ));
    
    Console.WriteLine($"Wrote {outFile}");
}