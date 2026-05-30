/*
MIT No Attribution
Copyright 2026 WhiteDuke
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SFML.Audio;
using SFML.System;
using SpessaSharp.MIDI;
using SpessaSharp.Sequencer;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Synthesizer.Engine.Sysex;
using SpessaSharp.Utils;

namespace SSTool.Util;

public sealed class Player: IDisposable
{
    public delegate bool AudioPlugin(
        float[][] inputs, float[][] outputs, int channels, int count);
    
    private const int BLOCK_SIZE = Synthesizer.SPESSA_BUFSIZE;
    
    public enum PlayerStatus: byte { Stop, Pause, Play }

    private readonly record struct Cmd(
        Cmd.CKind Kind, long Arg1 = 0, object? Arg2 = null)
    {
        public enum CKind
        {
            None,
            // Player
            Play, Stop, Pause, Loop,
            SetProgress, SetSoundBank,
            SetMidi, RemMidi,
            // Config
            SetInterpolationType,
            SetReverbMacro, SetChorusMacro, SetDelayMacro,
            // Midi
            NoteOn, NoteOff, Program,
        }
    }
    
    private sealed class SpessaStream: SoundStream
    {
        public const float BASE_VOL = 200;
        
        public static readonly SoundChannel[] Channels = [
            SoundChannel.FrontLeft, SoundChannel.FrontRight,];

        public readonly record struct Buffer(
            Spsc<IntPtr> Samples, SemaphoreSlim Lock);

        public readonly Buffer Full, Empty;

        private readonly CancellationToken _cancel;
        private readonly short[] _samples =
            new short[BLOCK_SIZE * Channels.Length];

        public SpessaStream(
            int sampleRate, 
            TimeSpan bufferLen,
            CancellationToken cancel)
        {
            Initialize(
                (uint)Channels.Length,
                (uint)sampleRate,
                Channels);
            
            var c   = (int)Math.Clamp(Math.Round(
                    sampleRate / (float)BLOCK_SIZE * bufferLen.TotalSeconds),
                    1, short.MaxValue);
            Full    = new Buffer(
                new Spsc<IntPtr>(c), new SemaphoreSlim(0, c));
            Empty   = new Buffer(
                new Spsc<IntPtr>(c), new SemaphoreSlim(c, c));
            Volume  = BASE_VOL;
            _cancel = cancel;
        }
    
        protected override bool OnGetData(out short[] samples)
        {
            samples = _samples;
            
            Full.Lock.Wait(_cancel);
            if (_cancel.IsCancellationRequested)
                return false;
            if (!Full.Samples.TryDequeue(out var ptr))
                throw new Exception("Impossible");

            Span<short> smpls;
            unsafe { smpls = new Span<short>((void*)ptr, _samples.Length); }

            smpls.CopyTo(_samples);
            smpls.Clear();
            
            if (!Empty.Samples.TryEnqueue(ptr))
                throw new Exception("Impossible");
            Empty.Lock.Release();

            return true;
        }

        protected override void OnSeek(Time timeOffset) {}
    }

    public readonly string Name;
    public volatile bool Debug;
    
    private readonly SpessaStream _stream;
    private readonly CancellationTokenSource _cts;
    private readonly Spsc<Cmd> _commands = new(64);
    private readonly IntPtr[] _ptrs;

    private long _avg;
    private long _progress;
    private volatile AudioPlugin? _plugin;
    private volatile PlayerStatus _status;
    private volatile bool _disposed;
    private volatile int _beat;

    public Player(
        SpessaSharpSequencer sequencer,
        string name         = "Player",
        TimeSpan? bufferLen = null)
    {
        Name            = name;
        Sequencer       = sequencer;
        _cts   = new CancellationTokenSource();
        _stream         = new SpessaStream(
            sequencer.Synth.SampleRate,
            bufferLen ?? TimeSpan.FromSeconds(.1),
            _cts.Token);
        _ptrs           = new IntPtr[_stream.Full.Samples.Capacity];

        for (var i = 0; i < _ptrs.Length; i++)
        {
            var bytesLen =
                BLOCK_SIZE * SpessaStream.Channels.Length * sizeof(short);
            nint mem;
            unsafe { mem = (IntPtr)NativeMemory.Alloc((UIntPtr)bytesLen); }
            _ptrs[i] = mem;
            _stream.Empty.Samples.TryEnqueue(mem);
        }

        var loop = new Thread(SeqLoop)
        {
            Name            = $"[{Name}] Loop",
            IsBackground    = true,
            Priority        = ThreadPriority.AboveNormal,
        };

        loop.Start();
    }

    public PlayerStatus Status
    {
        get => _status;
        set
        {
            switch (value)
            {
                case PlayerStatus.Stop:
                    Stop();
                    break;
                case PlayerStatus.Pause:
                    Pause();
                    break;
                case PlayerStatus.Play:
                    Play();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(value), value, null);
            }
        }
    }

    public TimeSpan AvgProcess =>
        TimeSpan.FromTicks(Interlocked.Read(ref _avg));

    public void Play() => Add(Cmd.CKind.Play);
    public void Stop() => Add(Cmd.CKind.Stop);
    public void Pause() => Add(Cmd.CKind.Pause);

    public void NoteOn(int chan, int key, int vel) => 
        Add(Cmd.CKind.NoteOn,
            ((long)(chan & 0xFF) << 16) |
            ((long)(key  & 0xFF) << 8)  |
            ((long)(vel  & 0xFF)));

    public void NoteOff(int chan, int key) => 
        Add(Cmd.CKind.NoteOff,
            ((long)(chan & 0xFF) << 8) |
            ((long)(key  & 0xFF)));

    public void Program(int chan, MidiPatch patch) =>
        Add(Cmd.CKind.Program,
            ((long)(chan & 0xFF)           << 24)  |
            ((long)(patch.BankMSB & 0xFF)  << 16)  |
            (long)((patch.BankLSB & 0xFF)  << 8)   |
            (long)(patch.Program & 0xFF));
    
    public void Set(SoundBank sb) => Add(Cmd.CKind.SetSoundBank, sb);
    public void Set(Midi midi) => Add(Cmd.CKind.SetMidi, midi);
    public void Remove(Midi midi) => Add(Cmd.CKind.RemMidi, midi);
    public void Remove(int midiIndex) => Add(Cmd.CKind.RemMidi, midiIndex);

    public float Volume
    {
        get => _stream.Volume / SpessaStream.BASE_VOL;
        set => _stream.Volume = value * SpessaStream.BASE_VOL;
    }
    
    public float PlaybackRate
    {
        get => Sequencer.PlaybackRate;
        set => Sequencer.PlaybackRate = value;
    }

    public float Pan
    {
        get => _stream.Pan;
        set => _stream.Pan = value;
    }

    public TimeSpan Time
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _progress));
        set => Add(Cmd.CKind.SetProgress, value.Ticks);
    }

    public bool Loop
    {
        get => Sequencer.LoopCount != 0;
        set => Add(Cmd.CKind.Loop, value ? 1 : 0);
    }

    public int Beat => _beat;
    public int BPM => Sequencer.BPM;
    public (int Top, int Bottom)? TimeSignature => Sequencer.TimeSignature;

    public Midi? Midi
    {
        get => Sequencer.Midi;
        set
        {
            if (value != null)              Set(value);
            else if (Midi is {} oldMidi)    Remove(oldMidi);
        }
    }

    public SpessaSharpSequencer Sequencer { get; }

    public int VoiceCount => Sequencer.Synth.VoiceCount;

    public Synthesizer.InterpolationType InterpolationType
    {
        get => Sequencer.Synth.SystemParameters.InterpolationType;
        set => Add(Cmd.CKind.SetInterpolationType, (long)value);
    }

    public Macro.Reverb ReverbMacro
    {
        get => Sequencer.Synth.ReverbMacro;
        set => Add(Cmd.CKind.SetReverbMacro, (long)value);
    }
    
    public Macro.Chorus ChorusMacro
    {
        get => Sequencer.Synth.ChorusMacro;
        set => Add(Cmd.CKind.SetChorusMacro, (long)value);
    }
    
    public Macro.Delay Delay
    {
        get => Sequencer.Synth.DelayMacro;
        set => Add(Cmd.CKind.SetDelayMacro, (long)value);
    }

    public AudioPlugin? Plugin
    {
        get => _plugin;
        set => _plugin = value;
    }

    public SoundBank SoundBank { set => Set(value); }

    private void Add(Cmd.CKind kind, long value = 0) =>
        Add(new Cmd(kind, Arg1: value));
    private void Add(Cmd.CKind kind, object value) =>
        Add(new Cmd(kind, Arg2: value));

    private void Add(Cmd cmd)
    {
        var spin = new SpinWait();
        while (!_commands.TryEnqueue(cmd)) spin.SpinOnce();
    }

    ~Player() => Dispose(false);

    public void Dispose()
    {
        _cts.Cancel();
        _stream.Stop();
        _cts.Dispose();
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        
        if (disposing)
        {
            // Called via Player.Dispose()
            // OK to use any private object references
        }

        unsafe
        {
            foreach (var ptr in _ptrs)
                NativeMemory.Free((void*)ptr);
        }
    }

    private void SeqLoop()
    {
        Thread.CurrentThread.Name = $"{Name}.SeqLoop";
        var cancel = _cts.Token;
        var cmdIdx = -1;
        var avg = 0L;

        var channels = SpessaStream.Channels.Length;
        var inputs = new float[channels][];
        var outputs = new float[channels][];
        for (var ch = 0; ch < channels; ch++)
        {
            inputs[ch] = new float[Synthesizer.SPESSA_BUFSIZE];
            outputs[ch] = new float[Synthesizer.SPESSA_BUFSIZE];
        }
        
        LoopStart:;
        if (_disposed) return; 
        
        // Read commands
        while (_commands.TryDequeue(out var cmd))
        {
            cmdIdx++;
            if (Debug) Console.WriteLine(
                $"[{DateTime.Now.TimeOfDay:hh\\:mm\\:ss\\.ff} | {Name} | {
                    _status}] {cmdIdx}: {cmd.Kind}");

            switch (cmd.Kind)
            {
                // Player
                case Cmd.CKind.Play:
                    // Can get here through a goto case
                    if (cmd.Kind is Cmd.CKind.Play && Sequencer.Midi is null)
                        continue;
                    if (_stream.Status != SoundStatus.Playing) _stream.Play();
                    if (_status == PlayerStatus.Play) continue;
                    
                    _status = PlayerStatus.Play;
                    Sequencer.Play();
                    break;
                case Cmd.CKind.Stop:
                    if (_status == PlayerStatus.Stop) continue;

                    _status = PlayerStatus.Stop;
                    Sequencer.Stop();

                    if (Sequencer.Songs.Length > 0)
                        // This resets the time
                        Sequencer.SongIndex += 0;
                    break;
                case Cmd.CKind.Pause:
                    if (Sequencer.Midi is null) continue;
                    if (_status == PlayerStatus.Pause) continue;
                    
                    _status = PlayerStatus.Pause;
                    Sequencer.Pause();
                    Sequencer.CurrentTime -= TimeSpan.FromSeconds(.05);// I don't know why I need to do this or hear garbage. Bug?
                    break;
                case Cmd.CKind.SetProgress:
                    Sequencer.CurrentTime = TimeSpan.FromTicks(cmd.Arg1);
                    break;
                case Cmd.CKind.SetSoundBank:
                    Sequencer.Pause();
                    Sequencer.Synth.SoundBankManager.Add(
                        (SoundBank)cmd.Arg2!, "main");
                    Sequencer.CurrentTime -= TimeSpan.FromSeconds(.05);
                    if (_status == PlayerStatus.Play) 
                        Sequencer.Play();
                    break;
                case Cmd.CKind.SetMidi:
                    Sequencer.SongIndex = Sequencer.Add((Midi)cmd.Arg2!);
                    break;
                case Cmd.CKind.RemMidi:
                    if (cmd.Arg2 is Midi midi)
                        Sequencer.Remove(midi);
                    else
                        Sequencer.Remove((int)cmd.Arg1);
                    break;
                case Cmd.CKind.Loop:
                    var loop = cmd.Arg1 != 0;
                    Sequencer.LoopCount = loop ? int.MaxValue : 0;
                    break;
                // Config
                case Cmd.CKind.SetInterpolationType:
                    var type = (Synthesizer.InterpolationType)cmd.Arg1;
                    Sequencer.Synth.Set(type);
                    break;
                case Cmd.CKind.SetReverbMacro:
                    Sequencer.Synth.ReverbMacro = (Macro.Reverb)cmd.Arg1;
                    break;
                case Cmd.CKind.SetChorusMacro:
                    Sequencer.Synth.ChorusMacro = (Macro.Chorus)cmd.Arg1;
                    break;
                case Cmd.CKind.SetDelayMacro:
                    Sequencer.Synth.DelayMacro = (Macro.Delay)cmd.Arg1;
                    break;
                // Midi
                case Cmd.CKind.NoteOn:
                {
                    var a = cmd.Arg1;
                    var (chan, key, vel) = (
                        (byte)(a >> 16), (byte)(a >> 8), (byte)a);
                    Sequencer.Synth.NoteOn(chan, key, vel);
                    goto case Cmd.CKind.Play;
                }
                case Cmd.CKind.NoteOff:
                {
                    var a = cmd.Arg1;
                    var (chan, key) = (
                        (byte)(a >> 8), (byte)a);
                    Sequencer.Synth.NoteOff(chan, key);
                    break;
                }
                case Cmd.CKind.Program:
                {
                    var a = cmd.Arg1;
                    var (chan, bMSB, bLSB, program) = (
                        (byte)(a >> 24), (byte)(a >> 16),
                        (byte)(a >> 8), (byte)a);

                    Sequencer.Synth.ControllerChange(
                        chan, Midi.CC.BankSelect, bMSB);
                    Sequencer.Synth.ControllerChange(
                        chan, Midi.CC.BankSelectLSB, bLSB);
                    Sequencer.Synth.ProgramChange(chan, program);
                    break;
                }
                // Error
                case Cmd.CKind.None:
                default: throw new ArgumentOutOfRangeException();
            }
        }
        
        // Update Sequencer
        var hasData = _status == PlayerStatus.Play || 
                      Sequencer.Synth.VoiceCount > 0;

        long total = 0;
        if (hasData)
        {
            _stream.Empty.Lock.Wait(cancel);
            if (cancel.IsCancellationRequested)
                return;
            if (!_stream.Empty.Samples.TryDequeue(out var ptr))
                throw new Exception("Impossible");

            Span<short> samples;
            var samplesLen = BLOCK_SIZE * channels;
            unsafe { samples = new Span<short>((void*)ptr, samplesLen); }
            
            foreach (var input in inputs) input.AsSpan().Clear();

            // HOT >>>>>>
            var t1 = Stopwatch.GetTimestamp();
            Sequencer.ProcessTick();
            _beat = Sequencer.Midi?.MidiTicksToBeats(Sequencer.Tick) ?? 0;
            Sequencer.Synth.Process(inputs[0], inputs[1]);
            total = Stopwatch.GetTimestamp() - t1;
            // TOH <<<<<<

            if (_plugin is {} plugin)
            {
                plugin(inputs, outputs, channels, Synthesizer.SPESSA_BUFSIZE);
                AudioUtil.Interleave(outputs[0], outputs[1], samples);
            }
            else
                AudioUtil.Interleave(inputs[0], inputs[1], samples);
            
            if (!_stream.Full.Samples.TryEnqueue(ptr))
                throw new Exception("Impossible");
            _stream.Full.Lock.Release();
        }

        // Update Status
        const double f = .01;
        avg = (long)(total * f + avg * (1 - f));
        var avgSecs = TimeSpan.FromSeconds(avg / (double)Stopwatch.Frequency);

        Interlocked.Exchange(ref _avg, avgSecs.Ticks);
        Interlocked.Exchange(ref _progress, Sequencer.CurrentTime.Ticks);

        if (!hasData) Thread.Sleep(1);
        goto LoopStart;
    }
    
    #region MultiThreading
    // I got these codes from the dark web.
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedInt { [FieldOffset(0)] public int Value; }
    
    /// <summary>Single producer, single consumer buffer</summary>
    private sealed class Spsc<T> where T : struct
    {
        public readonly int Capacity;
        private readonly T[] _buffer;
        private readonly int _mask;

        private PaddedInt _writeIndex;
        private PaddedInt _cachedReadIndex;
        private PaddedInt _readIndex;
        private PaddedInt _cachedWriteIndex;

        public Spsc(int capacity)
        {
            var cap = BitOperations.RoundUpToPowerOf2(
                (uint)Math.Clamp(capacity, 2, 1 << 16));
            Capacity    = (int)cap;
            _buffer     = new T[cap];
            _mask       = (int)(cap - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnqueue(in T command)
        {
            unchecked
            {
                var write = _writeIndex.Value;
                if (write - _cachedReadIndex.Value >= _buffer.Length)
                {
                    _cachedReadIndex.Value = Volatile.Read(ref _readIndex.Value);
                    if (write - _cachedReadIndex.Value >= _buffer.Length) return false;
                }

                _buffer[write & _mask] = command;
                Volatile.Write(ref _writeIndex.Value, write + 1);
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue(out T command)
        {
            unchecked
            {
                var read = _readIndex.Value;
                if (read == _cachedWriteIndex.Value)
                {
                    _cachedWriteIndex.Value = Volatile.Read(ref _writeIndex.Value);
                    if (read == _cachedWriteIndex.Value)
                    {
                        command = default;
                        return false;
                    }
                }

                command = _buffer[read & _mask];
                
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                    _buffer[read & _mask] = default;

                Volatile.Write(ref _readIndex.Value, read + 1);
                return true;
            }
        }
    }
    #endregion
}