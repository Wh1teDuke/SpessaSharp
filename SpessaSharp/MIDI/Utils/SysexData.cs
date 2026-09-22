using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Effects;

namespace SpessaSharp.MIDI.Utils;

internal static class SysexData
{
    private static readonly Dictionary<
        DrumParameter.Type, int> _GSDrumParamMap = new() 
        {
            { DrumParameter.Type.PitchCoarse, 1 },
            { DrumParameter.Type.Level, 2 },
            { DrumParameter.Type.AssignGroup, 3 },
            { DrumParameter.Type.Pan, 4 },
            { DrumParameter.Type.ReverbSend, 5 },
            { DrumParameter.Type.ChorusSend, 6 },
            { DrumParameter.Type.RxNoteOff, 7 },
            { DrumParameter.Type.RxNoteOn, 8 },
            { DrumParameter.Type.VariationSend, 9 },
            // Not a thing in GS
            { DrumParameter.Type.PitchFine, 127 },
        };
    
    private static readonly Dictionary<
        DrumParameter.Type, int> _XGDrumParamMap = new() 
    {
        { DrumParameter.Type.PitchCoarse, 0 },
        { DrumParameter.Type.PitchFine, 1 },
        { DrumParameter.Type.Level, 2 },
        { DrumParameter.Type.AssignGroup, 3 },
        { DrumParameter.Type.Pan, 4 },
        { DrumParameter.Type.ReverbSend, 5 },
        { DrumParameter.Type.ChorusSend, 6 },
        { DrumParameter.Type.VariationSend, 7 },
        { DrumParameter.Type.RxNoteOff, 9 },
        { DrumParameter.Type.RxNoteOn, 0xa },
    };

    public static readonly Effect.GSReverbParameter GSReverbAddresMap = new()
    {
        Character = 0x31,
        PreLowPass = 0x32,
        Level = 0x33,
        Time = 0x34,
        DelayFeedback = 0x35,
        PreDelayTime = 0x37,
    };

    public static readonly Effect.GSChorusParameter GSChorusAddressMap = new()
    {
        PreLowPass = 0x39,
        Level = 0x3a,
        Feedback = 0x3b,
        Delay = 0x3c,
        Rate = 0x3d,
        Depth = 0x3e,
        SendLevelToReverb = 0x3f,
        SendLevelToDelay = 0x40,
    };

    public static readonly Effect.GSDelayParameter GSDelayAddressMap = new()
    {
        PreLowPass = 0x51,
        TimeCenter = 0x52,
        TimeRatioLeft = 0x53,
        TimeRatioRight = 0x54,
        LevelCenter = 0x55,
        LevelLeft = 0x56,
        LevelRight = 0x57,
        Level = 0x58,
        Feedback = 0x59,
        SendLevelToReverb = 0x5a,
    };

    public static int GsuSerDrumParamMap(
        UserDrumSetParameter.Entry parameter) =>
        parameter.Type switch
        {
            UserDrumSetParameter.Type.DrumParameters => 
                parameter.AsDrumParameter.Type switch
                {
                    DrumParameter.Type.PitchCoarse => 1,
                    // Should never be used
                    DrumParameter.Type.PitchFine => 127,
                    DrumParameter.Type.Level => 2,
                    DrumParameter.Type.AssignGroup => 3,
                    DrumParameter.Type.Pan => 4,
                    DrumParameter.Type.ReverbSend => 5,
                    DrumParameter.Type.ChorusSend => 6,
                    DrumParameter.Type.RxNoteOff => 7,
                    DrumParameter.Type.RxNoteOn => 8,
                    DrumParameter.Type.VariationSend => 9,
                    _ => throw new Exception(
                        $"Invalid parameter {parameter.Type}")
                },
            //
            UserDrumSetParameter.Type.SourceDrumSet => 0xa,
            UserDrumSetParameter.Type.Program => 0xb,
            UserDrumSetParameter.Type.SourceNoteNumber => 0xc,
            _ => throw new Exception(
                $"Invalid parameter {parameter.Type}")
        };

    public static int? GSDrumParamMap(DrumParameter.Type type) => 
        _GSDrumParamMap.TryGetValue(type, out var value) ? value : null;
    public static int? XGDrumParamMap(DrumParameter.Type type) =>
        _XGDrumParamMap.TryGetValue(type, out var value) ? value : null;

    /// <summary> In GS, 0 is melodic and 1 is the first drum map. </summary>
    public const int DEFAULT_GS_DRUM_MAP = 1;
    /// <summary> In XG, 0 is melodic, 1 is a non-editable drum and 2 is the first drum map. </summary>
    public const int DEFAULT_XG_DRUM_MAP = 2;
    /// <summary>
    ///  The drum map number meaning melodic (not a drum channel).
    /// Shared by GS and XG.
    /// </summary>
    public const int MELODIC_MAP = 0;
}