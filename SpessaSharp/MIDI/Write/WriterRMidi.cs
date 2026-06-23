using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Write;

public static class WriterRMidi
{
    /// <summary>
    /// </summary>
    /// <param name="BankOffset">The bank offset for RMIDI.</param>
    /// <param name="Metadata">The metadata of the file. Optional.</param>
    /// <param name="CorrectBankOffset">If the MIDI file should internally be corrected to work with the set bank offset.</param>
    /// <param name="SoundBank">The optional sound bank instance used to correct bank offset.</param>
    public readonly record struct Options(
        int BankOffset,
        RMidi.Info? Metadata,
        bool CorrectBankOffset,
        SoundBank.SoundBank? SoundBank)
    {
        public static readonly Options Default = new(
            0, null, false, null);
    }

    private readonly record struct ChannelStatus(
        int Program, 
        bool IsDrum, 
        MidiMessage? LastBank,
        MidiMessage? LastBankLSB,
        bool HasBankSelect);

    /// <summary>
    /// Writes an RMIDI file. Note that this method modifies the MIDI file in-place.
    /// </summary>
    /// <param name="midi">MIDI to modify.</param>
    /// <param name="soundBank">The binary sound bank to embed into the file.</param>
    /// <param name="options">Extra options for writing the file.</param>
    /// <returns>The binary data</returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    internal static ArraySegment<byte> Save(
        Midi midi, ArraySegment<byte> soundBank, Options options)
    {
        const string DEFAULT_COPYRIGHT = "Created using SpessaSharp";
        
        var metadata = options.Metadata ?? new RMidi.Info();
        
        Debug.WriteLine("Writing the RMIDI File...");
        Debug.WriteLine("metadata " + metadata);
        Debug.WriteLine("Initial bank offset " + midi.BankOffset);

        if (options.CorrectBankOffset) 
        {
            if (options.SoundBank == null) 
                throw new ArgumentException(
                    "Sound bank must be provided if correcting bank offset.");
            CorrectBankOffset(midi, options.BankOffset, options.SoundBank);
        }

        var newMid = midi.Write();

        // Apply metadata
        metadata = metadata with
        {
            Name            = metadata.Name ?? midi.GetName(),
            CreationDate    = metadata.CreationDate ?? DateTime.Now,
            Copyright       = metadata.Copyright ?? DEFAULT_COPYRIGHT,
            Software        = metadata.Software ?? "SpessaSharp",
        };

        foreach (var key in Enum.GetValues<RMidi.Info.Key>())
        {
            if (metadata.Get(key) is not { } meta) continue;
            midi.SetRMidiInfo(key, meta);
        }
        
        // Info data for RMID
        var infoContent = new List<ArraySegment<byte>>();
        
        foreach (var (type, data) in midi.RmidiInfo) 
        {
            switch (type) 
            {
                case RMidi.Info.Key.Album:
                    // Note that there are two album chunks: IPRD and IALB
                    // Spessasynth uses IPRD, but writes both
                    WriteInfo(new RIFFChunk.FourCC("IALB"), data);
                    WriteInfo(new RIFFChunk.FourCC("IPRD"), data);
                    break;
                case RMidi.Info.Key.Software: 
                    WriteInfo(new RIFFChunk.FourCC("ISFT"), data);
                    break;
                case RMidi.Info.Key.InfoEncoding: 
                    WriteInfo(new RIFFChunk.FourCC("IENC"), data);
                    break;
                case RMidi.Info.Key.CreationDate: 
                    WriteInfo(new RIFFChunk.FourCC("ICRD"), data);
                    break;
                case RMidi.Info.Key.Picture: 
                    WriteInfo(new RIFFChunk.FourCC("IPIC"), data);
                    break;
                case RMidi.Info.Key.Name: 
                    WriteInfo(new RIFFChunk.FourCC("INAM"), data);
                    break;
                case RMidi.Info.Key.Artist: 
                    WriteInfo(new RIFFChunk.FourCC("IART"), data);
                    break;
                case RMidi.Info.Key.Genre: 
                    WriteInfo(new RIFFChunk.FourCC("IGNR"), data);
                    break;
                case RMidi.Info.Key.Copyright: 
                    WriteInfo(new RIFFChunk.FourCC("ICOP"), data);
                    break;
                case RMidi.Info.Key.Comment: 
                    WriteInfo(new RIFFChunk.FourCC("ICMT"), data);
                    break;
                case RMidi.Info.Key.Engineer: 
                    WriteInfo(new RIFFChunk.FourCC("IENG"), data);
                    break;
                case RMidi.Info.Key.Subject: 
                    WriteInfo(new RIFFChunk.FourCC("ISBJ"), data);
                    break;
                case RMidi.Info.Key.MidiEncoding: 
                    WriteInfo(new RIFFChunk.FourCC("MENC"), data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // Bank offset
        var DBNK = (Span<byte>)stackalloc byte[2];
        var seg = DBNK;
        Util.WriteLittleEndian(ref seg, options.BankOffset, 2);
        infoContent.Add(RIFFChunk.Write(new RIFFChunk.FourCC("DBNK"), DBNK));

        // Combine and write out
        Debug.WriteLine("Finished!");
        
        var infoSpan = CollectionsMarshal.AsSpan(infoContent);

        return RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("RIFF"), [
            Util.GetStringBytes("RMID"),
            RIFFChunk.Write(new RIFFChunk.FourCC("data"), newMid),
            RIFFChunk.WriteParts(new RIFFChunk.FourCC("INFO"), infoSpan, false, true),
            soundBank]);
        
        void WriteInfo(RIFFChunk.FourCC type, ArraySegment<byte> data) =>
            infoContent.Add(RIFFChunk.Write(type, data));
    }

    /// <summary>
    /// Add the offset to the bank.
    /// See https://github.com/spessasus/sf2-rmidi-specification#readme<br/>
    /// Also fix presets that don't exist<br/>
    /// Since midi player6 doesn't seem to default to 0 when non-existent ...
    /// </summary>
    /// <param name="midi"></param>
    /// <param name="bankOffset"></param>
    /// <param name="soundBank"></param>
    private static void CorrectBankOffset(
        Midi midi, int bankOffset, SoundBank.SoundBank soundBank)
    {
        // Start with GM, that way we add GS if there's either GS on or no reset
        // Cast is necessary here as TSC thinks we don't change it ever
        var system = Midi.System.GM;
        
        // The unwanted system messages such as gm on
        var unwantedSystems = new List<(int tNum, MidiMessage e)>();

        // It copies midiPorts everywhere else, but here 0 works so DO NOT CHANGE!
        var ports = new int[midi.Tracks.Count];// 0 by default
        var channelsAmount = 16 + midi.PortChannelOffsetMap.Max();
        var channels = new ChannelStatus[channelsAmount];
        
        for (var i = 0; i < channelsAmount; i++) 
        {
            channels[i] = new ChannelStatus(
                Program:        0,
                // Drums appear on 9 every 16 channels,
                IsDrum:         i % 16 == Synthesizer.Synthesizer.DEFAULT_PERCUSSION,
                LastBank:       null,
                LastBankLSB:    null,
                HasBankSelect:  false);
        }

        foreach (var entry in midi.Iterate())
        {
            ref var e = ref entry.Message;
            var trackNum = entry.TrackNum;

            var portOffset = midi.PortChannelOffsetMap[ports[trackNum]];
            if (e.StatusByte.Is(MidiMessage.Type.MidiPort)) 
            {
                ports[trackNum] = e.Data[0];
                continue;
            }

            var status = e.StatusByte.Status;
            if (!Is(status, MidiMessage.Type.ControllerChange) &&
                !Is(status, MidiMessage.Type.ProgramChange) &&
                !Is(status, MidiMessage.Type.SystemExclusive)) continue;
            
            if (Is(status, MidiMessage.Type.SystemExclusive))
            {
                var syx = MidiUtils.AnalyzeSysEx(e);
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (syx.MType)
                {
                    default: goto Continue;
                        
                    // Check for drum sysex
                    case MidiUtils.AnalyzedMessage.Type.DrumsOn:
                    {
                        var dO = syx.AsDrumsOn!.Value;
                        var sysexChannel = dO.Channel + portOffset;
                        // Ensure check as syx.channel may be above 15
                        if (sysexChannel < 0 ||
                            sysexChannel >= channels.Length)
                            break;
                        ref var chan = ref channels[sysexChannel];
                        chan = chan with { IsDrum = dO.IsDrum };
                        goto Continue;
                    }

                    case MidiUtils.AnalyzedMessage.Type.GlobalMidiParameter:
                    {
                        var gmp = syx.AsGlobalMidiParameter!.Value;
                        if (gmp.PType == GlobalMidiParameter.Type.MidiSystem)
                        {
                            system = gmp.AsMidiSystem;
                            if (system == Midi.System.GM)
                            {
                                // We do not want gm1
                                unwantedSystems.Add((tNum: trackNum, e: e));
                            }
                        }

                        break;
                    }

                    case MidiUtils.AnalyzedMessage.Type.AnalyzedParameter
                        when syx.AsAnalyzedParameter is
                            { AsControllerChange: {} cc }:
                    {
                        // Replace the system exclusive with a regular controller change
                        // Channel number may be above 15
                        if (cc.Channel >= 16) goto Continue;
                        
                        e = MidiMessage.ControllerChange(
                            e.Ticks, cc.Channel, cc.Controller, cc.Value);
                        SpessaLog.Info("Replaced a system exclusive with controller change!");

                        break; // Do not return, keep parsing
                    }

                    case MidiUtils.AnalyzedMessage.Type.ProgramChange:
                    {
                        // Replace the system exclusive with a regular program
                        var pc = syx.AsProgramChange!.Value;
                        // Channel number may be above 15
                        if (pc.Channel >= 16) goto Continue;
                        
                        e = MidiMessage.ProgramChange(
                            e.Ticks, pc.Channel, pc.Value );
                        SpessaLog.Info("Replaced a system exclusive with program change!");

                        break; // Do not return, keep parsing
                    }
                }
            }
            
            // Program change
            var chNum = e.StatusByte.Channel + portOffset;
            ref var ch = ref channels[chNum];
            if (Is(status, MidiMessage.Type.ProgramChange)) 
            {
                var sentProgram = e.Data[0];
                var patch = new MidiPatch(
                    Program: sentProgram,
                    BankLSB: TryGet(ch.LastBankLSB, 0),
                    // Make sure to take bank offset into account
                    BankMSB: BankSelectHacks.SubtractBankOffset(
                        TryGet(ch.LastBank, 0),
                        midi.BankOffset,
                        system == Midi.System.XG),
                    IsGMGSDrum: ch.IsDrum
                );

                var targetPreset = soundBank.GetPreset(patch, system);
                Debug.Write(
                    $"Input patch: {patch.ToMidiString()
                    }. Channel {chNum}. Changing patch to {targetPreset}.");

                // Set the program number
                var eData = e.Data;
                eData[0] = (byte)targetPreset.Program;

                if (targetPreset.IsGMGSDrum && BankSelectHacks.IsSystemXG(system)) 
                {
                    // GM/GS drums returned, leave as is
                    // (drums are already set since we got GMGS, just the sound bank doesn't have any XG.)
                    continue;
                }

                if (ch.LastBank == null)
                    continue;

                var lastBankData = ch.LastBank.Value.Data;
                lastBankData[1] = (byte)BankSelectHacks.AddBankOffset(
                    targetPreset.BankMSB,
                    bankOffset,
                    system == Midi.System.XG);

                if (ch.LastBankLSB == null)
                    continue;

                var lastBankLSBData = ch.LastBankLSB.Value.Data;
                lastBankLSBData[1] = (byte)targetPreset.BankLSB;
                continue;

                int TryGet(MidiMessage? e, int def) => 
                    e is not { } ev || ev.Data.Count >= 1 ? def : ev.Data[1];
            }

            // Controller change
            // We only care about bank-selects
            var isLSB = e.Data[0] == (int)Midi.CC.BankSelectLSB;
            if (e.Data[0] != (int)Midi.CC.BankSelect && !isLSB)
                continue;
            
            // Bank select
            ch = ch with { HasBankSelect = true };
            // Interpret
            if (isLSB) ch = ch with { LastBankLSB = e };
            else ch = ch with { LastBank = e };

            Continue:;
            continue;

            bool Is(int status, MidiMessage.Type type) => status == ID(type);
        }
        
        // Add missing bank selects
        // Add all bank selects that are missing for this track
        for (var chNum = 0; chNum < channelsAmount; chNum++)
        {
            var has = channels[chNum];
            if (has.HasBankSelect) continue;

            // Find the first program change (for the given channel)
            var midiChannel = chNum % 16;
            var status = ID(MidiMessage.Type.ProgramChange) | midiChannel;

            // Find track with this channel being used
            var portOffset = (chNum / 16) * 16;
            var port = midi.PortChannelOffsetMap.IndexOf(portOffset);
            var track = midi.Tracks.Find(t => 
                t.Port == port &&
                t.Channels.Get(midiChannel));

            if (track == null)
                // This channel is not used at all
                continue;

            var indexToAdd = track.EventList.FindIndex(
                e => e.StatusByte.Byte == status);

            if (indexToAdd == -1) 
            {
                // No program change...
                // Add programs if they are missing from the track
                // (need them to activate bank 1 for the embedded soundfont)
                var programIndex = track.EventList.FindIndex(
                    e =>
                        e.StatusByte.Byte is > 0x80 and < 0xf0 &&
                        (e.StatusByte.Byte & 0xf) == midiChannel);

                if (programIndex == -1)
                    // No voices??? skip
                    continue;

                var programTicks = track.Events[programIndex].Ticks;
                var targetProgram = (byte)soundBank.GetPreset(
                    new MidiPatch(), system).Program;

                track.Add(
                    MidiMessage.ProgramChange(
                        programTicks, midiChannel, targetProgram),
                    programIndex);

                indexToAdd = programIndex;
            }

            SpessaLog.Info($"Adding bank select for {chNum}");
            
            var ticks = track.Events[indexToAdd].Ticks;
            var targetPreset = soundBank.GetPreset(
                new MidiPatch(
                    BankLSB: 0,
                    BankMSB: 0,
                    Program: has.Program,
                    IsGMGSDrum: has.IsDrum),
                system);

            var targetBank = (byte)BankSelectHacks.AddBankOffset(
                targetPreset.BankMSB, 
                bankOffset, 
                system == Midi.System.XG);
            
            track.Add(
                MidiMessage.ControllerChange(
                    ticks, midiChannel, Midi.CC.BankSelect, targetBank),
                indexToAdd);
        }
        
        // Make sure to put gs if gm
        if (system == Midi.System.GM && 
            !BankSelectHacks.IsSystemXG(system))
        {
            foreach (var m in unwantedSystems) 
            {
                var track = midi.Tracks[m.tNum];
                track.DeleteEvent(track.Events.IndexOf(m.e));
            }

            // First event is track name for detection, don't break that
            var index = 0;
            if (midi.Tracks[0].Events[0].StatusByte.Is(
                    MidiMessage.Type.TrackName))
                index++;

            midi.Tracks[0].Add(MidiUtils.Reset(0, Midi.System.GS), index);
        }
        
        midi.Flush();
    }

    private static int ID(MidiMessage.Type t) => MidiMessage.ID(t);
}