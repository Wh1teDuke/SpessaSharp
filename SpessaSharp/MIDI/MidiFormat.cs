namespace SpessaSharp.MIDI;

/// <summary>
/// The MIDI file track format.
/// Format 0 allows only one track while format 1 allows more than one.
/// Format 2 is very rare and should not be used.
/// </summary>
public enum MidiFormat { m0, m1, m2 }