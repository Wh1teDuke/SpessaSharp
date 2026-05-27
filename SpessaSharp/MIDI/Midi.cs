using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SpessaSharp.MIDI.Read;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.MIDI.Write;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer;
using SpessaSharp.Synthesizer.Engine;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI;

/// <summary>Midi is the base of a complete MIDI file.</summary>
public sealed class Midi
{
    public enum CC
    {
        BankSelect, ModulationWheel, BreathController, UndefinedCC3,
        FootController, PortamentoTime, DataEntryMSB, MainVolume, Balance,
        UndefinedCC9, Pan, Expression, EffectControl1, EffectControl2,
        UndefinedCC14, UndefinedCC15, GeneralPurposeController1, 
        GeneralPurposeController2, GeneralPurposeController3, 
        GeneralPurposeController4, UndefinedCC20, UndefinedCC21, UndefinedCC22, 
        UndefinedCC23, Undefinedcc24, Undefinedcc25, Undefinedcc26, Undefinedcc27, 
        UndefinedCC28, Undefinedcc29, Undefinedcc30, Undefinedcc31, BankSelectLSB, 
        ModulationWheelLSB, BreathControllerLSB, UndefinedCC3LSB, FootControllerLSB,
        PortamentoTimeLSB, DataEntryLSB, MainVolumeLSB, BalanceLSB, UndefinedCC9LSB,
        PanLSB, ExpressionLSB, EffectControl1LSB, EffectControl2LSB, 
        UndefinedCC14lsb, UndefinedCC15lsb, UndefinedCC16lsb, UndefinedCC17lsb, 
        UndefinedCC18lsb, UndefinedCC19lsb, UndefinedCC20lsb, UndefinedCC21lsb, 
        UndefinedCC22lsb, UndefinedCC23lsb, UndefinedCC24lsb, UndefinedCC25lsb, 
        UndefinedCC26lsb, UndefinedCC27lsb, UndefinedCC28lsb, UndefinedCC29lsb, 
        UndefinedCC30lsb, UndefinedCC31lsb, SustainPedal, PortamentoOnOff, 
        SostenutoPedal, SoftPedal, LegatoFootSwitch, Hold2Pedal, SoundVariation, 
        FilterResonance, ReleaseTime, AttackTime, Brightness, DecayTime, 
        VibratoRate, VibratoDepth, VibratoDelay, SoundController10, 
        GeneralPurposeController5, GeneralPurposeController6, 
        GeneralPurposeController7, GeneralPurposeController8, 
        PortamentoControl, UndefinedCC85, UndefinedCC86, UndefinedCC87, 
        UndefinedCC88, UndefinedCC89, UndefinedCC90, ReverbDepth, TremoloDepth, 
        ChorusDepth, VariationDepth, PhaserDepth, DataIncrement, DataDecrement, 
        NonRegisteredParameterLSB, NonRegisteredParameterMSB, 
        RegisteredParameterLSB, RegisteredParameterMSB, UndefinedCC102lsb, 
        UndefinedCC103lsb, UndefinedCC104lsb, UndefinedCC105lsb, UndefinedCC106lsb,
        UndefinedCC107lsb, UndefinedCC108lsb, UndefinedCC109lsb, UndefinedCC110lsb,
        UndefinedCC111lsb, UndefinedCC112lsb, UndefinedCC113lsb, UndefinedCC114lsb,
        UndefinedCC115lsb, UndefinedCC116lsb, UndefinedCC117lsb, UndefinedCC118lsb,
        UndefinedCC119lsb, AllSoundOff, ResetAllControllers, LocalControlOnOff,
        AllNotesOff, OmniModeOff, OmniModeOn, MonoModeOn, PolyModeOn, 
    }
    
    public enum LoopType { Soft, Hard }
    
    public enum System { GM, GM2, GS, XG }
    
    /// <param name="Start">Start of the loop, in MIDI ticks.</param>
    /// <param name="End">End of the loop, in MIDI ticks.</param>
    /// <param name="Type">
    /// The type of the loop detected:<br/>
    /// - Soft - The playback will immediately jump to the loop start pointer without any further processing.<br/>
    /// - Hard - The playback will quickly process all messages from
    /// the start of the file to ensure that synthesizer is in the correct state.
    /// This is the default behavior.<br/><br/>
    ///
    /// Soft loop types are enabled for Touhou and GameMaker loop points.
    /// </param>
    public readonly record struct MidiLoop(int Start, int End, LoopType Type);
    
    /// <param name="Ticks">MIDI ticks of the change, absolute value from the start of the MIDI file.</param>
    public readonly record struct TempoChange(int Ticks, int MPQN)
    {
        ///<summary>New tempo in BPM.</summary>
        public double Tempo => 60_000_000d / MPQN;
    }

    /// <param name="Ticks">MIDI ticks of the change, absolute value from the start of the MIDI file.</param>
    /// <param name="Signature">New time signature. The bottom represents the power of two</param>
    public readonly record struct TimeSignatureChange(
        int Ticks, (int Top, int Bottom) Signature);

    /// <param name="MidiNote">The MIDI key number.</param>
    /// <param name="Start">Start of the note, in seconds.</param>
    /// <param name="Length">Length of the note, in seconds.</param>
    /// <param name="Velocity">The MIDI velocity of the note.</param>
    public readonly record struct NoteTime(
        int MidiNote, double Start, double Length, float Velocity);

    /// <param name="Tr">The track number of this event.</param>
    /// <param name="Ev">The index of this event within the track.</param>
    public readonly record struct TimelineEvent(int Tr, int Ev);

    /// <summary>The tracks in the sequence.</summary>
    public readonly List<Track> Tracks = [];

    public MidiMessage this[TimelineEvent ev] => Tracks[ev.Tr].Events[ev.Ev];

    /// <summary>
    /// A flattened, time‑sorted list of all events in the MIDI sequence.
    /// The order between the tracks is preserved.
    /// Each entry points to the event's track number and its index within that track.
    /// This is the recommended way of iterating over the MIDI sequence's events.
    /// </summary>
    private TimelineEvent[] _timeline = [];

    public ReadOnlySpan<TimelineEvent> Timeline => _timeline;

    public int MaxBeat => this[Timeline[^1]].Ticks / TimeDivision;
    
