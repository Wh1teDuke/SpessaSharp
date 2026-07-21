using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;

public sealed class MidiTestMaker
{
    public readonly record struct Options(
        int StartTicks,
        int TimeDivision,
        int Channel,
        Midi.System System
    )
    {
        public static readonly Options Default = new(
            StartTicks: 480,
            TimeDivision: 480,
            Channel: 0,
            Midi.System.GS
        );
    }

    private int _ticks;
    private Midi.System _system;

    private readonly MidiBuilder _builder;

    private readonly Track Track;
    private readonly MidiBuilder.TrackBuilder T;
    private readonly MidiBuilder.ChannelBuilder C;
    
    private readonly string Name;
    private readonly int Channel;

    public MidiTestMaker(string name, Options? options = null)
    {
        var opts = options ?? Options.Default;
        
        _builder = MidiBuilder.New(MidiBuilder.Options.Default with
        {
            TimeDivision    = opts.TimeDivision,
            Name            = name,
        });
        
        Channel = opts.Channel;
        Name = name;
        
        _builder.Midi.FileName = name.Replace(" ", "_").ToLowerInvariant();
        _ticks = opts.StartTicks;
        _system = opts.System;

        Track = _builder.Midi.Tracks[0];
        Track.Add(MidiUtils.Reset(0, opts.System), 0);
        T = _builder.OfTrack(Track);
        C = T.OfChannel(Channel);
    }

    public MidiTestMaker Reset(Midi.System system)
    {
        Text($"{system.ToString().ToUpperInvariant()} RESET");
        T.Reset(_ticks, system);
        _system = system;
        return Wait(480);
    }
    
    public MidiTestMaker Set(GlobalMidiParameter param)
    {
        T.Set(_ticks, _system, param);
        return this;
    }

    public MidiTestMaker Set(ChannelMidiParameter param)
    {
        C.Set(_ticks, _system, param);
        return this;
    }

    public MidiTestMaker CC(Midi.CC cc, int value)
    {
        C.CC(_ticks, cc, value);
        return this;
    }
    
    public MidiTestMaker ProgramChange(int msb, int lsb, int program)
    {
        Text($"Program change {msb}:{lsb} - {program}");
        CC(Midi.CC.BankSelectLSB, lsb);
        CC(Midi.CC.BankSelect, msb);
        C.ProgramChange(_ticks, program);
        return this;
    }
    
    public MidiTestMaker Text(string text)
    {
        T.Text(_ticks, text);
        return this;
    }

    public MidiTestMaker Wait(int ticks)
    {
        _ticks += ticks;
        return this;
    }

    public MidiTestMaker Note(int midiNote, int velocity, int duration = 480) => 
        NoteOn(midiNote, velocity).Wait(duration).NoteOff(midiNote);

    public MidiTestMaker NoteOff(int midiNote)
    {
        C.NoteOff(_ticks, midiNote);
        return this;
    }
    
    public MidiTestMaker NoteOn(int midiNote, int velocity)
    {
        C.NoteOn(_ticks, midiNote, velocity);
        return this;
    }
    
    public MidiTestMaker GS(int a1, int a2, int a3, ReadOnlySpan<int> data) 
    {
        T.SystemExclusive(
            _ticks, MidiUtils.Gs(a1, a2, a3, ToByteArray(data)));
        return this;
    }

    public MidiTestMaker XG(int a1, int a2, int a3, ReadOnlySpan<int> data) 
    {
        T.SystemExclusive(
            _ticks, MidiUtils.Xg(a1, a2, a3, ToByteArray(data)));
        return this;
    }
    
    public MidiTestMaker RPN(int rpn, int val) 
    {
        Text($"RPN {rpn:x} = {val:x}");
        C.RegisteredParameter(_ticks, rpn, val);
        return this;
    }

    /// <summary>Value is 7-bit only</summary>
    /// <param name="nrpn"></param>
    /// <param name="val">7-bit only</param>
    public MidiTestMaker NRPN(int nrpn, int val) 
    {
        // Text($"NRPN {nrpn:x} = {val:x}"); ???
        C.NonRegisteredParameter(_ticks, nrpn, val << 7);
        return this;
    }

    public void Modify(MidiEditor.Options opts) => _builder.Midi.Modify(opts);
    
    public void Flush() => _builder.Midi.Flush();

    public void Make(DirectoryInfo? outPath = null)
    {
        outPath ??= new DirectoryInfo(Path.Join("..", "generated"));
        
        // Wait a little
        Wait(480 * 2).CC(Midi.CC.ModulationWheel, 1);
        Flush();
        
        var outFile = new FileInfo(
            Path.Join(outPath.FullName, _builder.Midi.FileName + ".mid"));
        outPath.Create();

        using var writer = outFile.OpenWrite();
        writer.Write(_builder.Midi.Write());
        
        Console.WriteLine($"{Name} written as {outFile.FullName}");
    }

    private static byte[] ToByteArray(ReadOnlySpan<int> array)
    {
        var result = new byte[array.Length];
        for (var i = 0; i < array.Length; i++) result[i] = (byte)array[i];
        return result;
    }
}
