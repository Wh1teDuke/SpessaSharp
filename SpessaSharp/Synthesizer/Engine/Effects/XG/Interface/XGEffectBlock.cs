namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

/// <summary>
/// This is the base for all Yamaha XG effect interfaces.
/// It represents a single effect block.
/// There are 4 effect blocks types in XG: Reverb, Chorus, Variation and Insertion.
/// </summary>
public interface XGEffectBlock
{
    /// <summary> A snapshot of a <see cref="XGEffectBlock"/>. </summary>
    public class Snapshot
    {
        /// <summary>
        /// The type of the processor.
        /// Per XG spec, BASIC EFFECT (LSB = 0) will be used if the exact match is missing.
        /// If both are missing, fallback will be used.
        /// The newly selected processor is reset to its type defaults,
        /// mirroring the GS insertion behavior.
        /// </summary>
        public required int Type;
        /// <summary> The 16 parameter values (14-bit wide params, 7-bit single-byte params). </summary>
        public required int[] Params;
    }
    
    /// <summary>
    /// Sets the type of the processor.
    /// Per XG spec, BASIC EFFECT (LSB = 0) should be used if the exact match is missing.
    /// Type change should reset the parameters.
    /// Fallback behavior (no BASIC EFFECT) is up to the implementation.
    /// </summary>
    /// <param name="type">The 16-bit type value to use.</param>
    void SetType(int type);
    
    /// <summary>
    /// Resets this block to default values, including the processor type.
    /// </summary>
    void Reset();
    
    /// <summary>
    /// Sets the given effect parameter to the given value.
    /// </summary>
    /// <param name="param">The parameter number (0-based).</param>
    /// <param name="value">The value: 14-bit for two-byte params, 7-bit for single-byte params.</param>
    void SetParameter(int param, int value);
    
    /// <summary> Gets a snapshot of this XG effect block. </summary>
    Snapshot GetSnapshot();
    
    /// <summary> Restores this XG effect block from a snapshot. </summary>
    /// <param name="snapshot"> The snapshot to restore. </param>
    void ApplySnapshot(Snapshot snapshot);
}