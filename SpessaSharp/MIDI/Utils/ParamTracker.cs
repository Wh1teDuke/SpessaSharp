using SpessaSharp.Synthesizer.Engine.Channel;

namespace SpessaSharp.MIDI.Utils;

public struct ParamTracker(int channel)
{
    public record struct ParamController(int V, int Track, int Event);

    private bool _isRegistered = true;

    public ParamController rpnMSB = new(V: DataEntry.DEFAULT_RPN, 0, 0);
    public ParamController rpnLSB = new(V: DataEntry.DEFAULT_RPN, 0, 0);
    public ParamController nrpnMSB = new(V: DataEntry.DEFAULT_NRPN, 0, 0);
    public ParamController nrpnLSB = new(V: DataEntry.DEFAULT_NRPN, 0, 0);
    public ParamController dataMSB = new(V: 0, 0, 0);
    public ParamController dataLSB = new(V: 0, 0, 0);

    public ParamController ParamMSB
    {
        get => _isRegistered ? rpnMSB : nrpnMSB;
        set
        {
            if (_isRegistered) rpnMSB = value;
            else nrpnMSB = value;
        }
    }

    public ParamController ParamLSB
    {
        get => _isRegistered ? rpnLSB : nrpnLSB;
        set
        {
            if (_isRegistered) rpnLSB = value;
            else nrpnLSB = value;
        }
    }

    public void Reset()
    {
        _isRegistered = true;
        rpnLSB = rpnLSB with { V = DataEntry.DEFAULT_RPN };
        rpnMSB = rpnMSB with { V = DataEntry.DEFAULT_RPN };
        nrpnMSB = nrpnMSB with { V = DataEntry.DEFAULT_NRPN };
        nrpnLSB = nrpnLSB with { V = DataEntry.DEFAULT_NRPN };
        dataLSB = dataLSB with { V = 0 };
        dataMSB = dataMSB with { V = 0 };
    }

    public MidiUtils.AnalyzedParameter? ControllerChange(
        Midi.CC cc, int v, int track, int ev)
    {
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (cc) 
        {
            case Midi.CC.RegisteredParameterMSB:
            {
                ResetData();
                _isRegistered = true;
                rpnMSB = new ParamController(v, track, ev);
                break;
            }

            case Midi.CC.RegisteredParameterLSB: 
            {
                ResetData();
                _isRegistered = true;
                rpnLSB = new ParamController(v, track, ev);
                break;
            }

            case Midi.CC.NonRegisteredParameterMSB: 
            {
                ResetData();
                _isRegistered = false;
                nrpnMSB = new ParamController(v, track, ev);
                break;
            }

            case Midi.CC.NonRegisteredParameterLSB:
            {
                ResetData();
                _isRegistered = false;
                nrpnLSB = new ParamController(v, track, ev);
                break;
            }

            case Midi.CC.DataEntryMSB:
            {
                dataMSB = new ParamController(v, track, ev);
                return Analyze();
            }

            case Midi.CC.DataEntryLSB: 
            {
                dataLSB = new ParamController(v, track, ev);
                return Analyze();
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(cc), cc, null);
        }

        return null;
    }
    
    private void ResetData() 
    {
        // We call this in parameter set because
        // This is technically not a MIDI behavior,
        // But some MIDI files only send MSB data:
        // https://github.com/spessasus/spessasynth_core/pull/78#discussion_r3233413622
        dataLSB.V = 0;
        dataMSB.V = 0;
    }
    
    private MidiUtils.AnalyzedParameter Analyze() 
    {
        var v = (dataMSB.V << 7) | dataLSB.V;
        return _isRegistered
            ? MidiUtils.AnalyzeRPN(channel, (rpnMSB.V << 7) | rpnLSB.V, v)
            : MidiUtils.AnalyzeNRPN(channel, (nrpnMSB.V << 7) | nrpnLSB.V, v);
    }
}