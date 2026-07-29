using CSLua;
using CSLua.Extensions;
using CSLua.Util;
using SpessaSharp.Sequencer;
using SpessaSharp.Synthesizer;
using SSTool.Util;

namespace SSTool.Actions;

public static class ActionScript
{
    public static void Run(
        FileInfo? fileSoundBank,
        string? arg = null)
    {
        // Spessa
        var loop = true;
        var (sb, _) = Etc.GetSoundBank(fileSoundBank);
        
        var processor = new SpessaSharpProcessor(44_100);
        var sequencer = new SpessaSharpSequencer(processor);
        processor.SoundBankManager.Add(sb, "main");
        
        var player = new Player(sequencer);
        player.Play();
        player.Volume = 2;
        
        // Lua
        var L = Lua.New();
        L.OpenLibs();
        L.Open(SpessaSharpLib.NameFuncPair);
        SpessaSharpLib.Player = player;
        
        L.PushLightUserData(player);
        L.SetGlobal("player");
        
        L.SetGlobal("quit", Quit);
        
        ActionPlay.TriggerGC();

        while (loop)
        {
            // Prompt
            if (arg == null) Console.Write(">");
            
            var input = arg ?? Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input is "exit" or "quit" or "e" or "q") break;
            
            input = input.Replace("\\n", Environment.NewLine);

            if (arg != null)
                loop = false;

            try
            {
                L.Eval(input);
            }
            catch (LuaRuntimeException e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.Message);
                Console.ResetColor();
            }
        }
        
        if (arg != null) while (true)
        {
            Thread.Sleep(50);
            if (player.VoiceCount == 0) break;
        }
        
        return;

        int Quit(LuaState lua)
        {
            loop = false;
            return 0;
        }
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
                new("noteon",   SS_PlayNoteOn),
                new("note",     SS_PlayNote),
            ];

            lua.NewLib(define);
            return 1;
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
                case 4:
                    Player.Note(
                        0, 
                        lua.CheckInteger(1), 
                        lua.CheckInteger(2),
                        time + TimeSpan.FromSeconds(lua.ToNumber(3)));
                    break;
                // Chan, note, vel, dur
                case 3:
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
    }
}