using System.Diagnostics;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Synthesizer.Engine.Sysex;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine;

internal static class SystemExclusive
{
    /// <summary>Executes a system exclusive message for the synthesizer.</summary>
    /// <remarks>
    /// This is a rather extensive method that handles various system exclusive messages,
    /// including Roland GS, MIDI Tuning Standard, and other non-realtime messages.
    /// </remarks>
    /// <param name="synth"></param>
    /// <param name="syx">The system exclusive message as an array of bytes.</param>
    /// <param name="channelOffset">channelOffset The channel offset to apply (default is 0).</param>
    public static void Execute(
        Synthesizer synth, ReadOnlySpan<byte> syx, int channelOffset = 0)
    {
        channelOffset += synth.PortSelectChannelOffset;
        var manufacturer = syx[0];
        // Ensure that the device ID matches
        var deviceID = synth.SystemParameters.DeviceID;

        if (// The device ID can be set to "all" which it is by default
            deviceID != -1 &&
            syx[1] != 0x7f && // 0x7f means broadcast, i.e. all MIDI devices
            deviceID != syx[1])
            // Not our device ID
            return;

        switch (manufacturer)
        {
            default:
                SpessaLog.Unsupported(
                    "System Exclusive",
                    syx,
                    $"Unknown manufacturer: {manufacturer}");
                break;

            // Non realtime GM
            case 0x7e:
            // Realtime GM
            case 0x7f: 
                Universal.SystemExclusive(synth, syx, channelOffset);
                break;
            
            // Roland
            case 0x41: 
                RolandGS.SystemExclusive(synth, syx, channelOffset);
                break;

            // Yamaha
            case 0x43: 
                Yamaha.SystemExclusive(synth, syx, channelOffset);
                break;

            // Port select (Falcosoft MIDI Player)
            // https://www.vogons.org/viewtopic.php?p=1404746#p1404746
            case 0xf5: 
                if (syx.Length < 2) return;
                synth.PortSelectChannelOffset = (syx[1] - 1) * 16;
                // Create new port if needed
                while (synth.MidiChannels.Count <=
                       synth.PortSelectChannelOffset)
                {
                    Debug.WriteLine(
                        $"Port select, channel offset {
                        synth.PortSelectChannelOffset}. Creating a new port!");
                    for (var i = 0; i < 16; i++)
                        synth.CreateMIDIChannel(true);
                }
                break;
        }
    }

    [Conditional("DEBUG")]
    internal static void NotRecognized(
            ReadOnlySpan<byte> syx, string what) =>
        Debug.WriteLine(
            $"[WARN] Unrecognized {what} Sysex: {Util.ToHexString(syx)}");
}