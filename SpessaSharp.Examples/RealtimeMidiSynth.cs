#!/usr/bin/env -S dotnet run --configuration Release

#:project ../SpessaSharp/SpessaSharp.csproj
#:package OwnAudioSharp.Midi@3.1.0
#:package OwnAudioSharp.Basic@3.1.3

using System.Diagnostics;
using Ownaudio.Core;
using OwnAudio.Midi.IO;
using OwnaudioNET;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer;

// Note: On linux you may need to execute "sudo modprobe snd-virmidi"

// Process arguments
if (args.Length < 1)
{
    // Also ./RealtimeMidiSynth.cs <soundbank path> <sample rate> [device]
    Console.WriteLine("Usage: dotnet run RealtimeMidiSynth.cs -- <soundbank path> [sample rate] [device]");
    Environment.Exit(1);
}

// Initialize the synthesizer
var sfPath = args[0];
var sampleRate = args.Length > 1 ? int.Parse(args[1]) : 44_100;
// Quantum size is recommended to be 128 at 48kHz, 48000 / 375 = 128
// Scale this to the selected sample rate
var quantum = (int)Math.Round(sampleRate / 375f);

Console.WriteLine("Initializing synthesizer ...");
var synth = new SpessaSharpProcessor(
    sampleRate, Synthesizer.Options.Default with
    {
        EventsEnabled = false,
        MaxBufferSize = quantum,
    });

// Load sound bank
var sf = new FileInfo(sfPath);
synth.SoundBankManager.Add(SoundBank.From(sf), "main");

// Initialize the MIDI inputs
var device = args.Length > 2
    ? args[1] : MidiPortFactory.GetInputPortNames()[0];

Console.WriteLine($"Listening on '{device}'");
using var port = MidiPortFactory.OpenInput(device);
var synthLock = new Lock();
port.MessageReceived += m =>
{
    var (t, s, d1, d2) = (m.Type, m.Status, m.Data1, m.Data2);
    Console.WriteLine($"{t,-20}(0x{s:X2} 0x{d1:X2} 0x{d2:X2})");
    lock (synthLock) synth.ProcessMessage([s, d1, d2]);
};

port.Start();

var config = new AudioConfig
{
    SampleRate      = sampleRate,
    Channels        = 2,
    EnableInput     = false,
    EnableOutput    = true,
};

Logger.Log.LoggerLevel = Logger.Log.Level.Disabled;
OwnaudioNet.Initialize(config);
OwnaudioNet.Start();

// Initialize the audio stream
var left = new float[quantum];
var right = new float[quantum];
var interleaved = new float[left.Length * 2];
var watch = Stopwatch.StartNew();
var start = watch.Elapsed;

while (true)
{
    var t = (watch.Elapsed - start).TotalSeconds;
    var currentTime = 0d;
    lock (synthLock) currentTime = synth.CurrentTime;

    // Keep rendering until we are 0.1 seconds ahead of time
    if (currentTime - t >= .1)
    {
        Thread.Sleep(1);
        continue;
    }
    
    left.AsSpan().Clear();
    right.AsSpan().Clear();
    lock(synthLock) synth.Process(left, right);

    for (var i = 0; i < left.Length; i++)
    {
        interleaved[i * 2] = left[i];
        interleaved[i * 2 + 1] = right[i];
    }
    
    OwnaudioNet.Send(interleaved.AsSpan());
}