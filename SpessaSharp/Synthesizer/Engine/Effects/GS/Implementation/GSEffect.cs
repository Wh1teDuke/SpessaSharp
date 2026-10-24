namespace SpessaSharp.Synthesizer.Engine.Effects.GS.Implementation;

public static class GSEffect
{
    public enum GSReverbType
    {
        Character,//int
        PreLowPass,//int
        Level,//int
        Time,//int
        DelayFeedback,//int
        PreDelayTime,//int
        Macro,//int
    }

    public enum GSChorusType
    {
        PreLowPass,//int
        Level,//int
        Feedback,//int
        Delay,//int
        Rate,//int
        Depth,//int
        SendLevelToReverb,//int
        SendLevelToDelay,//int
        Macro,//int
    }

    public enum GSDelayType
    {
        PreLowPass,//int
        TimeCenter,//int
        TimeRatioLeft,//int
        TimeRatioRight,//int
        LevelCenter,//int
        LevelLeft,//int
        LevelRight,//int
        Level,//int
        Feedback,//int
        SendLevelToReverb,//int
        Macro,//int
    }

    public enum InsertionType
    {
        Type,
        SendLevelToReverb,
        SendLevelToChorus,
        SendLevelToDelay,
    }
}