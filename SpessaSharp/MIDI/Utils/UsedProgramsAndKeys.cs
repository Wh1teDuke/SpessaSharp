using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Utils;

/// <summary>
/// - Key - the preset.<br/>
/// - Value - A Set:<br/>
///   - Key: The MIDI note number.<br/>
///   - Value: The velocity for this note number.
/// </summary>
/// <param name="data"></param>
public readonly struct PresetsWithKeyCombinations(
    Dictionary<BasicPreset, HashSet<(int Key, int Velocity)>> data)
{
    private readonly Dictionary<
        BasicPreset, HashSet<(int Key, int Velocity)>> _data = data;

    public bool TryGetValue(
        BasicPreset key, 
        [MaybeNullWhen(false)] out HashSet<(int Key, int Velocity)> value) =>
        _data.TryGetValue(key, out value);

    public Enumerator GetEnumerator() => new (this);

    public ref struct Enumerator(PresetsWithKeyCombinations presets)
    {
        private Dictionary<
            BasicPreset, HashSet<(int Key, int Velocity)>>.Enumerator _enum =
            presets._data.GetEnumerator();

        private (BasicPreset, HashSet<(int Key, int Velocity)>)? _current = null;
        
        public (BasicPreset, HashSet<(int Key, int Velocity)>) Current => _current!.Value;

        public bool MoveNext()
        {
            while (_enum.MoveNext())
            {
                var entry = _enum.Current;
                if (entry.Value.Count == 0) continue;
                _current = (entry.Key, entry.Value);
                return true;
            }

            return false;
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        
        foreach (var (preset, combos) in this)
            sb.AppendLine($"{preset}: {combos}");

        return sb.ToString();
    }
}

internal static class UsedProgramsAndKeys
{
    public sealed class Cache
    {
        private readonly List<HashSet<(int Key, int Velocity)>> _keysCache = new(32);
        private bool _inUse;
        
        public readonly Dictionary<
            BasicPreset, HashSet<(int Key, int Velocity)>>
            ProgramsAndKeys = new(32);

        public void Done()
        {
            if (!_inUse)
                throw new InvalidOperationException("Cache not in use");
            _inUse = false;
        }

        public void Init()
        {
            if (_inUse)
                throw new InvalidOperationException(
                    "Cache is in use. Call Done() before Init()");
            
            _inUse = true;
            
            _keysCache.AddRange(ProgramsAndKeys.Values);
            ProgramsAndKeys.Clear();
        }

        public void Clear()
        {
            if (_inUse)
                throw new InvalidOperationException(
                    "Cache is in use. Call Done() before Clear()");
            
            _keysCache.Clear();
            ProgramsAndKeys.Clear();
        }

        public HashSet<(int Key, int Velocity)> NextKeySet()
        {
            HashSet<(int Key, int Velocity)> next;

            if (_keysCache.Count == 0)
                next = new HashSet<(int Key, int Velocity)>(256);
            else
            {
                next = _keysCache[^1];
                _keysCache.RemoveAt(_keysCache.Count - 1);
            }
            
            next.Clear();
            return next;
        }
    }
    
    private record struct InternalChannelType(
        BasicPreset? Preset,
        int BankMSB,
        int BankLSB,
        ParamTracker Param,
        bool IsDrum,
        int KeyShift);
    
    /// <summary>
    /// Gets the used programs and keys for this MIDI file with a given sound bank.
    /// </summary>
    /// <param name="mid"></param>
    /// <param name="getPreset">The preset provider.</param>
    /// <returns>Patch -> (Key-Velocity)</returns>
    public static PresetsWithKeyCombinations Get(
        Midi mid, IPresetGetter getPreset)
    {
        var cache = getPreset is SoundBankManager sbm ? sbm.Cache : new Cache();
        return Get(mid, getPreset, cache);
    }

    /// <summary>
    /// Gets the used programs and keys for this MIDI file with a given sound bank.
    /// </summary>
    /// <param name="mid"></param>
    /// <param name="getPreset">The preset provider.</param>
    /// <param name="cache"></param>
    /// <returns>Patch -> (Key-Velocity)</returns>
    public static PresetsWithKeyCombinations Get(
        Midi mid, IPresetGetter getPreset, Cache cache)
    {
        Debug.WriteLine(
            "Searching for all used programs and keys ...");

        cache.Init();
        
        // Find every used preset and every key:velocity for each.
        // Make sure to care about ports and drums.
        var channelsAmount = Math.Min(
            256, 16 + mid.PortChannelOffsetMap.Max());
        
        // Track channels and systems
        var channels =
            Util.Rent<InternalChannelType>(channelsAmount);
        var system = Midi.System.GS;
        var masterKeyShift = 0;
        
        for (var i = 0; i < channelsAmount; i++)
        {
            var isDrum = i % 16 == Synthesizer.Synthesizer.DEFAULT_PERCUSSION;
            channels[i] = new InternalChannelType(
                Preset: getPreset.GetPreset(
                    new MidiPatch { IsGMGSDrum = isDrum, }, system),
                BankMSB: 0, 
                BankLSB: 0,
                Param: new ParamTracker(i),
                IsDrum: isDrum,
                KeyShift: 0);
        }

        /*
         * Find all programs used and key-velocity combos in them
         * bank:program each has a set of midiNote-velocity
         */
        var usedProgramsAndKeys = cache.ProgramsAndKeys;
        var ports = (Span<int>)stackalloc int[mid.Tracks.Count];
        for (var i = 0; i < mid.Tracks.Count; i++) ports[i] = mid.Tracks[i].Port;
        var ids = (ReadOnlySpan<int>)[
            MidiMessage.ID(MidiMessage.Type.NoteOn),
            MidiMessage.ID(MidiMessage.Type.ControllerChange),
            MidiMessage.ID(MidiMessage.Type.ProgramChange),
            MidiMessage.ID(MidiMessage.Type.SystemExclusive),
        ];

        var offsets = mid.PortChannelOffsetMap;
        var timeline = mid.Timeline;
        foreach (var tl in timeline)
        {
            var trackNum = tl.Tr;
            var ev = tl.Ev;
            var e = mid[tl];
            
            // Do not assign ports to empty tracks
            // Testcase Cueshe - Bakit 1.mid
            if (e.StatusByte.Is(MidiMessage.Type.MidiPort) &&
                mid.Tracks[trackNum].Channels.Count > 0)
            {
                var port = e.Data[0];
                if (port >= offsets.Count || offsets[port] == -1) 
                {
                    Debug.WriteLine(
                        $"[WARN] Invalid port {port
                        } on track {trackNum}. (No offset found in the MIDI map.");
                    port = 0;
                }

                ports[trackNum] = port;
                continue;
            }

            var status = e.StatusByte.Status;
            if (!ids.Contains(e.StatusByte.Status)) continue;

            var channelOffset = 
                Math.Max(0, ports[trackNum] >= mid.PortChannelOffsetMap.Count
                ? 0
                : mid.PortChannelOffsetMap[ports[trackNum]]);

            if (status == MidiMessage.Type.ProgramChange.ID())
            {
                var channel = e.StatusByte.Channel + channelOffset;
                ref var ch = ref channels.AsSpan()[channel];
                
                ch.Preset = getPreset.GetPreset(new MidiPatch
                {
                    BankMSB = ch.BankMSB,
                    BankLSB = ch.BankLSB,
                    Program = e.Data[0],
                    IsGMGSDrum = ch.IsDrum,
                }, system);
            }
            else if (status == MidiMessage.Type.ControllerChange.ID())
            {
                var channel = e.StatusByte.Channel + channelOffset;
                ref var ch = ref channels.AsSpan()[channel];

                var cc = (Midi.CC)e.Data[0];
                var value = e.Data[1];
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (cc)
                {
                    // Registered param tracking
                    case Midi.CC.RegisteredParameterMSB:
                    case Midi.CC.RegisteredParameterLSB:
                    case Midi.CC.NonRegisteredParameterLSB:
                    case Midi.CC.NonRegisteredParameterMSB:
                        ch.Param.ControllerChange(cc, value, trackNum, ev);
                        break;

                    case Midi.CC.DataEntryMSB:
                    case Midi.CC.DataEntryLSB:
                    {
                        var analyzed = ch.Param.ControllerChange(cc, value, trackNum, ev);
                        // RPN#02 Coarse Tune is key-shift according to GM2 section 3.4.3
                        if (analyzed?.AsChannelMidiParameter?.Param is
                            { PType: ChannelMidiParameter.Type.KeyShift, AsInt: var val })
                            // Drum channels ignore key shift
                            // Testcase: th07_19_user_gm.mid
                            ch.KeyShift = ch.IsDrum ? 0 : val;
                        break;
                    }
                    
                    case Midi.CC.ResetAllControllers:
                        ch.Param.Reset();
                        break;

                    case Midi.CC.BankSelect:
                        ch = ch with { BankMSB = value, };
                        break;

                    case Midi.CC.BankSelectLSB: {
                        ch = ch with { BankLSB = value, };
                        break;
                    }
                    default: break;
                }
            }
            else if (status == MidiMessage.Type.NoteOn.ID())
            {
                var channel = e.StatusByte.Channel + channelOffset;
                var ch = channels[channel];
                
                // That's a note off
                if (e.Data[1] == 0) continue;
                
                // If there's no preset, ignore
                if (ch.Preset == null) continue;

                // Add the preset to the used list if it does not exist
                ref var keysForPreset = ref CollectionsMarshal
                    .GetValueRefOrAddDefault(
                        usedProgramsAndKeys, ch.Preset, out var exists);
                if (!exists) keysForPreset = cache.NextKeySet();
                
                // Add the key-velocity pair to the preset
                var midiNote =
                    e.Data[0] + (ch.IsDrum ? 0 : masterKeyShift) + ch.KeyShift;
                keysForPreset!.Add((midiNote, e.Data[1]));
            }
            else if (status == MidiMessage.Type.SystemExclusive.ID())
            {
                // Check for drum sysex
                var syx = MidiUtils.AnalyzeSysEx(e);
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (syx.MType)
                {
                    default: break;

                    case MidiUtils.AnalyzedMessage.Type.GlobalMidiParameter:
                    {
                        var gmp = syx.AsGlobalMidiParameter!.Value;
                        if (gmp.PType == GlobalMidiParameter.Type.KeyShift)
                            masterKeyShift = gmp.AsInt;
                        else if (gmp.PType == GlobalMidiParameter.Type.MidiSystem)
                        {
                            Reset(gmp.AsMidiSystem);
                            SpessaLog.Info($"{gmp.AsMidiSystem} on detected!");
                        }
                        break;
                    }

                    case MidiUtils.AnalyzedMessage.Type.AnalyzedParameter when
                        syx.AsAnalyzedParameter is 
                            { AsChannelMidiParameter: {} cmp }:
                    {
                        if (cmp.Param.PType == ChannelMidiParameter.Type.KeyShift)
                        {
                            ref var chan = ref channels.AsSpan()[cmp.Channel];
                            // Drum channels ignore key shift
                            // Testcase: th07_19_user_gm.mid
                            chan.KeyShift = chan.IsDrum ? 0 : cmp.Param.AsInt;                            
                        }
                        break;
                    }

                    case MidiUtils.AnalyzedMessage.Type.DrumsOn:
                    {
                        var dO = syx.AsDrumsOn!.Value;
                        var sysexChannel = dO.Channel + channelOffset;
                        ref var ch = ref channels.AsSpan()[sysexChannel];
                        ch.IsDrum = dO.IsDrum;
                        break;
                    }
                    case MidiUtils.AnalyzedMessage.Type.ProgramChange:
                    {
                        var pc = syx.AsProgramChange!.Value;
                        var sysexChannel = pc.Channel + channelOffset;
                        ref var ch = ref channels.AsSpan()[sysexChannel];
                        ch.Preset = getPreset.GetPreset(new MidiPatch(
                            BankMSB: ch.BankMSB,
                            BankLSB: ch.BankLSB,
                            Program: pc.Value,
                            IsGMGSDrum: ch.IsDrum), system);
                        break;
                    }
                    case MidiUtils.AnalyzedMessage.Type.AnalyzedParameter when
                        syx.AsAnalyzedParameter is {AsControllerChange: {} cc}:
                    {
                        var sysexChannel = cc.Channel + channelOffset;
                        ref var ch = ref channels.AsSpan()[sysexChannel];
                        
                        if (cc.Controller == Midi.CC.BankSelectLSB)
                            ch.BankLSB = cc.Value;
                        else if (cc.Controller == Midi.CC.BankSelect)
                            ch.BankMSB = cc.Value;
                        break;
                    }
                }
            }
        }

        #if DEBUG
        foreach (var (preset, keysForPreset) in usedProgramsAndKeys)
        {
            if (keysForPreset.Count == 0)
                Debug.WriteLine($"Detected change but no keys for {preset.Name}");
        }
        #endif
        
        Util.Return(channels);

        return new PresetsWithKeyCombinations(usedProgramsAndKeys);

        void Reset(Midi.System sys)
        {
            system = sys;
            masterKeyShift = 0;
            for (var i = 0; i < channelsAmount; i++) 
            {
                ref var ch = ref channels.AsSpan()[i];
                ch.Param.Reset();
                ch = ch with
                {
                    IsDrum = i % 16 == Synthesizer.Synthesizer.DEFAULT_PERCUSSION,
                    BankMSB = BankSelectHacks.GetDefaultBank(sys),
                    BankLSB = 0,
                    KeyShift = 0,
                };
            }   
        }
    }
}