using System.Runtime.CompilerServices;
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
    
    /// <summary>Seedable random generator</summary>
    /// <remarks>https://stackoverflow.com/a/47593316</remarks>
    /// <returns></returns>
    public static Func<double> SplitMix32(int seed)
    {
        var a = unchecked((uint)seed);
        return () =>
        {
            unchecked
            {
                a += 0x9e_37_79_b9;
                var t = a ^ (a >>> 16);
                t *= 0x21_f0_aa_ad;
                t ^= t >>> 15;
                t *= 0x73_5a_2d_97;
                return ((t ^ (t >>> 15)) >>> 0) / 4_294_967_296d;
            }
        };
    }

    [ThreadStatic] private static Func<double>? _rand;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Rand() => (_rand ??= SplitMix32(81_572))();

    static SpessaUtil() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}