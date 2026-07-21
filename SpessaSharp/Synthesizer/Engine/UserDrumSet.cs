using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
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
        _keyBindings.Clear();
    }

    public override bool IsDrum => Patch.IsDrum;

    /// <summary>Sets the source note number for a specific drum key.</summary>
    /// <param name="midiNote">The drum key to edit.</param>
    /// <param name="sourceNote">The MIDI source note number.</param>
    public void SetSourceNote(int midiNote, int sourceNote)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Key = sourceNote };
    }

    /// <summary>Sets the source program number for a specific drum key.</summary>
    /// <param name="midiNote">The drum key to edit.</param>
    /// <param name="sourceProgram">The MIDI source program number.</param>
    public void SetSourceProgram(int midiNote, int sourceProgram)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Patch = kb.Patch with { Program = sourceProgram } };
    }
    
    /// <summary>Sets the source MAP (bank LSB) number for a specific drum key.</summary>
    /// <param name="midiNote">The drum key to edit.</param>
    /// <param name="sourceMap">The MIDI source MAP (bank LSB) number.</param>
    public void SetSourceMap(int midiNote, int sourceMap)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Patch = kb.Patch with { BankLSB = sourceMap } };
    }

    private ref KeyBinding GetOrAdd(int midiNote)
    {
        ref var kb = ref CollectionsMarshal
            .GetValueRefOrAddDefault(_keyBindings, midiNote, out var exists);
        if (!exists) kb = new KeyBinding(Default, midiNote);
        return ref kb;
    }

    /// <summary>
    /// Resets all key bindings to the default GM/GS drum patch.
    /// </summary>
    public void Reset()
    {
        // Initialize all 128 keys to the default drum patch
        _keyBindings.Clear();
    }

    /// <summary>Returns the voice synthesis data for this preset.</summary>
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
        var vParams = resolvedPatch?
            .GetVoiceParameters(cCache, binding.Key, velocity) ?? [];

        // Ensure that the key sounds as intended, similarly to 'PGAL' DLS chunk alias
        foreach (var (_, param) in vParams)
        {
            var generators = param.Generators.AsSpan();
            ref var gen = ref generators[(int)Generator.Type.KeyNum];
            if (gen < 0) gen = (short)binding.Key;
        }
        
        return vParams;
    }
}