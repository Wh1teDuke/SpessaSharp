using System.Diagnostics;
using System.Runtime.CompilerServices;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI;


public readonly record struct StatusByte(byte Byte)
{
    public int Status => Byte & 0xf0;
    public int Channel => Byte & 0x0f;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Is(MidiMessage.Type type) => Byte == MidiMessage.ID(type);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool LE(MidiMessage.Type type) => Byte <= MidiMessage.ID(type);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool L(MidiMessage.Type type) => Byte < MidiMessage.ID(type);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GE(MidiMessage.Type type) => Byte >= MidiMessage.ID(type);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InRange(MidiMessage.Type min, MidiMessage.Type max) => 
        Byte >= MidiMessage.ID(min) && Byte <= MidiMessage.ID(max);
    
    public override string ToString() => $"StatusByte({Byte:X})";

    public static StatusByte Of(MidiMessage.Type type) => new (type.ID());
    public static implicit operator StatusByte(byte b) => new (b);
    public static implicit operator StatusByte(MidiMessage.Type type) => Of(type);
}

/// <summary>
/// Contains enums for midi events and controllers and functions to parse them
/// </summary>
/// <param name="Ticks">Absolute number of MIDI ticks from the start of the track.</param>
/// <param name="StatusByte">The MIDI message status byte. Note that for meta events, it is the second byte. (not 0xFF).</param>
/// <param name="Data">Message's binary data.</param>
public readonly record struct MidiMessage(
    int Ticks, StatusByte StatusByte, ArraySegment<byte> Data)
{
    public enum Type
    {
        NoteOff, NoteOn, PolyPressure, ControllerChange, ProgramChange,
        ChannelPressure, PitchWheel, SystemExclusive, Timecode,
        SongPosition, SongSelect, TuneRequest, Clock,
        Start, Continue, Stop, ActiveSensing, Reset, SequenceNumber,
        Text, Copyright, TrackName, InstrumentName, Lyric,
        Marker, CuePoint, ProgramName, MidiChannelPrefix, MidiPort,
        EndOfTrack, SetTempo, SmpteOffset, TimeSignature,
        KeySignature, SequenceSpecific
    }

    private static readonly int[] StatusID =
    [
        0x80, 0x90, 0xa0, 0xb0, 0xc0, 0xd0, 0xe0, 0xf0, 0xf1, 
        0xf2, 0xf3, 0xf6, 0xf8, 0xfa, 0xfb, 0xfc, 0xfe, 0xff,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 
        0x20, 0x21, 0x2f, 0x51, 0x54, 0x58, 0x59, 0x7f,
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Type TypeOf(int id)
    {
        var idx = StatusID.IndexOf(id);
        ArgumentOutOfRangeException.ThrowIfNegative(
            idx, "MidiMessage Type not found");
        return (Type)idx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ID(Type type) => StatusID[(int)type];

    public double GetTempo()
    {
        Debug.Assert(StatusByte.Byte == 0x51);
        return 60_000_000d / Util.ReadBigEndian(Data);
    }

    public override string ToString() =>
        $"MidiMessage(T {Ticks}, {StatusByte.Byte:X}, {
            string.Join(" ", Data.Select(b => b.ToString("X2")))})";

    /// <summary>Returns a new MIDI Pitch Wheel message.</summary>
    /// <param name="ticks">Time of this message in absolute MIDI ticks.</param>
    /// <param name="channel">The channel number of this message.</param>
    /// <param name="value">The new value, between 0 and 16_383, where 8_192 is the center (no pitch change).</param>
    /// <returns></returns>
    public static MidiMessage PitchWheel(int ticks, int channel, int value) =>
        new(ticks,
            (StatusByte)(Type.PitchWheel.ID() | (channel % 16)),
            DataOf(value & 0x7f, (value >> 7) & 0x7f));
    
    /// <summary>Returns a new MIDI Channel Pressure message.</summary>
    /// <param name="ticks">Time of this message in absolute MIDI ticks.</param>
    /// <param name="channel">The channel number of this message.</param>
    /// <param name="value">The new value, between 0 and 127.</param>
    /// <returns></returns>
    public static MidiMessage ChannelPressure(
            int ticks, int channel, int value) =>
        new(ticks,
            (StatusByte)(Type.ChannelPressure.ID() | (channel % 16)),
            DataOf(value));
    
    /// <summary>Returns a new MIDI Program Change message.</summary>
    /// <param name="ticks">Time of this message in absolute MIDI ticks.</param>
    /// <param name="channel">The channel number of this message.</param>
    /// <param name="program">The new MIDI program number, between 0 and 127.</param>
    /// <returns></returns>
    public static MidiMessage ProgramChange(
            int ticks, int channel, int program) =>
        new(ticks,
            (StatusByte)(Type.ProgramChange.ID() | (channel % 16)),
            DataOf(program));
    
    /// <summary>Returns a new MIDI Controller Change message.</summary>
    /// <param name="ticks">Time of this message in absolute MIDI ticks.</param>
    /// <param name="channel">The channel number of this message.</param>
    /// <param name="controller">The MIDI controller.</param>
    /// <param name="value">The new value.</param>
    /// <returns></returns>
    public static MidiMessage ControllerChange(
            int ticks, int channel, Midi.CC controller, int value) =>
        new(ticks,
            (StatusByte)(Type.ControllerChange.ID() | (channel % 16)),
            DataOf((int)controller, value));
    
    
    /// <summary>Returns a new MIDI System Exclusive message.</summary>
    /// <param name="ticks">Time of this message in absolute MIDI ticks.</param>
    /// <param name="data">The data of the system exclusive message, excluding the starting 0xF0 byte.</param>
    /// <returns></returns>
    public static MidiMessage SystemExclusive(
            int ticks, ReadOnlySpan<byte> data) =>
        new(ticks, Type.SystemExclusive, data.ToArray());

    /// <summary>
    /// Returns a new MIDI Registered Parameter message. Sends both data MSB and LSB.
    /// </summary>
    /// <param name="ticks">Time of this message in absolute MIDI ticks.</param>
    /// <param name="channel">The channel number of this message.</param>
    /// <param name="parameter">The 14-bit MIDI registered parameter number.</param>
    /// <param name="value">The 14-bit new value.</param>
    /// <returns></returns>
    public static MidiMessage[] RegisteredParameter(
        int ticks, int channel, int parameter, int value)
    {
        if (parameter is > 16_383 or < 0) 
            throw new ArgumentOutOfRangeException(nameof(parameter), "Parameter must be between 0 and 16383.");
        if (value is > 16_383 or < 0) 
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be between 0 and 16383.");
        
        return
        [
            ControllerChange(
                ticks, channel, Midi.CC.RegisteredParameterMSB, parameter >> 7),
            ControllerChange(
                ticks, channel, Midi.CC.RegisteredParameterLSB, parameter & 0x7f),
            ControllerChange(
                ticks, channel, Midi.CC.DataEntryMSB, value >> 7),
            ControllerChange(
                ticks, channel, Midi.CC.DataEntryLSB, value & 0x7f),
        ];
    }

    private static ArraySegment<byte> DataOf(params ReadOnlySpan<int> args)
    {
        var bytes = new byte[args.Length];
        for (var i = 0; i < args.Length; i++) bytes[i] = (byte)args[i];
        return bytes;
    }
}

public static class TypeEx
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ID(this MidiMessage.Type type) =>
        (byte)MidiMessage.ID(type);
}