using SpessaSharp.Synthesizer.Engine.Effects.XG.Implementation;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

/// <summary> A snapshot of a <see cref="XGEffectBlock.Snapshot"/> </summary>
public interface XGSystemEffectParameter
{
    /// <summary>
    /// The return (level) of the effect.
    /// 0 is silence (-Inf dB), 64 is normal (0 dB) and 127 is double the volume (+6 dB).
    /// </summary>
    public int Return { get; set; }

    /// <summary>
    /// The stereo panning of the effect.
    /// The stereo panning of this effect.
    /// 1 is hard left, 64 is center, 127 is hard right.
    /// </summary>
    public int Pan { get; set; }
}

/// <summary>
/// This interface represents a single XG system effect block.
/// System effects are global and have a send level for each channel, always adding wet output into them.
/// There are 3 System Effects:
/// Reverb <see cref="XGReverbBlock"/>,
/// Chorus <see cref="XGChorusBlock"/>,
/// Variation (in system mode) <see cref="DefaultXGVariation"/>
/// </summary>
public interface XGSystemEffectBlock: 
    XGEffectBlock,
    XGSystemEffectParameter
{
    public new class Snapshot : XGEffectBlock.Snapshot, XGSystemEffectParameter
    {
        public int Return { get; set; }
        public int Pan { get; set; }
    }

    new Snapshot GetSnapshot();

    void ApplySnapshot(Snapshot snapshot);
}