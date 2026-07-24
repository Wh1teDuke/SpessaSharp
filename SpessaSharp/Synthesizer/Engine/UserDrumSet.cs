using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Voice;
using SpessaSharp.Utils;

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
        DrumParameters Params,
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

    internal void CopyInto(Span<DrumParameters> dParams)
    {
        foreach (var i in _keyBindings.Keys)
        {
            // SC-55 uses 100 cents, SC-88 and above is 50
            // Refer to source binding and do it here
            ref var binding = ref CollectionsMarshal.GetValueRefOrNullRef(
                _keyBindings, i);

            binding = binding with
            {
                Params = binding.Params with
                {
                    Pitch = (int)(
                        binding.Params.Pitch * 
                        (binding.Patch.BankLSB == 1 ? 1 : 0.5))
                }
            };

            dParams[i] = binding.Params;
        }
    }
    
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
    
    public void SetSourcePitch(int midiNote, int sourcePitch)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Params = kb.Params with { Pitch = sourcePitch } };
    }
    
    public void SetSourceGain(int midiNote, float sourceGain)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Params = kb.Params with { Gain = sourceGain } };
    }
    
    public void SetSourceExclusiveClass(int midiNote, int sourceExClass)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Params = kb.Params with { ExclusiveClass = sourceExClass } };
    }
    
    public void SetSourcePan(int midiNote, int sourcePan)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Params = kb.Params with { Pan = sourcePan } };
    }
    
    public void SetSourceReverb(int midiNote, float sourceReverb)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Params = kb.Params with { ReverbGain = sourceReverb } };
    }
    
    public void SetSourceChorus(int midiNote, float sourceChorus)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Params = kb.Params with { ChorusGain = sourceChorus } };
    }
    
    public void SetSourceDelay(int midiNote, float sourceDelay)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Params = kb.Params with { DelayGain = sourceDelay } };
    }
    
    public void SetSourceNoteOff(int midiNote, bool rxNoteOff)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Params = kb.Params with { RxNoteOff = rxNoteOff } };
    }
    
    public void SetSourceNoteOn(int midiNote, bool rxNoteOn)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Params = kb.Params with { RxNoteOn = rxNoteOn } };
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
        if (!exists) kb = new KeyBinding(
            DefaultFor(midiNote), 
            Default,
            midiNote);
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
            note, new KeyBinding(DefaultFor(note), Default, note));

        var resolvedPatch = _resolvePatch(binding.Patch);
        if (resolvedPatch == null)
            // No match, no sound
            return [];
        
        SpessaLog.Info(
            $"Resolving patch for {
                binding.Patch.ToMidiString()} {
                    resolvedPatch.Name}");
        var vParams = 
            resolvedPatch.GetVoiceParameters(cCache, binding.Key, velocity);

        // Ensure that the key sounds as intended, similarly to 'PGAL' DLS chunk alias
        foreach (var (_, param) in vParams)
        {
            var generators = param.Generators.AsSpan();
            ref var gen = ref generators[(int)Generator.Type.KeyNum];
            if (gen < 0) gen = (short)binding.Key;
        }
        
        return vParams;
    }

    private static DrumParameters DefaultFor(int key) =>
        new()
        {
            Pitch = 0,
            Gain = 1,
            ExclusiveClass = 0,
            Pan = 64,
            ReverbGain = Channel.Reset.DefaultDrumReverb[key] / 127f,
            ChorusGain = 0,
            DelayGain = 0, // No drums have delay
            RxNoteOn = true,
            RxNoteOff = false,
        };
}