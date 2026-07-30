using CSLua;
using CSLua.Extensions;
using CSLua.Parse;
using CSLua.Util;
using SpessaSharp.MIDI;
using SpessaSharp.Sequencer;
using SpessaSharp.Synthesizer;
using SpessaSharp.Utils;
using SSTool.Util;

namespace SSTool.Actions;

public static class ActionScript
{
    public static void Run(
        FileInfo? fileSoundBank,
        string? arg = null)
    {
        // Spessa
        var (sb, _) = Etc.GetSoundBank(fileSoundBank);
        
        var processor = new SpessaSharpProcessor(44_100);
        var sequencer = new SpessaSharpSequencer(processor);
        processor.SoundBankManager.Add(sb, "main");
        
        var player = new Player(sequencer);
        player.Play();
        player.Volume = 2;
        
        // Lua
        var loop = true;
        var L = Lua.New();
        L.OpenLibs();
        L.Open(SpessaSharpLib.NameFuncPair);
        SpessaSharpLib.Player = player;
        
        foreach (var q in (ReadOnlySpan<string>)["quit", "exit"])
            L.SetGlobal(q, Quit);

        // Prompt
        var prompt = new Prompt();
        if (arg == null)
        {
            Console.WriteLine("Call 'quit()' or press Esc to stop.");
            prompt.PrintSymbol();
        }
        
        prompt.Position = Console.GetCursorPosition();

        // Loop
        ActionPlay.TriggerGC();
        
        while (loop)
        {
            // Prompt
            var input = arg;
            
            if (input == null)
            {
                var k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Escape) break;
                
                Console.CursorVisible = false;
                input = prompt.ProcessInput(k)?.Trim();
                Console.CursorVisible = true;

                if (string.IsNullOrWhiteSpace(input)) continue;
            }

            if (arg != null) loop = false;

            try
            {
                Console.WriteLine();
                L.Eval(input);
            }
            catch (LuaRuntimeException e)
            {
                LuaRuntimeException? err = null;

                if (!input.Any(char.IsWhiteSpace) && e.Message.EndsWith(
                    "Syntax error: Expected VCALL, got VINDEXED"))
                {
                    try { L.Eval($"print({input})"); }
                    catch (LuaRuntimeException e2) { err = e2; }
                }
                else err = e;

                if (err != null)
                {
                    Console.CursorLeft = 0;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(err.Message);
                    Console.ResetColor();                    
                }
            }
            finally
            {
                if (loop)
                {
                    prompt.PrintSymbol();
                    prompt.Position = Console.GetCursorPosition();                    
                }
            }
        }
        
        if (arg != null) while (true)
        {
            Thread.Sleep(50);
            if (player.VoiceCount == 0) break;
        }

        return;
        
