using System.Runtime.InteropServices;

namespace SpessaSharp.Sequencer;

public readonly struct Event
{
    public enum Type
    {
        /// <summary> Called when a MIDI message is sent and externalMIDIPlayback is true. </summary>
        MidiMessage,
        /// <summary>Called when the time is changed. It also gets called when a song gets changed.</summary>
        TimeChange,
        /// <summary> Called when the playback stops. </summary>
        Pause,
        SongEnded,
        /// <summary> Called when the song changes. </summary>
        SongChange,
        /// <summary> Called when the song list changes. </summary>
        SongListChange,
        /// <summary> Called when a new song is added to the song list </summary>
        SongAdded,
        /// <summary> Called when a MIDI Meta event is encountered. </summary>
        MetaEvent,
        /// <summary>Called when the loop count changes (decreases). </summary>
        LoopCountChange,
    }

    /// <summary> Called when a MIDI message is sent and externalMIDIPlayback is true. </summary>
    /// <param name="Message">The binary MIDI message.</param>
    /// <param name="Time">The synthesizer's current time when this event was sent.
    /// Use this for scheduling MIDI messages to your external MIDI device.</param>
    public readonly record struct CbMidiMessage(
        ArraySegment<byte> Message, double Time);
    
    /// <summary>Called when the time is changed. It also gets called when a song gets changed.</summary>
    /// <param name="NewTime">The new time in seconds.</param>
    public readonly record struct CbTimeChange(double NewTime);
    
    /// <summary> Called when the song changes. </summary>
    /// <param name="SongIndex">The index of the new song in the song list.</param>
    public readonly record struct CbSongChange(int SongIndex);
    
    /// <summary> Called when the song changes. </summary>
    /// <param name="SongIndex">The index of the new song in the song list.</param>
    public readonly record struct CbSongAdded(int SongIndex);
    
    /// <summary> Called when the song list changes. </summary>
    /// <param name="SongList">The new song list.</param>
    public readonly record struct CbSongListChange(MIDI.Midi[] SongList);
    
    /// <summary> Called when a MIDI Meta event is encountered. </summary>
    /// <param name="Event">The MIDI message of the meta event.</param>
    /// <param name="TrackIndex">The index of the track where the meta event was encountered.</param>
    public readonly record struct CbMetaEvent(
        MIDI.MidiMessage Event, int TrackIndex);
    
    /// <summary>Called when the loop count changes (decreases). </summary>
    /// <param name="NewCount">The new loop count.</param>
    public readonly record struct CbLoopCountChange(int NewCount);

    [StructLayout(LayoutKind.Explicit)]
    private struct EventsWithoutPointers
    {
        [FieldOffset(0)] public CbTimeChange TimeChange;
        [FieldOffset(0)] public CbSongChange SongChange;
        [FieldOffset(0)] public CbSongAdded SongAdded;
        [FieldOffset(0)] public CbLoopCountChange LoopCountChange;
    }

    private struct EventsWithPointers
    {
        public CbMidiMessage MidiMessage;
        public CbSongListChange SongListChange;
        public CbMetaEvent MetaEvent;
    }
    
    public Type EventType { get; init; }

    private EventsWithoutPointers E1 { get; init; }
    private EventsWithPointers E2 { get; init; }

    public static Event OfPause() => 
        new() { EventType = Type.Pause };
    
    public static Event OfSongEnded() => 
        new() { EventType = Type.SongChange };

    public static Event Of(CbTimeChange timeChange) => new()
    {
        EventType = Type.TimeChange,
        E1 = new EventsWithoutPointers { TimeChange = timeChange },
    };
    
    public static Event Of(CbSongChange songChange) => new()
    {
        EventType = Type.SongChange,
        E1 = new EventsWithoutPointers { SongChange = songChange },
    };
    
    public static Event Of(CbSongAdded songAdded) => new()
    {
        EventType = Type.SongAdded,
        E1 = new EventsWithoutPointers { SongAdded = songAdded },
    };
    
    public static Event Of(CbLoopCountChange loopCountChange) => new()
    {
        EventType = Type.LoopCountChange,
        E1 = new EventsWithoutPointers { LoopCountChange = loopCountChange },
    };
    
    public static Event Of(CbMidiMessage midiMessage) => new()
    {
        EventType = Type.MidiMessage,
        E2 = new EventsWithPointers { MidiMessage = midiMessage },
    };
    
    public static Event Of(CbSongListChange songListChange) => new()
    {
        EventType = Type.SongListChange,
        E2 = new EventsWithPointers { SongListChange = songListChange },
    };
    
    public static Event Of(CbMetaEvent metaEvent) => new()
    {
        EventType = Type.MetaEvent,
        E2 = new EventsWithPointers { MetaEvent = metaEvent },
    };

    public bool IsPause => EventType == Type.Pause;
    public bool IsSongEnded => EventType == Type.SongEnded;

    public CbTimeChange? AsTimeChange =>
        EventType == Type.TimeChange ? E1.TimeChange : null;
    public CbSongChange? AsSongChange =>
        EventType == Type.SongChange ? E1.SongChange : null;
    public CbSongAdded? AsSongAdded =>
        EventType == Type.SongAdded ? E1.SongAdded : null;
    public CbLoopCountChange? AsLoopCountChange =>
        EventType == Type.LoopCountChange ? E1.LoopCountChange : null;
    public CbMidiMessage? AsMidiMessage =>
        EventType == Type.MidiMessage ? E2.MidiMessage : null;
    public CbSongListChange? AsSongListChange =>
        EventType == Type.SongListChange ? E2.SongListChange : null;
    public CbMetaEvent? AsMetaEvent =>
        EventType == Type.MetaEvent ? E2.MetaEvent : null;
    
    public static implicit operator Event(CbTimeChange ev) => Of(ev);
    public static implicit operator Event(CbSongChange ev) => Of(ev);
    public static implicit operator Event(CbSongAdded ev) => Of(ev);
    public static implicit operator Event(CbLoopCountChange ev) => Of(ev);
    public static implicit operator Event(CbMidiMessage ev) => Of(ev);
    public static implicit operator Event(CbSongListChange ev) => Of(ev);
    public static implicit operator Event(CbMetaEvent ev) => Of(ev);
}