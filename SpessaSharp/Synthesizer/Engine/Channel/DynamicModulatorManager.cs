using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;

namespace SpessaSharp.Synthesizer.Engine.Channel;

/// <summary>A class for dynamic modulators that are assigned for more complex system exclusive messages</summary>
/// <param name="channel"></param>
public sealed class DynamicModulatorManager(int channel)
{
    private static readonly Voice.Voice.Modulator[] INITIAL_MODULATORS = [
        // Vibrato rate to that one GS rate (in bare Hz) map for special cases such as J-Cycle.mid
        Voice.Voice.Modulator.From(
            Modulator.Decoded(
                Modulator.GetModSourceEnum(
                    ModulatorCurve.Type.Linear,
                    true,
                    false,
                    true,
                    new Modulator.Source.Index(Midi.CC.VibratoRate)
                    ), // Linear forward bipolar
                0x0, // No controller
                (short)Generator.Type.VibLFORate,
                1_000,
                0)
        )
    ];
    
    /// <summary> The current dynamic modulator list. </summary>
    public readonly List<(
            Voice.Voice.Modulator Mod, 
            Voice.Voice.Modulator.ID ID)>
        ModulatorList = [];
    public bool Active;

    public void ResetModulators()
    {
        ModulatorList.Clear();

        foreach (var mod in INITIAL_MODULATORS)
            ModulatorList.Add((mod, new Voice.Voice.Modulator.ID(
                mod.Base.PrimarySource.ToSourceEnum(),
                mod.Base.Destination,
                mod.Base.PrimarySource.IsBipolar,
                mod.Base.PrimarySource.IsNegative)));
        
        Active = false;
    }

    public void SetupReceiver(
        int addr3, 
        int data, 
        int source,
        bool isCC,
        string sourceName, 
        bool bipolar = false)
    {
        Active = true;
        var centeredValue = data - 64;
        var centeredNormalized = centeredValue / 64f;
        var normalizedNotCentered = data / 127f;

        switch (addr3 & 0x0f)
        {
            case 0x00:
                // Pitch Control
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.FineTune,
                    (short)(centeredValue * 100),
                    bipolar);

                Logging(
                    channel,
                    centeredValue,
                    sourceName,
                    " pitch control",
                    "semitones");
                break;
            
            case 0x01: 
                // Cutoff
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.InitialFilterFc,
                    (short)(centeredNormalized * 9_600),
                    bipolar);

                Logging(
                    channel,
                    (short)(centeredNormalized * 9_600),
                    sourceName,
                    " filter control",
                    "cents");
                break;
            
