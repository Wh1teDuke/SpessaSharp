using SpessaSharp.Synthesizer.Engine.Effects.Insertion;

namespace SpessaSharp.Synthesizer.Engine.Effects;

public static class Effect
{
    public enum FxReverbType
    {
        Level,//int
        PreLowPass,//int
        Character,//int
        Time,//int
        DelayFeedback,//int
        PreDelayTime,//int
        Macro,//int
    }

    public enum FxChorusType
    {
        Level,//int
        PreLowPass,//int
        Feedback,//int
        Delay,//int
        Rate,//int
        Depth,//int
        SendLevelToReverb,//int
        SendLevelToDelay,//int
        Macro,//int
    }

    public enum FxDelayType
    {
        Level,//int
        PreLowPass,//int
        TimeCenter,//int
        TimeRatioLeft,//int
        TimeRatioRight,//int
        LevelCenter,//int
        LevelLeft,//int
        LevelRight,//int
        Feedback,//int
        SendLevelToReverb,//int
        Macro,//int
    }
    
    public abstract class BaseEffect
    {
        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the amount of the effect sent to the effect output.
        /// </summary>
        public virtual int Level { get; set; }

        /// <summary>
        /// 0-7<br/>
        /// A low-pass filter can be applied to the sound coming into the effect to cut the high
        /// frequency range. Higher values will cut more of the high frequencies, resulting in a
        /// more mellow effect sound.
        /// </summary>
        public virtual int PreLowPass { get; set; }

        internal int Macro;
    }

