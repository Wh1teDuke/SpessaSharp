using System.CommandLine;
using System.CommandLine.Invocation;
using SpessaSharp.Sequencer;
using SpessaSharp.Synthesizer;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;
using SSTool.Actions;

namespace SSTool.Cmd;

public static class ArgCommands
{
    private enum BoolType { True, False }

    public const int VERSION = 0;
    
    public static void Eval(string[] args)
    {
        if (args.Length == 0) args = ["--help"];

        ReadOnlySpan<Func<Command>> cmdList = 
            [Convert, Dump, Info, Play, Tour,];

        var root = Root();
        foreach (var cmd in cmdList)
            root.Subcommands.Add(cmd());
        
        var plResult = root.Parse(args);

        Environment.ExitCode = plResult.Invoke();
    }

    private static RootCommand Root()
    {
        var cmd = new RootCommand("SpessaSharp");
        
        // Override defaults
        foreach (var opt in cmd.Options)
        {
            if (opt is VersionOption)
                opt.Action = new CustomVersion();
        }

        return cmd;
    }

    private static Command Play()
    {
        // Play
        var cmd = new Command(
            "play",
            "Play a midi file")
        {
            Aliases = { "p" },
        };

        var getFiles = MidiMaybeSoundBank(cmd);
        var setRes = AddMasterParameterOptions(cmd);
        
        var oLoop = new Option<bool>("--loop", "-l")
        { Description = "Loop the midi", };

        var oGui = new Option<ActionPlay.GuiMode>("--gui")
        {
            Description = "GUI Mode",
            DefaultValueFactory = _ => ActionPlay.GuiMode.Full,
        };

        var oVST = new Option<FileInfo?>("--vst")
        {
            Description = "VST3 plugin path",
            Required = false,
        };
        
        cmd.Options.Add(oLoop);
        cmd.Options.Add(oGui);
        cmd.Options.Add(oVST);

        cmd.SetAction(pr =>
        {
            var (midi, sb) = getFiles(pr);
            var vst = pr.GetValue(oVST);
            var gui = pr.GetValue(oGui);
            ActionPlay.This(Setup, midi, sb, vst, gui);

            return;
            void Setup(SpessaSharpSequencer seq)
            {
                if (pr.GetValue(oLoop)) seq.LoopCount = 777;
                setRes(seq.Synth, pr);
            }
        });

        // Return
        return cmd;
    }
    
    private static Command Tour()
    {
        // Play
        var cmd = new Command(
            "tour",
            "Gives a tour through the instruments of the provided sound bank")
        {
            Aliases = { "t" },
        };

        var aIn = new Argument<FileInfo>("sb")
        {
            Description = "Sound bank",
            Arity = ArgumentArity.ExactlyOne,
        };
            
        cmd.Arguments.Add(aIn);
        
        cmd.SetAction(pr =>
        {
            var file = pr.GetRequiredValue(aIn);
            ActionTour.Of(file);
        });

        // Return
        return cmd;
    }

    private static Command Convert()
    {
        // Convert
        var cmd = new Command(
            "convert",
            "convert input files to provided format")
        {
            Aliases = { "c" },
        };
        
        var getFiles = MidiAndOrSoundBank(cmd);

        var oExt = new Option<string>("--output", "-o")
        {
            Description = "Output extension",
            Required = true,
        };
        
        cmd.Options.Add(oExt);
        var setRes = AddMasterParameterOptions(cmd);

        cmd.SetAction(pr =>
        {
            var (file, sb) = getFiles(pr);
            var ext = pr.GetRequiredValue(oExt);
            
            ActionConvert.To(Setup, file, sb, ext);
            
            return;
            void Setup(SpessaSharpProcessor proc) => setRes(proc, pr);
        });

        // Return
        return cmd;
    }

    private static Command Dump()
    {
        // Dump
        var cmd = new Command(
            "dump",
            "Dumps midi and sound bank files into a string representation for debugging purposes")
        {
            Aliases = { "d" },
        };

        var aIn = new Argument<FileInfo>("file")
        {
            Description = "File to dump",
            Arity = ArgumentArity.ExactlyOne,
        };

        var oOut = new Option<FileInfo?>("--output", "-o");
        
        cmd.Arguments.Add(aIn);
        cmd.Options.Add(oOut);
        
        cmd.SetAction(pr =>
        {
            var file = pr.GetRequiredValue(aIn);
            ActionDump.This(file, pr.GetValue(oOut));
        });
        
        // Return
        return cmd;
    }
    
    private static Command Info()
    {
        // INFO
        var cmd = new Command(
            "info",
            "Show information about a midi or soundbank file")
        {
            Aliases = { "i" },
        };

        var aIn = new Argument<FileInfo>("file")
        {
            Description = "File to get info",
            Arity = ArgumentArity.ExactlyOne,
        };
        
        cmd.Arguments.Add(aIn);
        
        cmd.SetAction(pr =>
        {
            var file = pr.GetRequiredValue(aIn);
            ActionInfo.Of(file);
        });
        
        // Return
        return cmd;
    }