    /// <summary>The time division of the sequence, representing the number of MIDI ticks per beat.</summary>
    public int TimeDivision { get; internal set; } = 480;
    /// <summary>The duration of the sequence, in seconds.</summary>
    private double _duration;

    /// <summary>The duration of the sequence.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(_duration);

    /// <summary>
    /// The tempo changes in the sequence, ordered from the last change to the first.
    /// Each change is represented by an object with a MIDI tick position and a tempo value in beats per minute.
    /// </summary>
    public readonly List<TempoChange> TempoChanges = [
        new (0, 500_000)];
    
    /// <summary>
    /// The time signature changes in the sequence.
    /// Each change is represented by an object with a MIDI tick position and a time signature.
    /// </summary>
    public readonly List<TimeSignatureChange> TimeSignatureChanges = [
        new(0, (4, 2))]; // 4 / (2 ^ 2)

    /// <summary>
    /// Any extra metadata found in the file.
    /// These messages were deemed "interesting" by the parsing algorithm
    /// </summary>
    public readonly List<MidiMessage> ExtraMetadata = [];
    /// <summary>An array containing the lyrics of the sequence.</summary>
    public readonly List<MidiMessage> Lyrics = [];
    /// <summary>The Tick position of the first note-on event in the MIDI sequence.</summary>
    public int FirstNoteOn { get; private set; }
    /// <summary>The MIDI key range used in the sequence, represented by a minimum and maximum note value.</summary>
    public (int Min, int Max) KeyRange { get; private set; } = (127, 0);
    /// <summary>The tick position of the last voice event (such as note-on, note-off, or control change) in the sequence.</summary>
    public int LastVoiceEventTick { get; private set; }
    /// <summary>
    /// An array of channel offsets for each MIDI port, using the SpessaSynth method.
    /// The index is the port number and the value is the channel offset.
    /// </summary>
    public readonly List<int> PortChannelOffsetMap = [0];
    /// <summary>The loop points (in ticks) of the sequence, including both start and end points.</summary>
    public MidiLoop Loop { get; set; } = new (Start: 0, End: 0, LoopType.Hard);
    /// <summary>The file name of the MIDI sequence, if provided during parsing.</summary>
    public string? FileName { get; set; }

    public MidiFormat Format { get; internal set; } = MidiFormat.m0;

    /**
     * The RMID (Resource-Interchangeable MIDI) info data, if the file is RMID formatted.
     * Otherwise, this object is empty.
     * Info type: Chunk data as a binary array.
     * Note that text chunks contain a terminal zero byte.
     */
    public readonly Dictionary<
        RMidi.Info.Key, ArraySegment<byte>> RmidiInfo = [];
    /// <summary>The bank offset used for RMID files.</summary>
    public int BankOffset { get; internal set; }
    /// <summary>
    /// If the MIDI file is a Soft Karaoke file (.kar), this is set to true.
    /// https://www.mixagesoftware.com/en/midikit/help/HTML/karaoke_formats.html
    /// </summary>
    public bool IsKaraokeFile { get; private set; }
    /// <summary>Indicates if this file is a Multi-Port MIDI file.</summary>
    public bool IsMultiPort { get; private set; }
    /// <summary>If the MIDI file is a DLS RMIDI file.</summary>
    public bool IsDLSRMIDI { get; internal set; }
    /// <summary>The embedded sound bank in the MIDI file, represented as an ArrayBuffer, if available.</summary>
    public ArraySegment<byte>? EmbeddedSoundBank { get; set; }

    /// <summary>
    /// The raw, encoded MIDI name, represented as a Uint8Array.
    /// Useful when the MIDI file uses a different code page.
    /// Undefined if no MIDI name could be found.
    /// </summary>
    private ArraySegment<byte>? _binaryName;
    
    /// <summary>The encoding of the RMIDI info in file, if specified.</summary>
    public ArraySegment<byte>? GetInfoEncoding()
    {
        if (!RmidiInfo.TryGetValue(
                RMidi.Info.Key.InfoEncoding, out var infoEnc))
            return null;
        if (infoEnc.Count == 0)
            return null;

        var lengthToRead = infoEnc.Count;
        // Some files don't have a terminal zero
        if (infoEnc[^1] == 0) lengthToRead--;
        
        return Util.ReadBinaryString(infoEnc[..lengthToRead]);
    }

    /// <summary>Loads a MIDI file (SMF, RMIDI, XMF) from a given ArraySegment.</summary>
    /// <param name="bytes">The ArraySegment containing the binary file data.</param>
    /// <param name="fileName">The optional name of the file, will be used if the MIDI file does not have a name.</param>
    /// <remarks>
    /// This function reads the MIDI file format, extracts the header and track chunks,
    /// and populates the MIDI instance with the parsed data.
    /// It supports Standard MIDI Files (SMF), RIFF MIDI (RMIDI), and Extensible Music Format (XMF).
    /// It also handles embedded soundbanks in RMIDI files.
    /// If the file is an RMIDI file, it will extract the embedded soundbank and store
    /// it in the `embeddedSoundFont` property of the MIDI instance.
    /// If the file is an XMF file, it will parse the XMF structure and extract the MIDI data.
    /// </remarks>
    public static Midi From(ArraySegment<byte> bytes, string? fileName = null)
    {
        var mid = new Midi();
        var initialString = Util.ReadBinaryString(bytes[..4]);
        
        if (Ascii.Equals(initialString, "RIFF"u8))
        {
            // Possibly an RMID file (https://github.com/spessasus/sf2-rmidi-specification#readme)
            ReaderRMidi.Load(mid, bytes, fileName);
        }
        else if (Ascii.Equals(initialString, "XMF_"u8))
        {
            // Extensible Music Format
            ReaderXMF.Load(mid, bytes, fileName);
        }
        else
        {
            // Assume Standard MIDI File
            ReaderMidi.Load(mid, bytes, fileName);
        }

        return mid;
    }

    /// <summary>Loads a MIDI file (SMF, RMIDI, XMF) from a given File.</summary>
    /// <param name="file">The file containing the MIDI data</param>
    public static Midi From(FileInfo file)
    {
        using var stream = file.OpenRead();
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return From(data, file.Name);
    }
    