    public class ReverbProcessorSnapshot : BaseEffect
    {
        /// <summary>
        /// 0-7.<br/>
        /// If character is not available, it should default to the first one.
        /// This parameter selects the type of reverb. 0–5 are reverb effects, and 6 and 7 are delay
        /// effects.
        /// </summary>
        public virtual int Character { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the time over which the reverberation will continue.
        /// Higher values result in longer reverberation.
        /// </summary>
        public virtual int Time { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter is used when the Reverb Character is set to 6 or 7, or the Reverb Type
        /// is set to Delay or Panning Delay (Rev Character 6, 7). It sets the way in which delays
        /// repeat. Higher values result in more delay repeats.
        /// </summary>
        public virtual int DelayFeedback { get; set; }

        /// <summary>
        /// 0 - 127 (ms)<br/>
        /// This parameter sets the delay time until the reverberant sound is heard.
        /// Higher values result in a longer pre-delay time, simulating a larger reverberant space.
        /// </summary>
        public virtual int PreDelayTime { get; set; }
    }

    public abstract class ReverbProcessor: ReverbProcessorSnapshot
    {
        /// <summary> Process the effect and ADDS it to the output. </summary>
        /// <param name="input">The input buffer to process. It always starts at index 0.</param>
        /// <param name="outputLeft">The left output buffer.</param>
        /// <param name="outputRight">The right output buffer.</param>
        /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
        /// <param name="sampleCount">The amount of samples to mix.</param>
        public abstract void Process(
            ReadOnlySpan<float> input,
            Span<float> outputLeft,
            Span<float> outputRight,
            int startIndex,
            int sampleCount);
        
        /// <summary> Gets a synthesizer from this effect processor instance. </summary>
        /// <returns></returns>
        public abstract ReverbProcessorSnapshot GetSnapshot();
    }

    public class ChorusProcessorSnapshot : BaseEffect
    {
        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the level at which the chorus sound is re-input (fed back) into the
        /// chorus. By using feedback, a denser chorus sound can be created.
        /// Higher values result in a greater feedback level.
        /// </summary>
        public virtual int Feedback { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the delay time of the chorus effect.
        /// </summary>
        public virtual int Delay { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the speed (frequency) at which the chorus sound is modulated.
        /// Higher values result in faster modulation.
        /// </summary>
        public virtual int Rate { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the depth at which the chorus sound is modulated.
        /// Higher values result in deeper modulation.
        /// </summary>
        public virtual int Depth { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the amount of chorus sound that will be sent to the reverb.
        /// Higher values result in more sound being sent.
        /// </summary>
        public virtual int SendLevelToReverb { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the amount of chorus sound that will be sent to the delay.
        /// Higher values result in more sound being sent.
        /// </summary>
        public virtual int SendLevelToDelay { get; set; }
    }
    
    public abstract class ChorusProcessor: ChorusProcessorSnapshot
    {
        /// <summary> Process the effect and ADDS it to the output. </summary>
        /// <param name="input">The input buffer to process. It always starts at index 0.</param>
        /// <param name="outputLeft">The left output buffer.</param>
        /// <param name="outputRight">The right output buffer.</param>
        /// <param name="outputReverb">The mono input for reverb. It always starts at index 0.</param>
        /// <param name="outputDelay">The mono input for delay. It always starts at index 0.</param>
        /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
        /// <param name="sampleCount">The amount of samples to mix.</param>
        public abstract void Process(
            ReadOnlySpan<float> input,
            Span<float> outputLeft,
            Span<float> outputRight,
            Span<float> outputReverb,
            Span<float> outputDelay,
            int startIndex,
            int sampleCount);
        
        /// <summary> Gets a synthesizer from this effect processor instance. </summary>
        /// <returns></returns>
        public abstract ChorusProcessorSnapshot GetSnapshot();
    }

    public class DelayProcessorSnapshot : BaseEffect
    {
        /// <summary>
        /// 0-115<br/>
        /// 0.1ms-340ms-1000ms<br/>
        /// The delay effect has three delay times; center, left and
        /// right (when listening in stereo). Delay Time Center sets the delay time of the delay
        /// located at the center.
        /// Refer to SC-8850 Owner's Manual p. 236 for the exact mapping of the values.
        /// </summary>
        public virtual int TimeCenter { get; set; }

        /// <summary>
        /// 0-120<br/>
        /// 4% - 500%<br/>
        /// This parameter sets the delay time of the delay located at the
        /// left as a percentage of the Delay Time Center (up to a max. of 1.0 s).
        /// The resolution is 100/24(%).
        /// </summary>
        public virtual int TimeRatioLeft { get; set; }

        /// <summary>
        /// 1-120<br/>
        /// 4%-500%<br/>
        /// This parameter sets the delay time of the delay located at the right as a percentage of
        /// the Delay Time Center (up to a max. of 1.0 s).
        /// The resolution is 100/24(%).
        /// </summary>
        public virtual int TimeRatioRight { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the volume of the central delay. Higher values result in a louder
        /// center delay.
        /// </summary>
        public virtual int LevelCenter { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the volume of the left delay. Higher values result in a louder left delay.
        /// </summary>
        public virtual int LevelLeft { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the volume of the right delay. Higher values result in a louder
        /// right delay.
        /// </summary>
        public virtual int LevelRight { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// (-64)-63<br/>
        /// This parameter affects the number of times the delay will repeat. With a value of 0,
        /// the delay will not repeat. With higher values there will be more repeats.
        /// With negative (-) values, the center delay will be fed back with inverted phase.
        /// Negative values are effective with short delay times.
        /// </summary>
        public virtual int Feedback { get; set; }

        /// <summary>
        /// 0-127<br/>
        /// This parameter sets the amount of delay sound that is sent to the reverb.
        /// Higher values result in more sound being sent.
        /// </summary>
        public virtual int SendLevelToReverb { get; set; }
    }
    
    public abstract class DelayProcessor: DelayProcessorSnapshot
    {
        /// <summary> Process the effect and ADDS it to the output. </summary>
        /// <param name="input">The input buffer to process. It always starts at index 0.</param>
        /// <param name="outputLeft">The left output buffer.</param>
        /// <param name="outputRight">The right output buffer.</param>
        /// <param name="outputReverb">The mono input for reverb. It always starts at index 0.</param>
        /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
        /// <param name="sampleCount">sampleCount The amount of samples to mix.</param>
        public abstract void Process(
            ReadOnlySpan<float> input,
            Span<float> outputLeft,
            Span<float> outputRight,
            Span<float> outputReverb,
            int startIndex,
            int sampleCount);
        
        /// <summary> Gets a synthesizer from this effect processor instance. </summary>
        /// <returns></returns>
        public abstract DelayProcessorSnapshot GetSnapshot();
    }

    public abstract class InsertionProcessor
    {
        public delegate InsertionProcessor Constructor(
            int sampleRate, int maxBufferSize);

        internal static readonly Constructor[] List = 
        [
            (_, _) => new ThruFX(),
            (sr, _) => new StereoEQFX(sr),
            (sr, _) => new PhaserFX(sr),
            (sr, _) => new AutoPanFX(sr),
            (sr, _) => new AutoWahFX(sr),
            (sr, mbs) => new PhAutoWahFX(sr, mbs),
            (sr, _) => new TremoloFX(sr),
        ];
        
        /// <summary>
        /// The EFX type of this processor, stored as MSB shl | LSB. For example 0x30, 0x10 is 0x3010
        /// </summary>
        public abstract int Type { get; }

        /// <summary>
        /// 0-1 (floating point)<br/>
        /// This parameter sets the amount of insertion sound that will be sent to the reverb.
        /// Higher values result in more sound being sent.
        /// </summary>
        public float SendLevelToReverb { get; set; }

        /// <summary>
        /// 0-1 (floating point)<br/>
        /// This parameter sets the amount of insertion sound that will be sent to the chorus.
        /// Higher values result in more sound being sent.
        /// </summary>
        public float SendLevelToChorus { get; set; }

        /// <summary>
        /// 0-1 (floating point)<br/>
        /// This parameter sets the amount of insertion sound that will be sent to the delay.
        /// Higher values result in more sound being sent.
        /// </summary>
        public float SendLevelToDelay { get; set; }
        
        /// <summary>
        /// Resets the params to their default values.
        /// This does not need to reset send levels.
        /// </summary>
        public abstract void Reset();

        /// <summary> Sets an EFX parameter. </summary>
        /// <param name="parameter">The parameter number (0x03-0x16).</param>
        /// <param name="value">The new value (0-127).</param>
        public abstract void SetParameter(int parameter, int value);

        /// <summary> Process the effect and ADDS it to the output. </summary>
        /// <param name="inputLeft">The left input buffer to process. It always starts at index 0.</param>
        /// <param name="inputRight">The right input buffer to process. It always starts at index 0.</param>
        /// <param name="outputLeft">The left output buffer.</param>
        /// <param name="outputRight">The right output buffer.</param>
        /// <param name="outputReverb">The mono input for reverb. It always starts at index 0.</param>
        /// <param name="outputChorus">The mono input for chorus. It always starts at index 0.</param>
        /// <param name="outputDelay">The mono input for delay. It always starts at index 0.</param>
        /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
        /// <param name="sampleCount">The amount of samples to mix.</param>
        public abstract void Process(
            ReadOnlySpan<float> inputLeft,
            ReadOnlySpan<float> inputRight,

            Span<float> outputLeft,
            Span<float> outputRight,

            Span<float> outputReverb,
            Span<float> outputChorus,
            Span<float> outputDelay,

            int startIndex,
            int sampleCount);
    }

    /// <summary> </summary>
    /// <param name="Type"></param>
    /// <param name="Params">20 parameters for the effect, 255 means "no change" + 3 effect sends (index 20, 21, 22)</param>
    /// <param name="Channels">A boolean list for channels that have the insertion effect enabled.</param>
    public readonly record struct InsertionProcessorSnapshot(
        int Type, ArraySegment<byte> Params, ArraySegment<bool> Channels);
}