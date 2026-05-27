using System.Diagnostics;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;

namespace SpessaSharp.Synthesizer.Engine.Sysex;

public static class Macro
{
    public enum Reverb
    { Room1, Room2, Room3, Hall1, Hall2, Plate, Delay, PanningDelay }

    public enum Chorus
    {
        Chorus1, Chorus2, Chorus3, Chorus4, 
        FeedbackChorus, Flanger, ShortDelay, ShortDelayFB
    }

    public enum Delay
    {
        Delay1, Delay2, Delay3, Delay4, 
        PanDelay1, PanDelay2, PanDelay3, PanDelay4, 
        DelayToReverb, PanRepeat,
    }
    
    public static void SetReverb(Synthesizer synth, Reverb macro) =>
        SetReverb(synth, (int)macro);
    
    public static void SetReverb(Synthesizer synth, int macro)
    {
        if (synth.SystemParameters.ReverbLock)
            return;

        // SC-8850 manual page 81
        var rev = synth.ReverbProcessor;
        rev.Level = 64;
        rev.PreDelayTime = 0;
        rev.Character = macro;
        rev.Macro = macro;
        switch (macro) 
        {
            /*
             * REVERB MACRO is a macro parameter that allows global setting of reverb parameters.
             * When you select the reverb type with REVERB MACRO, each reverb parameter will be set to their most
             * suitable value.
             *
             * Room1, Room2, Room3
             * These reverbs simulate the reverberation of a room. They provide a well-defined
             * spacious reverberation.
             * Hall1, Hall2
             * These reverbs simulate the reverberation of a concert hall. They provide a deeper
             * reverberation than the Room reverbs.
             * Plate
             * This simulates a plate reverb (a studio device using a metal plate).
             * Delay
             * This is a conventional delay that produces echo effects.
             * Panning Delay
             * This is a special delay in which the delayed sounds move left and right.
             * It is effective when you are listening in stereo.
             */
            case 0: 
                // Room1
                rev.Character = 0;
                rev.PreLowPass = 3;
                rev.Time = 80;
                rev.DelayFeedback = 0;
                rev.PreDelayTime = 0;
                break;

            case 1: 
                // Room2
                rev.PreLowPass = 4;
                rev.Time = 56;
                rev.DelayFeedback = 0;
                break;

            case 2: 
                // Room3
                rev.PreLowPass = 0;
                rev.Time = 72;
                rev.DelayFeedback = 0;
                break;

            case 3: 
                // Hall1
                rev.PreLowPass = 4;
                rev.Time = 72;
                rev.DelayFeedback = 0;
                break;

            case 4: 
                // Hall2
                rev.PreLowPass = 0;
                rev.Time = 64;
                rev.DelayFeedback = 0;
                break;

            case 5: 
                // Plate
                rev.PreLowPass = 0;
                rev.Time = 88;
                rev.DelayFeedback = 0;
                break;

            case 6: 
                // Delay
                rev.PreLowPass = 0;
                rev.Time = 32;
                rev.DelayFeedback = 40;
                break;

            case 7: 
                // Panning delay
                rev.PreLowPass = 0;
                rev.Time = 64;
                rev.DelayFeedback = 32;
                break;

            default: 
                // Check for invalid macros
                // Testcase: 18 - Dichromatic Lotus Butterfly ~ Ancients (ZUN).mid
                Debug.WriteLine($"[WARN] Invalid reverb macro: {macro}");
                return;
        }

        synth.CallEvent(Event.CbEffectChange.OfReverb(
            Effect.FxReverbType.Macro, macro));
    }

    public static void SetChorus(Synthesizer synth, Chorus macro) =>
        SetChorus(synth, (int)macro);
    
    public static void SetChorus(Synthesizer synth, int macro) 
    {
        if (synth.SystemParameters.ChorusLock)
            return;

        // SC-8850 manual page 83
        var chr = synth.ChorusProcessor;
        chr.Level = 64;
        chr.PreLowPass = 0;
        chr.Delay = 127;
        chr.SendLevelToDelay = 0;
        chr.SendLevelToReverb = 0;
        chr.Macro = macro;
        
        switch (macro) 
        {
            /*
             * CHORUS MACRO is a macro parameter that allows global setting of chorus parameters.
             * When you select the chorus type with CHORUS MACRO, each chorus parameter will be set to their
             * most suitable value.
             *
             * Chorus1, Chorus2, Chorus3, Chorus4
             * These are conventional chorus effects that add spaciousness and depth to the
             * sound.
             * Feedback Chorus
             * This is a chorus with a flanger-like effect and a soft sound.
             * Flanger
             * This is an effect sounding somewhat like a jet airplane taking off and landing.
             * Short Delay
             * This is a delay with a short delay time.
             * Short Delay (FB)
             * This is a short delay with many repeats.
             */
            case 0: 
                // Chorus1
                chr.Feedback = 0;
                chr.Delay = 112;
                chr.Rate = 3;
                chr.Depth = 5;
                break;

            case 1: 
                // Chorus2
                chr.Feedback = 5;
                chr.Delay = 80;
                chr.Rate = 9;
                chr.Depth = 19;
                break;

            case 2: 
                // Chorus3
                chr.Feedback = 8;
                chr.Delay = 80;
                chr.Rate = 3;
                chr.Depth = 19;
                break;

            case 3: 
                // Chorus4
                chr.Feedback = 16;
                chr.Delay = 64;
                chr.Rate = 9;
                chr.Depth = 16;
                break;

            case 4: 
                // FbChorus
                chr.Feedback = 64;
                chr.Delay = 127;
                chr.Rate = 2;
                chr.Depth = 24;
                break;

            case 5: 
                // Flanger
                chr.Feedback = 112;
                chr.Delay = 127;
                chr.Rate = 1;
                chr.Depth = 5;
                break;

            case 6: 
                // SDelay
                chr.Feedback = 0;
                chr.Depth = 127;
                chr.Rate = 0;
                chr.Depth = 127;
                break;

            case 7: 
                // SDelayFb
                chr.Feedback = 80;
                chr.Depth = 127;
                chr.Rate = 0;
                chr.Depth = 127;
                break;

            default: 
                // Check for invalid macros
                // Testcase: 18 - Dichromatic Lotus Butterfly ~ Ancients (ZUN).mid
                Debug.WriteLine($"Invalid chorus macro: {macro}");
                return;
        }
        
        synth.CallEvent(Event.CbEffectChange.OfChorus(
            Effect.FxChorusType.Macro, macro));
    }

