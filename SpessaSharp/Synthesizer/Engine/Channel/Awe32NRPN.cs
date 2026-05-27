using SpessaSharp.SoundBank;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Channel;

public static class Awe32NRPN
{
    /// <summary>
    /// SoundBlaster AWE32 NRPN generator mappings.<br/>
    /// http://archive.gamedev.net/archive/reference/articles/article445.html<br/>
    /// https://github.com/user-attachments/files/15757220/adip301.pdf
    /// </summary>
    private static readonly Generator.Type[] AWE_NRPN_GENERATOR_MAPPINGS =
    [
        Generator.Type.DelayModLFO,
        Generator.Type.FreqModLFO,

        Generator.Type.DelayVibLFO,
        Generator.Type.FreqVibLFO,

        Generator.Type.DelayModEnv,
        Generator.Type.AttackModEnv,
        Generator.Type.HoldModEnv,
        Generator.Type.DecayModEnv,
        Generator.Type.SustainModEnv,
        Generator.Type.ReleaseModEnv,

        Generator.Type.DelayVolEnv,
        Generator.Type.AttackVolEnv,
        Generator.Type.HoldVolEnv,
        Generator.Type.DecayVolEnv,
        Generator.Type.SustainVolEnv,
        Generator.Type.ReleaseVolEnv,

        Generator.Type.FineTune,

        Generator.Type.ModLFOToPitch,
        Generator.Type.VibLFOToPitch,
        Generator.Type.ModEnvToPitch,
        Generator.Type.ModLFOToVolume,

        Generator.Type.InitialFilterFc,
        Generator.Type.InitialFilterQ,

        Generator.Type.ModLFOToFilterFc,
        Generator.Type.ModEnvToFilterFc,

        Generator.Type.ChorusEffectsSend,
        Generator.Type.ReverbEffectsSend
    ];

    public sealed class ChannelGenerators
    {
        /// <summary>
        /// An array of offsets generators for SF2 NRPN support.
        /// A value of 0 means no change; -10 means 10 lower, etc.
        /// </summary>
        public readonly short[] Offsets = new short[Generator.Amount];

        /// <summary>A small optimization that disables applying offsets until at least one is set.</summary>
        public bool OffsetsEnabled;

        /// <summary>
        /// An array of overrides generators for AWE32 NRPN support.
        /// A value of GENERATOR_OVERRIDE_NO_CHANGE_VALUE (-32,767) means no change;
        /// other values replace current generators.
        /// </summary>
        public readonly short[] Overrides = new short[Generator.Amount];
        
        /// <summary>A small optimization that disables applying overrides until at least one is set.</summary>
        public bool OverridesEnabled;
    }

