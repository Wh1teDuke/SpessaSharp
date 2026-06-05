<p align='center'><img src='spessasharp_logo_rounded.png' width='300' alt='SpessaSharp logo'></p>

**SpessaSharp** is a C# port of the [**SpessaSynth**](https://github.com/spessasus/spessasynth_core) library by [**Spessasus**](https://github.com/spessasus).

[<img src="https://raw.githubusercontent.com/oprypin/nightly.link/refs/heads/master/logo.svg" alt="" height="16" style="height: 16px; vertical-align: sub"> SpessaSharp command line tool](https://nightly.link/Wh1teDuke/SpessaSharp/workflows/sstool/master?preview)

Last commit: [add a type field](https://github.com/spessasus/spessasynth_core/commit/acb1c98a195a4181730824fff37b869193a82509)

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