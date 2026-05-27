using System.Runtime.CompilerServices;
using SpessaSharp.MIDI;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine;

/// <summary>
/// </summary>
/// <param name="Velocity">The new override velocity. -1 means unchanged.</param>
/// <param name="Patch">The MIDI patch this key uses. -1 on any property means unchanged.</param>
/// <param name="Gain">Linear gain override for the voice.</param>
public record struct KeyModifier(
    int Velocity, MidiPatch Patch, float Gain)
{
    public static readonly KeyModifier Default = new(
        -1,
        new MidiPatch(-1, -1, -1, false),
        1);

    public sealed class Manager
    {
        /// <summary>The velocity override mappings for MIDI keys stored as [channelNumber][midiNote].</summary>
        private KeyModifier?[]?[] _keyMappings = [];
        
        /// <summary> Add a mapping for a MIDI key to a KeyModifier. </summary>
        /// <param name="channel">The MIDI channel number.</param>
        /// <param name="midiNote">The MIDI note number (0-127).</param>
        /// <param name="mapping">The KeyModifier to apply for this key.</param>
        public void AddMapping(int channel, int midiNote, KeyModifier mapping) 
        {
            if (channel >= _keyMappings.Length)
                Array.Resize(ref _keyMappings, channel + 1);
            var notes = _keyMappings[channel] ??= new KeyModifier?[128];
            notes[midiNote] = mapping;
        }

        /// <summary> Delete a mapping for a MIDI key. </summary>
        /// <param name="channel">The MIDI channel number.</param>
        /// <param name="midiNote">The MIDI note number (0-127).</param>
        public void DeleteMapping(int channel, int midiNote)
        {
            if (channel >= _keyMappings.Length) return;
            var notes = _keyMappings[channel];
            notes?[midiNote] = null;
        }
        
        /// <summary> Clear all key mappings. </summary>
        public void ClearMappings() => _keyMappings = [];
        
        /// <summary> Sets the key mappings to a new array. </summary>
        /// <param name="mappings">A 2D array where the first dimension is the channel number and the second dimension is the MIDI note number.</param>
        public void SetMappings(KeyModifier?[]?[] mappings) => 
            _keyMappings = mappings;

        /// <summary> Returns the current key mappings. </summary>
        /// <returns></returns>
        public KeyModifier?[]?[] GetMappings() => _keyMappings;

        /// <summary> Gets the velocity override for a MIDI key. </summary>
        /// <param name="channel">The MIDI channel number.</param>
        /// <param name="midiNote">The MIDI note number (0-127).</param>
        /// <returns>The velocity override, or null if no override is set.</returns>
        public int? GetVelocity(int channel, int midiNote) =>
            TryGet(channel)?[midiNote]?.Velocity;
        
        /// <summary> Gets the gain override for a MIDI key. </summary>
        /// <param name="channel">The MIDI channel number.</param>
        /// <param name="midiNote">The MIDI note number (0-127).</param>
        /// <returns>The gain override, or 1 if no override is set.</returns>
        public float GetGain(int channel, int midiNote) =>
            TryGet(channel)?[midiNote]?.Gain ?? 1;
        
        /// <summary> Checks if a MIDI key has an override for the patch. </summary>
        /// <param name="channel">The MIDI channel number.</param>
        /// <param name="midiNote">The MIDI note number (0-127).</param>
        /// <returns>True if the key has an override patch, false otherwise.</returns>
        public bool HasOverridePatch(int channel, int midiNote) =>
            TryGet(channel)?[midiNote]?.Patch.BankMSB >= 0;

        /// <summary>Gets the patch override for a MIDI key. </summary>
        /// <param name="channel">The MIDI channel number.</param>
        /// <param name="midiNote">The MIDI note number (0-127).</param>
        /// <returns>An object containing the bank and program numbers.</returns>
        /// <exception cref="Exception">Error if no modifier is set for the key.</exception>
        public MidiPatch GetPatch(int channel, int midiNote) =>
            TryGet(channel)?[midiNote]?.Patch
                ?? throw SpessaException.Invalid("No modifier.");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private KeyModifier?[]? TryGet(int channel) =>
            channel < _keyMappings.Length ? _keyMappings[channel] : null;
    }
}