            case 0x02: 
                // Amplitude
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.Amplitude,
                    (short)(centeredNormalized * 1_000), // Generator is 1/10%
                    bipolar);

                Logging(
                    channel,
                    (short)(centeredNormalized * 100),
                    sourceName,
                    "amplitude",
                    "%");
                break;
            
            case 0x03: 
                // LFO1 Rate
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.VibLFORate,
                    (short)(centeredNormalized * 1_000), // Generator is 1/100Hz
                    bipolar);

                Logging(
                    channel,
                    (short)(centeredNormalized * 10),
                    sourceName,
                    " LFO1 rate",
                    "Hz");
                break;
            
            case 0x04:
                // LFO1 pitch depth
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.VibLFOToPitch,
                    (short)(normalizedNotCentered * 600),
                    bipolar);

                Logging(
                    channel,
                    (short)(normalizedNotCentered * 600),
                    sourceName,
                    " LFO1 pitch depth",
                    "cents");
                break;
            
            case 0x05: 
                // LFO1 filter depth
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.VibLFOToFilterFc,
                    (short)(normalizedNotCentered * 2_400),
                    bipolar);

                Logging(
                    channel,
                    (short)(normalizedNotCentered * 2_400),
                    sourceName,
                    " LFO1 filter depth",
                    "cents");
                break;

            case 0x06: 
                // LFO1 amplitude depth
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.VibLFOAmplitudeDepth,
                    (short)(normalizedNotCentered * 1_000), // Generator is 1/10%
                    bipolar);

                Logging(
                    channel,
                    (short)(normalizedNotCentered * 100),
                    sourceName,
                    " LFO1 amplitude depth",
                    "%");
                break;

            case 0x07: 
                // LFO1 Rate
                SetModulator(
                    source,
                    isCC,
                   Generator.Type.ModLFORate,
                   (short)(centeredNormalized * 1_000), // Generator is 1/100Hz
                    bipolar);

                Logging(
                    channel,
                    (short)(centeredNormalized * 10),
                    sourceName,
                    " LFO2 rate",
                    "Hz");
                break;

            case 0x08: 
                // LFO2 pitch depth
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.ModLFOToPitch,
                    (short)(normalizedNotCentered * 600),
                    bipolar);

                Logging(
                    channel,
                    (short)(normalizedNotCentered * 600),
                    sourceName,
                    " LFO2 pitch depth",
                    "cents");
                break;

            case 0x09: 
                // LFO2 filter depth
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.ModLFOToFilterFc,
                    (short)(normalizedNotCentered * 2_400),
                    bipolar);

                Logging(
                    channel,
                    (short)(normalizedNotCentered * 2_400),
                    sourceName,
                    " LFO2 filter depth",
                    "cents");
                break;

            case 0x0a: 
                // LFO2 amplitude depth
                SetModulator(
                    source,
                    isCC,
                    Generator.Type.ModLFOAmplitudeDepth,
                    (short)(normalizedNotCentered * 1_000), // Generator is 1/10%
                    bipolar);

                Logging(
                    channel,
                    (short)(normalizedNotCentered * 100),
                    sourceName,
                    " LFO2 amplitude depth",
                    "%");
                break;
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="source">Like in midiControllers: values below NON_CC_INDEX_OFFSET are CCs, above are regular modulator sources.</param>
    /// <param name="isCC">If the source is an SF2 source or a MIDI CC source.</param>
    /// <param name="destination">The generator type to modulate.</param>
    /// <param name="amount">The amount of modulation to apply.</param>
    /// <param name="isBipolar">If true, the modulation is bipolar (ranges from -1 to 1 instead of from 0 to 1).</param>
    /// <param name="isNegative">If true, the modulation is negative (goes from 1 to 0 instead of from 0 to 1).</param>
    private void SetModulator(
        int source,
        bool isCC,
        Generator.Type destination,
        short amount,
        bool isBipolar = false,
        bool isNegative = false)
    {
        var id = new Voice.Voice.Modulator.ID(
            source, destination, isBipolar, isNegative);

        if (amount == 0) DeleteModulator(id);

        foreach (ref var mod in CollectionsMarshal.AsSpan(ModulatorList))
        {
            if (mod.ID != id) continue;
            mod.Mod = mod.Mod with
            {
                Base = mod.Mod.Base with { TransformAmount = amount }
            };
            return;
        }
        
        var modulator = Voice.Voice.Modulator.From(new Modulator(
            new Modulator.Source(
                isBipolar,
                isNegative,
                new Modulator.Source.Index((byte)source),
                isCC,
                ModulatorCurve.Type.Linear),
            new Modulator.Source(),
            destination,
            amount,
            0));

        ModulatorList.Add((modulator, id));
    }

    private void DeleteModulator(Voice.Voice.Modulator.ID id)
    {
        int? index = null;
        for (var i = 0; i < ModulatorList.Count; i++)
        {
            if (ModulatorList[i].ID != id) continue;
            index = i;
            break;
        }
        
        if (index is {} idx) ModulatorList.RemoveAt(idx);
    }

    [Conditional("DEBUG")]
    private static void Logging(
        int channel, float value, string whatName, string what, string units) =>
        Debug.WriteLine(
            $"Channel {channel} {whatName}{what} is now set to {value} {units}.");
}