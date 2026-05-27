using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Voice;

/// <summary> Contains a function for computing all modulators </summary>
internal static class CptModulator
{
    private const float EFFECT_MODULATOR_TRANSFORM_MULTIPLIER = 1_000f / 200f;

    /// <summary> Computes a given modulator </summary>
    /// <param name="chan"></param>
    /// <param name="voice">The voice of this modulator.</param>
    /// <param name="pitchWheel">The pitch wheel value, as channel determines if it's a per-note or a global value.</param>
    /// <param name="modulatorIndex">The modulator to compute</param>
    /// <returns>The computed value</returns>
    public static short Compute(
        MidiChannel chan, Voice voice, short pitchWheel, int modulatorIndex)
    {
        var modulator = voice.Modulators[modulatorIndex];
        if (modulator.Base.TransformAmount == 0)
        {
            voice.ModulatorValues[modulatorIndex] = 0;
            return 0;
        }

        var sourceValue = modulator.Base.PrimarySource.GetValue(
            chan, pitchWheel, voice);
        var secondSrcValue = modulator.Base.SecondarySource.GetValue(
            chan, pitchWheel, voice);

        // See the comment for isEffectModulator (modulator.ts in basic_soundbank) for explanation
        var transformAmount = modulator.Base.TransformAmount;
        if (modulator.IsEffectModulator && transformAmount <= 1_000)
            transformAmount = (short)Math.Min(
                transformAmount * EFFECT_MODULATOR_TRANSFORM_MULTIPLIER,
                1_000f);

        // Compute the modulator
        var computedValue = sourceValue * secondSrcValue * transformAmount;
        if (modulator.Base.TType == Modulator.TransformType.Absolute)
            // Abs value
            computedValue = Math.Abs(computedValue);

        // Resonant modulator: take its value and ensure that it won't change the final gain
        if (modulator.IsDefaultResonantModulator)
            // Half the gain, negates the filter
            voice.ResonanceOffset = Math.Max(0, computedValue / 2);

        // Modulation depth
        if (modulator.IsModWheelModulator)
            computedValue *= chan.MidiParamArray.ModulationDepth;

        return voice.ModulatorValues[modulatorIndex] = (short)computedValue;
    }

    /// <summary>
    /// Computes modulators of a given voice. Source and index indicate what modulators shall be computed.
    /// </summary>
    /// <param name="chan"></param>
    /// <param name="voice">The voice to compute modulators for.</param>
    /// <param name="sourceUsesCC">What modulators should be computed, -1 means all, 0 means modulator source enum 1 means midi controller.</param>
    /// <param name="sourceIndex">Enum for the source.</param>
    public static void ComputeAll(
        MidiChannel chan,
        Voice voice,
        int sourceUsesCC = -1,
        int sourceIndex = 0)
    {
        var rentArray = ArraySegment<short>.Empty;
        ComputeAll(ref rentArray, chan, voice, sourceUsesCC, sourceIndex);
        if (!rentArray.AsSpan().IsEmpty) Util.Return(rentArray);
    }

    private static void ComputeAll(
        ref ArraySegment<short> rentArray,
        MidiChannel chan,
        Voice voice,
        int sourceUsesCC = -1,
        int sourceIndex = 0) 
    {
        Debug.Assert(sourceUsesCC is >= -1 and <= 1);

        var modulators = CollectionsMarshal.AsSpan(voice.Modulators);
        var generators = voice.Generators.AsSpan();

        // Apply offsets if enabled
        if (chan.Generators.OffsetsEnabled)
        {
            var g = chan.Generators.Offsets;
            rentArray = Util.Rent<short>(generators.Length);
            generators.CopyTo(rentArray);
            generators = rentArray;

            for (var i = 0; i < generators.Length; i++)
                generators[i] += g[i];
        }

        var modulatedGenerators = voice.ModulatedGenerators.AsSpan();
        var pitch  = chan.PerNotePitch
            ? chan.PitchWheels[voice.MidiNote]
            : (short)chan.MidiParamArray.PitchWheel;
        
        if (sourceUsesCC == -1) 
        {
            // All modulators mode: compute all modulators
            generators.CopyTo(modulatedGenerators);

            for (var i = 0; i < modulators.Length; i++) 
            {
                ref readonly var mod = ref modulators[i];
                // Prevent -32k overflow
                // Testcase: gm.dls polysynth
                modulatedGenerators[(int)mod.Base.Destination] = 
                    (short)Math.Clamp(
                    modulatedGenerators[(int)mod.Base.Destination] +
                    chan.ComputeModulator(voice, pitch, i),
                    short.MinValue,
                    short.MaxValue);
            }

            // Apply limits
            for (var gen = 0; gen < modulatedGenerators.Length; gen++)
            {
                if (!Generator.Limits.TryGetValue(
                        (Generator.Type)gen, out var limit))
                    // Skip unused
                    continue;

                modulatedGenerators[gen] = (short)Math.Clamp(
                    modulatedGenerators[gen], limit.Min, limit.Max);
            }

            return;
        }

        // Optimized mode: calculate only modulators that use the given source
        var sourceCC = sourceUsesCC != 0;//!!sourceUsesCC;
        for (var i = 0; i < modulators.Length; i++) 
        {
            ref readonly var mod = ref modulators[i];
            // If the modulator is influenced by the change
            if ((mod.Base.PrimarySource.IsCC != sourceCC ||
                 mod.Base.PrimarySource.SIndex.AsInt != sourceIndex) &&
                (mod.Base.SecondarySource.IsCC != sourceCC ||
                 mod.Base.SecondarySource.SIndex.AsInt != sourceIndex)) continue;

            var destination = mod.Base.Destination;
            var outputValue = generators[(int)destination];

            // Compute our modulator
            chan.ComputeModulator(voice, pitch, i);

            // Sum the values of all modulators for this destination
            for (var j = 0; j < modulators.Length; j++)
                if (modulators[j].Base.Destination == destination)
                    outputValue += voice.ModulatorValues[j];

            // Apply the limits instantly to prevent -32k overflow
            // Testcase: gm.dls polysynth
            var limits = Generator.Limits[destination];
            modulatedGenerators[(int)destination] = (short)Math.Clamp(
                outputValue, limits.Min, limits.Max);
        }
    }
}