        void Quit() => loop = false;
    }

    private sealed class Prompt
    {
        private string _buffer = "";
        private readonly List<string> _history = [];
        private int _historyIndex;
        
        public (int L, int T) Position;
        public string Symbol = "> ";
        
        public string? ProcessInput(ConsoleKeyInfo key)
        {
            var w = Console.BufferWidth;

            // Modify Buffer
            var doClear = false;
            if (!char.IsControl(key.KeyChar))
            {
                _buffer += key.KeyChar;
            }
            else if (key.Key == ConsoleKey.Backspace && _buffer.Length > 0)
            {
                _buffer = _buffer[..^1];
                doClear = true;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                _buffer += '\n';
            }
            // - History
            else if (
                key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow &&
                _history.Count > 0)
            {
                _historyIndex += key.Key == ConsoleKey.UpArrow ? -1 : +1;
                if (_historyIndex < _history.Count && _historyIndex >= 0)
                {
                    ClearLine();
                    _buffer = _history[_historyIndex];
                }
                else if (key.Key == ConsoleKey.DownArrow)
                    _historyIndex = _history.Count - 1;
                else 
                    _historyIndex = 0;
            }

            // Restart cursor
            Console.SetCursorPosition(Position.L, Position.T);

            // Print
            var block = 0;
            var span = _buffer.AsSpan();
            var printNewLine = false;
            Range? last = null;
            foreach (var r in span.SplitAny(' ', '\n'))
            {
                var token = span[r];
                block += BlockCounter(token);

                // in between
                if (last is not null)
                {
                    var itoken = span[last.Value.End .. r.Start];
                    if (itoken.Contains('\n'))
                        printNewLine = true;
                    else if (!itoken.IsEmpty)
                        Console.Write(itoken);
                }

                if (!token.IsEmpty)
                {
                    if (printNewLine)
                    {
                        printNewLine = false;
                        PrintNewLine(block);
                    }
                    
                    ConsoleColor? color = null;
                    if (IsReservedWord(token))
                        color = ConsoleColor.Green;

                    if (color != null)
                        Console.ForegroundColor = color.Value;

                    Console.Write(token);

                    if (color != null)
                        Console.ResetColor();    
                }

                last = r;
            }

            if (doClear)
            {
                Console.Write(' ');
                Console.CursorLeft--;
                if (Console.CursorLeft == 0 && _buffer.Length > 0)
                    Console.CursorTop++;
            }

            // On new line, if block == 0, return current buffer
            // otherwise, keep buffering the input
            if (key.Key != ConsoleKey.Enter || block > 0)
            {
                if (printNewLine) PrintNewLine(block);
                return null;
            }

            var result = _buffer;
            _buffer = "";

            // Update history
            var histItem = result.Trim();
            _history.RemoveAll(p => p == histItem);
            _history.Add(histItem);
            _historyIndex = _history.Count;

            // Get out of here stalker
            return result;
            
            void ClearLine()
            {
                var l = Console.CursorLeft;
                Console.CursorLeft = 1;
                Span<char> empty = stackalloc char[w - l - 2];
                empty.Fill(' ');
                Console.Write(empty);
                Console.CursorLeft = l;
            }
            
            void PrintNewLine(int block)
            {
                block = Math.Max(block, 0);
                Console.WriteLine();
                ClearLine();
                PrintSymbol();
                Span<char> empty = stackalloc char[block * 2];
                empty.Fill(' ');
                Console.Write(empty);
            }
        }
        
        public void PrintSymbol() => Console.Write(Symbol);

        // TODO: LLex.IsReservedWord should accept ReadOnlySpan<char>
        public static bool IsReservedWord(ReadOnlySpan<char> word) =>
            LLex.IsReservedWord(word);

        public static int BlockCounter(ReadOnlySpan<char> word) => word switch
        {
            "end" or "until" => -1,
            "do" or "repeat" or "function" or "then" => +1,
            _ => 0,
        };
    }

    private static class SpessaSharpLib
    {
        public const string LIB_NAME = "spessa";
	
        public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);

        public static Player Player = null!;

        public static int OpenLib(LuaState lua)
        {
            ReadOnlySpan<NameFuncPair> define =
            [
                new("noteon",       SS_PlayNoteOn),
                new("note",         SS_PlayNote),
                new("loadmidi",     SS_LoadMidi),
                new("play",         SS_Play),
                new("pause",        SS_Pause),
                new("stop",         SS_Stop),
            ];

            lua.NewLib(define);
            
            var mt = new LuaTable(lua);
            
            // Getter
            mt.Set("__index", L =>
            {
                _ = L.CheckTable(1);
                var key = L.CheckString(2);

                return key switch
                {
                    "version" => SS_GetVersion(L),
                    "volume" => SS_GetVolume(L),
                    _ => 0
                };
            });
            
            // Setter
            mt.Set("__newindex", L =>
            {
                _ = L.CheckTable(1);
                var key = L.CheckString(2);

                return key switch
                {
                    "volume" => SS_SetVolume(L),
                    _ => 0
                };
            });
            
            lua.PushTable(mt);
            lua.SetMetaTable(-2);
            
            return 1;
        }
        
        private static int SS_GetVersion(LuaState lua)
        {
            lua.PushString(
                typeof(SpessaUtil).Assembly.GetName().Version!.ToString(3));
            return 1;
        }

        private static int SS_GetVolume(LuaState lua)
        {
            lua.PushNumber(Player.Volume);
            return 1;
        }
        
        private static int SS_SetVolume(LuaState lua)
        {
            var v = (float)lua.CheckNumber(3);
            Player.Volume = v;
            return 0;
        }

        private static int SS_PlayNoteOn(LuaState lua)
        {
            // TODO Parse notes
            switch (lua.GetTop())
            {
                // Note
                case 1:
                    Player.NoteOn(0, lua.CheckInteger(1), 60);
                    break;
                // Note, vel
                case 2:
                    Player.NoteOn(0, lua.CheckInteger(1), lua.CheckInteger(2));
                    break;
                // Chan, note, vel
                case 3:
                    Player.NoteOn(lua.CheckInteger(1), lua.CheckInteger(2), lua.CheckInteger(3));
                    break;
                default:
                    lua.Error("note expects at least a key number");
                    break;
            }

            return 0;
        }
        
        private static int SS_PlayNote(LuaState lua)
        {
            var time = Player.Time;
            
            switch (lua.GetTop())
            {
                // Note, dur
                case 2:
                    Player.Note(
                        0, 
                        lua.CheckInteger(1), 
                        60,
                        time + TimeSpan.FromSeconds(lua.CheckNumber(2)));
                    break;
                // Note, vel, dur
                case 3:
                    Player.Note(
                        0, 
                        lua.CheckInteger(1), 
                        lua.CheckInteger(2),
                        time + TimeSpan.FromSeconds(lua.ToNumber(3)));
                    break;
                // Chan, note, vel, dur
                case 4:
                    Player.Note(
                        lua.CheckInteger(1), 
                        lua.CheckInteger(3),
                        lua.CheckInteger(4),
                        time + TimeSpan.FromSeconds(lua.ToNumber(4)));
                    break;
                default:
                    lua.Error("note expects at least a key number and a duration");
                    break;
            }

            return 0;
        }
        
        private static int SS_LoadMidi(LuaState lua)
        {
            var path = lua.CheckString(1);
            var midi = Midi.From(new FileInfo(path));
            lua.PushLightUserData(midi);
            return 1;
        }

        private static int SS_Play(LuaState lua)
        {
            if (lua.GetTop() == 0)
            {
                Player.Play();
                return 0;
            }
            
            var what = lua.CheckLightUserData(1);

            if (what is not Midi midi)
            {
                lua.Error("expected a midi object");
                return 0;
            }
                
            Player.Stop();
            Player.Midi = midi;
            Player.Play();
            return 0;
        }
        
        private static int SS_Pause(LuaState lua)
        {
            Player.Pause();
            return 0;
        }

        private static int SS_Stop(LuaState lua)
        {
            Player.Stop();
            return 0;
        }
    }
}