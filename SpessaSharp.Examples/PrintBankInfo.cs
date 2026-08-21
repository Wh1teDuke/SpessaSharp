#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj

using SpessaSharp.SoundBank;

// Process arguments
if (args.Length != 1)
{
    // Also: ./PrintBankInfo.cs <sf2/dls input path>
    Console.WriteLine("Usage: dotnet PrintBankInfo.cs -- <sf2/dls input path>");
    return;
}

var indent = 0;
var filePath = args[0];
var bank = SoundBank.From(new FileInfo(filePath));
WriteLine("Loaded bank: " + bank.Info.Name);

Group("Bank Information");
var info = bank.Info;
WriteLine($"Name: {info.Name}");
WriteLine($"Version: {info.Version.Major}.{info.Version.Minor}");
WriteLine($"Creation date: {info.CreationDate:s}");
WriteLine($"Sound engine: {info.SoundEngine}");

if (info.Engineer is {} engineer)
    WriteLine($"Engineer: {engineer}");
if (info.Product is {} product)
    WriteLine($"Product: {product}");
if (info.Copyright is { } copyright)
    WriteLine($"Copyright: {copyright}");
if (info.Comment is {} comment)
    WriteLine($"Comment: {comment}");
if (info.Software is {} software)
    WriteLine($"Software: {software}");
if (info.Subject is {} subject)
    WriteLine($"Subject: {subject}");
if (info.RomInfo is {} romInfo)
    WriteLine($"ROM Info: {romInfo}");
if (info.RomVersion is {} romVersion)
    WriteLine($"ROM Version: {romVersion.Major}.{romVersion.Minor}");

WriteLine($"Preset count: {bank.Presets.Count}");
WriteLine($"Instrument count: {bank.Instruments.Count}");
WriteLine($"Sample count: {bank.Samples.Count}");
GroupEnd();

Group("Preset data:");
foreach (var preset in bank.Presets)
{
    Group($"--- {preset} ---");
    
    Group("Zones:");
    WriteLine("--- Global Zone ---");
    WriteLine($"Key range: {preset.GlobalZone.KeyRange}");
    WriteLine($"Velocity range: {preset.GlobalZone.VelRange}");

    foreach (var zone in preset.Zones)
    {
        WriteLine($"--- {zone.Instrument.Name} ---");
        WriteLine($"Key range: {zone.Basic.KeyRange}");
        WriteLine($"Velocity range: {zone.Basic.VelRange}");
    }
    
    GroupEnd();
    GroupEnd();
}
GroupEnd();

Group("Instrument data:");
foreach (var inst in bank.Instruments)
{
    Group($"--- {inst.Name} ---");
    WriteLine($"Linked presets: {string.Join(
        ", ", inst.LinkedTo.Select(p => p.Name))}");
    
    Group("Zones:");
    WriteLine("--- Global Zone ---");
    WriteLine($"Key range: {inst.GlobalZone.KeyRange}");
    WriteLine($"Velocity range: {inst.GlobalZone.VelRange}");

    foreach (var zone in inst.Zones)
    {
        WriteLine($"--- {zone.Sample.Name} ---");
        WriteLine($"Key range: {zone.Basic.KeyRange}");
        WriteLine($"Velocity range: {zone.Basic.VelRange}");
    }
    
    GroupEnd();
    GroupEnd();
}
GroupEnd();

Group("Sample data:");
foreach (var sample in bank.Samples)
{
    Group($"--- {sample.Name} ---");

    WriteLine($"MIDI Key: {sample.OriginalKey}");
    WriteLine($"Cent Correction: {sample.PitchCorrection}");
    WriteLine($"Compressed: {sample.IsCompressed}");
    WriteLine($"Sample Link: {(
        sample.LinkedSample is { } link ? link.Name : "unlinked")}");
    
    WriteLine($"Linked Instruments: {string.Join(
        ", ", sample.LinkedTo.Select(i => i.Name))}");
    
    GroupEnd();
}
GroupEnd();

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