    public static void SetDelay(Synthesizer synth, Delay macro) =>
        SetDelay(synth, (int)macro);
    
    public static void SetDelay(Synthesizer synth, int macro)
    {
        if (synth.SystemParameters.DelayLock)
            return;

        // SC-8850 manual page 85
        var dly = synth.DelayProcessor;
        dly.Level = 64;
        dly.PreLowPass = 0;
        dly.SendLevelToReverb = 0;
        dly.LevelRight = dly.LevelLeft = 0;
        dly.LevelCenter = 127;
        dly.Macro = macro;

        switch (macro) 
        {
            /*
             * DELAY MACRO is a macro parameter that allows global setting of delay parameters. When you select the delay type with DELAY MACRO, each delay parameter will be set to their most
             * suitable value.
             *
             * Delay1, Delay2, Delay3
             * These are conventional delays. 1, 2 and 3 have progressively longer delay times.
             * Delay4
             * This is a delay with a rather short delay time.
             * Pan Delay1. Pan Delay2. Pan Delay3
             * The delay sound moves between left and right. This is effective when listening in
             * stereo. 1, 2 and 3 have progressively longer delay times.
             * Pan Delay4
             * This is a rather short delay with the delayed sound moving between left and
             * right.
             * It is effective when listening in stereo.
             * Delay To Reverb
             * Reverb is added to the delay sound, which moves between left and right.
             * It is effective when listening in stereo.
             * PanRepeat
             * The delay sound moves between left and right,
             * but the pan positioning is different from the effects listed above.
             * It is effective when listening in stereo.
             */
            case 0: 
                // Delay1
                dly.TimeCenter = 97;
                dly.TimeRatioRight = dly.TimeRatioLeft = 1;
                dly.Feedback = 80;
                break;

            case 1: 
                // Delay2
                dly.TimeCenter = 106;
                dly.TimeRatioRight = dly.TimeRatioLeft = 1;
                dly.Feedback = 80;
                break;

            case 2: 
                // Delay3
                dly.TimeCenter = 115;
                dly.TimeRatioRight = dly.TimeRatioLeft = 1;
                dly.Feedback = 72;
                break;

            case 3: 
                // Delay4
                dly.TimeCenter = 83;
                dly.TimeRatioRight = dly.TimeRatioLeft = 1;
                dly.Feedback = 72;
                break;

            case 4: 
                // PanDelay1
                dly.TimeCenter = 105;
                dly.TimeRatioLeft = 12;
                dly.TimeRatioRight = 24;
                dly.LevelCenter = 0;
                dly.LevelLeft = 125;
                dly.LevelRight = 60;
                dly.Feedback = 74;
                break;

            case 5: 
                // PanDelay2
                dly.TimeCenter = 109;
                dly.TimeRatioLeft = 12;
                dly.TimeRatioRight = 24;
                dly.LevelCenter = 0;
                dly.LevelLeft = 125;
                dly.LevelRight = 60;
                dly.Feedback = 71;
                break;

            case 6: 
                // PanDelay3
                dly.TimeCenter = 115;
                dly.TimeRatioLeft = 12;
                dly.TimeRatioRight = 24;
                dly.LevelCenter = 0;
                dly.LevelLeft = 120;
                dly.LevelRight = 64;
                dly.Feedback = 73;
                break;

            case 7: 
                // PanDelay4
                dly.TimeCenter = 93;
                dly.TimeRatioLeft = 12;
                dly.TimeRatioRight = 24;
                dly.LevelCenter = 0;
                dly.LevelLeft = 120;
                dly.LevelRight = 64;
                dly.Feedback = 72;
                break;

            case 8: 
                // DelayToReverb
                dly.TimeCenter = 109;
                dly.TimeRatioLeft = 12;
                dly.TimeRatioRight = 24;
                dly.LevelCenter = 0;
                dly.LevelLeft = 114;
                dly.LevelRight = 60;
                dly.Feedback = 61;
                dly.SendLevelToReverb = 36;
                break;

            case 9: 
                // PanRepeat
                dly.TimeCenter = 110;
                dly.TimeRatioLeft = 21;
                dly.TimeRatioRight = 32;
                dly.LevelCenter = 97;
                dly.LevelLeft = 127;
                dly.LevelRight = 67;
                dly.Feedback = 40;
                break;

            default: 
                // Check for invalid macros
                // Testcase: 18 - Dichromatic Lotus Butterfly ~ Ancients (ZUN).mid
                Debug.WriteLine($"Invalid delay macro: {macro}");
                return;
        }
        
        synth.CallEvent(Event.CbEffectChange.OfDelay(
            Effect.FxDelayType.Macro, macro));
    }
}