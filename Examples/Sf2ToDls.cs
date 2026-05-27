#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj

using SpessaSharp.SoundBank;

// Process arguments
if (args.Length != 2)
{
    // Also: ./Sf2ToDls.cs <dls input path> <sf2 output path>
    Console.WriteLine("Usage: dotnet Sf2ToDls.cs -- <dls input path> <sf2 output path>");
    return;
}

var sf2Path = args[0];
var dlsPath = args[1];

Console.WriteLine("[WARN] DLS conversion may lose data.");
var bank = SoundBank.From(new FileInfo(sf2Path));

Console.WriteLine($"Name: {bank.Info.Name}");
Console.WriteLine("Writing file ...");

bank.WriteDLS(new FileInfo(dlsPath));
Console.WriteLine($"File written to {dlsPath}");