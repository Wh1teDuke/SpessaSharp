#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj

using SpessaSharp.SoundBank;
using SpessaSharp.Utils;

// Process arguments
if (args.Length != 2)
{
    // Also: ./ExtractFirstSampleAsWav.cs <soundbank path> <wav output path>
    Console.WriteLine("Usage: dotnet ExtractFirstSampleAsWav.cs -- <soundbank path> <wav output path>");
    return;
}

var output = args[1];
var bank = SoundBank.From(new FileInfo(args[0]));
var sample = bank.Samples[0];

Console.WriteLine($"Exporting sample: {sample.Name}");
var wav = AudioUtil.ToWav([sample.GetAudioData()], sample.Rate);

File.WriteAllBytes(output, wav);
Console.WriteLine($"File written to {output}");