    /// <summary>Copies a MIDI.</summary>
    /// <param name="mid">The MIDI to copy.</param>
    /// <returns>The copied MIDI.</returns>
    public static Midi Copy(Midi mid) 
    {
        var m = new Midi();
        m.CopyFrom(mid);
        return m;
    }
    
    /// <summary>Copies a MIDI.</summary>
    /// <param name="mid">The MIDI to copy.</param>
    private void CopyFrom(Midi mid) 
    {
        CopyMetadataFrom(mid);

        // Deep copy (SpessaSharp: not really)
        EmbeddedSoundBank = mid.EmbeddedSoundBank;
        Tracks.Clear();
        // Deep copy of each track array
        foreach (var track in mid.Tracks)
            Tracks.Add(Track.Copy(track));
        _timeline = mid.Timeline.ToArray();
    }
    
    /// <summary>Converts MIDI ticks to time in seconds.</summary>
    /// <param name="ticks">The time in MIDI ticks.</param>
    /// <returns>The time in seconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Zero tempo changes</exception>
    /// <exception cref="InvalidOperationException">Last tempo change is not at tick 0</exception>
    public double MidiTicksToSeconds(int ticks)
    {
        // One is added automatically, but the user may have tampered with it
        ArgumentOutOfRangeException.ThrowIfZero(
            TempoChanges.Count,
            "There are no tempo changes in the sequence. At least one is needed.");

        // Sanity check
        if (TempoChanges[^1].Ticks != 0) throw SpessaException.Invalid(
            $"The last tempo change is not at 0 ticks. Got {
                TempoChanges[^1].Ticks} ticks.");
        
        ticks = Math.Max(ticks, 0);

        // Tempo changes are reversed, so the first element is the last tempo change
        // And the last element is the first tempo change
        // (always at tick 0 and tempo 120)
        // Find the last tempo change that has occurred
        var tempoIndex = -1;
        for (var i = 0; i < TempoChanges.Count; i++)
        {
            if (TempoChanges[i].Ticks > ticks) continue;
            tempoIndex = i;
            break;
        }
        
        Debug.Assert(tempoIndex != -1);

        var totalSeconds = 0d;
        while (tempoIndex < TempoChanges.Count) 
        {
            var tempo = TempoChanges[tempoIndex++];
            // Calculate the difference and tempo time
            var ticksSinceLastTempo = (double)ticks - tempo.Ticks;
            totalSeconds +=
                (ticksSinceLastTempo * 60) / (tempo.Tempo * TimeDivision);
            ticks = tempo.Ticks;
        }

        return totalSeconds;
    }

    /// <summary>Converts seconds to time in MIDI ticks.</summary>
    /// <param name="seconds">The time in seconds.</param>
    /// <returns>The time in MIDI ticks.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Zero tempo changes</exception>
    /// <exception cref="InvalidOperationException">Last tempo change is not at tick 0</exception>
    public int SecondsToMidiTicks(double seconds)
    {
        // One is added automatically, but the user may have tampered with it
        ArgumentOutOfRangeException.ThrowIfZero(
            TempoChanges.Count,
            "There are no tempo changes in the sequence. At least one is needed.");

        // Sanity check
        if (TempoChanges[^1].Ticks != 0) throw SpessaException.Invalid(
            $"The last tempo change is not at 0 ticks. Got {
                TempoChanges[^1].Ticks} ticks.");
        
        seconds = Math.Max(seconds, 0);
        if (seconds == 0) return 0;

        // Tempo changes are reversed, so the first element is the last tempo change
        // And the last element is the first tempo change
        // (always at tick 0 and tempo 120)

        var remainingSeconds = seconds;
        var totalTicks = 0d;
        for (var i = TempoChanges.Count - 1; i >= 0; i--) 
        {
            var currentTempo = TempoChanges[i];
            TempoChange? next = i == 0 ? null : TempoChanges[i - 1];

            var ticksToNextTempo = next is {} n
                ? n.Ticks - currentTempo.Ticks
                : int.MaxValue;

            var oneTickToSeconds =
                60d / (currentTempo.Tempo * TimeDivision);
            var secondsToNextTempo = ticksToNextTempo * oneTickToSeconds;

            // In this tempo change
            if (remainingSeconds <= secondsToNextTempo) 
            {
                totalTicks += remainingSeconds / oneTickToSeconds;
                return Util.Round(totalTicks);
            }

            // Not in this tempo change
            totalTicks += ticksToNextTempo;
            remainingSeconds -= secondsToNextTempo;
        }
        
        return Util.Round(totalTicks);
    }

    /// <summary>Converts MIDI ticks to time in beats.</summary>
    /// <param name="ticks">The time in MIDI ticks.</param>
    /// <returns>The time in beats.</returns>
    public int MidiTicksToBeats(int ticks) => ticks / TimeDivision;

    /// <summary>Converts beats to time in MIDI ticks.</summary>
    /// <param name="beats">The time in beats.</param>
    /// <returns>The time in MIDI ticks.</returns>
    public int BeatsToMidiTicks(int beats) => beats * TimeDivision;

    public void InsertBefore(TimelineEvent tl, MidiMessage msg) =>
        Tracks[tl.Tr].Add(msg, tl.Ev);

    public void InsertAfter(TimelineEvent tl, MidiMessage msg) =>
        Tracks[tl.Tr].Add(msg, tl.Ev + 1);

    /// <summary>
    /// Gets the used programs and keys for this MIDI file with a given sound bank.
    /// </summary>
    /// <param name="getPreset">The Preset provider.</param>
    /// <returns>The output data is a key-value pair: Preset -> (Key-Velocity)</returns>
    public PresetsWithKeyCombinations GetUsedProgramsAndKeys(
        IPresetGetter getPreset) => UsedProgramsAndKeys.Get(this, getPreset);

    /// <summary>
    /// Preloads all voices for this sequence in a given synth.
    /// This caches all the needed voices for playing back this sequencer, resulting in a smooth playback.
    /// The sequencer calls this function by default when loading the songs.
    /// </summary>
    /// <param name="synth">synth</param>
    public void Preload(SpessaSharpProcessor synth) 
    {
        Debug.WriteLine("Preloading samples ...");

        // Smart preloading: load only samples used in the midi!
        var used = GetUsedProgramsAndKeys(
            synth.SoundBankManager);
        foreach (var (preset, combos) in used)
        {
            Debug.WriteLine($"Preloading used samples on {preset.Name} ...");
            foreach (var (midiNote, velocity) in combos) 
                synth.GetVoicesForPreset(preset, midiNote, velocity);
        }
        synth.SoundBankManager.Cache.Done();
    }
    
