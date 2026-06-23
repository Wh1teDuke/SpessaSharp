using SFML.Audio;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.SoundBank.SoundFont;
using SpessaSharp.Synthesizer;
using SpessaSharp.Utils;
using SSTool.Util;

namespace SSTool.Actions;

public static class ActionConvert
{
    public static void To(
        Action<SpessaSharpProcessor> setup,
        FileInfo? file1,
        FileInfo? file2,
        string extension)
    {
        if (file1 == null && file2 == null)
            Etc.Error("Must provide at least a midi or soundbank file");
        
        var inputFile = (file1 ?? file2)!;
        var outputName = Path.GetFileNameWithoutExtension(inputFile.Name);
        if (extension.StartsWith('.'))
            extension = extension[1..];
        
        if (extension.Contains('.'))
        {
            outputName = extension;
            extension = Path.GetExtension(extension)[1..];
        }
        else
            outputName = $"{outputName}.{extension}";
        
        if (file1 != null)
            Console.WriteLine("Input A: " + file1.FullName);
        if (file2 != null)
            Console.WriteLine("Input B: " + file2.FullName);
        Console.WriteLine("Format:  " + extension);
        
        if (file1 is { Exists: false })
            Etc.Error($"File '{file1.FullName}' doesn't exist");
        if (file2 is { Exists: false })
            Etc.Error($"File '{file2.FullName}' doesn't exist");
        
        switch (extension.ToLowerInvariant())
        {
            case "rmi":
            {
                if (file1 == null)
                    Etc.Error("Must provide a midi and a soundbank");
                
                var mid = Midi.From(file1);
                var sb = Etc.GetSoundBank(file1, file2).Item1;
                sb.Trim(mid);
                var binData = mid.WriteRMIDI(sb.WriteSF2());
                File.WriteAllBytes(outputName, binData);
                break;
            }
            case var ex and ("wav" or "ogg" or "flac"):
            {
                if (file1 == null)
                    Etc.Error("Must provide a midi and a soundbank");

                var wav = Util.Convert.ToWav(file1, file2, setup);

                if (ex == "wav")
                {
                    File.WriteAllBytes(outputName, wav);    
                }
                else
                {
                    using var buffer = new SoundBuffer(wav);
                    if (!buffer.SaveToFile(outputName))
                        Etc.Error("There was some error converting the file");
                }
                
                break;
            }

            case var ex and ("sf2" or "sf3" or "sf4" or "dls"):
            {
                SoundBank? sb = null;
                var file = inputFile;

                switch (SpessaUtil.WhatIs(file))
                {
                    case SpessaUtil.FileKind.Midi:
                    case SpessaUtil.FileKind.EmbeddedMidi:
                    {
                        var mid = Midi.From(file);
                        if (mid.EmbeddedSoundBank == null)
                            Etc.Error("Must provide a soundbank or rmidi with embedded soundfont");
                    
                        sb = SoundBank.From(
                            mid.EmbeddedSoundBank.Value);
                        break;
                    }
                    case SpessaUtil.FileKind.SoundBank:
                    {
                        sb = SoundBank.From(file);
                        break;
                    }
                    default:
                        Etc.Error($"Unknown or unsupported file format '{
                            file.Extension}'");
                        break;
                }
                
                if (ex == "sf3")
                    Etc.Warn("Vorbis encoding not supported");

                var fileInfo = new FileInfo(outputName);
                switch (ex)
                {
                    case "sf2" or "sf3" or "sf4":
                    {
                        var opt = SF2WriteOptions.Default;
                        //if (ex is not "sf2") opt = opt with { Compress = true };
                    
                        if (ex is "sf4") 
                            sb.WriteSFE(
                                fileInfo,
                                SFEWriteOptions.Default with { Base = opt });
                        else 
                            sb.WriteSF2(fileInfo, opt);
                        break;
                    }
                    
                    case "dls":
                    {
                        sb.WriteDLS(fileInfo);
                        break;
                    }
                }

                break;
            }

            default:
                Etc.Error("Unknown or unsupported output extension");
                break;
        }
        
        Console.WriteLine();
        Console.WriteLine($"File written to '{outputName}'");
    }
}