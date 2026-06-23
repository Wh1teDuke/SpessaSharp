using System.Diagnostics.CodeAnalysis;
using SpessaSharp.SoundBank;
using SpessaSharp.Utils;


namespace SSTool.Util;

public static class Etc
{
    [DoesNotReturn]
    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("ERROR: " + message);
        Console.ResetColor();
        Environment.Exit(1);   
    }
    
    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("WARN: " + message);
        Console.ResetColor();
    }
    
    public static FileInfo? FindSoundBank(FileInfo midi)
    {
        FileInfo? soundBank = null;

        var midiName = Path.GetFileNameWithoutExtension(midi.Name);

        // Use the first available soundbank
        var dir =
            midi.Directory ?? new DirectoryInfo(Environment.CurrentDirectory);

        foreach (var file in dir.GetFiles())
        {
            if (SpessaUtil.WhatIs(file) is not SpessaUtil.FileKind.SoundBank)
                continue;

            // Bigger better, ignores compressed banks but skips tiny banks like vgmtrans outputs.
            if (soundBank == null || soundBank.Length < file.Length)
                soundBank = file;
            // Stop on perfect match
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(file.Name),
                    midiName,
                    StringComparison.OrdinalIgnoreCase))
                return file;
        }
        
        return soundBank;
    }

    public static (SoundBank, FileInfo?) GetSoundBank(
        FileInfo fileMidi, FileInfo? fileSoundBank)
    {
        if (fileSoundBank != null)
        {
            if (!fileSoundBank.Exists)
                Error($"Sound bank '{fileSoundBank.FullName}' does not exist");
            return (SoundBank.From(fileSoundBank), fileSoundBank);
        }

        var sb = FindSoundBank(fileMidi);
        return (
            sb?.Exists ?? false
                ? SoundBank.From(sb)
                : SoundBank.From(DefaultSoundBank.Get()), 
            sb);
    }

    public readonly record struct PlayablePreset(
        BasicPreset Preset,
        (int Min, int Max) Key, 
        BasicInstrument.Zone[] Samples);

    public static List<PlayablePreset> GetPresetPlayableData(SoundBank sb)
    {
        var filter = new HashSet<(BasicInstrument, string, (int, int))>();
        var result = new List<PlayablePreset>();

        foreach (var preset in sb.Presets)
        {
            var range = (Min: int.MaxValue, Max: int.MinValue);
            foreach (var zone2 in preset.Zones.SelectMany(
                         zone1 => zone1.Instrument.Zones.Where(zone2 =>
                             zone2.Basic.HasKeyRange)))
            {
                range.Min = Math.Min(range.Min, zone2.Basic.KeyRange.Min);
                range.Max = Math.Max(range.Max, zone2.Basic.KeyRange.Max);
            }

            if (range == (int.MaxValue, int.MinValue)) continue;
            
            filter.Clear();
            var samples = !preset.IsDrum 
                ? []
                : preset.Zones
                .SelectMany(z1 => z1.Instrument.Zones)
                .Where(z => filter.Add(
                    (z.ParentInstrument, z.Sample.Name, z.Basic.KeyRange)))
                .Where(z => (z.Basic.HasKeyRange
                    ? z.Basic.KeyRange
                    : z.ParentInstrument.GlobalZone.KeyRange).Min != -1)
                .Distinct()
                .OrderBy(z => z.Basic.KeyRange.Min)
                .ThenBy(z => z.ParentInstrument.Name)
                .ToArray();

            result.Add(new PlayablePreset(preset, range, samples));
        }

        return result.OrderBy(pp => pp.Preset.IsDrum).ToList();
    }

    #region Vorbis
    public static ArraySegment<float> DecodeVorbis(ArraySegment<byte> data) =>
        SfmlUtil.DecodeVorbis(data);
    #if false
    // https://github.com/SteveLillis/.NET-Ogg-Vorbis-Encoder/blob/master/OggVorbisEncoder.Example/Encoder.cs
    public static ArraySegment<byte> EncodeVorbis(
        ArraySegment<float> audioData, int sampleRate)
    {
        // TODO: Quality
        const int writeBufferSize = 512;
        var channels = 1;
        var data = new[] {audioData.Array! };
        
        using var outputData = new MemoryStream();
        var info = VorbisInfo.InitVariableBitRate(
            channels, sampleRate, 0.5f);

        var serial = Random.Shared.Next();
        var oggStream = new OggStream(serial);

        var comments = new Comments();
        var infoPacket = HeaderPacketBuilder.BuildInfoPacket(info);
        var commentsPacket = HeaderPacketBuilder.BuildCommentsPacket(comments);
        var booksPacket = HeaderPacketBuilder.BuildBooksPacket(info);

        oggStream.PacketIn(infoPacket);
        FlushPages(oggStream, outputData, true);
        
        oggStream.PacketIn(commentsPacket);
        oggStream.PacketIn(booksPacket);

        FlushPages(oggStream, outputData, true);
        
        var processingState = ProcessingState.Create(info);

        for (var readIndex = 0; readIndex < audioData.Count; readIndex += writeBufferSize)
        {
            var length = Math.Min(writeBufferSize, audioData.Count - readIndex);
            processingState.WriteData(data, length, readIndex + audioData.Offset);

            while (!oggStream.Finished && processingState.PacketOut(out var packet))
            {
                oggStream.PacketIn(packet);
                FlushPages(oggStream, outputData, false);
            }
        }

        processingState.WriteEndOfStream();
        while (!oggStream.Finished && processingState.PacketOut(out var packet))
        {
            oggStream.PacketIn(packet);
            FlushPages(oggStream, outputData, false);
        }

        FlushPages(oggStream, outputData, true);

        outputData.Seek(0, SeekOrigin.Begin);
        return outputData.ToArray();
        
        void FlushPages(OggStream oggStream, Stream output, bool force)
        {
            while (oggStream.PageOut(out var page, force))
            {
                output.Write(page.Header, 0, page.Header.Length);
                output.Write(page.Body, 0, page.Body.Length);
            }
        }
    }
    #endif
    #endregion
}