    /// <summary>
    /// Function that emulates AWE32 similarly to fluidsynth
    /// https://github.com/FluidSynth/fluidsynth/wiki/FluidFeatures
    /// </summary>
    /// <remarks>
    /// Note: This makes use of findings by mrbumpy409:
    /// https://github.com/fluidSynth/fluidsynth/issues/1473<br/>
    /// The excellent test files are available here, also collected and converted by mrbumpy409:
    /// https://github.com/mrbumpy409/AWE32-midi-conversions
    /// </remarks>
    /// <param name="chan"></param>
    /// <param name="paramLSB">NRPN LSB</param>
    /// <param name="dataValue">14-bit</param>
    internal static void Handle(MidiChannel chan, int paramLSB, int dataValue)
    {
        // Center the value
        // Though ranges reported as 0 to 127 only use LSB
        var dataLSB = dataValue & 0x7f;
        dataValue -= 8_192;

        var generator = Generator.Type.Invalid;
        if (paramLSB < 0 || paramLSB >= AWE_NRPN_GENERATOR_MAPPINGS.Length)
        {
            SpessaLog.Unsupported(
                $"AWE32 LSB for {chan.Channel}",
                [(byte)paramLSB],
                "Invalid Generator Number");
        }
        else
            generator = AWE_NRPN_GENERATOR_MAPPINGS[paramLSB];

        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (generator) 
        {
            default: 
                // This should not happen
                break;

            // Delays
            case Generator.Type.DelayModLFO:
            case Generator.Type.DelayVibLFO:
            case Generator.Type.DelayVolEnv:
            case Generator.Type.DelayModEnv: 
            {
                var milliseconds = 4 * Math.Clamp(dataValue, 0, 5_900);
                // Convert to timecents
                chan.SetGeneratorOverride(
                    generator, MsToTimecents(milliseconds));
                break;
            }

            // Attacks
            case Generator.Type.AttackVolEnv:
            case Generator.Type.AttackModEnv: 
            {
                var milliseconds = Math.Clamp(dataValue, 0, 5_940);
                // Convert to timecents
                chan.SetGeneratorOverride(
                    generator, MsToTimecents(milliseconds));
                break;
            }

            // Holds
            case Generator.Type.HoldVolEnv:
            case Generator.Type.HoldModEnv: 
            {
                var milliseconds = Math.Clamp(dataValue, 0, 8_191);
                // Convert to timecents
                chan.SetGeneratorOverride(
                    generator, MsToTimecents(milliseconds));
                break;
            }

            // Decays and releases (share clips and units)
            case Generator.Type.DecayModEnv:
            case Generator.Type.DecayVolEnv:
            case Generator.Type.ReleaseVolEnv:
            case Generator.Type.ReleaseModEnv: 
            {
                var milliseconds = 4 * Math.Clamp(dataValue, 0, 5_940);
                // Convert to timecents
                chan.SetGeneratorOverride(
                    generator, MsToTimecents(milliseconds));
                break;
            }

            // Lfo frequencies
            case Generator.Type.FreqVibLFO:
            case Generator.Type.FreqModLFO: 
            {
                var hertz = 0.084f * dataLSB;
                // Convert to abs cents
                chan.SetGeneratorOverride(
                    generator, HzToCents(hertz), true);
                break;
            }

            // Sustains
            case Generator.Type.SustainVolEnv:
            case Generator.Type.SustainModEnv: 
            {
                // 0.75 dB is 7.5 cB
                var centibels = dataLSB * 7.5f;
                chan.SetGeneratorOverride(generator, (short)centibels);
                break;
            }

            // Pitch
            case Generator.Type.FineTune: 
                // Data is already centered
                chan.SetGeneratorOverride(generator, (short)dataValue, true);
                break;

            // Lfo to pitch
            case Generator.Type.ModLFOToPitch:
            case Generator.Type.VibLFOToPitch: 
            {
                var cents = Math.Clamp(
                    dataValue, -127, 127) * 9.375f;
                chan.SetGeneratorOverride(generator, (short)cents, true);
                break;
            }

            // Env to pitch
            case Generator.Type.ModEnvToPitch: 
            {
                var cents = Math.Clamp(dataValue, -127, 127) * 9.375f;
                chan.SetGeneratorOverride(generator, (short)cents);
                break;
            }

            // Mod lfo to vol
            case Generator.Type.ModLFOToVolume: 
            {
                // 0.1875 dB is 1.875 cB
                var centibels = 1.875f * dataLSB;
                chan.SetGeneratorOverride(generator, (short)centibels, true);
                break;
            }

            // Filter fc
            case Generator.Type.InitialFilterFc: 
            {
                // Minimum: 100 Hz -> 4335 cents
                var fcCents = 4_335 + 59 * dataLSB;
                chan.SetGeneratorOverride(generator, (short)fcCents, true);
                break;
            }

            // Filter Q
            case Generator.Type.InitialFilterQ: 
            {
                // Note: this uses the "modulator-ish" approach proposed by mrbumpy409
                // Here https://github.com/FluidSynth/fluidsynth/issues/1473
                var centibels = 215 * (dataLSB / 127f);
                chan.SetGeneratorOverride(generator, (short)centibels, true);
                break;
            }

            // To filterFc
            case Generator.Type.ModLFOToFilterFc: 
            {
                var cents = Math.Clamp(dataValue, -64, 63) * 56.25f;
                chan.SetGeneratorOverride(generator, (short)cents, true);
                break;
            }

            case Generator.Type.ModEnvToFilterFc: 
            {
                var cents = Math.Clamp(dataValue, -64, 63) * 56.25f;
                chan.SetGeneratorOverride(generator, (short)cents);
                break;
            }

            // Effects
            case Generator.Type.ChorusEffectsSend:
            case Generator.Type.ReverbEffectsSend: 
                chan.SetGeneratorOverride(
                    generator,
                    (short)(Math.Clamp(dataValue, 0, 255) * (1_000f / 255)));
                break;
        }
    }

    private static short MsToTimecents(int ms) =>
        (short)float.Max(short.MinValue, 1_200 * float.Log2(ms / 1_000f));
    
    private static short HzToCents(float hz) =>
        (short)(6_900 + 1_200 * float.Log2(hz / 440));
}