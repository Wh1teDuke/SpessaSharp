using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Synthesizer;
using SpessaSharp.Utils;

namespace SpessaSharp.Sequencer;

public sealed class SpessaSharpSequencer
{
    /// <summary>Sequencer's song list.</summary>
    private readonly List<Midi> _songs = new (32);
    
    /// <summary>The shuffled song indexes. This is used when shuffle mode is enabled.</summary>
    public readonly List<int> ShuffledSongIndexes = new (32);
    
    /// <summary>The synthesizer connected to the sequencer.</summary>
    public readonly SpessaSharpProcessor Synth;
    
    /// <summary>
    /// If the MIDI messages should be sent to an event instead of the synth.
    /// This is used by spessasynth_lib to pass them over to Web MIDI API.
    /// </summary>
    public bool ExternalPlayback = false;
    
    /// <summary>
    /// If the notes that were playing when the sequencer was paused should be re-triggered. Defaults to true.
    /// </summary>
    public bool RetriggerPausedNotes = true;

    /// <summary>
    /// The loop count of the sequencer.
    /// If set to Infinity, it will loop forever.
    /// If set to zero, the loop is disabled.
    /// </summary>
    public int LoopCount = 0;
    
    /// <summary>Indicates if the sequencer should skip to the first note on event. Defaults to true.</summary>
    public bool SkipToFirstNoteOn = true;

    /// <summary>Indicates if the sequencer has finished playing.</summary>
    public bool IsFinished { get; internal set; }

    /// <summary>Indicates if the synthesizer should preload the voices for the newly loaded sequence. Recommended.</summary>
    public bool Preload = true;
    
    /// <summary>Called when the sequencer calls an event.</summary>
    public Action<Event>? OnEventCall;
    
    /// <summary>The time of the first note in seconds.</summary>
    internal double FirstNoteTime = 0;
    
    /// <summary>How long a single MIDI tick currently lasts in seconds.</summary>
    internal double OneTickToSeconds = 0;

    /// <summary>
    /// The current event index in the sorted event list.
    /// This is used to track which event is currently being processed.
    /// </summary>
    internal int Index;
    
    /// <summary>The time that has already been played in the current song.</summary>
    internal double PlayedTime = 0;

    /// <summary>The paused time of the sequencer. If the sequencer is not paused, this is undefined.</summary>
    internal float? PausedTime = null;
    
    /// <summary>Absolute time of the sequencer when it started playing. It is based on the synth's current time.</summary>
    private double _absoluteStartTime = 0;

    internal sealed class PlayingNoteList
    {
        private byte[] _data;
        private int _channels;

        public PlayingNoteList(int channels)
        {
            _channels = channels;
            _data = new byte[_channels * 128];
            Clear();
        }

        public void Grow(int channels)
        {
            _channels += channels;
            var pSize = _data.Length;
            Array.Resize(ref _data, _channels * 128);
            _data.AsSpan(pSize).Fill(byte.MaxValue);
        }
        
