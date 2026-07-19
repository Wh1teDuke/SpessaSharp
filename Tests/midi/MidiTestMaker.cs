using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;

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

    private MidiBuilder _builder;
    private int _ticks;
    private Midi.System _system;

    private readonly Track Track;
    private readonly MidiBuilder.TrackBuilder TBuilder;
    
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
        TBuilder = _builder.OfTrack(Track);
    }

    public MidiTestMaker Reset(Midi.System system)
    {
        Text($"{system.ToString().ToUpperInvariant()} RESET");
        TBuilder.Reset(_ticks, system);
        _system = system;
        return Wait(480);
    }

    public MidiTestMaker CC(Midi.CC cc, int value)
    {
        TBuilder.OfChannel(0).CC(_ticks, cc, value);
        return this;
    }
    
    public MidiTestMaker ProgramChange(int msb, int lsb, int program)
    {
        Text($"Program change {msb}:{lsb} - {program}");
        CC(Midi.CC.BankSelectLSB, lsb);
        CC(Midi.CC.BankSelect, msb);
        TBuilder.OfChannel(0).ProgramChange(_ticks, program);
        return this;
    }
    
    public MidiTestMaker Text(string text)
    {
        TBuilder.Text(_ticks, text);
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
        TBuilder.OfChannel(0).NoteOff(_ticks, midiNote);
        return this;
    }
    
    public MidiTestMaker NoteOn(int midiNote, int velocity)
    {
        TBuilder.OfChannel(0).NoteOn(_ticks, midiNote, velocity);
        return this;
    }

    public void Make(DirectoryInfo? outPath = null)
    {
        outPath ??= new DirectoryInfo(Path.Join("..", "generated"));
        
        // Wait a little
        Wait(480 * 2).CC(Midi.CC.ModulationWheel, 1);
        _builder.Midi.Flush();
        
        var outFile = new FileInfo(
            Path.Join(outPath.FullName, _builder.Midi.FileName + ".mid"));
        outPath.Create();

        using var writer = outFile.OpenWrite();
        writer.Write(_builder.Midi.Write());
        
        Console.WriteLine($"{Name} written as {outFile.FullName}");
    }
}
