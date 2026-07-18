<p align='center'><img src='spessasharp_logo_rounded.png' width='200' alt='SpessaSharp logo'></p>

**SpessaSharp** is a C# port of the multipurpose SF2/DLS/MIDI JavaScript library [**SpessaSynth**](https://github.com/spessasus/spessasynth_core) by [**Spessasus**](https://github.com/spessasus).

[<img src="https://www.nuget.org/Content/gallery/img/nuget-lockup-dark-fill.svg" alt="" height="16" style="height: 16px; vertical-align: sub">](https://www.nuget.org/packages/SpessaSharp)
 | [<img src="https://raw.githubusercontent.com/oprypin/nightly.link/refs/heads/master/logo.svg" alt="" height="16" style="height: 16px; vertical-align: sub"> Command-line tool](https://nightly.link/Wh1teDuke/SpessaSharp/workflows/sstool/master?preview) | <img src='https://spessasus.github.io/spessasynth_core/assets/favicon.png' height="16" style="height: 16px; vertical-align: sub"></img> Last commit: [fix brackets](https://github.com/spessasus/spessasynth_core/commit/7639b96c794c902362ba70d7969c13ab9f6c1a52)

## Example

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