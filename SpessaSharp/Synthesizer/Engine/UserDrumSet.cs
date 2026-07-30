using System.Diagnostics;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// The key parameters for this drum set.
    /// Index is the MIDI key, value are the parameters for this key.
    /// </summary>
    private readonly Dictionary<int, UserDrumSetParameter> _keyParams = [];

    /// <summary>
    /// Callback that resolves a <see cref="MidiPatch"/> to a <see cref="SynthPatch"/>.
    /// Provided by the <see cref="SoundBankManager"/>.
    /// </summary>
    private readonly ResolvePatch _resolvePatch;

    internal void CopyInto(Span<DrumParameter> dParams)
    {
        foreach (var i in _keyParams.Keys)
        {
            ref var binding = ref CollectionsMarshal.GetValueRefOrNullRef(
                _keyParams, i);

            binding = binding with
            {
                DrumParameters = binding.DrumParameters with
                {
                    // SC-55 uses 100 cents, SC-88 and above is 50
                    // Refer to source binding and do it here
                    PitchCoarse = binding.DrumParameters.PitchCoarse * 
                                  (binding.SourceDrumSet == 1 ? 1 : 0.5f),
                },
            };

            dParams[i] = binding.DrumParameters;
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

        // Correct Init
        Reset();
    }

    public override bool IsDrum => Patch.IsDrum;

    /// <summary>Sets the source note number for a specific drum key.</summary>
    /// <param name="midiNote">The drum key to edit.</param>
    /// <param name="sourceNote">The MIDI source note number.</param>
    public void SetSourceNote(int midiNote, int sourceNote)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { SourceNoteNumber = sourceNote };
    }
    
    public void SetPitchCoarse(int midiNote, float sourcePitch)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { DrumParameters = kb.DrumParameters 
            with { PitchCoarse = sourcePitch } };
    }
    
    public void SetLevel(int midiNote, int sourceLevel)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { DrumParameters = kb.DrumParameters 
            with { Level = sourceLevel } };
    }
    
    public void SetAssignGroup(int midiNote, int sourceExClass)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { DrumParameters = kb.DrumParameters 
            with { AssignGroup = sourceExClass } };
    }
    
    public void SetPan(int midiNote, int sourcePan)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { DrumParameters = kb.DrumParameters 
            with { Pan = sourcePan } };
    }
    
    public void SetReverb(int midiNote, int sourceReverb)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { DrumParameters = kb.DrumParameters 
            with { ReverbSend = sourceReverb } };
    }
    
    public void SetChorus(int midiNote, int sourceChorus)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { DrumParameters = kb.DrumParameters 
            with { ChorusSend = sourceChorus } };
    }
    
    public void SetVariationSend(int midiNote, int sourceDelay)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { DrumParameters = kb.DrumParameters 
            with { VariationSend = sourceDelay } };
    }
    
    public void SetNoteOff(int midiNote, bool rxNoteOff)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { DrumParameters = kb.DrumParameters 
            with { RxNoteOff = rxNoteOff } };
    }
    
    public void SetNoteOn(int midiNote, bool rxNoteOn)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { DrumParameters = kb.DrumParameters 
            with { RxNoteOn = rxNoteOn } };
    }

    /// <summary>Sets the source program number for a specific drum key.</summary>
    /// <param name="midiNote">The drum key to edit.</param>
    /// <param name="sourceProgram">The MIDI source program number.</param>
    public void SetProgram(int midiNote, int sourceProgram)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { Program = sourceProgram };
    }
    
    /// <summary>Sets the source MAP (bank LSB) number for a specific drum key.</summary>
    /// <param name="midiNote">The drum key to edit.</param>
    /// <param name="sourceMap">The MIDI source MAP (bank LSB) number.</param>
    public void SetSourceDrumSet(int midiNote, int sourceMap)
    {
        ref var kb = ref GetOrAdd(midiNote);
        kb = kb with { SourceDrumSet = sourceMap };
    }
    public void Set(int midiNote, UserDrumSetParameter.Entry entry)
    {
        switch (entry.Type)
        {
            case UserDrumSetParameter.Type.DrumParameters:
                var dpEntry = entry.AsDrumParameter;
                switch (dpEntry.Type)
                {
                    case DrumParameter.Type.PitchCoarse:
                        SetPitchCoarse(midiNote, dpEntry.AsFloat);
                        break;
                    case DrumParameter.Type.PitchFine:
                        throw new UnreachableException();
                    case DrumParameter.Type.Level:
                        SetLevel(midiNote, dpEntry.AsInt);
                        break;
                    case DrumParameter.Type.AssignGroup:
                        SetAssignGroup(midiNote, dpEntry.AsInt);
                        break;
                    case DrumParameter.Type.Pan:
                        SetPan(midiNote, dpEntry.AsInt);
                        break;
                    case DrumParameter.Type.ReverbSend:
                        SetReverb(midiNote, dpEntry.AsInt);
                        break;
                    case DrumParameter.Type.ChorusSend:
                        SetChorus(midiNote, dpEntry.AsInt);
                        break;
                    case DrumParameter.Type.VariationSend:
                        SetVariationSend(midiNote, dpEntry.AsInt);
                        break;
                    case DrumParameter.Type.RxNoteOn:
                        SetNoteOn(midiNote, dpEntry.AsBool);
                        break;
                    case DrumParameter.Type.RxNoteOff:
                        SetNoteOff(midiNote, dpEntry.AsBool);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                break;
            case UserDrumSetParameter.Type.SourceDrumSet:
                SetSourceDrumSet(midiNote, entry.AsInt);
                break;
            case UserDrumSetParameter.Type.Program:
                SetProgram(midiNote, entry.AsInt);
                break;
            case UserDrumSetParameter.Type.SourceNoteNumber:
                SetSourceNote(midiNote, entry.AsInt);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public bool IsSet(int midiNote, UserDrumSetParameter.Entry entry) => 
        _keyParams.TryGetValue(midiNote, out var param) &&
        entry == param[entry.Type];

    private ref UserDrumSetParameter GetOrAdd(int midiNote)
    {
        ref var kb = ref CollectionsMarshal
            .GetValueRefOrAddDefault(_keyParams, midiNote, out var exists);
        if (!exists) kb = UserDrumSetParameter.GetDefault(midiNote);
        return ref kb;
    }

    /// <summary>Resets the drum set.</summary>
    public void Reset()
    {
        // Initialize all 128 keys to the default drum patch
        _keyParams.Clear();
    }

    /// <summary>Gets a snapshot of this User Drum Set instance.</summary>
    /// <returns></returns>
    public UserDrumSetParameter.Entry[] GetSnapshot()
    {
        var result = new UserDrumSetParameter.Entry[
            128 * UserDrumSetParameter.Count];

        for (var midiNote = 0; midiNote < 128; midiNote++)
        {
            var udp = 
                _keyParams.TryGetValue(midiNote, out var kb)
                    ? kb : DefaultFor(midiNote);

            udp.CopyTo(result.AsSpan(
                midiNote * UserDrumSetParameter.Count,
                UserDrumSetParameter.Count));
        }
        
        return result;
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
        var binding = _keyParams.GetValueOrDefault(
            note, DefaultFor(note));

        var tempPatch = new MidiPatch(
            binding.Program, 0, binding.SourceDrumSet, true);
        var resolvedPatch = _resolvePatch(tempPatch);
        // Protect from binding to self as well
        if (resolvedPatch == null || resolvedPatch == this)
        {
            resolvedPatch = _resolvePatch(new MidiPatch(0, 0, 0, true));
            
            if (resolvedPatch == null)
                // No match, no sound
                return [];
        }

        var vParams = 
            resolvedPatch.GetVoiceParameters(
                cCache, binding.SourceNoteNumber, velocity);

        // Ensure that the key sounds as intended, similarly to 'PGAL' DLS chunk alias
        foreach (var (_, param) in vParams)
        {
            var generators = param.Generators.AsSpan();
            ref var gen = ref generators[(int)Generator.Type.KeyNum];
            if (gen < 0) gen = (short)binding.SourceNoteNumber;
        }
        
        return vParams;
    }

    private static UserDrumSetParameter DefaultFor(int key) =>
        new()
        {
            DrumParameters = new DrumParameter
            {
                PitchCoarse     = 0,
                // Unused, shouldn't matter
                PitchFine       = 0,
                Level           = 120,
                AssignGroup     = 0,
                Pan             = 64,
                ReverbSend      = Channel.Reset.DefaultDrumReverb[key],
                ChorusSend      = 0,
                RxNoteOn        = true,
                RxNoteOff       = false,
                VariationSend   = 0,                
            },
    
            SourceNoteNumber = key,
            SourceDrumSet = 0,
            Program = 0,
        };
}