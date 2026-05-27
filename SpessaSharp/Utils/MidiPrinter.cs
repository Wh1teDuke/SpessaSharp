using System.IO.Hashing;
using System.Text;

namespace SpessaSharp.Utils;

public sealed class MidiPrinter(
    MIDI.Midi midi,
    StringBuilder? sb               = null,
    MidiPrinter.Options? options    = null): BasePrinter(sb)
{
    private const int Version = 0;

    public readonly record struct Options(
        bool ReplaceSharpWithSynth
    );
    
    public Options Opts { get; init; } = options ?? new Options();

    public override string Print()
    {
        Clear();
        AppendLine($"[MidiPrinter v{Version}]").AppendLine().AppendLine();

        AppendLine($"Duration: {Math.Truncate(midi.Duration.TotalSeconds)}");
        AppendLine($"TimeDivision: {midi.TimeDivision}");
        AppendLine($"LastVoiceEventTick: {midi.LastVoiceEventTick}");
        AppendLine($"KeyRange: {midi.KeyRange.Min}/{midi.KeyRange.Max}");
        AppendLine($"FirstNoteOn: {midi.FirstNoteOn}");
        AppendLine($"Loop: {midi.Loop.Start}/{midi.Loop.End}/{ToStr(midi.Loop.Type)}");
        AppendLine($"Format: {(int)midi.Format}");
        AppendLine($"BankOffset: {midi.BankOffset}");
        AppendLine($"IsKaraokeFile: {ToStr(midi.IsKaraokeFile)}");
        AppendLine($"IsMultiPort: {ToStr(midi.IsMultiPort)}");
        AppendLine($"IsDLSRMIDI: {ToStr(midi.IsDLSRMIDI)}");
        AppendLine($"RMidi Info: {midi.RmidiInfo.Count}");

        if (midi.GetRMidiInfo() is { } rmidiInfo)
        {
            if (rmidiInfo.Name != null)
                AppendLine($"  Name: {rmidiInfo.Name}");
            if (rmidiInfo.Engineer != null)
                AppendLine($"  Engineer: {rmidiInfo.Engineer}");
            if (rmidiInfo.Artist != null)
                AppendLine($"  Artist: {rmidiInfo.Artist}");
            if (rmidiInfo.Album != null)
                AppendLine($"  Album: {rmidiInfo.Album}");
            if (rmidiInfo.Genre != null)
                AppendLine($"  Genre: {rmidiInfo.Genre}");
            if (rmidiInfo.Picture is {} pic)
            {
                var hash = XxHash3.HashToUInt64(pic);
                AppendLine($"  Picture: (Len: {pic.Count}, XxHash3: {hash})");
            }
            if (rmidiInfo.Comment != null)
                AppendLine($"  Comment: {rmidiInfo.Comment}");
            if (rmidiInfo.CreationDate is {} cDate)
                AppendLine($"  CreationDate: {ToISOString(cDate)}");
            if (rmidiInfo.Copyright != null)
                AppendLine($"  Copyright: {rmidiInfo.Copyright}");
            if (rmidiInfo.InfoEncoding != null)
                AppendLine($"  InfoEncoding: {rmidiInfo.InfoEncoding}");
            if (rmidiInfo.MidiEncoding != null)
                AppendLine($"  MidiEncoding: {rmidiInfo.MidiEncoding}");
            if (rmidiInfo.Software != null)
                AppendLine($"  Software: {rmidiInfo.Software}");
            if (rmidiInfo.Subject != null)
                AppendLine($"  Subject: {rmidiInfo.Subject}");
        }
        
        AppendLine($"PortChannelOffsetMap: {
            midi.PortChannelOffsetMap.Count} [{string.Join(',', midi.PortChannelOffsetMap)}]");
        AppendLine($"TempoChanges: {midi.TempoChanges.Count}");
        AppendLine("[");
        foreach (var c in midi.TempoChanges)
            AppendLine($"  (Te {c.Tempo:F1}, Ti {c.Ticks}),");
        AppendLine("]");
        AppendLine();

        // Embedded
        if (midi.EmbeddedSoundBank is not {} esb)
            AppendLine("EmbeddedSoundBank: no");
        else
        {
            var hash = XxHash3.HashToUInt64(esb);
            AppendLine($"EmbeddedSoundBank(Len: {esb.Count}, XxHash3: {hash})");
        }
        
        // Tracks
        for (var i = 0; i < midi.Tracks.Count; i++)
        {
            var t = midi.Tracks[i];
            AppendLine($"[Track {i} L:{t.Events.Length}] {t.Name}");

            var channelsStr = "";
            var chanLen = 0;
            for (var c = 0; c < 16; c++)
            {
                if (!t.Channels.Get(c)) continue;
                channelsStr += c + ",";
                chanLen++;
            }

            if (channelsStr == "") channelsStr = ",";

            AppendLine($"(Port: {t.Port}, Channels: {chanLen} [{
                channelsStr[..^1]}])");

            for (var j = 0; j < t.Events.Length; j++)
            {
                var m = t.Events[j];
                AppendLine($"{j}: {m.ToString()[4..]}");
            }

            AppendLine();
        }

        AppendLine("ExtraMetadata:");
        for (var index = 0; index < midi.ExtraMetadata.Count; index++)
        {
            var m = midi.ExtraMetadata[index];
            AppendLine($"{index}: {m.ToString()[4..]}");
        }

        AppendLine("Lyrics:");
        for (var index = 0; index < midi.Lyrics.Count; index++)
        {
            var m = midi.Lyrics[index];
            AppendLine($"{index}: {m.ToString()[4..]}");
        }
        
        var result = GetString();
        Clear();
        
        return result;
    }
}