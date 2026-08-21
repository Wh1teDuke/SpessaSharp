#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj

using System.Diagnostics;
using SpessaSharp.MIDI;
using SpessaSharp.Sequencer;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

// https://github.com/spessasus/spessasynth_core/blob/master/examples/midi_to_wav_node.ts
// ./MidiToWavNode.cs -- <soundbank path> <midi path> <wav output path>
// dotnet run MidiToWavNode.cs -- <soundbank path> <midi path> <wav output path>

// Process arguments
if (args.Length != 3)
{
    Console.WriteLine("Usage: ./MidiToWavNode.cs -- <soundbank path> <midi path> <wav output path>");
    Environment.Exit(1);
}

var pathSoundBank = args[0];
var pathMidi = args[1];
var pathWav = args[2];

Console.WriteLine($"SoundBank: {pathSoundBank}");
Console.WriteLine($"Midi:      {pathMidi}");
Console.WriteLine($"Wav:       {pathWav}");

// Read MIDI and sound bank
// Parse the MIDI and sound bank
var midi = Midi.From(new FileInfo(pathMidi));
var soundBank = SoundBank.From(new FileInfo(pathSoundBank));

// Initialize the synthesizer
const int sampleRate = 48_000;
var processor = new SpessaSharpProcessor(
    sampleRate,
    Synthesizer.Options.Default with { EventsEnabled = false });

processor.SoundBankManager.Add(soundBank, "main");

// Enable verbose information during render
Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
Debug.AutoFlush = true;

// Enable uncapped voice count
processor.Set(GlobalSystemParameter.Of(
    GlobalSystemParameter.Type.AutoAllocateVoices, true));


// Initialize the sequencer
var sequencer = new SpessaSharpSequencer(processor);
sequencer.LoadNewSongList([midi]);
sequencer.Play();

// Prepare the output buffers
var sampleCount = (int)Math.Ceiling(
    sampleRate * (midi.Duration.TotalSeconds + 2));
var outLeft = new float[sampleCount];
var outRight = new float[sampleCount];
var start = Stopwatch.StartNew();

var filledSamples = 0;
// Note: buffer size is recommended to be very small, as this is the interval between modulator updates and LFO updates
const int BUFFER_SIZE = 128;

var i = 0;
var durationRounded = (int)Math.Floor(sequencer.Midi!.Duration.TotalSeconds);

while (filledSamples < sampleCount) 
{
    // Process sequencer
    sequencer.ProcessTick();
    // Render
    var bufferSize = Math.Min(BUFFER_SIZE, sampleCount - filledSamples);
    processor.Process(outLeft, outRight, filledSamples, bufferSize);
    filledSamples += bufferSize;
    i++;

    // Log progress
    if (i % 100 == 0) 
    {
        Console.WriteLine(
            $"Rendered {(int)Math.Floor(sequencer.CurrentTime.TotalSeconds)
            } / {durationRounded}");
    }
}

Console.WriteLine();

var diff = start.Elapsed;

Console.WriteLine(
    $@"Rendered in {diff:mm\:ss\.ff} ({Math.Round(midi.Duration / diff)}x)");

var wave = AudioUtil.ToWav([outLeft, outRight], sampleRate);
File.WriteAllBytes(pathWav, wave);
Console.WriteLine($"File written to '{pathWav}'");