    /// <summary>
    /// Updates all internal values of the MIDI.
    /// </summary>
    /// <param name="sortEvents">If the events should be sorted by ticks. Recommended to be true.</param>
    public void Flush(bool sortEvents = true) 
    {
        if (sortEvents) 
        {
            foreach (var t in Tracks) 
            {
                // Sort the track by ticks
                Util.Sort(t.EventList, (e1, e2) => e1.Ticks - e2.Ticks);
                //t.Events.Sort((e1, e2) => e1.Ticks - e2.Ticks);
            }
        }

        ParseInternal();
    }
    
    /// <summary>Calculates all note times in seconds.</summary>
    /// <param name="minDrumLength">The shortest a drum note (channel 10) can be, in seconds.</param>
    /// <returns>An array of 16 channels, each channel containing its notes,
    /// with their key number, velocity, absolute start time and length in seconds.</returns>
    public List<NoteTime>[] GetNoteTimes(int minDrumLength = 0) =>
        GetNoteTimes(this, minDrumLength);

    private static List<NoteTime>[] GetNoteTimes(
        Midi midi, int minDrumLength = 0)
    {
        // An array of 16 arrays (channels)
        var noteTimes = new List<NoteTime>[16];

        foreach (ref var l in noteTimes.AsSpan())
            l = [];

        var elapsedTime = 0d;
        var oneTickToSeconds = 60 / (120d * midi.TimeDivision);
        var unfinished = 0;
        
        // Store notes that we started but didn't finish
        // MIDI note: index to the note times (we accept multiple notes)
        var unfinishedNotes = new Dictionary<int, List<int>>[16];
        foreach (ref var l in unfinishedNotes.AsSpan())
            l = [];
        
        var i = 0;
        var timeline = midi.Timeline;
        while (i < timeline.Length)
        {
            var tl = timeline[i];
            var ev = midi[tl];
            var status = ev.StatusByte.Byte >> 4;
            var channel = ev.StatusByte.Byte & 0x0f;
            
            // Note off
            if (status == 0x8) NoteOff(ev.Data[0], channel);
            
            // Note on
            else if (status == 0x9)
            {
                var midiNote = ev.Data[0];
                var velocity = ev.Data[1];
                if (velocity == 0)
                {
                    // Never mind, its note off
                    NoteOff(midiNote, channel);
                }
                else
                {
                    // Stop previous
                    NoteOff(midiNote, channel);
                    var noteTime = new NoteTime(
                        midiNote, 
                        elapsedTime, 
                        -1, 
                        velocity);
                    var times = noteTimes[channel];
                    times.Add(noteTime);
                    var unfinishedChannel = unfinishedNotes[channel];
                    if (!unfinishedChannel.ContainsKey(midiNote))
                        unfinishedChannel[midiNote] = [];
                    unfinishedNotes[channel][midiNote].Add(times.Count - 1);
                    unfinished++;
                }
            }
            // Set Tempo
            else if (ev.StatusByte.Byte == 0x51)
                oneTickToSeconds = 60d / (ev.GetTempo() * midi.TimeDivision);

            if (++i >= timeline.Length) break;

            elapsedTime += 
                oneTickToSeconds * (midi[timeline[i]].Ticks - ev.Ticks);
        }
        
        // Finish the unfinished notes
        if (unfinished > 0)
        {
            // For every channel, for every note that is unfinished (has -1 length)
            for (var channel = 0; channel < unfinishedNotes.Length; channel++)
            {
                foreach (var noteIndexes in unfinishedNotes[channel].Values)
                {
                    foreach (var noteIndex in noteIndexes)
                    {
                        ref var note = ref CollectionsMarshal.AsSpan(
                            noteTimes[channel])[noteIndex];
                        note = note with { Length = elapsedTime - note.Start, };   
                    }
                }
            }
        }
        
        return noteTimes;

        void NoteOff(int midiNote, int channel)
        {
            var ch = unfinishedNotes[channel];
            if (!ch.TryGetValue(midiNote, out var noteIndexes))
                return;
            
            if (noteIndexes.Count == 0) return;
            // FIFO, match behavior of the synth
            var noteIndex = noteIndexes[^1];
            noteIndexes.RemoveAt(noteIndexes.Count - 1);
            
            ref var note = ref CollectionsMarshal.AsSpan(
                noteTimes[channel])[noteIndex];

            var time = elapsedTime - note.Start;
            note = note with
            {
                Length = channel == Synthesizer.Synthesizer.DEFAULT_PERCUSSION
                    ? Math.Max(time, minDrumLength)
                    : time,
            };
            
            ch.Remove(midiNote);
            unfinished--;
        }
    }

    /// <summary>
    /// Exports the midi as a standard MIDI file.
    /// </summary>
    /// <returns>The binary file data.</returns>
    /// <exception cref="Exception"></exception>
    public ArraySegment<byte> Write() => WriterMidi.Save(this);

    /// <summary>
    /// Writes an RMIDI file. Note that this method modifies the MIDI file <b>in-place</b>.
    /// </summary>
    /// <param name="soundBank">The binary sound bank to embed into the file.</param>
    /// <param name="options">Extra options for writing the file.</param>
    /// <returns>The binary file data.</returns>
    public ArraySegment<byte> WriteRMIDI(
        ArraySegment<byte> soundBank, WriterRMidi.Options? options = null) =>
            WriterRMidi.Save(
                this, soundBank, options ?? WriterRMidi.Options.Default);

    /// <summary>
    /// Allows easily modifying the sequence's programs and controllers.
    /// This is a very sophisticated method that supports various MIDI systems and inserts/deletes messages appropriately.
    /// <remarks>This modifies the MIDI sequence <b>in-place</b>.</remarks>
    /// </summary>
    /// <param name="opts">Options to modify the midi</param>
    public void Modify(MidiModifyOptions opts) =>
        MidiEditor.Modify(this, opts);

