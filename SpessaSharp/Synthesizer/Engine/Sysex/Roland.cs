using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Sysex;

internal static class Roland
{
    /// <summary>
    /// Handles a GS system exclusive<br/>
    /// http://www.bandtrax.com.au/sysex.htm<br/>
    /// https://cdn.roland.com/assets/media/pdf/SC-8850_OM.pdf
    /// </summary>
    /// <param name="synth"></param>
    /// <param name="syx"></param>
    /// <param name="channelOffset"></param>
    public static void SystemExclusive(
        Synthesizer synth, ReadOnlySpan<byte> syx, int channelOffset = 0)
    {
        // 0x12: DT1 (Device Transmit)
        if (syx.Length >= 4 && syx[3] == 0x12) 
        {
            // Model ID
            switch (syx[2]) 
            {
                // GS
                case 0x42: 
                {
                    // This is a GS sysex
                    var a1 = syx[4];
                    var a2 = syx[5];
                    var a3 = syx[6];

                    // Sanity check
                    var data = byte.Min(syx[7], 127);
                    // SYSTEM MODE SET
                    if (
                        a1 == 0 && 
                        a2 == 0 && 
                        a3 == 0x7f &&
                        data is 0x00 or 0x01) 
                    {
                        if (data == 0x01) 
                        {
                            // Double module mode, ensure at least 32 channels
                            SpessaLog.GSInfo("Mode", "Double Module");
                            while (synth.MidiChannels.Count < 32)
                                synth.CreateMIDIChannel(true);
                        }
                            
                        // This is a GS reset
                        SpessaLog.CoolInfo("MIDI System", "Roland GS");
                        synth.Reset(Midi.System.GS);
                        return;
                    }

                    // Patch Parameter
                    if (a1 is 0x40 or 0x50) 
                    {
                        // 50 means BLOCK B (+16 channels)
                        // Testcase: 95043-2.KYC.mid
                        if (a1 == 0x50)
                            channelOffset += 16;
                            
                        // System Parameter
                        if (a2 == 0x00) 
                        {
                            switch (a3) 
                            {
                                case 0x00: 
                                {
                                    // Roland GS master tune
                                    var tune =
                                        (data << 12) |
                                        (syx[8] << 8) |
                                        (syx[9] << 4) |
                                        syx[10];
                                    var cents = (tune - 1_024) / 10f;
                                    synth.Set((
                                        GlobalMidiParameter.Type.FineTune, 
                                        cents));
                                    SpessaLog.GSInfo("Master Tune", cents, "cents");
                                    break;
                                }

                                case 0x04: 
                                    // Roland GS master volume
                                    SpessaLog.GSInfo("Master Volume", data);
                                    synth.Set(
                                        (GlobalMidiParameter.Type.Volume,
                                            data / 127f));
                                    break;

                                case 0x05: 
                                    // Roland master key shift
                                    var transpose = data - 64;
                                    SpessaLog.GSInfo("Master Key-Shift", transpose);
                                    synth.Set((
                                        GlobalMidiParameter.Type.KeyShift,
                                        transpose));
                                    break;

                                case 0x06: 
                                    // Roland master pan
                                    // 63, it ranges from 1 to 127, NOT 0 to 127!
                                    var pan = (data - 64) / 63f;

                                    SpessaLog.GSInfo("Master Pan", pan);
                                    synth.Set((
                                        GlobalMidiParameter.Type.Pan, pan));
                                    break;

                                case 0x7f: 
                                    // Roland mode set
                                    // GS mode set
                                    if (data == 0x00) 
                                    {
                                        // This is a GS reset
                                        SpessaLog.CoolInfo("MIDI System", "Roland GS");
                                        synth.Reset(Midi.System.GS);
                                    } 
                                    else if (data == 0x7f) 
                                    {
                                        // GS mode off
                                        SpessaLog.CoolInfo("MIDI System", "General MIDI 1");
                                        synth.Reset(Midi.System.GM);
                                    }
                                    break;

                                default:
                                    SpessaLog.GSFail("System Parameter", syx);
                                    break;
                            }
                            return;
                        }

                        // Part Parameter, Patch Common (Effects)
                        if (a2 == 0x01) 
                        {
                            var isReverb = a3 is >= 0x30 and <= 0x37;
                            var isChorus = a3 is >= 0x38 and <= 0x40;
                            var isDelay = a3 is >= 0x50 and <= 0x5a;
                            // Disable effect editing if locked
                            if (isReverb && synth.SystemParameters.ReverbLock)
                                return;
                            if (isChorus && synth.SystemParameters.ChorusLock)
                                return;
                            if (isDelay && synth.SystemParameters.DelayLock)
                                return;
                            /*
                            0x40 - chorus to delay
                            enable delay that way
                             */
                            synth.DelayActive |= a3 == 0x40 || isDelay;

                            switch (a3) 
                            {
                                default: 
                                    SpessaLog.GSFail(
                                        "Patch Common Parameter",
                                        [a3]);
                                    break;

                                case 0x00: 
                                {
                                    // Patch name
                                    var patchName = Util.ReadBinaryString(
                                        syx.Slice(7, 16));
                                    SpessaLog.GSInfo(
                                        "Patch Name", 
                                        Util.ToString(patchName));
                                    synth.CallEvent(new Event.CbDisplayMessage(
                                        syx.ToArray()));
                                    break;
                                }
                                // Reverb
                                case 0x30: 
                                    // Reverb macro
                                    synth.ReverbProcessor.Macro = data;
                                    synth.SetReverbMacro(data);
                                    SpessaLog.GSInfo("Reverb Macro", data);
                                    // Event called in setMacro
                                    break;
                                case 0x31: 
                                    // Reverb character
                                    synth.ReverbProcessor.Character = data;
                                    SpessaLog.GSInfo("Reverb Character", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfReverb(
                                            Effect.FxReverbType.Character,
                                            data));
                                    break;
                                case 0x32:
                                    // Reverb pre-PLF
                                    synth.ReverbProcessor.PreLowPass = data;
                                    SpessaLog.GSInfo("Reverb Pre-LPF", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfReverb(
                                            Effect.FxReverbType.PreLowPass,
                                            data));
                                    break;
                                case 0x33: 
                                    // Reverb level
                                    synth.ReverbProcessor.Level = data;
                                    SpessaLog.GSInfo("Reverb Level", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfReverb(
                                            Effect.FxReverbType.Level,
                                            data));
                                    break;
                                case 0x34: 
                                    // Reverb time
                                    synth.ReverbProcessor.Time = data;
                                    SpessaLog.GSInfo("Reverb Time", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfReverb(
                                            Effect.FxReverbType.Time,
                                            data));
                                    break;
                                case 0x35: 
                                    // Reverb delay feedback
                                    synth.ReverbProcessor.DelayFeedback = data;
                                    SpessaLog.GSInfo("Reverb Delay Feedback", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfReverb(
                                            Effect.FxReverbType.DelayFeedback,
                                            data));
                                    break;

                                case 0x36: 
                                    // Reverb send to chorus, legacy SC-55 that's recognized by later models and unsupported.
                                    break;

                                case 0x37:
                                    // Reverb predelay time
                                    synth.ReverbProcessor.PreDelayTime = data;
                                    SpessaLog.GSInfo("Reverb Predelay Time", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfReverb(
                                            Effect.FxReverbType.PreDelayTime,
                                            data));
                                    break;

                                // Chorus
                                case 0x38: 
                                    // Chorus macro
                                    synth.ChorusProcessor.Macro = data;
                                    synth.SetChorusMacro(data);
                                    SpessaLog.GSInfo("Chorus Macro", data);
                                    // Event called in setMacro
                                    break;
                                case 0x39: 
                                    // Chorus pre-LPF
                                    synth.ChorusProcessor.PreLowPass = data;
                                    SpessaLog.GSInfo("Pre-LPF", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfChorus(
                                            Effect.FxChorusType.PreLowPass,
                                            data));
                                    break;
                                case 0x3a: 
                                    // Chorus level
                                    synth.ChorusProcessor.Level = data;
                                    SpessaLog.GSInfo("Chorus Level", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfChorus(
                                            Effect.FxChorusType.Level,
                                            data));
                                    break;
                                case 0x3b: 
                                    // Chorus feedback
                                    synth.ChorusProcessor.Feedback = data;
                                    SpessaLog.GSInfo("Chorus Feedback", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfChorus(
                                            Effect.FxChorusType.Feedback,
                                            data));
                                    break;
                                case 0x3c: 
                                    // Chorus delay
                                    synth.ChorusProcessor.Delay = data;
                                    SpessaLog.GSInfo("Chorus Delay", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfChorus(
                                            Effect.FxChorusType.Delay,
                                            data));
                                    break;
                                case 0x3d: 
                                    // Chorus rate
                                    synth.ChorusProcessor.Rate = data;
                                    SpessaLog.GSInfo("Chorus Rate", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfChorus(
                                            Effect.FxChorusType.Rate,
                                            data));
                                    break;
                                case 0x3e: 
                                    // Chorus depth
                                    synth.ChorusProcessor.Depth = data;
                                    SpessaLog.GSInfo("Chorus Depth", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfChorus(
                                            Effect.FxChorusType.Depth,
                                            data));
                                    break;
                                case 0x3f: 
                                    // Chorus send level to reverb
                                    synth.ChorusProcessor.SendLevelToReverb =
                                        data;
                                    SpessaLog.GSInfo(
                                        "Chorus Send Level To Reverb", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfChorus(
                                            Effect.FxChorusType.SendLevelToReverb,
                                            data));
                                    break;
                                case 0x40: 
                                    // Chorus send level to delay
                                    synth.ChorusProcessor.SendLevelToDelay =
                                        data;
                                    SpessaLog.GSInfo(
                                        "Chorus Send Level To Delay", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfChorus(
                                            Effect.FxChorusType.SendLevelToDelay,
                                            data));
                                    break;

                                // Delay
                                case 0x50: 
                                    // Delay macro
                                    synth.DelayProcessor.Macro = data;
                                    synth.SetDelayMacro(data);
                                    SpessaLog.GSInfo("Delay Macro", data);
                                    // Event called in setMacro
                                    break;
                                case 0x51: 
                                    // Delay pre-PLF
                                    synth.DelayProcessor.PreLowPass = data;
                                    SpessaLog.GSInfo("Delay Pre-LPF", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.PreLowPass,
                                            data));
                                    break;
                                case 0x52: 
                                    // Delay time center
                                    synth.DelayProcessor.TimeCenter = data;
                                    SpessaLog.GSInfo("Delay Time Center", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.TimeCenter,
                                            data));
                                    break;
                                case 0x53: 
                                    // Delay time ratio left
                                    synth.DelayProcessor.TimeRatioLeft = data;
                                    SpessaLog.GSInfo("Delay Time Ratio Left", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.TimeRatioLeft,
                                            data));
                                    break;
                                case 0x54: 
                                    // Delay time ratio right
                                    synth.DelayProcessor.TimeRatioRight = data;
                                    SpessaLog.GSInfo("Delay Time Ratio Right", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.TimeRatioRight,
                                            data));
                                    break;
                                case 0x55: 
                                    // Delay level center
                                    synth.DelayProcessor.LevelCenter = data;
                                    SpessaLog.GSInfo("Delay Level Center", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.LevelCenter,
                                            data));
                                    break;
                                case 0x56: 
                                    // Delay level left
                                    synth.DelayProcessor.LevelLeft = data;
                                    SpessaLog.GSInfo("Delay Level Left", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.LevelLeft,
                                            data));
                                    break;
                                case 0x57: 
                                    // Delay level right
                                    synth.DelayProcessor.LevelRight = data;
                                    SpessaLog.GSInfo("Delay Level Right", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.LevelRight,
                                            data));
                                    break;
                                case 0x58: 
                                    // Delay level
                                    synth.DelayProcessor.Level = data;
                                    SpessaLog.GSInfo("Delay Level", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.Level,
                                            data));
                                    break;
                                case 0x59: 
                                    // Delay feedback
                                    synth.DelayProcessor.Feedback = data;
                                    SpessaLog.GSInfo("Delay Feedback", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.Feedback,
                                            data));
                                    break;
                                case 0x5a: 
                                    // Delay send level to reverb
                                    synth.DelayProcessor.SendLevelToReverb =
                                        data;
                                    SpessaLog.GSInfo(
                                        "Delay Send Level To Reverb", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfDelay(
                                            Effect.FxDelayType.SendLevelToReverb,
                                            data));
                                    break;
                            }
                            break;
                        }

                        // EFX Parameter
                        if (a2 == 0x03) 
                        {
                            if (synth.SystemParameters.InsertionEffectLock)
                                return;

                            // Write parameters
                            if (a3 is >= 0x03 and <= 0x19)
                                synth.InsertionParams[a3 - 3] = data;

                            if (a3 is >= 0x03 and <= 0x16) 
                            {
                                synth.InsertionProcessor.SetParameter(a3, data);
                                SpessaLog.GSInfo($"EFX Parameter {a3 - 2}", data);
                                synth.CallEvent(
                                    Event.CbEffectChange.OfInsertion(
                                        a3, data));
                                return;
                            }
                            switch (a3) 
                            {
                                default: 
                                    SpessaLog.GSFail("Insertion Effect", [a3]);
                                    return;

                                case 0x00: 
                                {
                                    // EFX Type
                                    var type = (data << 8) | syx[8];
                                        
                                    if (synth.InsertionEffects.TryGetValue(
                                        type, out var proc)) 
                                    {
                                        SpessaLog.GSInfo("EFX Type", 
                                            type.ToString("X"));
                                        synth.InsertionProcessor = proc;
                                    } 
                                    else 
                                    {
                                        synth.InsertionProcessor =
                                            synth.InsertionFallback;
                                        SpessaLog.GSFail(
                                            "EFX Processor", 
                                            [data, syx[8]], 
                                            "Using Thru.");
                                    }
                                    synth.ResetInsertionParams();
                                    synth.InsertionProcessor.Reset();
                                    // Special case: 16-bit value
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfInsertion(
                                            0, type));
                                    return;
                                }

                                case 0x17: 
                                    // To reverb
                                    // Divide, insertions use 0-1
                                    synth.InsertionProcessor.SendLevelToReverb =
                                        (data / 127f) *
                                        Synthesizer.EFX_SENDS_GAIN_CORRECTION;
                                    SpessaLog.GSInfo("EFX Send Level to Reverb", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfInsertion(
                                            a3, data));
                                    return;

                                case 0x18: 
                                    // To chorus
                                    // Divide, insertions use 0-1
                                    synth.InsertionProcessor.SendLevelToChorus =
                                        (data / 127f) *
                                        Synthesizer.EFX_SENDS_GAIN_CORRECTION;
                                    SpessaLog.GSInfo("EFX Send Level to Chorus", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfInsertion(
                                            a3, data));
                                    return;

                                case 0x19: 
                                    // To delay
                                    // Divide, insertions use 0-1
                                    synth.InsertionProcessor.SendLevelToDelay =
                                        (data / 127f) *
                                        Synthesizer.EFX_SENDS_GAIN_CORRECTION;
                                    synth.DelayActive = true;
                                    SpessaLog.GSInfo("EFX Send Level to Delay", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfInsertion(
                                            a3, data));
                                    return;
                            }
                        }

                        // Patch Parameters
                        if (a2 >> 4 == 1) 
                        {
                            // This is an individual part (channel) parameter
                            // Determine the channel
                            // Note that: 0 means channel 9 (drums), and only then 1 means channel 0, 2 channel 1, etc.
                            // SC-8850 manual, page 237
                            var channel =
                                MidiUtils.SyxToChannel(a2 & 0x0f) + channelOffset;
                            // For example, 0x1A means A = 11, which corresponds to channel 12 (counting from 1)

                            if (!Util.InRange(synth.MidiChannels, channel))
                            {
                                SpessaLog.GSFail(
                                    $"Patch Parameter for {channel}", 
                                    syx,
                                    "Invalid channel number");
                                return;
                            }
                                
                            var ch = synth.MidiChannels[channel];
                            switch (a3) 
                            {
                                default: 
                                    // This is some other GS sysex...
                                    SpessaLog.GSFail(
                                        $"Patch Part Parameter for {channel}", 
                                       [a3]);
                                    return;

                                case 0x00: 
                                    // Tone number (program change)
                                    ch.ControllerChange(
                                        Midi.CC.BankSelect, data);
                                    ch.ProgramChange(syx[8]);
                                    break;

                                case 0x02: 
                                    // Rx. channel (0x10 is OFF)
                                    var rxChannel =
                                        data == 0x10
                                            ? -1
                                            : data + channelOffset;
                                    ch.Set((
                                        ChannelMidiParameter.Type.RxChannel,
                                        rxChannel));
                                    synth.CustomChannelNumbers |=
                                        rxChannel != ch.Channel;
                                    SpessaLog.GSInfo(
                                        $"Rx. Channel on {channel}", rxChannel);
                                    break;

                                case 0x13: 
                                    // Mono/poly
                                    var poly = data == 1;
                                    ch.Set((
                                        ChannelMidiParameter.Type.PolyMode,
                                        poly));
                                    SpessaLog.GSInfo(
                                        $"Mono/poly on {channel}",
                                        poly ? "POLY" : "MONO");
                                    break;

                                case 0x14:
                                    ch.Set((MidiChannel.Assign)data);
                                    SpessaLog.GSInfo($"MidiChannel.Assign mode on {channel}", data);
                                    break;

                                case 0x15: 
                                    // This is the Use for Drum Part sysex (multiple drums)
                                    ch.Set((
                                        ChannelMidiParameter.Type.DrumMap, 
                                        data));
                                    var isDrums = data > 0; // If set to other than 0, is a drum channel
                                    ch.SetGSDrums(isDrums);
                                    SpessaLog.GSInfo($"Drums on {channel}", isDrums);
                                    return;

                                case 0x16: 
                                {
                                    // This is the pitch key shift sysex
                                    var keyShift = data - 64;
                                    ch.Set((ChannelMidiParameter.Type.KeyShift, keyShift));
                                    SpessaLog.GSInfo($"Key Shift for {channel}", keyShift);
                                    return;
                                }

                                // Pitch offset fine in Hz is not supported so far
                                case 0x19: 
                                    // Part level (cc#7)
                                    ch.ControllerChange(
                                        Midi.CC.MainVolume, data);
                                    return;
                                    
                                case 0x1a:
                                    // Velocity Sense Depth
                                    ch.Set((
                                        ChannelMidiParameter.Type.VelocitySenseDepth,
                                        data));
                                    SpessaLog.GSInfo(
                                        $"Velocity Sense Depth for {channel}", data);
                                    return;

                                case 0x1b: 
                                    // Velocity Sense Offset
                                    ch.Set((
                                        ChannelMidiParameter.Type.VelocitySenseOffset,
                                        data));
                                    SpessaLog.GSInfo(
                                        $"Velocity Sense Offset for {channel}", data);
                                    return;

                                // Pan position
                                case 0x1c: 
                                {
                                    // 0 is random
                                    var panPosition = data;
                                    var randomPan = panPosition == 0;
                                    ch.Set((
                                        ChannelMidiParameter.Type.RandomPan,
                                        randomPan));
                                        
                                    if (randomPan) 
                                    {
                                        SpessaLog.GSInfo(
                                            $"Random pan on {channel}", "ON");
                                    } 
                                    else 
                                    {
                                        ch.ControllerChange(
                                            Midi.CC.Pan, panPosition);
                                    }
                                    break;
                                }

                                case 0x1f: 
                                    // CC1 controller number
                                    ch.Set(new ChannelMidiParameter(
                                        ChannelMidiParameter.Type.CC1, 
                                        (Midi.CC)data));
                                    SpessaLog.GSInfo(
                                        $"CC1 Controller Number for {channel}",
                                        data);
                                    break;

                                case 0x20: 
                                    // CC2controller number
                                    ch.Set(new ChannelMidiParameter(
                                        ChannelMidiParameter.Type.CC2, 
                                        (Midi.CC)data));
                                    SpessaLog.GSInfo(
                                        $"CC2 Controller Number for {channel}", 
                                        data);
                                    break;

                                // Chorus send
                                case 0x21: 
                                    ch.ControllerChange(
                                        Midi.CC.ChorusDepth, data);
                                    break;

                                // Reverb send
                                case 0x22: 
                                    ch.ControllerChange(
                                        Midi.CC.ReverbDepth, data);
                                    break;

                                case 0x2a: 
                                {
                                    // Fine tune
                                    // 0-16384
                                    var tune = (data << 7) | syx[8];
                                    var cents = (tune - 8_192) / 81.92f;
                                    ch.Set((
                                        ChannelMidiParameter.Type.FineTune,
                                        cents));
                                    SpessaLog.GSInfo(
                                        $"Fine tuning for {channel}",
                                        Util.Round(cents),
                                        "cents");
                                    break;
                                }

                                // Delay send
                                case 0x2c: 
                                    ch.ControllerChange(
                                        Midi.CC.VariationDepth, data);
                                    break;

                                case 0x30: 
                                    // Vibrato rate
                                    ch.ControllerChange(
                                        Midi.CC.VibratoRate, data);
                                    break;

                                case 0x31: 
                                    // Vibrato depth
                                    ch.ControllerChange(
                                        Midi.CC.VibratoDepth, data);
                                    break;

                                case 0x32: 
                                    // Filter cutoff
                                    // It's so out of order, Roland...
                                    ch.ControllerChange(
                                        Midi.CC.Brightness, data);
                                    break;

                                case 0x33: 
                                    // Filter resonance
                                    ch.ControllerChange(
                                        Midi.CC.FilterResonance, data);
                                    break;

                                case 0x34: 
                                    // Attack time
                                    ch.ControllerChange(
                                        Midi.CC.AttackTime, data);
                                    break;

                                case 0x35: 
                                    // Decay time
                                    ch.ControllerChange(
                                        Midi.CC.DecayTime, data);
                                    break;

                                case 0x36: 
                                    // Release time
                                    ch.ControllerChange(
                                        Midi.CC.ReleaseTime, data);
                                    break;

                                case 0x37: 
                                    // Vibrato delay
                                    // It seems that they forgot about it and put it last...
                                    ch.ControllerChange(
                                        Midi.CC.VibratoDelay, data);
                                    break;

                                case 0x40: 
                                {
                                    // Scale tuning: up to 12 bytes
                                    var tuningBytes = syx.Length - 9; // Data starts at 7, minus checksum and f7
                                    // Read em bytes
                                    var newTuning = (Span<byte>)stackalloc byte[12];
                                    for (var i = 0; i < tuningBytes; i++) 
                                        newTuning[i] = (byte)(syx[i + 7] - 64);
                                    ch.SetOctaveTuning(newTuning);
                                    SpessaLog.GSInfo(
                                        $"Octave Scale Tuning for {channel}",
                                        string.Join(", ", newTuning.ToArray()));
                                    break;
                                }
                            }
                                
                            return;
                        }

                        // Patch Parameter controllers
                        if (a2 >> 4 == 2) 
                        {
                            // This is an individual part (channel) parameter
                            // Determine the channel
                            // Note that: 0 means channel 9 (drums), and only then 1 means channel 0, 2 channel 1, etc.
                            // SC-8850 manual, page 237
                            var channel =
                                MidiUtils.SyxToChannel(a2 & 0x0f) + channelOffset;
                            // For example, 0x1A means A = 11, which corresponds to channel 12 (counting from 1)
                            var ch = synth.MidiChannels[channel];
                            switch (a3 & 0xf0) 
                            {
                                default: 
                                    // This is some other GS sysex...
                                    SpessaLog.GSFail(
                                        $"Patch Parameter Controller for {channel}",
                                        [(byte)(a3 & 0xf0)]);
                                    break;

                                case 0x00: 
                                    // Modulation wheel
                                    if ((a3 & 0x0f) == 0x04) {
                                        // LFO1 Pitch depth
                                        // Special case:
                                        // If the source is a mod wheel, it's a strange way of setting the modulation depth
                                        // Testcase: J-Cycle.mid (it affects gm.dls which uses LFO1 for modulation)
                                        var cents = (data / 127f) * 600;
                                        ch.Set(
                                            (ChannelMidiParameter.Type.ModulationDepth,
                                                cents));
                                        SpessaLog.GSInfo(
                                            $"Modulation depth for {channel}",
                                            Util.Round(cents),
                                            "cents");
                                        break;
                                    }
                                    ch.DynamicModulators.SetupReceiver(
                                        a3,
                                        data,
                                        (int)Midi.CC.ModulationWheel,
                                        true,
                                        "mod wheel");
                                    break;

                                case 0x10: 
                                    // Pitch wheel
                                    if ((a3 & 0x0f) == 0x00) 
                                    {
                                        // See https://github.com/spessasus/SpessaSynth/issues/154
                                        // Pitch control
                                        // Special case:
                                        // If the source is a pitch wheel, it's a strange way of setting the pitch wheel range
                                        // Testcase: th07_03.mid
                                        var centeredValue = data - 64;
                                        ch.Set(
                                            (ChannelMidiParameter.Type.PitchWheelRange,
                                                (float)centeredValue));
                                        SpessaLog.GSInfo(
                                            $"Pitch Wheel Range for {channel}",
                                            centeredValue,
                                            "semitones");
                                        break;
                                    }
                                    ch.DynamicModulators.SetupReceiver(
                                        a3,
                                        data,
                                        Modulator.Source.ID(Modulator.Source.ControllerSource.PitchWheel),
                                        false,
                                        "pitch wheel",
                                        true);
                                    break;

                                case 0x20: 
                                    // Channel pressure
                                    ch.DynamicModulators.SetupReceiver(
                                        a3,
                                        data,
                                        Modulator.Source.ID(Modulator.Source.ControllerSource.ChannelPressure),
                                        false,
                                        "channel pressure");
                                    break;

                                case 0x30: 
                                    // Poly pressure
                                    ch.DynamicModulators.SetupReceiver(
                                        a3,
                                        data,
                                        Modulator.Source.ID(Modulator.Source.ControllerSource.PolyPressure),
                                        false,
                                        "poly pressure");
                                    break;

                                case 0x40: 
                                    // CC1
                                    ch.DynamicModulators.SetupReceiver(
                                        a3,
                                        data,
                                        (int)ch.MidiParamArray.CC1,
                                        true,
                                        "CC1");
                                    break;

                                case 0x50: 
                                    // CC2
                                    ch.DynamicModulators.SetupReceiver(
                                        a3,
                                        data,
                                        (int)ch.MidiParamArray.CC2,
                                        true,
                                        "CC2");
                                    break;
                            }
                            return;
                        }

                        // Patch Parameter Tone Map
                        if (a2 >> 4 == 4) 
                        {
                            // This is an individual part (channel) parameter
                            // Determine the channel
                            // Note that: 0 means channel 9 (drums), and only then 1 means channel 0, 2 channel 1, etc.
                            // SC-8850 manual, page 237
                            var channel =
                                MidiUtils.SyxToChannel(a2 & 0x0f) + channelOffset;
                            // For example, 0x1A means A = 11, which corresponds to channel 12 (counting from 1)
                            var ch = synth.MidiChannels[channel];

                            switch (a3) 
                            {
                                default: 
                                    // This is some other GS sysex...
                                    SpessaLog.GSFail(
                                        "Patch Part Parameter", [a3]);
                                    break;

                                case 0x00:
                                case 0x01: 
                                    // Tone map number (cc#32)
                                    ch.ControllerChange(
                                        Midi.CC.BankSelectLSB,
                                        data);
                                    break;

                                case 0x22: 
                                {
                                    // EFX assign
                                    var efx = data == 1;
                                    ch.Set((
                                        ChannelMidiParameter.Type.EfxAssign, 
                                        efx));
                                    synth.InsertionActive |= efx;
                                    SpessaLog.GSInfo(
                                        $"EFX assign for {channel}",
                                        efx ? "EFX" : "BYPASS");
                                    break;
                                }
                            }
                            return;
                        }
                        SpessaLog.GSFail("Patch Parameter", syx);
                        SpessaLog.GSFail("Patch Parameter", syx);
                        return;
                    }
                    // Drum setup
                    if (a1 is 0x41 or 0x51) 
                    {
                        // 51 means BLOCK B (+16 channels)
                        // Testcase: 95043-2.KYC.mid
                        if (synth.SystemParameters.DrumLock) 
                            return;

                        var map = (a2 >> 4) + 1;
                        var drumKey = a3;
                        var param = (byte)(a2 & 0xf);
                        switch (param) 
                        {
                            default: 
                                SpessaLog.GSFail("Drum Setup", [param]);
                                return;

                            case 0x0: 
                            {
                                // Drum map name
                                var patchName = Util.ReadBinaryString(
                                    syx.Slice(7, 12)).ToArray();
                                SpessaLog.GSInfo(
                                    $"Drum Map name for MAP{map}", 
                                    Util.ToString(patchName));
                                synth.CallEvent(Event.Of(
                                    new Event.CbDisplayMessage(patchName)));
                                break;
                            }

                            case 0x1: 
                            {
                                // Here it's relative to 60, not 64 like NRPN. For some reason...

                                var pitch = data - 60;
                                foreach (var ch in synth.MidiChannels) 
                                {
                                    if (ch.MidiParamArray.DrumMap != map)
                                        continue;
                                    // Apply same thing: SC-55 uses 100 cents, SC-88 and above is 50
                                    ref var dp = ref ch.DrumParams[drumKey];
                                    dp = dp with { Pitch = pitch *
                                                           (ch.Patch.BankLSB == 1 ? 100 : 50) };
                                }
                                SpessaLog.GSInfo(
                                    $"Drum Pitch for MAP{map}, key {drumKey}",
                                    pitch);
                                break;
                            }

                            case 0x2: 
                                // Drum Level
                                foreach (var ch in synth.MidiChannels) 
                                {
                                    if (ch.MidiParamArray.DrumMap != map)
                                        continue;
                                    ref var dp = ref ch.DrumParams[drumKey];
                                    dp = dp with { Gain = data / 120f };
                                }
                                SpessaLog.GSInfo(
                                    $"Drum Level for MAP{map}, key {drumKey}",
                                    data);
                                break;

                            case 0x3: 
                                // Drum Assign Group (exclusive class)
                                foreach (var ch in synth.MidiChannels) 
                                {
                                    if (ch.MidiParamArray.DrumMap != map)
                                        continue;
                                    ref var dp = ref ch.DrumParams[drumKey];
                                    dp = dp with { ExclusiveClass = data };
                                }
                                SpessaLog.GSInfo(
                                    $"Drum Assign Group for MAP{map}, key {drumKey}",
                                    data);
                                break;

                            case 0x4: 
                                // Pan
                                foreach (var ch in synth.MidiChannels) 
                                {
                                    if (ch.MidiParamArray.DrumMap != map)
                                        continue;
                                    ref var dp = ref ch.DrumParams[drumKey];
                                    dp = dp with { Pan = data };
                                }
                                SpessaLog.GSInfo(
                                    $"Drum Pan for MAP{map}, key {drumKey}",
                                    data);
                                break;

                            case 0x5: 
                                // Reverb
                                foreach (var ch in synth.MidiChannels) 
                                {
                                    if (ch.MidiParamArray.DrumMap != map)
                                        continue;
                                    ref var dp = ref ch.DrumParams[drumKey];
                                    dp = dp with { ReverbGain = data / 127f };
                                }
                                SpessaLog.GSInfo(
                                    $"Drum Reverb for MAP{map}, key {drumKey}",
                                    data);
                                break;

                            case 0x6: 
                                // Chorus
                                foreach (var ch in synth.MidiChannels) 
                                {
                                    if (ch.MidiParamArray.DrumMap != map)
                                        continue;
                                    ref var dp = ref ch.DrumParams[drumKey];
                                    dp = dp with { ChorusGain = data / 127f };
                                }
                                SpessaLog.GSInfo(
                                    $"Drum Chorus for MAP{map}, key {drumKey}",
                                    data);
                                break;

                            case 0x7: 
                                // Receive Note Off
                                foreach (var ch in synth.MidiChannels) 
                                {
                                    if (ch.MidiParamArray.DrumMap != map)
                                        continue;
                                    ref var dp = ref ch.DrumParams[drumKey];
                                    dp = dp with { RxNoteOff = data == 1 };
                                }
                                SpessaLog.GSInfo(
                                    $"Drum Note Off for MAP{map}, key {drumKey}",
                                    data == 1 ? "ON" : "OFF");
                                break;

                            case 0x8: 
                                // Receive Note On
                                foreach (var ch in synth.MidiChannels) 
                                {
                                    if (ch.MidiParamArray.DrumMap != map)
                                        continue;
                                    ref var dp = ref ch.DrumParams[drumKey];
                                    dp = dp with { RxNoteOn = data == 1 };
                                }
                                SpessaLog.GSInfo(
                                    $"Drum Note On for MAP{map}, key {drumKey}",
                                    data == 1 ? "ON" : "OFF");
                                break;

                            case 0x9: 
                                // Delay
                                foreach (var ch in synth.MidiChannels) 
                                {
                                    if (ch.MidiParamArray.DrumMap != map)
                                        continue;
                                    ref var dp = ref ch.DrumParams[drumKey];
                                    dp = dp with { DelayGain = data / 127f };
                                }
                                SpessaLog.GSInfo(
                                    $"Drum Delay for MAP{map}, key {drumKey}",
                                    data
                                );
                                break;
                        }
                        return;
                    }
                    // User drum set
                    if (a1 == 0x21)
                    {
                        var drumSetNumber = a2 >> 4;
                        var drumSet = synth.SoundBankManager.UserDrumSets[
                            drumSetNumber];
                        var midiNote = a3;
                        var command = (byte)(a2 & 0xf);
                        switch (command)
                        {
                            default:
                                SpessaLog.GSFail("User Drum set", syx);
                                return;
                            
                            // User drum set name
                            case 0: 
                            {
                                var newName = Util.ToString(
                                    Util.ReadBinaryString(
                                        syx.Slice(12, 7)));
                                drumSet.Name = newName;
                                SpessaLog.GSInfo(
                                    $"User Drum Set {drumSetNumber} name", newName);
                                return;
                            }
                            
                            // Source drum set
                            case 0xa: 
                            {
                                drumSet.SetSourceMap(midiNote, data);
                                SpessaLog.GSInfo(
                                    $"User Drum Set {drumSetNumber} source drum set for {midiNote}",
                                    data);
                                return;
                            }
                            
                            // Program number
                            case 0xb: 
                            {
                                drumSet.SetSourceProgram(midiNote, data);
                                SpessaLog.GSInfo(
                                    $"User Drum Set {drumSetNumber} source program for {midiNote}",
                                    data);
                                return;
                            }
                            
                            // Source note number
                            case 0xc: 
                            {
                                drumSet.SetSourceNote(midiNote, data);
                                SpessaLog.GSInfo(
                                    $"User Drum Set {drumSetNumber} source note for {midiNote}",
                                    data);
                                return;
                            }
                        }
                    }
                    // This is some other GS sysex...
                    SpessaLog.GSFail("System Exclusive", syx);
                    return;
                }

                // GS Display
                case 0x45: 
                    // 0x45: GS Display Data
                    // Check for embedded copyright
                    // (Roland SC display sysex) http://www.bandtrax.com.au/sysex.htm
                    // Sound Canvas Display
                    if (syx[4] == 0x10) // Sound Canvas Display
                    {
                        synth.CallEvent(new Event.CbDisplayMessage(
                            syx.ToArray()));
                    }
                    return;

                // Some Roland
                case 0x16: 
                    if (syx[4] == 0x10) 
                    {
                        // This is a roland master volume message
                        synth.Set((
                            GlobalMidiParameter.Type.Volume,
                            syx[7] / 100f));
                        SpessaLog.CoolInfo(
                            $"Roland Master Volume control", syx[7]);
                        return;
                    } 
                    else 
                    {
                        SpessaLog.Unsupported("Roland", syx);
                    }

                    break;
            }
        } 
        else 
        {
            // This is something else...
            SpessaLog.Unsupported("Roland", syx);
            return;
        }
    }
}