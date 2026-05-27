using System.Text;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.SoundBank;
using SpessaSharp.Utils;
using SSTool.Util;

namespace SSTool.Actions;

public static class ActionInfo
{
    public static void Of(FileInfo file)
    {
        switch (SpessaUtil.WhatIs(file))
        {
            case SpessaUtil.FileKind.Midi:
            case SpessaUtil.FileKind.EmbeddedMidi:
                Etc.Error("Not Implemented yet");
                break;
            
            case SpessaUtil.FileKind.SoundBank:
                Of(SoundBank.From(file), file);
                break;

            default:
                Etc.Error("Unsupported file format");
                break;
        }
    }

    public static void Of(SoundBank sb, FileInfo file)
    {
        var str = new StringBuilder();
        str.AppendLine("[Sound Bank]");

        /* Info */
        {
            var i = sb.Info;
            str.AppendLine("<Info>");
            str.AppendLine($"File:         {file.FullName}");
            str.AppendLine($"Name:         {i.Name}");
            str.AppendLine($"Engineer:     {i.Engineer ?? "-"}");
            str.AppendLine($"Version:      {i.Version.Major}.{i.Version.Minor}");
            str.AppendLine($"Date:         {i.CreationDate:s}");
            str.AppendLine($"Subject:      {i.Subject ?? "-"}");
            if (i.Comment?.Contains('\n') ?? false)
                str.AppendLine("Comment:").AppendLine(i.Comment).AppendLine();
            else
                str.AppendLine($"Comment:      {i.Comment ?? "-"}");
            str.AppendLine($"Software:     {i.Software ?? "-"}");
            str.AppendLine($"Product:      {i.Product ?? "-"}");
            str.AppendLine($"Sound Engine: {i.SoundEngine ?? "-"}");
            str.AppendLine($"ROM Info:     {i.RomInfo ?? "-"}");
            str.AppendLine($"Copyright:    {i.Copyright ?? "-"}");
        }

        /* Preset */
        {
            str.AppendLine();
            str.AppendLine("<Preset>");

            InstrumentInfo.Family? fam = null;
            BasicInstrument.Zone[]? prevZones = null;
            var presets = Etc.GetPresetPlayableData(sb);

            for (var i = 0; i < presets.Count; i++)
            {
                var (preset, range, samples) = presets[i];

                const int padName = 40;
                const int padCanName = 28;

                var patch = preset.Patch with
                {
                    Data = preset.Patch.Data with
                    {
                        IsGMGSDrum = preset.IsDrum
                    }
                };

                var pFam = InstrumentInfo.GetFamily(patch.Data);

                if (samples.AsSpan().IsEmpty && fam != pFam)
                    str.AppendLine().AppendLine($">{InstrumentInfo.ToString(pFam)}");

                if (!preset.IsDrum)
                {
                    var pName = InstrumentInfo.ToString(
                        InstrumentInfo.GetMelodic(patch.Data));
                    var cannonicalName = $"({pName})";
                    str.Append(cannonicalName);
                    str.Append(' ', Math.Max(1, padCanName - cannonicalName.Length));
                }

                var presetStr = $"{(patch.IsGMGSDrum
                    ? $"#  DRUMS:{patch.Program:000}  "
                    : $"{patch.BankLSB:000}:{patch.BankMSB:000}:{
                        patch.Program:000}")} {patch.Name}";

                if (samples.Length != 0 && (prevZones == null || prevZones.Length == 0))
                    str.AppendLine();
                
                prevZones = samples;

                str.Append(presetStr);

                var pad = Math.Max(
                    1,
                    padName + (preset.IsDrum ? padCanName : 0) - presetStr.Length);

                str.Append(' ', pad);
                str.Append($"Key = {range.Min}-{range.Max}");

                str.AppendLine();

                fam = pFam;

                if (i != presets.Count - 1 && 
                    samples.AsSpan().SequenceEqual(presets[i + 1].Samples))
                    continue;

                foreach (var z in samples)
                {
                    var sRan = z.Basic.HasKeyRange
                        ? z.Basic.KeyRange
                        : z.ParentInstrument.GlobalZone.KeyRange;

                    var s = z.Sample;
                    var tube = z == samples[^1] ? '└' : '│';
                    str.Append($"   {tube} ");

                    const int padDrumCanName = 23;

                    var canName =
                        InstrumentInfo.TryGetDrum(sRan.Min) is { } drum
                            ? $"({InstrumentInfo.ToString(drum)})"
                            : "(???)";

                    str.Append(canName);
                    str.Append(' ', Math.Max(1, padDrumCanName - canName.Length));
                    str.Append($"{z.ParentInstrument.Name}/{s.Name}");
                    var nameLen =
                        1 + z.ParentInstrument.Name.Length + s.Name.Length;

                    str.Append(' ', Math.Max(1, padName - nameLen));
                    str.Append($"Key = {sRan.Min}");
                    str.AppendLine();
                }

                if (!samples.AsSpan().IsEmpty)
                    str.AppendLine();
            }
        }

        /* Flush */
        foreach (var chunk in str.GetChunks())
            Console.Write(chunk.Span);
    }
}