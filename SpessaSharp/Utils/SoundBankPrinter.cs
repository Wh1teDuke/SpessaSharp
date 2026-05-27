using System.IO.Hashing;
using System.Text;
using SpessaSharp.SoundBank;
using SpessaSharp.SoundBank.DLS;
using SpessaSharp.SoundBank.SoundFont;

namespace SpessaSharp.Utils;

public sealed class SoundBankPrinter(
    SoundBank.SoundBank bank,
    StringBuilder? sb                   = null,
    SoundBankPrinter.Options? options   = null): BasePrinter(sb)
{
    private const int Version = 0;
    
    
    public readonly record struct Options(
        bool ReplaceSharpWithSynth  = true,
        bool IncludeVersion         = true,
        bool IncludeSourceEngine    = true,
        bool IncludeComment         = true,
        bool IncludeSubject         = true,
        bool IncludeProduct         = true,
        bool IncludeHash            = true
    );
    
    public Options Opts { get; init; } = 
        options ?? new Options(ReplaceSharpWithSynth: true);

    public override string Print()
    {
        Clear();
        AppendLine($"[SoundBankPrinter v{Version}]").AppendLine().AppendLine();
        
        // Info
        var info = bank.Info;
        var date =
            DateTime.UtcNow - info.CreationDate.ToUniversalTime()
                <= TimeSpan.FromSeconds(5)
                ? "Now" : ToISOString(info.CreationDate);
        
        AppendLine("[INFO]");
        AppendLine($"Name: {info.Name}");
        if (Opts.IncludeVersion)
            AppendLine($"Version: {info.Version.Major}.{info.Version.Minor}");
        AppendLine($"CreationDate: {date}");
        if (Opts.IncludeSourceEngine)
            AppendLine($"SoundEngine: {info.SoundEngine ?? "undefined"}");
        AppendLine($"Engineer: {info.Engineer ?? "undefined"}");
        if (Opts.IncludeProduct)
            AppendLine($"Product: {Filter(info.Product ?? "undefined")}");
        AppendLine($"Copyright: {info.Copyright ?? "undefined"}");
        if (Opts.IncludeComment)
            AppendLine($"Comment: {Filter(info.Comment ?? "undefined")}");
        if (Opts.IncludeSubject)
            AppendLine($"Subject: {(info.Subject ?? "undefined")}");
        AppendLine($"RomInfo: {info.RomInfo ?? "undefined"}");
        AppendLine($"Software: {Filter(info.Software ?? "undefined")}");
        AppendLine($"RomVersion: {(info.RomVersion == null
            ? "undefined"
            : $"{info.RomVersion.Value.Major}.{info.RomVersion.Value.Minor}")}");
        AppendLine($"CustomDefaultModulators: {ToStr(bank.CustomDefaultModulators)}");
        AppendLine($"IsXGBank: {ToStr(bank.IsXGBank)}");

        AppendLine();
        
        AppendLine($"Presets: {bank.Presets.Count}");
        AppendLine($"Instruments: {bank.Instruments.Count}");
        AppendLine($"Samples: {bank.Samples.Count}");
        AppendLine($"Default Modulators: {bank.DefaultModulators.Count}");
        AppendLine();

        // Presets
        Separator('#');
        AppendLine().AppendLine($"[Presets ({bank.Presets.Count})]");
        foreach (var p in bank.Presets)
        {
            Separator('-');
            AppendLine($"[{p.BankMSB:D3}:{p.BankLSB:D3}:{p.Program:D3}] {p.Name}");
            AppendLine($"Library: {p.Library}, Morphology: {p.Morphology
            }, Genre: {p.Genre}, AnyDrums: {ToStr(p.IsDrum)
            }, XGDrums: {ToStr(p.IsXGDrum)}, GM/GSDrum: {ToStr(p.IsGMGSDrum)}");
            AppendLine($"Zones ({p.Zones.Count}): {string.Join(", ", p.Zones.Select(z => $"'{z.Instrument.Name}'"))}");
            
            AddIndent("GlobalZone:");
            Append(p.GlobalZone);
            SubIndent();
            
            AddIndent("Zones:");
            foreach (var z in p.Zones)
            {
                AppendLine($"Instrument: {z.Instrument.Name}");
                Append(z.Basic);
            }
            SubIndent();
        }
        
        // Instruments
        Separator('#');
        AppendLine().AppendLine(
            $"[Instruments ({bank.Instruments.Count})]");
        foreach (var ins in bank.Instruments)
        {
            Separator('-');
            AppendLine($"{ins.Name}"); 
            AppendLine($"UseCount: {ins.UseCount}");
            AppendLine($"LinkedTo ({ins.LinkedTo.Count}): {
                string.Join(", ", ins.LinkedTo.Select(p => $"'{p.Name}'"))}");
            
            AddIndent("GlobalZone:");
            Append(ins.GlobalZone);
            SubIndent();

            AddIndent("Zones:");
            foreach (var z in ins.Zones)
            {
                AppendLine($"Sample: {z.Sample.Name}");
                Append(z.Basic);
            }
            SubIndent();
        }
        
        // Samples
        Separator('#');
        AppendLine().AppendLine($"[Samples ({bank.Samples.Count})]");
        foreach (var s in bank.Samples)
        {
            Separator('-');
            AppendLine($"{s.Name} (UseCount: {s.UseCount}, LinkedTo: {
                string.Join(", ", s.LinkedTo.Select(i => $"'{i.Name}'"))} ({s.LinkedTo.Count
                }), IsCompressed: {ToStr(s.IsCompressed)}, Linked: {s.LinkedSample?.Name ?? "-"
                }, LoopStart: {s.LoopStart}, LoopEnd: {s.LoopEnd}, OriginalKey: {s.OriginalKey
                }, PitchCorrection: {s.PitchCorrection}, Rate: {s.Rate}, Type: {BasicSample.ValueOf(s.SType)})");

            var raw = s.GetRawData(true);
            var hash = Opts.IncludeHash 
                ? XxHash3.HashToUInt64(raw)
                : 0;
            AppendLine($"RawData(Len: {raw.Count}, XxHash3: {hash})");
            
            if (s is SFSample sfS)
            {
                AppendLine($"[SFSample]: (LinkedSampleIndex: {sfS.LinkedSampleIndex})");
            }
            else if (s is DLSSample dlsS)
            {
                AppendLine($"[DLSSample]: (WFormatTag: {dlsS.WFormatTag}, BytesPerSample: {dlsS.BytesPerSample})");
            }
        }
        
        // Default Modulators
        Separator('#');
        AppendLine().AppendLine(
            $"[Default Modulators ({bank.DefaultModulators.Count})]");
        foreach (var m in bank.DefaultModulators)
        {
            Append(m);
            AppendLine();
        }

        var result = GetString();
        Clear();
        
        return result;
    }

    private void Append(Modulator mod)
    {
        Append($"Dst: {ToStr(mod.Destination)}, ");
        Append($"TT: {Modulator.ID(mod.TType)}, ");
        Append($"TA: {mod.TransformAmount}, ");
        Append($"PS: ({ToStr(mod.PrimarySource)}), ");
        Append($"SS: ({ToStr(mod.SecondarySource)})");
    }

    private void Append(Generator gen) => 
        Append($"{ToStr(gen.GType)}: {gen.Value}");

    private SoundBankPrinter Append(BasicZone zone)
    {
        AppendLine($"Vel: {zone.VelRange.Min}/{zone.VelRange.Max}");
        AppendLine($"Key: {zone.KeyRange.Min}/{zone.KeyRange.Max}");
        AppendLine($"FineTuning: {zone.FineTuning}");

        AddIndent($"MODs ({zone.Modulators.Count}):");
        foreach (var mod in zone.Modulators)
        {
            Append(mod);
            AppendLine();
        }
        IncIndent(-2);
        
        AddIndent($"GENs ({zone.Generators.Count}):");
        foreach (var gen in zone.Generators)
        {
            Append(gen);
            AppendLine();
        }
        IncIndent(-2);

        return this;
    }

    private string Filter(string s) =>
        Opts.ReplaceSharpWithSynth
            ? s.Replace("SpessaSharp", "SpessaSynth") : s;
    
    
    private void Separator(char c) => AppendLine(c, 80);
    private void AddIndent(string text) => AppendLine(text).IncIndent(+2);
    private void SubIndent() => IncIndent(-2).AppendLine();
}