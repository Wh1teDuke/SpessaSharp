using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Voice;

namespace SpessaSharp.Synthesizer;


/// <summary>
/// A generic synthesizer patch that can return voice parameters.
/// This is used for the virtual GS user drum preset.
/// </summary>
public abstract class SynthPatch
{
    public MidiPatch.Full Patch;
        
    /// <summary>Returns the voice synthesis data for this preset.</summary>
    /// <param name="cCache"></param>
    /// <param name="note">The MIDI note number.</param>
    /// <param name="velocity">The MIDI velocity.</param>
    /// <returns>The returned sound data.</returns>
    internal abstract ArraySegment<((BasicZone, BasicZone), Voice.Parameters)>
        GetVoiceParameters(
            CachedVoice.Base.Cache cCache, int note, int velocity);
}