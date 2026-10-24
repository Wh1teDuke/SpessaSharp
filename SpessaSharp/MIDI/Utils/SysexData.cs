using SpessaSharp.Synthesizer.Engine.Channel;

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