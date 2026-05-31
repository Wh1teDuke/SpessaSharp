using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Utils;

/// <summary>A class that helps to build a MIDI file from scratch.</summary>
public readonly record struct MidiBuilder
{
    public readonly record struct TrackBuilder(MidiBuilder Base, Track Track)
    {
        public ChannelBuilder OfChannel(int chan) => new(this, chan);
        public DrumBuilder DrumBuilder => new (this);

        public void AddEvent(
            int ticks, StatusByte sb, ArraySegment<byte> eventData) =>
                Base.AddEvent(Track, ticks, sb, eventData);
        
        public void SetLoopStart(int ticks) => Base.SetLoopStart(Track, ticks);
        public void SetLoopEnd(int ticks) => Base.SetLoopEnd(Track, ticks);
        
        public void NoteOn(int ticks, int channel, int midiNote, int velocity) =>
            Base.NoteOn(Track, ticks, channel, midiNote, velocity);
        
        public void NoteOff(int ticks, int channel, int midiNote, int velocity = 64) =>
            Base.NoteOff(Track, ticks, channel, midiNote, velocity);
        
        public void Text(int ticks, string text) => AddEvent(
            ticks,
            MidiMessage.Type.Text,
            SpessaUtil.MidiEncoding.GetBytes(text));
    }

    public readonly record struct ChannelBuilder(
        TrackBuilder Base, int Channel)
    {
        public void NoteOn(int ticks, int midiNote, int velocity) =>
            Base.NoteOn(ticks, Channel, midiNote, velocity);

        public void NoteOff(int ticks, int midiNote, int velocity = 64) =>
            Base.NoteOff(ticks, Channel, midiNote, velocity);
        
        public void Note(int ticks, int duration, int midiNote, int velocity)
        {
            NoteOn(ticks, midiNote, velocity);
            NoteOff(ticks + duration, midiNote);
        }

        public void AddEvent(
            int ticks, StatusByte sb, ArraySegment<byte> eventData) =>
            Base.AddEvent(ticks, sb, eventData);

        public void ProgramChange(int ticks, int program) =>
            Base.Base.ProgramChange(Base.Track, ticks, Channel, program);

        public void CC(int ticks, Midi.CC controller, int value) =>
            Base.Base.ControllerChange(
                Base.Track, ticks, Channel, (int)controller, value);

        public void SweepCC(
            int ticks, Midi.CC controller, Range range, int tickStep = 480, int dataStep = 1)
        {
            if (range.Start.IsFromEnd || range.End.IsFromEnd)
                throw new ArgumentException("Range must be absolute");
            
            var data = range.Start.Value;
            while (data < range.End.Value)
            {
                var v = Math.Min(data, range.End.Value - 1);
                CC(ticks, controller, v);
                ticks += tickStep;
                data += dataStep;
            }
            
            CC(ticks, controller, Math.Min(data, range.End.Value - 1));
        }

        public void PitchWheel(int ticks, int pitch) =>
            Base.Base.PitchWheel(Base.Track, ticks, Channel, pitch);

        public void RegisteredParameter(int ticks, int parameter, int value) =>
            Base.Base.RegisteredParameter(
                Base.Track, ticks, Channel, parameter, value);

        public void NonRegisteredParameter(int ticks, int parameter, int value) =>
            Base.Base.NonRegisteredParameter(
                Base.Track, ticks, Channel, parameter, value);
    }

    public readonly record struct DrumBuilder(TrackBuilder Base, int Channel = 9)
    {
        public void Play(int ticks, InstrumentInfo.Drum drum, int velocity)
        {
            var note = InstrumentInfo.ToMidiNote(drum);
            Base.NoteOn(ticks, Channel, note, velocity);
            // Drum notes can be released immediately
            Base.NoteOff(ticks, Channel, note);
        }
    }
    
    /// <summary>
    /// </summary>
    /// <param name="TimeDivision">The MIDI file's tick precision (how many ticks fit in a quarter note).</param>
    /// <param name="InitialTempo">The MIDI file's initial tempo in BPM.</param>
    /// <param name="Format">The MIDI file's MIDI track format.</param>
    /// <param name="Name">The MIDI file's name. Will be appended to the conductor track.</param>
    public readonly record struct Options(
        int TimeDivision = 480,
        int InitialTempo = 120,
        MidiFormat Format = MidiFormat.m0,
        string Name = "Untitled song")
    {
        public static readonly Options Default = new()
        {
            TimeDivision = 480,
            InitialTempo = 120,
            Format = MidiFormat.m0,
            Name = "Untitled song",
        };
    }

    public Midi Midi { get; init; }

    public static MidiBuilder New(Options? options = null)
    {
        var opts = options ?? Options.Default;

        if (opts.Format == MidiFormat.m2)
            throw new ArgumentException(
                "MIDI format 2 is not supported in the MIDI builder. Consider using format 1");
        
        var midi = new Midi();
        midi.SetRMidiInfo(RMidi.Info.Key.MidiEncoding, "UTF-8");
        midi.Format = opts.Format;
        midi.TimeDivision = opts.TimeDivision;
        midi.FileName = opts.Name;

        var result = new MidiBuilder { Midi = midi }; 
        
        // Create the first (conductor) track with the file name
        result.AddTrack(opts.Name);
        result.SetTempo(0, opts.InitialTempo);

        return result;
    }

    public TrackBuilder OfTrack(Track track) => new (this, track);
    public TrackBuilder OfTrack(int track) => new (this, Midi.Tracks[track]);
    
    /// <summary>Adds a new Set Tempo event.</summary>
    /// <param name="ticks">The tick number of the event.</param>
    /// <param name="tempo">The tempo in beats per minute (BPM).</param>
    public void SetTempo(int ticks, int tempo) 
    {
        var array = new byte[3];

        tempo = 60_000_000 / tempo;

        // Extract each byte in big-endian order
        array[0] = (byte)((tempo >> 16) & 0xff);
        array[1] = (byte)((tempo >> 8) & 0xff);
        array[2] = (byte)(tempo & 0xff);

        AddEvent(Midi.Tracks[0], ticks, MidiMessage.Type.SetTempo, array);
    }

    /// <summary>Adds a new MIDI track.</summary>
    /// <param name="name">The new track's name.</param>
    /// <param name="port">The new track's port.</param>
    /// <exception cref="ArgumentException">MIDI with format 0</exception>
    public void AddTrack(string name, int port = 0) 
    {
        if (Midi is { Format: MidiFormat.m0, Tracks.Count: > 0 })
            throw new ArgumentException(
                "Can't add more tracks to MIDI format 0. Consider using format 1.");

        var track = new Track { Name = name, Port = port };

        Midi.Tracks.Add(track);

        AddEvent(track, 0, MidiMessage.Type.TrackName,
            SpessaUtil.MidiEncoding.GetBytes(name));
        AddEvent(track, 0, MidiMessage.Type.MidiPort, DataOf(port));
    }
    
    /// <summary>Adds a new MIDI Event.</summary>
    /// <param name="track">The track to use.</param>
    /// <param name="ticks">The tick time of the event (absolute).</param>
    /// <param name="sb">MIDI Status byte.</param>
    /// <param name="eventData">The raw event data.</param>
    public void AddEvent(
        Track track,
        int ticks,
        StatusByte sb,
        ArraySegment<byte> eventData)
    {
        if (sb.GE(MidiMessage.Type.NoteOff) && // Voice event
            Midi.Format == MidiFormat.m1 && track == Midi.Tracks[0])
            throw new ArgumentException(
                "Can't add voice messages to the conductor track (0) in format 1. Consider using format 0 using a different track.");

        track.EventList.Add(new MidiMessage(ticks, sb, eventData));
    }
    
    /// <summary>Adds a new Note On event.</summary>
    /// <param name="ticks">The tick time of the event.</param>
    /// <param name="track">The track number to use.</param>
    /// <param name="channel">The channel to use.</param>
    /// <param name="midiNote">The midi note of the keypress.</param>
    /// <param name="velocity">The velocity of the keypress.</param>
    public void NoteOn(
        int ticks, int track, int channel, int midiNote, int velocity) =>
            NoteOn(Midi.Tracks[track], ticks, channel, midiNote, velocity);

    public void NoteOn(
        Track track, int ticks, int channel, int midiNote, int velocity) 
    {
        channel %= 16;
        midiNote %= 128;
        velocity %= 128;

        AddEvent(
            track, ticks,
            SB(MidiMessage.Type.NoteOn, channel), DataOf(midiNote, velocity));
    }
    
    /// <summary>Adds a new Note Off event.</summary>
    /// <param name="ticks">The tick time of the event.</param>
    /// <param name="track">The track number to use.</param>
    /// <param name="channel">The channel to use.</param>
    /// <param name="midiNote">The midi note of the key release.</param>
    /// <param name="velocity">Optional and unsupported by SpessaSynth.</param>
    public void NoteOff(
        int ticks, int track, int channel, int midiNote, int velocity = 64) =>
        NoteOff(Midi.Tracks[track], ticks, channel, midiNote, velocity);
    
    public void NoteOff(
        Track track,
        int ticks,
        int channel,
        int midiNote,
        int velocity = 64)
    {
        channel %= 16;
        midiNote %= 128;

        AddEvent(
            track, ticks,
            SB(MidiMessage.Type.NoteOff, channel), DataOf(midiNote, velocity));
    }

    /// <summary>Adds a new Program Change event.</summary>
    /// <param name="track">The track to use.</param>
    /// <param name="ticks">The tick time of the event.</param>
    /// <param name="channel">The channel to use.</param>
    /// <param name="program">The MIDI program to use.</param>
    public void ProgramChange(Track track, int ticks, int channel, int program)
    {
        channel %= 16;
        program %= 128;

        AddEvent(
            track, ticks,
            SB(MidiMessage.Type.ProgramChange, channel), DataOf(program));
    }

    public void SetLoopStart(int track, int ticks) =>
        SetLoopStart(Midi.Tracks[track], ticks);
    
    public void SetLoopStart(Track track, int ticks) =>
        AddEvent(track, ticks, MidiMessage.Type.Marker, DataOf("loopstart"));

    public void SetLoopEnd(int track, int ticks) =>
        SetLoopEnd(Midi.Tracks[track], ticks);
    
    public void SetLoopEnd(Track track, int ticks) =>
        AddEvent(track, ticks, MidiMessage.Type.Marker, DataOf("loopend"));

    /// <summary>Adds a new Controller Change event.</summary>
    /// <param name="track">The track to use.</param>
    /// <param name="ticks">The tick time of the event.</param>
    /// <param name="channel">The channel to use.</param>
    /// <param name="controller">The MIDI CC to use.</param>
    /// <param name="value">The new CC value.</param>
    public void ControllerChange(
        Track track, int ticks, int channel, int controller, int value) 
    {
        channel %= 16;
        controller %= 128;
        value %= 128;

        AddEvent(
            track, ticks,
            SB(MidiMessage.Type.ControllerChange, channel),
            DataOf(controller, value));
    }

    /// <summary>Adds a new Pitch Wheel event.</summary>
    /// <param name="track">The track to use.</param>
    /// <param name="ticks">The tick time of the event.</param>
    /// <param name="channel">The channel to use.</param>
    /// <param name="pitch">The pitch (0 - 16383).</param>
    public void PitchWheel(Track track, int ticks, int channel, int pitch) 
    {
        channel %= 16;
        pitch %= 16_384;

        AddEvent(
            track, ticks,
            SB(MidiMessage.Type.PitchWheel, channel),
            DataOf(pitch & 0x7f, (pitch >> 7) & 0x7f));
    }

    /// <summary>Selects a new Registered Parameter Number.</summary>
    /// <param name="ticks">Ticks the tick time of the events.</param>
    /// <param name="track">The track to use.</param>
    /// <param name="channel">The channel to use.</param>
    /// <param name="parameter">The 14-bit registered parameter number. For example 0 is pitch wheel range.</param>
    /// <param name="value">The 14-bit value for this parameter.</param>
    public void RegisteredParameter(
        int ticks, int track, int channel, int parameter, int value) =>
        RegisteredParameter(
            Midi.Tracks[track], ticks, channel, parameter, value);
    
    /// <summary>Selects a new Registered Parameter Number.</summary>
    /// <param name="track">The track to use.</param>
    /// <param name="ticks">Ticks the tick time of the events.</param>
    /// <param name="channel">The channel to use.</param>
    /// <param name="parameter">The 14-bit registered parameter number. For example 0 is pitch wheel range.</param>
    /// <param name="value">The 14-bit value for this parameter.</param>
    public void RegisteredParameter(
        Track track, int ticks, int channel, int parameter, int value)
    {
        var builder = this;
        CC(Midi.CC.RegisteredParameterMSB, parameter >> 7);
        CC(Midi.CC.RegisteredParameterLSB, parameter & 0x7f);
        CC(Midi.CC.DataEntryMSB, value >> 7);
        CC(Midi.CC.DataEntryLSB, value & 0x7f);
        return;

        void CC(Midi.CC cc, int val) =>
            builder.ControllerChange(track, ticks, channel, (int)cc, val);
    }
    
    public void NonRegisteredParameter(
        int ticks, int track, int channel, int parameter, int value) =>
        NonRegisteredParameter(Midi.Tracks[track], ticks, channel, parameter, value);
    
    public void NonRegisteredParameter(
        Track track, int ticks, int channel, int parameter, int value)
    {
        var builder = this;
        CC(Midi.CC.NonRegisteredParameterMSB, parameter >> 7);
        CC(Midi.CC.NonRegisteredParameterLSB,parameter & 0x7f);
        CC(Midi.CC.DataEntryMSB, value >> 7);
        CC(Midi.CC.DataEntryLSB, value & 0x7f);
        return;

        void CC(Midi.CC cc, int val) =>
            builder.ControllerChange(track, ticks, channel, (int)cc, val);
    }

    private static StatusByte SB(MidiMessage.Type type, int channel) =>
        new ((byte)(MidiMessage.ID(type) | channel));
    private static ArraySegment<byte> DataOf(params ReadOnlySpan<int> args) =>
        args.ToArray().Select(i => (byte)i).ToArray();
    private static ArraySegment<byte> DataOf(string text) =>
        Util.GetStringBytes(text);
}