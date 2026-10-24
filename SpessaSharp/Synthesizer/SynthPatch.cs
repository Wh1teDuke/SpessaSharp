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
    
    public string Name
    {
        get => Patch.Name;
        set => Patch = Patch with { Name = value };
    }

    public int BankMSB
    {
        get => Patch.BankMSB;
        set => Patch = Patch with { Data = Patch.Data with { BankMSB = value }};
    }

    public int BankLSB
    {
        get => Patch.BankLSB;
        set => Patch = Patch with { Data = Patch.Data with { BankLSB = value }};
    }

    public int Program
    {
        get => Patch.Program;
        set => Patch = Patch with { Data = Patch.Data with { Program = value }};
    }

    public abstract bool IsDrum { get; }

    public bool IsGMGSDrum
    {
        get => Patch.IsGMGSDrum;
        set => Patch = Patch with { Data = Patch.Data with { IsGMGSDrum = value }};
    }
    
    public bool IsXGDrum => Patch.IsXGDrum;
    
    /// <summary>Checks if the bank and program numbers are the same for the given preset as this one.</summary>
    /// <param name="preset">The preset to check.</param>
    /// <returns></returns>
    public bool Matches(MidiPatch preset) => Patch.Data.Matches(preset);
        
    /// <summary>Returns the voice synthesis data for this preset.</summary>
    /// <param name="cCache"></param>
    /// <param name="note">The MIDI note number.</param>
    /// <param name="velocity">The MIDI velocity.</param>
    /// <returns>The returned sound data.</returns>
    internal abstract ArraySegment<((BasicZone, BasicZone), Voice.Parameters)>
        GetVoiceParameters(
            CachedVoice.Base.Cache cCache, int note, int velocity);
}