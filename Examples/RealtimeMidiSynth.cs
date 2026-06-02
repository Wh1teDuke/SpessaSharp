#!/usr/bin/env -S dotnet run --configuration Release

#:project ../SpessaSharp/SpessaSharp.csproj
#:package OwnAudioSharp.Midi@3.0.13
#:package OwnAudioSharp.Basic@3.0.11

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
    // Also ./RealtimeMidiSynth.cs <soundbank path> [device]
    Console.WriteLine("Usage: dotnet run RealtimeMidiSynth.cs -- <soundbank path> [device]");
    Environment.Exit(1);
}

// Initialize the synthesizer
const int sampleRate = 44_100;
Console.WriteLine("Initializing synthesizer ...");
var sfPath = args[0];
var sf = new FileInfo(sfPath);

var synth = new SpessaSharpProcessor(
    sampleRate, Synthesizer.Options.Default with
    { EventsEnabled = false, });

synth.SoundBankManager.Add(SoundBank.From(sf), "main");

// Initialize the MIDI inputs
var device = args.Length > 1
    ? args[1]
    : MidiPortFactory.GetInputPortNames()[0];

Console.WriteLine($"Listening on '{device}'");
using var port = MidiPortFactory.OpenInput(device);
var synthLock = new Lock();
port.MessageReceived += m =>
{
    var (t, s, d1, d2) = (m.Type, m.Status, m.Data1, m.Data2);
    Console.WriteLine($"{t,-20}(0x{s:X2} 0x{d1:X2} 0x{d2:X2})");
    lock(synthLock)
        synth.ProcessMessage([s, d1, d2]);
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
const int quantum = 64;
const int blockSize = 4;
var left = new float[quantum * blockSize];
var right = new float[quantum * blockSize];
var interleaved = new float[left.Length * 2];
var watch = Stopwatch.StartNew();
var start = watch.Elapsed;

while (true)
{
    var t = (watch.Elapsed - start).TotalSeconds;
    var currentTime = 0d;
    lock (synthLock) currentTime = synth.CurrentTime;

    if (currentTime - t > 0.5)
    {
        Thread.Sleep(1);
        continue;
    }
    
    left.AsSpan().Clear();
    right.AsSpan().Clear();
    var write = 0;
    lock(synthLock)
    {
        for (var i = 0; i < blockSize; i++)
        {
            synth.Process(left, right, write, quantum);
            write += quantum;
        }
    }

    for (var i = 0; i < left.Length; i++)
    {
        interleaved[i * 2] = left[i];
        interleaved[i * 2 + 1] = right[i];
    }
    
    OwnaudioNet.Send(interleaved.AsSpan());
}