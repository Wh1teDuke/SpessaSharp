namespace SpessaSharp.MIDI.Utils;

public static class ExtendedParameters
{
    public static class RPN
    {
        public const int PitchWheelRange    = 0x00_00;
        public const int FineTuning         = 0x00_01;
        public const int CoarseTuning       = 0x00_02;
        public const int ModulationDepth    = 0x00_05;
        public const int ResetParameters    = 0x3f_ff;
    }

    public static class NRPN
    {
        public static class MSB
        {
            public const int PartParameter  = 0x01;
            public const int DrumPitch      = 0x18;
            public const int DrumPitchFine  = 0x19;
            public const int DrumLevel      = 0x1a;
            public const int DrumPan        = 0x1c;
            public const int DrumReverb     = 0x1d;
            public const int DrumChorus     = 0x1e;
            public const int DrumDelay      = 0x1f;
            
            public const int awe32          = 0x7f;
            public const int SF2            = 120;
        }
        
        /// <summary>
        /// https://cdn.roland.com/assets/media/pdf/SC-8850_OM.pdf<br/>
        /// http://hummer.stanford.edu/sig/doc/classes/MidiOutput/rpn.html<br/>
        /// These also seem to match XG
        /// </summary>
        public static class LSB
        {
            public const int VibratoRate            = 0x08;
            public const int VibratoDepth           = 0x09;
            public const int VibratoDelay           = 0x0a;

            public const int TVFCutoffFrequency     = 0x20;
            public const int TVFResonance           = 0x21;

            public const int EnvelopeAttackTime     = 0x63;
            public const int EnvelopeDecayTime      = 0x64;
            public const int EnvelopeReleaseTime    = 0x66;
        }
    }
}