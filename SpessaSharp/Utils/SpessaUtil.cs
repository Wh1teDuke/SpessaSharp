using System.Text;
using SpessaSharp.MIDI;

namespace SpessaSharp.Utils;

public static class SpessaUtil
{
    public static readonly Encoding MidiEncoding =
        Encoding.GetEncoding(28_591);

    public enum FileKind { Midi, SoundBank, EmbeddedMidi }

    public static FileKind? WhatIs(FileInfo file) => WhatIs(file.Extension);

    public static FileKind? WhatIs(string ext)
    {
        if (ext.Contains('.')) ext = ext.Replace(".", null);
        if (ext.Any(char.IsUpper)) ext = ext.ToLowerInvariant();

        return ext switch
        {
            "mid" or "midi" or "smf" or "kar"
                    => FileKind.Midi,
            "sf" or "sf2" or "sf3" or "sf4" or "sfogg" or "dls" or "dlp"
                    => FileKind.SoundBank,
            "rmi" or "xmf" or "mxmf"
                    => FileKind.EmbeddedMidi,
            _       => null
        };
    }

    public static object? TryParse(FileInfo file)
    {
        var triedMidi = false;
        try
        {
            switch (WhatIs(file))
            {
                case FileKind.EmbeddedMidi:
                case FileKind.Midi:
                    triedMidi = true;
                    return Midi.From(file);
                case FileKind.SoundBank:
                case null:
                    return SoundBank.SoundBank.From(file);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch { /* Ignored */ }

        try
        {
            return triedMidi 
                ? SoundBank.SoundBank.From(file)
                : Midi.From(file);
        }
        catch { /* Ignored */ }

        return null;
    }

    static SpessaUtil() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}