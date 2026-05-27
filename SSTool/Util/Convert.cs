using SpessaSharp.MIDI;
using SpessaSharp.Sequencer;
using SpessaSharp.Synthesizer;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SSTool.Util;

public static class Convert
{
    public static byte[] ToWav(
        FileInfo fileMidi,
        FileInfo? fileSoundBank,
        Action<SpessaSharpProcessor>? setup = null)
    {
        var (soundBank, fileSb) =
            Etc.GetSoundBank(fileMidi, fileSoundBank);
        
        Console.WriteLine($"Midi:      {fileMidi.FullName}");
        Console.WriteLine($"SoundBank: {fileSb?.FullName ?? "<Embedded GM.dls>"}");
        
        // Read data
        if (!fileMidi.Exists)
            Etc.Error($"Midi '{fileMidi.FullName}' not found");

        var midi = Midi.From(fileMidi);

        const int sampleRate = 48_000;
        var processor = new SpessaSharpProcessor(
            sampleRate,
            Synthesizer.Options.Default with { EventsEnabled = false });

        processor.SoundBankManager.Add(soundBank, "main");
        processor.Set((GlobalSystemParameter.Type.AutoAllocateVoices, true));
        setup?.Invoke(processor);
        
        var sequencer = new SpessaSharpSequencer(processor);
        sequencer.LoadNewSongList([midi]);
        sequencer.Play();
        
        // Render data
        var sampleCount = (int)Math.Ceiling(
            sampleRate * (midi.Duration.TotalSeconds + 2));
        var outLeft = new float[sampleCount];
        var outRight = new float[sampleCount];
        
        var filledSamples = 0;
        // Note: buffer size is recommended to be very small, as this is the interval between modulator updates and LFO updates
        const int BUFFER_SIZE = 128;
        
        var i = 0;
        var durationRounded = (int)Math.Floor(
            sequencer.Midi!.Duration.TotalSeconds);

        var p = Console.GetCursorPosition();
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
            if (i % 1_000 == 0) 
            {
                Console.SetCursorPosition(0, p.Top);
                Console.Write($"Rendered {
                    (int)Math.Floor(sequencer.CurrentTime.TotalSeconds)} / {durationRounded}");
            }
        }
        
        return AudioUtil.ToWav([outLeft, outRight], sampleRate);
    }
}