        public int this[int channel, int note]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var index = channel * 128 + note;
                // Sanity check
                return index >= _data.Length ? byte.MaxValue : _data[index];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                var index = channel * 128 + note;
                // Sanity check
                if (index >= _data.Length) Grow(channel - _channels + 1);
                _data[index] = (byte)value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int channel, int note) =>
            this[channel, note] = byte.MaxValue;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int channel, int note) =>
            this[channel, note] != byte.MaxValue;
        
        public void Clear() => _data.AsSpan().Fill(byte.MaxValue);

        public Enumerator GetEnumerator() => new(this);

        public ref struct Enumerator(PlayingNoteList list)
        {
            private ReadOnlySpan<byte> _pnd = list._data;
            private int _i = -1;
            public (int Channel, int Note, int Velocity) Current =>
                (_i / 128, _i % 128, _pnd[_i]);
            public bool MoveNext()
            {
                _pnd = _pnd[++_i..];
                return (_i = _pnd.IndexOfAnyExcept(byte.MaxValue)) != -1;
            }
        }
    }
    
    /// <summary>
    /// Currently playing notes, for pressing them after pausing.
    /// Dictionary per channel, key: velocity.
    /// If a dictionary doesn't contain a key then this note is not playing.
    /// </summary>
    internal readonly PlayingNoteList PlayingNotes;
    
    /// <summary>MIDI Port number for each of the MIDI tracks in the current sequence.</summary>
    internal readonly List<int> CurrentPorts = new (32);
    
    /// <summary>This is used to assign new MIDI port offsets to new ports.</summary>
    internal int MidiPortChannelOffset = 0;

    internal double CurrentTempo;

    public int BPM =>
        (int)(Math.Round(CurrentTempo * PlaybackRate * 100) / 100);

    public (int Top, int Bottom) TimeSignature { get; internal set; } = (4, 2);

    public int Tick { get; internal set; }

    /// <summary>Channel offsets for each MIDI port. Stored as: midi port -> channel offset</summary>
    internal readonly int[] MidiPortChannelOffsets = 
        Enumerable.Repeat(-1, 256).ToArray();

    internal int _songIndex = -1;
    
    /// <summary>Internal playback rate.</summary>
    internal float _playbackRate = 1;

    private readonly Random _rand = new();

    /// <summary>Initializes a new Sequencer without any songs loaded.</summary>
    /// <param name="proc">The synthesizer processor to use with this sequencer.</param>
    public SpessaSharpSequencer(SpessaSharpProcessor proc)
    {
        Synth = proc;
        _absoluteStartTime = Synth.CurrentTime;
        // Use the actual count of the synth channels (as it may have grown)
        PlayingNotes = new PlayingNoteList(Synth.MidiChannels.Length);
    }
    
    /// <summary>The currently loaded MIDI data.</summary>
    public Midi? Midi { get;  internal set; }

    /// <summary>The length of the current sequence in seconds.</summary>
    public TimeSpan Duration => Midi?.Duration ?? TimeSpan.Zero;

    public ReadOnlySpan<Midi> Songs => CollectionsMarshal.AsSpan(_songs);

    /// <summary>The current song index in the song list. If shuffle mode is enabled, this is the index of the shuffled song list.</summary>
    public int SongIndex
    {
        get => _songIndex;
        set
        {
            var oldIndex = _songIndex;
            _songIndex = Math.Max(0, value % _songs.Count);
            
            if (oldIndex == _songIndex)
                CurrentTimeSec = 0;
            else
                LoadCurrentSong();
        }
    }

    /// <summary>Controls if the sequencer should shuffle the songs in the song list. If true, the sequencer will play the songs in a random order. Songs are shuffled on a <b>LoadNewSongList</b> call.</summary>
    public bool ShuffleMode { get; set; }

    /// <summary>The sequencer's playback rate. This is the rate at which the sequencer plays back the MIDI data.</summary>
    public float PlaybackRate
    {
        get => _playbackRate;
        set
        {
            var t = CurrentTimeSec;
            _playbackRate = value;
            RecalculateStartTime(t);
        }
    }

    /// <summary>The current time of the sequencer. This is the time in seconds since the sequencer started playing.</summary>
    public TimeSpan CurrentTime
    {
        get => TimeSpan.FromSeconds(CurrentTimeSec);
        set => CurrentTimeSec = value.TotalSeconds;
    }
    
    internal double CurrentTimeSec
    {
        get =>
            // Return the paused time if it's set to something other than undefined
            PausedTime ??
            (Synth.CurrentTime - _absoluteStartTime) * _playbackRate;

        set
        {
            if (Midi == null) return;
            
            if (Paused) PausedTime = (float)value;
            if (value > Midi.Duration.TotalSeconds || value < 0) 
            {
                // Time is 0
                if (SkipToFirstNoteOn)
                    SetTimeTicks(Midi.FirstNoteOn - 1);
                else
                    SetTimeTicks(0);
            } 
            else if (SkipToFirstNoteOn && value < FirstNoteTime) 
            {
                SetTimeTicks(Midi.FirstNoteOn - 1);
                return;
            } 
            else 
            {
                PlayingNotes.Clear();
                CallEvent( new Event.CbTimeChange(value));
                SetTimeTo(value);
                RecalculateStartTime(value);
            }
        }
    }
    
    /// <summary>True if paused, false if playing or stopped</summary>
    public bool Paused => PausedTime != null;

    /// <summary>Processes a single MIDI tick. You should call this every rendering quantum to process the sequencer events in real-time.</summary>
    public void ProcessTick() => 
        Sequencer.ProcessTick.Execute(this);
    
    /// <summary>Starts or resumes the playback of the sequencer. If the sequencer is paused, it will resume from the paused time.</summary>
    public void Play()
    {
        if (Midi == null) 
        {
            Debug.WriteLine(
                "No songs loaded in the sequencer. Ignoring the play call.");
            return;
        }

        // Reset the time
        if (CurrentTimeSec >= Midi.Duration.TotalSeconds)
            CurrentTimeSec = 0;

        // Unpause if paused
        if (Paused) 
        {
            // Adjust the start time
            RecalculateStartTime(PausedTime ?? 0);
        }

        // Do not retrigger if external playback is enabled since we're not tracking notes there
        if (RetriggerPausedNotes && !ExternalPlayback)
        {
            foreach (var (ch, n, v) in PlayingNotes)
                SendNoteOn(ch, n, v);
        }
        
        PausedTime = null;
    }
    
    /// <summary>Pauses the playback.</summary>
    public void Pause() => PauseInternal(false);
    
    /// <summary>Loads a new song list into the sequencer.</summary>
    /// <param name="midiBuffers">The list of songs to load.</param>
    public void LoadNewSongList(IEnumerable<Midi> midiBuffers) 
    {
        // Parse the MIDIs (only the array buffers, MIDI is unchanged)
        _songIndex = 0;
        _songs.Clear();
        _songs.AddRange(midiBuffers);
        if (_songs.Count == 0) return;
        
        ShuffleSongIndexes();
        CallEvent(new Event.CbSongListChange(_songs.ToArray()));

        // Preload all songs (without embedded sound banks)
        if (Preload) 
        {
            Debug.WriteLine("Preloading songs ...");
            foreach (var song in _songs)
                if (song.EmbeddedSoundBank == null) 
                    song.Preload(Synth);
        }

        LoadCurrentSong();
    }
    
    /// <summary>Adds a new song to the playlist if wasn't added yet</summary>
    /// <param name="song">The song to add</param>
    /// <returns>The index of the added song</returns>
    public int Add(Midi song)
    {
        if (_songs.IndexOf(song) is var i and >= 0) return i;

        _songs.Add(song);
        ShuffleSongIndexes();
        CallEvent(new Event.CbSongAdded(_songs.Count - 1));
        if (Preload && song.EmbeddedSoundBank == null)
            song.Preload(Synth);
        
        return _songs.Count - 1;
    }

    /// <summary>Removes the song from the playlist if existed before</summary>
    /// <param name="songIndex">The song index to be removed</param>
    /// <returns>Whether the playlist contained the song</returns>
    public bool Remove(int songIndex) =>
        songIndex >= 0 && songIndex < _songs.Count && Remove(_songs[songIndex]);

    /// <summary>Removes the song from the playlist if existed before</summary>
    /// <param name="song">The song to be removed</param>
    /// <returns>Whether the playlist contained the song</returns>
    public bool Remove(Midi song)
    {
        var index = _songs.IndexOf(song);
        if (index == -1) return false;

        _songs.RemoveAt(index);
        ShuffleSongIndexes();
        if (_songIndex >= index && _songIndex-- == index)
            LoadCurrentSong();
        return true;
    }

    internal void CallEvent(Event ev) => OnEventCall?.Invoke(ev);

    private void PauseInternal(bool isFinished) 
    {
        if (Paused) return;

        Stop();

        // Remove in next breaking release
        CallEvent( Event.OfPause());
        if (isFinished) CallEvent(Event.OfSongEnded());
    }
    
    internal void SongIsFinished() 
    {
        IsFinished = true;
        if (_songs.Count == 1) 
        {
            PauseInternal(true);
            return;
        }
        _songIndex++;
        _songIndex %= _songs.Count;
        LoadCurrentSong();
    }
    
    /// <summary>Stops the playback</summary>
    public void Stop()
    {
        PausedTime = (float)CurrentTimeSec;
        SendMidiAllOff();
    }
    
    /// <summary>Adds a new port (16 channels) to the synth.</summary>
    internal void AddNewMidiPort()
    {
        const int channels = 16;
        PlayingNotes.Grow(channels);
        for (var i = 0; i < channels; i++) Synth.CreateMIDIChannel();
    }
    
    internal void SendMidiMessage(ArraySegment<byte> message) 
    {
        if (!ExternalPlayback) 
        {
            Debug.WriteLine(
                $"Attempting to send {Util.ToHexString(message)
                } to the synthesizer via sendMIDIMessage. This shouldn't happen!");
            return;
        }

        CallEvent(new Event.CbMidiMessage(message, Synth.CurrentTime));
    }

    private void SendMidiAllOff() 
    {
        // Disable sustain
        for (var i = 0; i < 16; i++)
            SendCC(i, Midi.CC.SustainPedal, 0);

        if (!ExternalPlayback)
        {
            Synth.StopAllChannels();
            return;
        }
        
        // External
        // Off all playing notes
        foreach (var (ch, n, _) in PlayingNotes)
            SendNoteOff(ch, n);

        // Send off controllers
        for (var c = 0; c < 16; c++)
            SendCC(c, Midi.CC.AllNotesOff, 0);
    }

    internal void SendMidiReset() 
    {
        SendMidiAllOff();
        if (!ExternalPlayback) 
        {
            Synth.Reset();
            return;
        }

        var result = (Span<byte>)stackalloc byte[1 + MidiUtils.GsDataMinLen];
        SendMidiSysEx(MidiUtils.GsData(
            0x40, // System parameter - Address
            0x00, // Global mode parameter -  Address
            0x7f, // MODE SET - Address
            [0x00], // 00 = GS Reset - Data
            result));
    }

    private void LoadCurrentSong() 
    {
        var index = _songIndex;
        if (ShuffleMode) index = ShuffledSongIndexes[_songIndex];
        
        LoadNewSequence(_songs[index]);
    }

    private void SendMidiSysEx(ReadOnlySpan<byte> syx)
    {
        if (!ExternalPlayback) 
        {
            Synth.SystemExclusive(syx);
            return;
        }
        
        var msg = Util.Rent<byte>(syx.Length + 1);
        msg[0] = MidiMessage.Type.SystemExclusive.ID();
        syx.CopyTo(msg[1..]);
        SendMidiMessage(msg);
        Util.Return(msg);
    }

    private void ShuffleSongIndexes()
    {
        ShuffledSongIndexes.Clear();
        for (var i = 0; i < _songs.Count; i++)
            ShuffledSongIndexes.Add(i);
        _rand.Shuffle(CollectionsMarshal.AsSpan(ShuffledSongIndexes));
    }
    
    /// <summary>Sets the time in MIDI ticks.</summary>
    /// <param name="ticks">The MIDI ticks to set the time to.</param>
    internal void SetTimeTicks(int ticks) 
    {
        if (Midi == null) return;

        PlayingNotes.Clear();
        var seconds = Midi.MidiTicksToSeconds(ticks);
        CallEvent(new Event.CbTimeChange(seconds));

        var isNotFinished = SetTimeTo(0, ticks);
        RecalculateStartTime(PlayedTime);
        if (!isNotFinished) return;
    }

    /// <summary>Recalculates the absolute start time of the sequencer.</summary>
    /// <param name="time">The time in seconds to recalculate the start time for.</param>
    private void RecalculateStartTime(double time) =>
        _absoluteStartTime = Synth.CurrentTime - time / _playbackRate;
    
    /// <summary>Jumps to a MIDI tick without any further processing.</summary>
    /// <param name="targetTicks">The MIDI tick to jump to.</param>
    internal void JumpToTick(int targetTicks) 
    {
        if (Midi is not {} m) return;

        SendMidiAllOff();
        var seconds = m.MidiTicksToSeconds(targetTicks);
        CallEvent(new Event.CbTimeChange(seconds));

        // Recalculate time and reset indexes
        RecalculateStartTime(seconds);
        PlayedTime = seconds;

        int? idx = null;
        var timeline = m.Timeline;
        for (var i = 0; i < timeline.Length; i++)
        {
            if (m[timeline[i]].Ticks < targetTicks) continue;
            idx = i;
            break;
        }

        Index = idx ?? timeline.Length;

        // Correct tempo
        // Some softy-looped files (example: th06_06.mid) have slightly mismatched tempos
        foreach (var tempo in m.TempoChanges)
        {
            if (tempo.Ticks > targetTicks) continue;
            OneTickToSeconds = 60d / (tempo.Tempo * m.TimeDivision);
            break;
        }
    }
    
    /*
    SEND MIDI METHOD ABSTRACTIONS
    These abstract the difference between spessasynth and external MIDI
     */

    private void SendNoteOn(int channel, int midiNote, int velocity) 
    {
        if (!ExternalPlayback) 
        {
            Synth.NoteOn(channel, midiNote, velocity);
            return;
        }

        channel %= 16;

        SendMidiMessage(new [] {
            (byte)(MidiMessage.Type.NoteOn.ID() | channel),
            (byte)midiNote,
            (byte)velocity});
    }

    private void SendNoteOff(int channel, int midiNote) 
    {
        if (!ExternalPlayback) 
        {
            Synth.NoteOff(channel, midiNote);
            return;
        }

        channel %= 16;
        SendMidiMessage(new []{
            (byte)(MidiMessage.Type.NoteOff.ID() | channel),
            (byte)midiNote,
            (byte)64 // Make sure to send velocity as well
        });
    }

    internal void SendCC(int channel, Midi.CC type, int value) 
    {
        if (!ExternalPlayback) 
        {
            Synth.ControllerChange(channel, type, value);
            return;
        }
        
        channel %= 16;
        SendMidiMessage(new [] {
            (byte)(MidiMessage.Type.ControllerChange.ID() | channel),
            (byte)type,
            (byte)value
        });
    }

    /// <summary>Sets the pitch of the given channel</summary>
    /// <param name="channel">Usually 0-15: the channel to change pitch</param>
    /// <param name="pitch">The 14-bit pitch value</param>
    internal void SendPitchWheel(int channel, int pitch) 
    {
        if (!ExternalPlayback) 
        {
            Synth.PitchWheel(channel, (short)pitch, null);
            return;
        }

        channel %= 16;
        SendMidiMessage(new [] {
            (byte)(MidiMessage.Type.PitchWheel.ID() | channel),
            (byte)(pitch & 0x7f),
            (byte)(pitch >> 7)
        });
    }

    internal void AssignPort(int trackNum, int port) =>
        NewSequence.AssignPort(this, trackNum, port);

    private void LoadNewSequence(Midi midi) =>
        NewSequence.Load(this, midi);

    internal void ProcessEvent(MidiMessage ev, int trackIndex) =>
        Sequencer.ProcessEvent.Execute(this, ev, trackIndex);

    private bool SetTimeTo(double time, int? ticks = null) =>
        SetTime.To(this, time, ticks);
}