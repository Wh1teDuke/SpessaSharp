#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj
#:package OwnAudioSharp.Basic@3.0.13

using System.Diagnostics;
using System.Runtime;
using Ownaudio.Core;
using OwnaudioNET;
using SpessaSharp.MIDI;
using SpessaSharp.Synthesizer;
using SpessaSharp.Sequencer;
using SpessaSharp.SoundBank;

GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

Console.WriteLine("OwnAudio Example begins");

// ./OwnAudioPlayer.cs <midi> <soundbank>
// or dotnet run OwnAudioPlayer.cs -- <midi> <soundbank>

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
Console.WriteLine("OwnAudio Example ends");
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
    public bool IsFinished =>
        sequencer is { IsFinished: true, Synth.VoiceCount: 0 };
    
    public void Play()
    {
        new Thread(() =>
        {
            const int BLOCK_SIZE = Synthesizer.SPESSA_BUFSIZE;
            var config = new AudioConfig
            {
                SampleRate      = sequencer.Synth.SampleRate,
                Channels        = 2,
                EnableInput     = false,
                EnableOutput    = true,
            };

            Logger.Log.LoggerLevel = Logger.Log.Level.Disabled;
            OwnaudioNet.Initialize(config);
            OwnaudioNet.Start();
        
            sequencer.Play();

            var left = new float[BLOCK_SIZE];
            var right = new float[BLOCK_SIZE];
            var interleaved = new float[BLOCK_SIZE * 2];
            var watch = Stopwatch.StartNew();
            var start = watch.Elapsed;
            
            while (!IsFinished)
            {
                var t = (watch.Elapsed - start).TotalSeconds;
                if (sequencer.Synth.CurrentTime - t > 0.5)
                {
                    Thread.Sleep(1);
                    continue;
                }
                
                left.AsSpan().Clear();
                right.AsSpan().Clear();
                interleaved.AsSpan().Clear();
                
                sequencer.ProcessTick();
                sequencer.Synth.Process(left, right);

                for (var i = 0; i < BLOCK_SIZE; i++)
                {
                    interleaved[i * 2 + 0] = left[i];
                    interleaved[i * 2 + 1] = right[i];
                }
                
                OwnaudioNet.Send(interleaved);
            }
            
            OwnaudioNet.Stop();
        })
        {
            IsBackground    = true,
            Priority        = ThreadPriority.AboveNormal,
        }.Start();
    }
}