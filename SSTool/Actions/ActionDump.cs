using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.Utils;
using SSTool.Util;

namespace SSTool.Actions;

public static class ActionDump
{
    public static void This(
        FileInfo input, FileInfo? output = null)
    {
        if (!input.Exists)
            Etc.Error($"File not found: {input.FullName}");

        var fileKind = SpessaUtil.WhatIs(input);
        if (fileKind is null)
            Etc.Error($"File '{input.FullName}' not supported.");

        output ??= new FileInfo(
            Path.ChangeExtension(input.FullName, "txt"));

        BasePrinter printer = fileKind switch
        {
            SpessaUtil.FileKind.Midi or
            SpessaUtil.FileKind.EmbeddedMidi =>
                new MidiPrinter(Midi.From(input)),
            SpessaUtil.FileKind.SoundBank =>
                new SoundBankPrinter(SoundBank.From(input.OpenRead())),
            _ => throw new ArgumentOutOfRangeException()
        };
        
        using var stream = output.OpenWrite();
        using var writer = new StreamWriter(stream);
        writer.Write(printer.Print());
    }
}