    /// <summary>
    /// Modifies the sequence *in-place* according to the locked presets and controllers in the given snapshot.
    /// Note that this ignores the MIDI parameters and only applies system parameter tuning.
    /// </summary>
    /// <param name="snapshot">The snapshot to apply.</param>
    public void Apply(SynthesizerSnapshot snapshot) =>
        ApplySnapshot.To(this, snapshot);

    /// <summary>Gets the MIDI's decoded name.</summary>
    /// <param name="encoding">The encoding to use if the MIDI uses an extended code page.</param>
    /// <remarks>RMIDI encoding overrides the provided encoding.</remarks>
    public string? GetName(string encoding = "Shift_JIS") 
    {
        string? rawName = null;
        var n = (string?)GetRMidiInfo(RMidi.Info.Key.Name);
        if (n != null) return n.Trim();

        if (_binaryName is {} bName) 
        {
            encoding = (string?)GetRMidiInfo(RMidi.Info.Key.MidiEncoding)
                       ?? encoding;
            try 
            {
                // Trim since "                                                                "
                // Is not a valid name
                // MIDI file with that name: th07_10.mid
                rawName = Encoding.GetEncoding(encoding).GetString(bName).Trim();
            } 
            catch (Exception error) 
            { Debug.WriteLine($"[WARN] Failed to decode MIDI name: {error}"); }
        }

        return rawName ?? FileName;
    }
    
    /// <summary>
    /// Gets the decoded extra metadata as text and removes any unneeded characters (such as "@T" for karaoke files)
    /// </summary>
    /// <param name="encoding">The encoding to use if the MIDI uses an extended code page.</param>
    /// /// <remarks> RMIDI encoding overrides the provided encoding.</remarks>
    /// <returns></returns>
    public IEnumerable<string> GetExtraMetadata(string encoding = "Shift_JIS") 
    {
        encoding = GetInfoEncoding() is {} ie
            ? Util.ToString(ie) : encoding;
        var decoder = Encoding.GetEncoding(encoding);
        return ExtraMetadata.Select((d) => {
            var decoded = decoder.GetString(d.Data);
            return RegexExt.RmidiMeta().Replace(decoded, "").Trim();
        });
    }
    
    /// <summary>
    /// Sets a given RMIDI info value.
    /// </summary>
    /// <param name="infoType">The type to set.</param>
    /// <param name="infoData">The value to set it to.</param>
    /// <remarks>This sets the Info encoding to utf-8.</remarks>
    public void SetRMidiInfo(RMidi.Info.Key infoType, object infoData) 
    {
        RmidiInfo[RMidi.Info.Key.InfoEncoding] =
            Util.GetStringBytes("utf-8", true);

        if (infoType == RMidi.Info.Key.Picture) 
        {
            // TS2339: Property buffer does not exist on type string | ArrayBuffer | Date
            // Property buffer does not exist on type string
            RmidiInfo[RMidi.Info.Key.Picture] = (ArraySegment<byte>)infoData;
        } 
        else if (infoType == RMidi.Info.Key.CreationDate) 
        {
            RmidiInfo[RMidi.Info.Key.CreationDate] = 
                Util.GetStringBytes(Util.ToIsoString((DateTime)infoData), true);
        } 
        else
        {
            var encoder = Encoding.UTF8.GetEncoder();
            var len = encoder.GetByteCount((string)infoData, true);
            var data = new byte[len + 1]; // Add zero byte
            var encoded = Encoding.UTF8.GetEncoder().GetBytes(
                (string)infoData, data, true);
            Debug.Assert(encoded == len);
            RmidiInfo[infoType] = data;
        }
    }

    public RMidi.Info? GetRMidiInfo()
    {
        if (RmidiInfo.Count == 0) return null;

        return new RMidi.Info(
            Name:           (string?)GetRMidiInfo(RMidi.Info.Key.Name),
            Engineer:       (string?)GetRMidiInfo(RMidi.Info.Key.Engineer),
            Artist:         (string?)GetRMidiInfo(RMidi.Info.Key.Artist),
            Album:          (string?)GetRMidiInfo(RMidi.Info.Key.Album),
            Genre:          (string?)GetRMidiInfo(RMidi.Info.Key.Genre),
            Picture:        (ArraySegment<byte>?)GetRMidiInfo(RMidi.Info.Key.Picture),
            Comment:        (string?)GetRMidiInfo(RMidi.Info.Key.Comment),
            CreationDate:   (DateTime?)GetRMidiInfo(RMidi.Info.Key.CreationDate),
            Copyright:      (string?)GetRMidiInfo(RMidi.Info.Key.Copyright),
            InfoEncoding:   (string?)GetRMidiInfo(RMidi.Info.Key.InfoEncoding),
            MidiEncoding:   (string?)GetRMidiInfo(RMidi.Info.Key.MidiEncoding),
            Software:       (string?)GetRMidiInfo(RMidi.Info.Key.Software),
            Subject:        (string?)GetRMidiInfo(RMidi.Info.Key.Subject)
        );
    }
    
    /// <summary>
    /// Gets a given chunk from the RMIDI information, undefined if it does not exist.
    /// </summary>
    /// <param name="infoType">The metadata type.</param>
    /// <returns>String, Date, ArraySegment or null.</returns>
    public object? GetRMidiInfo(RMidi.Info.Key infoType)
    {
        if (!RmidiInfo.TryGetValue(infoType, out var val)) 
            return null;

        var encoding = GetInfoEncoding() is {} ie
            ? Util.ToString(ie) : "UTF-8";

        if (infoType == RMidi.Info.Key.Picture)
            return val;

        if (infoType == RMidi.Info.Key.CreationDate)
            return Util.ParseDateString(
                Util.ToString(Util.ReadBinaryString(val)));

        try
        {
            var decoder = Encoding.GetEncoding(encoding);
            var infoBuffer = val;
            if (infoBuffer[^1] == 0) 
            {
                // Do not decode the terminal byte
                infoBuffer = infoBuffer[..^1];
            }
            return decoder.GetString(infoBuffer).Trim();
        } 
        catch (Exception error) 
        {
            Debug.WriteLine($"[WARN] Failed to decode {infoType} name: {error}");
            return null;
        }
    }

