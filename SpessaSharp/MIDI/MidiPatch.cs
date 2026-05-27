using System.Runtime.CompilerServices;
using System.Text;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI;

/// <summary> </summary>
/// <param name="Program">The MIDI program number.</param>
/// <param name="BankMSB">The MIDI bank MSB number.</param>
/// <param name="BankLSB">The MIDI bank LSB number.</param>
/// <param name="IsGMGSDrum">If the preset is marked as GM/GS drum preset. Note that XG drums do not have this flag.</param>
public readonly record struct MidiPatch(
    int Program, int BankMSB, int BankLSB, bool IsGMGSDrum)
{
    /// <summary>
    /// </summary>
    /// <param name="Data">The Patch itself.</param>
    /// <param name="Name">The name of the patch.</param>
    /// <param name="IsDrum">
    /// Indicates if this patch is a drum patch.
    /// This is the recommended way of determining if this is a drum preset.<br/>
    /// If <b>IsDrum</b> is true, then this is a GM/GS drum preset.<br/>
    /// If <b>IsDrum</b> is false, then this is a GM2/XG drum preset.
    /// </param>
    public readonly record struct Full(
        MidiPatch Data, string Name, bool IsDrum)
    {
        public int BankMSB => Data.BankMSB;
        public int BankLSB => Data.BankLSB;
        public int Program => Data.Program;
        public bool IsGMGSDrum => Data.IsGMGSDrum;
        /// <summary>Checks if the given patch is an XG/GM2 drum patch.</summary>
        public bool IsXGDrum => IsDrum && !IsGMGSDrum;

        /// <summary>
        /// Converts a given `MIDIPatchFull`to string.
        /// The format is:<br/>
        /// - `[MIDIPatch string] D [name]` for `isDrum` set to `true`.<br/>
        /// - `[MIDIPatch string] M [name]` for `isDrum` set to `false`.
        /// </summary>
        public string ToFullMidiString() =>
            $"{Data.ToMidiString()} {(IsDrum ? "D" : "M")} {Name}";

        /// <summary>Gets a named MIDI patch from a string.</summary>
        /// <param name="midiString"></param>
        /// <exception cref="Exception"></exception>
        public static Full From(ReadOnlySpan<char> midiString)
        {
            var firstSpace = midiString.IndexOf(' ');
            if (firstSpace == -1) Throw(midiString);
            var secondSpace = midiString[(firstSpace + 1) .. ].IndexOf(' ');
            if (secondSpace == -1) Throw(midiString);

            var midiPart = midiString[.. firstSpace];
            var drumMode = midiString[(firstSpace + 1) .. secondSpace];
            var name = midiString[(secondSpace + 1) ..];
            var patch = MidiPatch.From(midiPart);
            return new Full(
                patch, name.ToString(), Ascii.Equals(drumMode, "D"));
            
            void Throw(ReadOnlySpan<char> midiString) =>
                SpessaException.ParsingMidi(
                    $"Invalid named MIDI string: {midiString}");
        }
    }
    
    /// <summary>
    /// Converts a given `MIDIPatch` to a string.
    /// The format is:<br/>
    /// - `DRUM:program` for `GMGSDrum` set to `true`.<br/>
    /// - `bankLSB:bankMSB:program` for `GMGSDrum` set to `false`.
    /// </summary>
    public string ToMidiString() =>
        IsGMGSDrum ? $"DRUM:{Program}" : $"{BankLSB}:{BankMSB}:{Program}";

    /// <summary> Checks if two MIDI patches represent the same one. </summary>
    public bool Matches(MidiPatch p2) => 
        IsGMGSDrum || p2.IsGMGSDrum 
            ? IsGMGSDrum == p2.IsGMGSDrum && Program == p2.Program 
            : this == p2;

    /// <summary>A comparison function for `.sort()` or `.toSorted()`, ordering the patches in ascending order.</summary>
    /// <returns>Order</returns>
    public static int Compare(MidiPatch a, MidiPatch b)
    {
        // Force drum presets to be last
        if (a.IsGMGSDrum && !b.IsGMGSDrum) return +1;
        if (!a.IsGMGSDrum && b.IsGMGSDrum) return -1;
        
        // First, sort by program
        if (a.Program != b.Program) return a.Program - b.Program;
        
        return a.BankMSB != b.BankMSB
            // Next, sort by bankMSB
            ? a.BankMSB - b.BankMSB
            // Finally, sort by bankLSB
            : a.BankLSB - b.BankLSB;
    }

    /// <summary>Gets a MIDI patch from a string.</summary>
    /// <returns>The parsed patch</returns>
    public static MidiPatch From(ReadOnlySpan<char> midiString)
    {
        var c = midiString.Count(':');
        if (c is > 2 or < 1) throw SpessaException.ParsingMidi(
            $"Invalid MIDI string: {midiString}");
    
        var parts = midiString.Split(':');
        parts.MoveNext();

        if (midiString.StartsWith("DRUM"))
        {
            parts.MoveNext();
            var p = int.Parse(midiString[parts.Current]);
            return new MidiPatch(0, 0, p, true);
        }
    
        var bl = int.Parse(midiString[parts.Current]);
        parts.MoveNext();
        var bm = int.Parse(midiString[parts.Current]);
        parts.MoveNext();
        var pr = int.Parse(midiString[parts.Current]);
        return new MidiPatch(bl, bm, pr, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator MidiPatch(Full fullPatch) =>
        fullPatch.Data;
}