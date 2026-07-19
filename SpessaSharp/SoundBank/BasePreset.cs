using SpessaSharp.MIDI;
using SpessaSharp.Synthesizer;

namespace SpessaSharp.SoundBank;

public abstract class BasePreset: SynthPatch
{
    public interface IGetter<out T> where T : BasePreset
    {
        public T? GetPreset(MidiPatch patch, Midi.System system);
    }
}