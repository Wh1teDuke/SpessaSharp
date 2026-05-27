using System.Text;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Sequencer;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer;
using SSTool.Util;

namespace SSTool.Actions;

public static class ActionTour
{
    public static void Of(FileInfo file)
    {
        Console.WriteLine("SoundBank: " + file.FullName);
        
        if (!file.Exists)
            Etc.Error($"Sound bank '{file.FullName}' not found");

        var soundBank = SoundBank.From(file.OpenRead());
        
        // Preprocess presets
        var dataList = new List<(
            bool IsDrums, 
            (int Min, int Max) KeyRange,
            BasicPreset Preset,
            BasicInstrument.Zone? Drum
        )>();

        var usedZones = new HashSet<BasicInstrument.Zone>();
        foreach (var (preset, keyRange, samples) in 
            Etc.GetPresetPlayableData(soundBank))
        {
            if (samples.Length == 0)
            {
                dataList.Add((preset.IsDrum, keyRange, preset, null));
                continue;
            }

            foreach (var sample in samples)
            {
                if (!usedZones.Add(sample)) continue;
                dataList.Add((true, keyRange, preset, sample));
            }
        }

        var data = dataList.ToArray();
        var pointer = 0;

        var processor = new SpessaSharpProcessor(44_100);
        var sequencer = new SpessaSharpSequencer(processor);
        processor.SoundBankManager.Add(soundBank, "main");
        sequencer.LoadNewSongList([]);
        
        var player = new Player(sequencer);
        Console.CursorVisible = false;
        var sb = new StringBuilder();

        const string helpMsg = 
            """
            [<Q|Esc> Quit, <space> Skip, <1,2> Speed             ]
            [<3,4> Prev/Next Instrument, <5,6> Prev/Next Category]
            """;
        
        GC.Collect();
        GC.WaitForPendingFinalizers();

        player.Volume *= 2;
        player.Play();
        ReadOnlySpan<int> keys = [0, 2, 4, 5, 7, 9, 11, 12];
        var window = 2;
        var speed = 280;//ms
        var chan = 0;
        var i = 0;
        var oldNote = -1;
        var oldChan = -1;
        
        var dataLock = new Lock();
        var skipData = false;
        var move = 0;
        var moveCat = 0;

        // Input
        ThreadPool.QueueUserWorkItem(t =>
        {
            while (true)
            {
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(1);
                    continue;
                }

                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (Console.ReadKey(true).Key)
                {
                    // Quit
                    case ConsoleKey.Q or ConsoleKey.Escape:
                        Environment.Exit(0);
                        return;
                    // Skip
                    case ConsoleKey.Spacebar:
                    {
                        using var _ =  dataLock.EnterScope();
                        move = +1;
                        skipData = true;
                        continue;
                    }
                    // Speed
                    case var k and (ConsoleKey.D1 or ConsoleKey.D2):
                    {
                        using var _ =  dataLock.EnterScope();
                        speed = Math.Clamp(speed + 25 * (k == ConsoleKey.D2 ? -1 : +1), 50, 1_000);
                        continue;
                    }
                    // Prev/Next Instrument
                    case var k and (ConsoleKey.D3 or ConsoleKey.D4):
                    {
                        using var _ =  dataLock.EnterScope();
                        move = k == ConsoleKey.D3 ? -1 : +1;
                        skipData = true;
                        continue;
                    }
                    // Prev/Next Family
                    case var k and (ConsoleKey.D5 or ConsoleKey.D6):
                    {
                        using var _ =  dataLock.EnterScope();
                        moveCat = k == ConsoleKey.D5 ? -1 : +1;
                        skipData = true;
                        continue;
                    }
                    default:
                        continue;
                }
            }
        });
        
