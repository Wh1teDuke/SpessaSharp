**SpessaSharp** is a C# port of the multipurpose SF2/DLS/MIDI JavaScript library [**SpessaSynth**](https://github.com/spessasus/spessasynth_core) by [**Spessasus**](https://github.com/spessasus).

* Play MIDI files using SF2/SF3/DLS files
* Read and write MIDI files
* Read and write SF2/SF3 files
* Convert DLS to SF2


```csharp
using SpessaSharp.MIDI;
using SpessaSharp.Sequencer;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer;
using SpessaSharp.Utils;

// Process arguments
if (args is not [var pathSoundBank, var pathMidi, var pathWav, ..])
  throw new Exception("Expected sound bank, midi, and wav path");

// Read MIDI and sound bank
var midi = Midi.From(new FileInfo(pathMidi));
var soundBank = SoundBank.From(new FileInfo(pathSoundBank));

// Initialize the synthesizer
const int sampleRate = 48_000;
var processor = new SpessaSharpProcessor(sampleRate);
processor.SoundBankManager.Add(soundBank, "main");

// Initialize the sequencer
var sequencer = new SpessaSharpSequencer(processor);
sequencer.LoadNewSongList([midi]);
sequencer.Play();

// Prepare the output buffers
var sampleCount = (int)Math.Ceiling(sampleRate * (midi.Duration.TotalSeconds + 2));
var outLeft = new float[sampleCount];
var outRight = new float[sampleCount];
var filledSamples = 0;

// Note: buffer size is recommended to be very small, 
// as this is the interval between modulator updates and LFO updates
const int BUFFER_SIZE = 128;

while (filledSamples < sampleCount)
{
    // Process sequencer
    sequencer.ProcessTick();
    // Render
    var bufferSize = Math.Min(BUFFER_SIZE, sampleCount - filledSamples);
    processor.Process(outLeft, outRight, filledSamples, bufferSize);
    filledSamples += bufferSize;
}

var wave = AudioUtil.ToWav([outLeft, outRight], sampleRate);
File.WriteAllBytes(pathWav, wave);
```