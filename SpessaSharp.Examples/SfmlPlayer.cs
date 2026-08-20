#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj
#:package SFML.Audio@3.0.0

using System.Runtime;

using SpessaSharp.MIDI;
using SpessaSharp.Synthesizer;
using SpessaSharp.Sequencer;
using SpessaSharp.SoundBank;

using SFML.Audio;
using SFML.System;

GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

Console.WriteLine("SFML Example begins");

// ./SfmlPlayer.cs <midi> <soundbank>
// or dotnet run SfmlPlayer.cs -- <midi> <soundbank>

var defaultMidi = Path.Join("assets", "Fleetwood Mac - Little Lies.mid");
var defaultSoundBank = Path.Join("assets", "SuperSmallFont.sf2"); 

var midiName = defaultMidi;
var soundBankName = defaultSoundBank;

if (args.Length >= 1)
    midiName = args[0];
if (args.Length >= 2)
    soundBankName = args[1];

if (!File.Exists(midiName))
    Quit($"Midi '{midiName}' not found");

if (!File.Exists(soundBankName))
    Quit($"SoundBank '{soundBankName}' not found");

Console.WriteLine("Midi:      " + midiName);
Console.WriteLine("SoundBank: " + soundBankName);

// Parse midi and soundbank
var midi = Midi.From(new FileInfo(midiName));
var soundBank = SoundBank.From(new FileInfo(soundBankName));

var processor = new SpessaSharpProcessor(44_100);
var sequencer = new SpessaSharpSequencer(processor);
processor.SoundBankManager.Add(soundBank, "main");

sequencer.LoadNewSongList([midi]);
var player = new Player(sequencer);

GCSettings.LargeObjectHeapCompactionMode =
    GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect();
GC.WaitForPendingFinalizers();

player.Play();

Console.Write("Press any key to quit ...");

while (!Console.KeyAvailable && !player.IsFinished) 
    Thread.Sleep(100);

if (Console.KeyAvailable) Console.ReadKey(true);
Console.WriteLine();
Console.WriteLine("SFML Example ends");
return;

void Quit(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("ERROR: " + message);
    Console.ResetColor();
    Environment.Exit(1);   
}

internal sealed class Player(SpessaSharpSequencer sequencer)
{
    private sealed class SpessaStream: SoundStream
    {
        private const int BLOCK_SIZE = Synthesizer.SPESSA_BUFSIZE;
        private readonly short[] _samples = new short[BLOCK_SIZE * 2];
        public readonly SpessaSharpSequencer Sequencer;

        public SpessaStream(SpessaSharpSequencer sequencer)
        {
            Sequencer = sequencer;
            Initialize(
                2,
                (uint)sequencer.Synth.SampleRate,
                [SoundChannel.FrontLeft, SoundChannel.FrontRight]);
            Volume = 200;
        }

        protected override bool OnGetData(out short[] samples)
        {
            Sequencer.ProcessTick();
            Sequencer.Synth.Process(_samples);
            samples = _samples;
            return !Sequencer.IsFinished || Sequencer.Synth.VoiceCount > 0;
        }

        protected override void OnSeek(Time timeOffset) {}
    }

    private readonly SpessaStream _stream = new(sequencer);
    
    public bool IsFinished =>
        sequencer is { IsFinished: true, Synth.VoiceCount: 0 };

    public void Play()
    {
        _stream.Sequencer.Play();
        _stream.Play();
    }
}