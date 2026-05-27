using SpessaSharp.MIDI;

namespace SpessaSharp.SoundBank;

public interface IPresetGetter
{
    public BasicPreset? GetPreset(MidiPatch patch, Midi.System system);
}