    /// <summary>
    /// Iterates over the MIDI file, ordered by the time the events happen.
    /// You probably should use the `timeline` property
    /// if you're not mutating the MIDI in the iteration loop.
    /// </summary>
    public Enumerable Iterate() => new (this);

    public readonly ref struct Enumerable(Midi midi)
    { public Enumerator GetEnumerator() => new(midi); }
    
    public ref struct Enumerator
    {
        public readonly ref struct Entry(
            ref MidiMessage msg, int track, ArraySegment<int> indexes)
        {
            public readonly ref MidiMessage Message = ref msg;
            public readonly int TrackNum = track;
            public readonly ArraySegment<int> EventIndexes = indexes;
        }

        private readonly Midi _midi;
        private readonly ArraySegment<int> _eventIndexes;
        private int _remainingTracks;
        private int? _prevTrackNum;

        public Enumerator(Midi midi)
        {
            _midi = midi;
            _remainingTracks = _midi.Tracks.Count;

            // Indexes for tracks
            _eventIndexes = Util.Rent<int>(_midi.Tracks.Count);
            _eventIndexes.AsSpan().Clear();
        }

        public Entry Current { get; private set; }

        public bool MoveNext()
        {
            if (_prevTrackNum is {} pTrackNum)
            {
                _prevTrackNum = null;
                _eventIndexes[pTrackNum]++;
            }
            
            while (_remainingTracks > 0) 
            {
                var trackNum = 0;
                var ticks = int.MaxValue;
                
                for (var i = 0; i < _midi.Tracks.Count; i++)
                {
                    var tr = _midi.Tracks[i].Events;
                    if (_eventIndexes[i] >= tr.Length) continue;

                    if (tr[_eventIndexes[i]].Ticks < ticks)
                    {
                        trackNum = i;
                        ticks = tr[_eventIndexes[i]].Ticks;
                    }
                }
                
                var track = _midi.Tracks[trackNum].EventList;
                if (_eventIndexes[trackNum] >= track.Count) 
                {
                    _remainingTracks--;
                    continue;
                }

                var idx = _eventIndexes[trackNum];
                Current = new Entry(
                    ref CollectionsMarshal.AsSpan(track)[idx], 
                    trackNum, 
                    _eventIndexes);
                _prevTrackNum = trackNum;
                return true;
            }

            return false;
        }

        public void Dispose() => Util.Return(_eventIndexes);
    }
    
    // INTERNAL USE ONLY!
    private void CopyMetadataFrom(Midi mid) 
    {
        // Properties can be assigned
        FileName = mid.FileName;
        TimeDivision = mid.TimeDivision;
        _duration = mid._duration;
        FirstNoteOn = mid.FirstNoteOn;
        LastVoiceEventTick = mid.LastVoiceEventTick;
        Format = mid.Format;
        BankOffset = mid.BankOffset;
        IsKaraokeFile = mid.IsKaraokeFile;
        IsMultiPort = mid.IsMultiPort;
        IsDLSRMIDI = mid.IsDLSRMIDI;

        // Copying arrays
        TempoChanges.Clear();
        TimeSignatureChanges.Clear();
        ExtraMetadata.Clear();
        Lyrics.Clear();
        PortChannelOffsetMap.Clear();
        
        TempoChanges.AddRange(mid.TempoChanges);
        TimeSignatureChanges.AddRange(mid.TimeSignatureChanges);
        ExtraMetadata.AddRange(mid.ExtraMetadata);
        Lyrics.AddRange(mid.Lyrics);
        PortChannelOffsetMap.AddRange(mid.PortChannelOffsetMap);
        
        _binaryName = mid._binaryName;

        // Copying objects
        Loop = mid.Loop;
        KeyRange = mid.KeyRange;

        RmidiInfo.Clear();
        foreach (var (key, val) in mid.RmidiInfo) 
            RmidiInfo[key] = val;
    }