    private static Func<ParseResult, (FileInfo, FileInfo?)> 
        MidiMaybeSoundBank(Command cmd)
    {
        // Required: Midi
        // Optional: Sound bank
        var aMidi = new Argument<FileInfo>("midi")
        {
            Description = "Midi file",
            Arity = ArgumentArity.ZeroOrOne,
        };
        
        var oMidi = new Option<FileInfo?>("--midi", "-m")
        {
            Description = "Midi file",
        };
        
        var oSb = new Option<FileInfo?>("--soundbank")
        {
            Description = "SoundFont or DLS file",
        };
        
        var aSb = new Argument<FileInfo>("soundbank") 
        { 
            Description = "SoundFont or DLS file",
            Arity = ArgumentArity.ZeroOrOne,
        };
        
        cmd.Arguments.Add(aMidi);
        cmd.Arguments.Add(aSb);
        cmd.Options.Add(oMidi);
        cmd.Options.Add(oSb);
        
        cmd.Validators.Add(result =>
        {
            var hasFile = 
                result.GetValue(oMidi) is not null || 
                result.GetValue(aMidi) is not null;

            if (!hasFile)
                result.AddError("A file argument must be provided.");
        });

        return pr =>
        {
            var mid = pr.GetValue(oMidi) ?? pr.GetValue(aMidi);
            var sb = pr.GetValue(oSb) ?? pr.GetValue(aSb);
            return (mid!, sb);
        };
    }
    
    private static Func<ParseResult, (FileInfo?, FileInfo?)> 
        MidiAndOrSoundBank(Command cmd)
    {
        // Optional: Midi
        // Optional: Sound bank
        // At least, one of them must be present
        
        var aMidi = new Argument<FileInfo?>("midi")
        {
            Description = "Midi file",
            Arity = ArgumentArity.ZeroOrOne,
        };
        
        var oMidi = new Option<FileInfo?>("--midi", "-m")
        {
            Description = "Midi file",
        };
        
        var oSb = new Option<FileInfo?>("--soundbank")
        {
            Description = "SoundFont or DLS file",
        };
        
        var aSb = new Argument<FileInfo?>("soundbank") 
        { 
            Description = "SoundFont or DLS file",
            Arity = ArgumentArity.ZeroOrOne,
        };
        
        cmd.Arguments.Add(aMidi);
        cmd.Arguments.Add(aSb);
        cmd.Options.Add(oMidi);
        cmd.Options.Add(oSb);
        
        cmd.Validators.Add(result =>
        {
            var hasFile = 
                result.GetValue(oMidi) is not null || 
                result.GetValue(aMidi) is not null || 
                result.GetValue(oSb) is not null || 
                result.GetValue(aSb) is not null;

            if (!hasFile)
                result.AddError("At least one file argument must be provided.");
        });

        return pr =>
        {
            var mid = pr.GetValue(oMidi) ?? pr.GetValue(aMidi);
            var sb = pr.GetValue(oSb) ?? pr.GetValue(aSb);
            return (mid, sb);
        };
    }

    private static Action<SpessaSharpProcessor, ParseResult> 
        AddMasterParameterOptions(Command cmd)
    {
        var list = new List<Option>();
        
        foreach (var type in GlobalSystemParameter.List)
        {
            var name = $"--{GlobalSystemParameter.Names[(int)type]}";

            // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
            Option opt = GlobalSystemParameter.TypeOf(type) switch
            {
                Params.Type.InterpolationType =>
                    new Option<Synthesizer.InterpolationType?>(name)
                    {
                        DefaultValueFactory = 
                            _ => GlobalSystemParameters.Default.Get(type).AsInterpolationType,
                        Description = GlobalSystemParameter.Descriptions[(int)type],
                    },
                Params.Type.Int =>
                    new Option<int?>(name)
                    {
                        DefaultValueFactory = 
                            _ => GlobalSystemParameters.Default.Get(type).AsInt,
                        Description = GlobalSystemParameter.Descriptions[(int)type],
                    },
                Params.Type.Float =>
                    new Option<float?>(name)
                    {
                        DefaultValueFactory = 
                            _ => GlobalSystemParameters.Default.Get(type).AsFloat,
                        Description = GlobalSystemParameter.Descriptions[(int)type],
                    },
                Params.Type.Bool =>
                    new Option<bool?>(name)
                    {
                        DefaultValueFactory =
                            _ => GlobalSystemParameters.Default.Get(type).AsBool,
                        Description = GlobalSystemParameter.Descriptions[(int)type],
                    },
                _ => throw new ArgumentOutOfRangeException()
            };

            cmd.Options.Add(opt);
            list.Add(opt);
        }
        
        return (proc, pr) =>
        {
            foreach (var type in GlobalSystemParameter.List)
            {
                var opt = list[(int)type];
                var op = pr.GetResult(opt);
                if (op?.Implicit ?? true) continue;

                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (GlobalSystemParameter.TypeOf(type))
                {
                    case Params.Type.InterpolationType:
                        var interType = op.GetValue(
                            (Option<Synthesizer.InterpolationType?>)opt);
                        if (interType != null)
                            proc.Set(interType.Value);
                        break;
                    case Params.Type.Int:
                        var valInt = op.GetValue((Option<int?>)opt);
                        if (valInt != null)
                            proc.Set((type, valInt.Value));
                        break;
                    case Params.Type.Float:
                        var valFloat = op.GetValue((Option<float?>)opt);
                        if (valFloat != null)
                            proc.Set((type, valFloat.Value));
                        break;
                    case Params.Type.Bool:
                        var valBool = op.GetValue((Option<bool?>)opt);
                        if (valBool != null)
                            proc.Set((type, valBool.Value));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        };
    }
    
    private sealed class CustomVersion : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult pr)
        {
            var Out = pr.InvocationConfiguration.Output;
            Out.WriteLine(VERSION.ToString());
            return 0;
        }
    }

    private static Option<BoolType> NewFlag(string name) => new (name)
    {
        DefaultValueFactory = _ => BoolType.True,
        Arity = ArgumentArity.ZeroOrOne,
    };
}