using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Voice;

namespace SpessaSharp.Synthesizer.Engine;

/// <summary>
/// A GS User drum set that allows mapping each MIDI key to a different preset and key.
/// This is used for the virtual GS user drum preset.
/// Each of the 128 MIDI keys can be independently bound to any `MIDIPatch`
/// and a specific key within that patch.
/// </summary>
public sealed class UserDrumSet: SynthPatch
{
    public delegate SynthPatch? ResolvePatch(MidiPatch patch);
    
    public static readonly MidiPatch Default = 
        new(0, 0, 0, true);
    
    public readonly record struct KeyBinding(
        MidiPatch Patch, 
        int Key);

    /// <summary>
    /// The key bindings for this drum set.
    /// Index is the MIDI key, value is the bound patch and target key.
    /// </summary>
    private readonly Dictionary<int, KeyBinding> _keyBindings = [];

    /// <summary>
    /// Callback that resolves a <see cref="MidiPatch"/> to a <see cref="SynthPatch"/>.
    /// Provided by the <see cref="SoundBankManager"/>.
    /// </summary>
    private readonly ResolvePatch _resolvePatch;
    
    /// <summary>
    /// Creates a new custom drum set.
    /// </summary>
    /// <param name="program">The MIDI program number for this drum set.</param>
    /// <param name="name">The display name of this drum set.</param>
    /// <param name="resolvePatch">
    /// A callback that resolves a <see cref="MidiPatch"/> to a
    /// <see cref="SynthPatch"/>. Returns <see langword="null"/> if no matching preset
    /// is found. Used to look up the actual preset when a note is played.
    /// </param>
    public UserDrumSet(
        int program,
        string name,
        ResolvePatch resolvePatch)
    {
        Patch = new MidiPatch.Full(
            new MidiPatch(program, 0, 0, true),
            name,
            true
        );
        
        _resolvePatch = resolvePatch;

        Reset();
    }

    public override bool IsDrum => Patch.IsDrum;

    /// <summary>
    /// Sets the key binding for a given MIDI key.
    /// </summary>
    /// <param name="midiKey">The MIDI key to bind (0-127).</param>
    /// <param name="target">the MIDI patch to use for this key and the key to play from the target patch (0-127).</param>
    public void SetKeyBinding(int midiKey, (MidiPatch Patch, int Key) target)
    {
        _keyBindings[midiKey] = new KeyBinding(target.Patch, target.Key);
    }

    /// <summary>
    /// Resets all key bindings to the default GM/GS drum patch.
    /// </summary>
    public void Reset()
    {
        // Initialize all 128 keys to the default drum patch
        _keyBindings.Clear();
    }

    /// <summary>
    /// Returns the voice synthesis data for this preset.
    /// </summary>
    /// <param name="cCache"></param>
    /// <param name="note">The MIDI note number.</param>
    /// <param name="velocity">The MIDI velocity.</param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    internal override ArraySegment<((BasicZone, BasicZone), Voice.Voice.Parameters)> 
        GetVoiceParameters(CachedVoice.Base.Cache cCache, int note, int velocity)
    {
        var binding = _keyBindings.GetValueOrDefault(
            note, new KeyBinding(Default, note));

        var resolvedPatch = _resolvePatch(binding.Patch);
        return resolvedPatch?
            .GetVoiceParameters(cCache, binding.Key, velocity) ?? [];
    }
}