using System.Runtime;
using System.Text;
using SpessaSharp.MIDI;
using SpessaSharp.Sequencer;
using SpessaSharp.Synthesizer;
using SpessaSharp.Synthesizer.Engine.Sysex;
using SSTool.Util;

namespace SSTool.Actions;

public static class ActionPlay
{
    public enum GuiMode { Full, None }

    public static void This(
        Action<SpessaSharpSequencer> setup,
        FileInfo fileMidi,
        FileInfo? fileSoundBank,
        GuiMode gui)
    {
        if (!fileMidi.Exists)
            Etc.Error($"Midi '{fileMidi.FullName}' not found");
        
        var (sb, fileSb) =
            Etc.GetSoundBank(fileMidi, fileSoundBank);
        
        Console.WriteLine("Midi:      " + fileMidi.FullName);
        Console.WriteLine("SoundBank: " + (fileSb?.FullName ?? "<Embedded GM.dls>"));

        var midi = Midi.From(fileMidi);

        var processor = new SpessaSharpProcessor(44_100);
        var sequencer = new SpessaSharpSequencer(processor);
        processor.SoundBankManager.Add(sb, "main");
        setup(sequencer);

        sequencer.LoadNewSongList([midi]);

        Console.WriteLine($"'{midi.GetName()}' with '{sb.Info.Name}'");
        
        var player = new Player(
            sequencer,
            bufferLen: TimeSpan.FromSeconds(.6));

        if (gui == GuiMode.None)
        {
            TriggerGC();
            player.Play();
            while (!player.Sequencer.IsFinished || player.VoiceCount > 0)
                Thread.Sleep(100);
            return;
        }

        Console.CursorVisible = false;
        var edInterval = Process<Synthesizer.InterpolationType>();
        var edReverb = Process<Macro.Reverb>();
        var edChorus = Process<Macro.Chorus>();
        var edDelay = Process<Macro.Delay>();

        // Shortened names
        for (var i = 0; i < edChorus.String.Length; i++)
        {
            edChorus.String[i] = edChorus.String[i].Replace("Chorus", "Chr");
            edChorus.Upper[i] = edChorus.Upper[i].Replace("CHORUS", "Chr");
        }
        
        for (var i = 0; i < edDelay.String.Length; i++)
        {
            edDelay.String[i] = edDelay.String[i].Replace("Delay", "Dly");
            edDelay.Upper[i] = edDelay.Upper[i].Replace("DELAY", "DLY");
        }

        var pos = Console.GetCursorPosition();
        var str = new StringBuilder();

        TriggerGC();

        player.Play();
        var loop = true;
        var clear = false;
        var renderLines = 0;

        while (loop)
        {
            PrintInfo();
            clear = true;
            
            const float volInc = .2f;
            const float panInc = .1f;
            const float seekInc = 5f; // Seconds

            if (!Console.KeyAvailable)
            {
                Thread.Sleep(250);
                continue;
            }
                
            var k = Console.ReadKey(true);

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (k.Key)
            {
                // Quit
                case ConsoleKey.Q or ConsoleKey.Escape:
                    loop = false;
                    continue;
                
                // Stop
                case ConsoleKey.S:
                    player.Stop();
                    break;
                
                // Play
                case ConsoleKey.A:
                    player.Play();
                    break;
                
                // Pause
                case ConsoleKey.D:
                    player.Pause();
                    break;

                // Vol
                case ConsoleKey.D1:
                    player.Volume -= volInc;
                    break;
                case ConsoleKey.D2:
                    player.Volume += volInc;
                    break;

                // Pan
                case ConsoleKey.D3:
                    player.Pan -= panInc;
                    break;
                case ConsoleKey.D4:
                    player.Pan += panInc;
                    break;
                
                // Seek
                case ConsoleKey.D5:
                    player.Time -= TimeSpan.FromSeconds(seekInc);
                    break;
                case ConsoleKey.D6:
                    player.Time += TimeSpan.FromSeconds(seekInc);
                    break;
                
                // Interpolation
                case ConsoleKey.D7:
                case ConsoleKey.D8:
                {
                    var d = k.Key - ConsoleKey.D7;
                    Set(
                        () => player.InterpolationType, 
                        i => player.InterpolationType = i,
                        edInterval.Values, d);
                    break;
                }
                
                // Reverb Macro
                case ConsoleKey.E:
                case ConsoleKey.R:
                {
                    var d = k.Key == ConsoleKey.E ? -1 : 1;
                    Set(
                        () => processor.ReverbMacro, 
                        i => processor.ReverbMacro = i,
                        edReverb.Values, d);
                    break;
                }
                
                // Chorus Macro
                case ConsoleKey.T:
                case ConsoleKey.Y:
                {
                    var d = k.Key == ConsoleKey.T ? -1 : 1;
                    Set(
                        () => processor.ChorusMacro, 
                        i => processor.ChorusMacro = i,
                        edChorus.Values, d);
                    break;
                }
                
                // Delay Macro
                case ConsoleKey.U:
                case ConsoleKey.I:
                {
                    var d = k.Key == ConsoleKey.U ? -1 : 1;
                    Set(
                        () => processor.DelayMacro, 
                        i => processor.DelayMacro = i,
                        edDelay.Values, d);
                    break;
                }

                default:
                    continue;
            }
        }
        
        Console.WriteLine();
        return;

        void Set<T>(Func<T> get, Action<T> set, T[] vals, int dir)
            where T: struct, Enum
        {
            var i = vals.IndexOf(get());
            i += dir > 0 ? +1 : -1;
            if (i < 0) i = vals.Length - 1;
            else if (i >= vals.Length) i = 0;
            set(vals[i]);
        }

        void PrintInfo()
        {
            if (clear)
                Console.SetCursorPosition(
                    pos.Left, 
                    Math.Max(0, Console.CursorTop - renderLines));

            renderLines = 0;
            str.Clear();
            var bLen = Console.BufferWidth;
            var strPos = 0;
            
            // Line *******************
            // Player status
            var status = player.Status;
            var pause = status == Player.PlayerStatus.Pause ? "PAUSE" : "Pause";
            var play = status == Player.PlayerStatus.Play ? "PLAY" : "Play";
            var stop = status == Player.PlayerStatus.Stop ? "STOP" : "Stop";
            str.Append($"[a] {play} [s] {stop} [d] {pause} ");
            
            // Voices count
            str.Append($"Voices: {player.VoiceCount} ");
            
            // Process Time
            str.Append($"Tick: {player.AvgProcess.TotalMilliseconds:F2}ms");

            NewLine();
            
            // Volume
            str.Append($"[1-2] Volume: {(int)Math.Round(player.Volume * 100)}% ");
            // Pan
            str.Append($"[3-4] Pan: {(int)Math.Round(player.Pan * 100)}% ");

            // Time
            var time = player.Time;
            str.Append($@"[5-6] Seek {time:mm\:ss}[");

            const int durLen = 10;
            var l = midi.Duration.Ticks;
            var c = time.Ticks;
            var p = c / (float)l;
            var dp = (int)Math.Round(p * durLen);

            var p1 = str.Length;
            str.Append('=', dp).Append('-', durLen - dp);

            if (sequencer.LoopCount != 0 &&
                ((midi.Loop.Start != 0 &&
                  midi.Loop.Start != midi.FirstNoteOn) ||
                 (midi.Loop.End != 0 &&
                   midi.Loop.End != midi.LastVoiceEventTick)) &&
                (int)DateTime.Now.TimeOfDay.TotalSeconds % 2 != 0)
            {
                var lastTick = (float)midi[midi.Timeline[^1]].Ticks;
                var loopStart = midi.Loop.Start / lastTick;
                var loopEnd = midi.Loop.End / lastTick;

                var ls = (int)(loopStart * durLen) - 1;
                var le = (int)(loopEnd * durLen) - 1;

                str[p1 + ls] = '(';
                str[p1 + le] = ')';
            }

            str.Append($@"]{midi.Duration:mm\:ss} ");

            NewLine();
            
            // Interpolation Type
            str.Append("[7-8] ");
            Set(edInterval, player.InterpolationType);

            NewLine();

            // Reverb
            str.Append("[E-R] ");
            Set(edReverb, processor.ReverbMacro);
            
            NewLine();

            // Chorus
            str.Append("[T-Y] ");
            Set(edChorus, processor.ChorusMacro);
            
            NewLine();

            // Delay
            str.Append("[U-I] ");
            Set(edDelay, processor.DelayMacro);

            NewLine();

            // Quit
            str.Append("Press [Q|Esc] to quit ...");
            str.Append(' ', Math.Max(0, bLen - (str.Length - strPos) - 1));
            
            foreach (var chunk in str.GetChunks())
                Console.Write(chunk.Span);

            return;
            
            void NewLine()
            {
                renderLines++;
                str.Append(' ', Math.Max(0, bLen - (str.Length - strPos)));
                strPos = str.Length;
            } 

            void Set<T>((T[] Values, string[] String, string[] Upper) enumData, T current)
                where T : struct, Enum
            {
                for (var i = 0; i < enumData.Values.Length; i++)
                {
                    var val = enumData.Values[i];
                    var name = enumData.String[i];
                    if (EqualityComparer<T>.Default.Equals(val, current))
                        name = enumData.Upper[i];

                    str.Append(name).Append(' ');
                }
            }
        }

        (T[] Values, string[] String, string[] Upper) Process<T>() 
            where T : struct, Enum
        {
            var vals = Enum.GetValues<T>();
            var names = vals.Select(e => e.ToString()).ToArray();
            var upper = names.Select(n => n.ToUpperInvariant()).ToArray();
            return (vals, names, upper);
        }

        static void TriggerGC()
        {
            GCSettings.LargeObjectHeapCompactionMode =
                GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}