        // Output
        var first = true;
        while (data.Length > pointer)
        {
            var s = 0;
            lock (dataLock) s = speed;
            
            Thread.Sleep(s);
            sb.Clear();

            var (isDrums, keyRange, preset, z) = data[pointer];
            var note = 0;
            
            if (i == 0 || isDrums)
                RefreshNames();

            if (!isDrums)
            {
                // Melodic
                var index = i++;
                if (index >= keys.Length)
                {
                    Next();
                    continue;
                }

                note = Math.Clamp(
                    60 + keys[index], keyRange.Min, keyRange.Max);
            }
            else
            {
                // Drum
                i++;
                note = z?.Basic.KeyRange.Min ?? 60;

                if (i > 3)
                {
                    Next();
                    continue;
                }
            }

            {
                if (oldNote != -1) 
                    player.NoteOff(oldChan, oldNote);
                
                if (!isDrums && chan % 16 == Synthesizer.DEFAULT_PERCUSSION)
                    chan = (chan + 1) % 16;

                player.Program(chan, preset.Patch.Data);
                player.NoteOn(chan, note, 65);
            }

            if (sb.Length != 0)
            {
                // Progress
                var c = pointer / (float)data.Length;
                var p1 = sb.Length;
                sb.Append($"[{pointer}/{data.Length} ({c * 100:F2}%)] ");
                var p2 = sb.Length;
                var l = Math.Min(Console.BufferWidth, 55 - (p2 - p1));
                sb.Append('=', Math.Max(0, (int)(c * l)));
                sb.Append('-', Math.Max(0, l - (int)(c * l) - 1));
                sb.AppendLine();
                sb.Append(helpMsg);
                var str = sb.ToString();
                
                if (!first)
                {
                    var nl = str.AsSpan().Count(Environment.NewLine);
                    Console.CursorLeft = 0;
                    Console.CursorTop -= nl;
                }
                first = false;
                
                Console.Write(str);
            }

            oldNote = note;
            oldChan = chan;
            
            Thread.Sleep(100 + (isDrums ? 50 : 0));
            
            using var _ = dataLock.EnterScope();
            if (skipData)
            {
                var cat1 = 
                    InstrumentInfo.GetFamily(preset.Patch.Data);

                while (moveCat != 0 &&
                       (pointer += moveCat) > 0 &&
                       pointer < data.Length)
                {
                    var cat2 = 
                        InstrumentInfo.GetFamily(data[pointer].Preset.Patch.Data);
                    if (cat2 != cat1) break;
                }

                Next(move);
            }
            skipData = false;
            move = 0;
            moveCat = 0;
            continue;

            void Next(int dir = +1)
            {
                pointer += dir;
                pointer = Math.Max(pointer, 0); 
                i = 0;
                chan = (chan + 1) % 16;
                Thread.Sleep(400);
            }
        }

        return;

        void RefreshNames()
        {
            sb.Clear();

            for (var i = pointer - window; i <= pointer + window; i++)
            {
                if (i >= 0 && i < data.Length)
                {
                    var p1 = sb.Length;
                    if (i == pointer) sb.Append('→');
                    AppendName(data[i]);
                    var p2 = sb.Length;
                    sb.Append(' ', Math.Max(0, 60 - (p2 - p1)));
                }
                else
                    sb.Append("---").Append(' ', Math.Max(0, 60 - 3));
                
                sb.AppendLine();
            }
        }

        void AppendName((
            bool IsDrums,
            (int Min, int Max) KeyRange,
            BasicPreset Preset,
            BasicInstrument.Zone? Drum
            ) entry)
        {
            var patch = entry.Preset.Patch.Data;
            if (!entry.IsDrums)
            {
                var (famStr, insStr) = (
                    InstrumentInfo.ToString(InstrumentInfo.GetFamily(patch)), 
                    InstrumentInfo.ToString(InstrumentInfo.GetMelodic(patch)));
                sb.Append($"[{famStr}/{insStr}] ").Append(entry.Preset);                
            }
            else
            {
                patch = entry.Preset.Patch.Data with 
                    { IsGMGSDrum = entry.Preset.IsDrum };
                
                var z = entry.Drum!.Value;
                var (famStr, insStr) = (
                    "Drum",
                    InstrumentInfo.TryGetDrum(z.Basic.KeyRange.Min) is {} drum ?
                        InstrumentInfo.ToString(drum) 
                        : "???");
                
                sb
                    .Append($"[{famStr}/{insStr}] ")
                    .Append($"{z.ParentInstrument.Name}/{z.Sample.Name}");
            }
        }
    }
}