    /// <summary>Parses internal MIDI values</summary>
    private void ParseInternal()
    {
        Debug.WriteLine("Interpreting MIDI events...");
        
        /*
         * For karaoke files, text events starting with @T are considered titles,
         * usually the first one is the title, and the latter is things such as "sequenced by" etc.
         */
        var karaokeHasTitle = false;

        // Reset values
        // https://github.com/spessasus/spessasynth_core/issues/20
        TempoChanges.Clear();
        TempoChanges.Add(new TempoChange(0, 500_000));
        TimeSignatureChanges.Clear();
        TimeSignatureChanges.Add(new TimeSignatureChange(0, (4, 2)));
        ExtraMetadata.Clear();
        Lyrics.Clear();
        FirstNoteOn = 0;
        KeyRange = (127, 0);
        LastVoiceEventTick = 0;
        PortChannelOffsetMap.Clear();
        PortChannelOffsetMap.Add(0);
        Loop = new MidiLoop(0, 0, LoopType.Hard);
        // Do not reset RMIDI info (parsed in MIDI loader)
        // Do not reset bank offset (parsed in MIDI loader)
        IsKaraokeFile = false;
        IsMultiPort = false;

        // Name is already provided in RMIDInfo
        var nameDetected = RmidiInfo.ContainsKey(RMidi.Info.Key.Name);
        
        // Loop tracking
        int? loopStart = null;
        int? loopEnd = null;
        var loopType = LoopType.Hard;
        var usedChannels = new HashSet<int>();

        foreach (var track in Tracks)
        {
            usedChannels.Clear();
            var trackHasVoiceMessages = false;

            for (var i = 0; i < track.Events.Length; i++)
            {
                var e = track.Events[i];
                // Check if it's a voice message
                var sb = e.StatusByte.Byte;
                
                if (sb is >= 0x80 and < 0xf0)
                {
                    trackHasVoiceMessages = true;
                    // Last voice event tick
                    if (e.Ticks > LastVoiceEventTick)
                        LastVoiceEventTick = e.Ticks;
                    
                    // Interpret the voice message
                    var s = sb & 0xf0;
                    
                    if (s == MidiMessage.Type.ControllerChange.ID())
                    {
                        // Cc change: loop points
                        switch (e.Data[0])
                        {
                            // Touhou
                            case 2:
                            // RPG Maker
                            case 111:
                                // For Touhou and RPG Maker, the data value must be 0.
                                if (e.Data[1] == 0) loopStart = e.Ticks;
                                break;
                            // EMIDI/XMI
                            case 116:
                                loopStart = e.Ticks;
                                break;
                            // Touhou
                            case 4:
                            // EMIDI/XMI
                            case 117:
                                // For Touhou loops, the data value must be 0.
                                if (loopEnd == null &&
                                    (e.Data[0] != 4 ||
                                    (e.Data[0] == 4 &&
                                     e.Data[1] == 0)))
                                {
                                    loopEnd = e.Ticks;
                                    loopType = LoopType.Soft;
                                }
                                else
                                {
                                    // This controller has occurred more than once;
                                    // This means
                                    // That it doesn't indicate the loop
                                    loopEnd = 0;
                                    loopType = LoopType.Hard;
                                }
                                break;

                            case 0:
                                // Check RMID
                                if (IsDLSRMIDI && e.Data[1] != 0 && e.Data[1] != 127)
                                {
                                    Debug.WriteLine("DLS RMIDI with offset 1 detected!");
                                    BankOffset = 1;
                                }
                                break;
                        }
                    }
                    // Note on: used notes tracking and key range
                    else if (s == MidiMessage.Type.NoteOn.ID())
                    {
                        usedChannels.Add(sb & 0x0f);
                        var note = e.Data[0];
                        
                        KeyRange = (
                            Min: Math.Min(KeyRange.Min, note),
                            Max: Math.Max(KeyRange.Max, note));
                    }
                }

                var eventText = Util.ReadBinaryString(e.Data);
                // Interpret the message
                if (e.StatusByte.Is(MidiMessage.Type.EndOfTrack))
                {
                    if (i != track.Events.Length - 1) 
                    {
                        track.DeleteEvent(i--);
                        Debug.WriteLine("[WARN] Unexpected EndOfTrack. Removing");
                    }
                }
                else if (e.StatusByte.Is(MidiMessage.Type.TimeSignature))
                {
                    // Add the time signature change
                    var newTimeSignature = new TimeSignatureChange(
                        e.Ticks, (e.Data[0], e.Data[1]));
                    // Check against default Time sign
                    if (newTimeSignature != TimeSignatureChanges[^1])
                        TimeSignatureChanges.Add(newTimeSignature);
                }
                else if (e.StatusByte.Is(MidiMessage.Type.SetTempo))
                {
                    // Add the tempo change
                    TempoChanges.Add(new TempoChange(
                        e.Ticks, 
                        Util.ReadBigEndian(e.Data[..3])));
                }
                else if (e.StatusByte.Is(MidiMessage.Type.Marker))
                {
                    // Check for loop markers
                    var trimEventText = eventText[Ascii.Trim(eventText)];
                    if (Util.EqualsIgnoreCase(
                            trimEventText, "start", "loopstart"))
                        loopStart = e.Ticks;
                    else if (Util.EqualsIgnoreCase(trimEventText, "loopend"))
                        loopEnd = e.Ticks;
                }
                else if (e.StatusByte.Is(MidiMessage.Type.Copyright))
                {
                    ExtraMetadata.Add(e);
                }

                // Fallthrough
                else if (e.StatusByte.Is(MidiMessage.Type.Text) ||
                         e.StatusByte.Is(MidiMessage.Type.Lyric))
                {
                    if (e.StatusByte.Is(MidiMessage.Type.Lyric))
                    {
                        // Note here: .kar files sometimes just use...
                        // Lyrics instead of text because why not (of course)
                        // Perform the same check for @KMIDI KARAOKE FILE
                        var trimEventText = eventText[Ascii.Trim(eventText)];
                        if (trimEventText.AsSpan().StartsWith("@KMIDI KARAOKE FILE"u8))
                        {
                            IsKaraokeFile = true;
                            Debug.WriteLine("Karaoke MIDI detected!");
                        }

                        if (IsKaraokeFile)
                        {
                            e = e with { StatusByte = MidiMessage.Type.Text };
                            track.EventList[i] = e;
                        }
                        else
                        {
                            Lyrics.Add(e);
                        }
                    }
                    
                    if (e.StatusByte.Is(MidiMessage.Type.Text))
                    {
                        // Possibly Soft Karaoke MIDI file
                        // It has a text event at the start of the file
                        // "@KMIDI KARAOKE FILE"
                        var checkedText = 
                            eventText[Ascii.Trim(eventText)].AsSpan();
                        if (checkedText.StartsWith("@KMIDI KARAOKE FILE"u8))
                        {
                            IsKaraokeFile = true;
                            Debug.WriteLine("Karaoke MIDI detected!");
                        }
                        else if (IsKaraokeFile)
                        {
                            // Check for @T (title)
                            // Or @A because it is a title too sometimes?
                            // IDK it's strange
                            if (checkedText.StartsWith("@T"u8) ||
                                checkedText.StartsWith("@A"u8)) 
                            {
                                if (karaokeHasTitle) 
                                {
                                    // Append to metadata
                                    ExtraMetadata.Add(e);
                                } else 
                                {
                                    _binaryName = e.Data[2..];
                                    karaokeHasTitle = true;
                                    nameDetected = true;
                                }
                            } 
                            else if (!checkedText.StartsWith((byte)'@')) 
                            {
                                // Non @: the lyrics
                                Lyrics.Add(e);
                            }
                        }
                    }
                }
            }
            
            // Add used channels
            foreach (var c in usedChannels)
                track.Channels.Set(c, true);
            
            // Track name
            track.Name = "";
            var trackNameIdx = track.EventList.FindIndex(
                e => e.StatusByte.Is(MidiMessage.Type.TrackName));
            // Don't add the first track's name as it's not metadata, it's the name!
            if (trackNameIdx != -1 && Tracks.IndexOf(track) > 0)
            {
                var trackName = track.Events[trackNameIdx];
                var segName = Util.ReadBinaryString(trackName.Data);
                track.Name = Util.ToString(segName);
                // If the track has no voice messages, its "track name" event (if it has any)
                // Is some metadata.
                // Add it to copyright
                if (
                    !trackHasVoiceMessages &&
                    !track.Name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                {
                    ExtraMetadata.Add(trackName);
                }
            }
        }
        
        // Reverse the tempo changes
        TempoChanges.Reverse();
        Debug.WriteLine("Correcting loops, ports and detecting notes...");

        var fNoteOn = int.MaxValue;
        foreach (var track in Tracks)
        {
            var firstNoteOn = track.EventList.FindIndex(
                e => e.StatusByte.Status == MidiMessage.ID(MidiMessage.Type.NoteOn));
            if (firstNoteOn != -1)
                fNoteOn = Math.Min(fNoteOn, track.Events[firstNoteOn].Ticks);
        }
        
        FirstNoteOn = fNoteOn;
        
        Debug.WriteLine($"First note-on detected at: {FirstNoteOn} ticks!");
        
        // Loop detection
        loopStart ??= FirstNoteOn;

        if (loopEnd is null or 0) loopEnd = LastVoiceEventTick;
        Loop = new MidiLoop(loopStart.Value, loopEnd.Value, loopType);
        
        // Loop fix:
        // Rarely loopEnd is declared via meta, just after the last voice event, treat the loop event as voice
        // Testcase: 7. Bad Apple!! (icebhm23230 - XG).mid
        LastVoiceEventTick = Math.Max(LastVoiceEventTick, Loop.End);
        
        Debug.WriteLine($"Loop points: start: {Loop.Start} end: {Loop.End}");
        
        // Determine ports
        var portOffset = 0;
        PortChannelOffsetMap.Clear();
        foreach (var track in Tracks) 
        {
            track.Port = -1;
            if (!track.Channels.HasAnySet()) continue;

            foreach (var e in track.Events) 
            {
                if (!e.StatusByte.Is(MidiMessage.Type.MidiPort))
                    continue;

                var port = e.Data[0];
                track.Port = port;

                if (PortChannelOffsetMap.Count <= port
                    || PortChannelOffsetMap[port] == -1)
                {
                    while (PortChannelOffsetMap.Count <= port)
                        PortChannelOffsetMap.Add(-1);
                    PortChannelOffsetMap[port] = portOffset;
                    portOffset += 16;
                }
            }
        }
        
        // Fix empty port channel offsets (do a copy to turn empty slots into undefined so the map goes over them)
        CollectionsMarshal.AsSpan(PortChannelOffsetMap).Replace(-1, 0);
        
        // Fix midi ports:
        // MIDI tracks without ports will have a value of -1
        // If all ports have a value of -1, set it to 0,
        // Otherwise take the first midi port and replace all -1 with it,
        // Why would we do this?
        // Some midis (for some reason) specify all channels to port 1 or else,
        // But leave the conductor track with no port pref.
        // This spessasynth to reserve the first 16 channels for the conductor track
        // (which doesn't play anything) and use the additional 16 for the actual ports.
        var defaultPort = int.MaxValue;
        foreach (var track in Tracks) 
        {
            if (track.Port != -1 && defaultPort > track.Port)
                defaultPort = track.Port;
        }

        if (defaultPort == int.MaxValue)
            defaultPort = 0;
        
        foreach (var track in Tracks)
            if (track.Port == -1) //|| track.Port == undefined) 
                track.Port = defaultPort;
        
        // Add fake port if empty
        if (PortChannelOffsetMap.Count == 0)
            PortChannelOffsetMap.Add(0);
        if (PortChannelOffsetMap.Count < 2) 
        {
            Debug.WriteLine("No additional MIDI Ports detected.");
        } 
        else 
        {
            IsMultiPort = true;
            Debug.WriteLine("MIDI Ports detected!");
        }
        
        // MIDI name
        if (!nameDetected) 
        {
            if (Tracks.Count > 1) 
            {
                // If more than 1 track and the first track has no notes,
                // Just find the first trackName in the first track.
                if (
                    !Tracks[0].EventList.Any(
                        message =>
                            message.StatusByte.GE(MidiMessage.Type.NoteOn)
                            && message.StatusByte.L(MidiMessage.Type.PolyPressure)
                    )
                ) {
                    var name = Tracks[0].EventList.FindIndex(
                        message => message.StatusByte.Is(
                            MidiMessage.Type.TrackName)
                    );
                    if (name != -1)
                        _binaryName = Tracks[0].Events[name].Data;
                }
            } 
            else 
            {
                // If only 1 track, find the first "track name" event
                var name = Tracks[0].EventList.FindIndex(
                    message => message.StatusByte.Is(
                            MidiMessage.Type.TrackName)
                );
                if (name != -1)
                    _binaryName = Tracks[0].Events[name].Data;
            }
        }
        
        // Remove empty strings
        ExtraMetadata.RemoveAll(c => c.Data.Count == 0);

        // Sort lyrics (https://github.com/spessasus/spessasynth_core/issues/10)
        if (Lyrics.Count > 0)
        {
            var sorted =
                Lyrics.OrderBy(e => e.Ticks).ToList();
            Lyrics.Clear();
            Lyrics.AddRange(sorted);
        }
        
        // If the first event is not at 0 ticks, add a track name
        // https://github.com/spessasus/SpessaSynth/issues/145
        if (Tracks.All(t => t.Events[0].Ticks != 0)) 
        {
            var track = Tracks[0];
            track.Add(new MidiMessage(
                0,
                MidiMessage.Type.TrackName,
                // Can copy
                _binaryName ?? ArraySegment<byte>.Empty), 0);
        }
        
        _duration = MidiTicksToSeconds(LastVoiceEventTick);
        
        // Get sorted events
        _timeline = new TimelineEvent[Tracks.Sum(t => t.Events.Length)];
        var tlIndex = 0;

        foreach (var e in Iterate())
            _timeline[tlIndex++] = new TimelineEvent(
                Tr: e.TrackNum, Ev: e.EventIndexes[e.TrackNum]);
        
        Debug.Assert(_timeline.Length == tlIndex);
        
        // Invalidate raw name if empty
        if (_binaryName?.Count == 0)
            _binaryName = null;
        
        Debug.WriteLine(
            $"MIDI file parsed. Total tick time: {LastVoiceEventTick
            }, total seconds time: {
                TimeSpan.FromSeconds(Math.Ceiling(_duration)).TotalSeconds}");
    }
}