#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj

using SpessaSharp.SoundBank;

// Process arguments
if (args.Length != 2)
{
    // Also: ./DlsToSf2.cs <dls input path> <sf2 output path>
    Console.WriteLine("Usage: dotnet DlsToSf2.cs -- <dls input path> <sf2 output path>");
    return;
}

var dlsPath = args[0];
var sf2Path = args[1];

var bank = SoundBank.From(new FileInfo(dlsPath));

Console.WriteLine($"Name: {bank.Info.Name}");
Console.WriteLine("Writing file ...");

bank.WriteSF2(new FileInfo(sf2Path));
Console.WriteLine($"File written to {sf2Path}");