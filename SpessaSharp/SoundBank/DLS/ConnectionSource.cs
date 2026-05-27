using SpessaSharp.MIDI;

namespace SpessaSharp.SoundBank.DLS;

public readonly record struct ConnectionSource(
    ConnectionSource.DLSSource Source,
    ModulatorCurve.Type Transform,
    bool Bipolar,
    bool Invert)
{
    public static readonly ConnectionSource Default = new(
        DLSSource.None,
        // Source curve type maps to a soundfont curve type in section 2.10, table 9
        ModulatorCurve.Type.Linear,
        false,
        false);
    
    public readonly record struct DLSSource(int Value)
    {
        public static readonly DLSSource None = new(0x0);
        public static readonly DLSSource ModLFO = new(0x1);
        public static readonly DLSSource Velocity = new(0x2);
        public static readonly DLSSource KeyNum = new(0x3);
        public static readonly DLSSource VolEnv = new(0x4);
        public static readonly DLSSource ModEnv = new(0x5);
        public static readonly DLSSource PitchWheel = new(0x6);
        public static readonly DLSSource PolyPressure = new(0x7);
        public static readonly DLSSource ChannelPresure = new(0x8);
        public static readonly DLSSource VibratoLFO = new(0x9);

        public static readonly DLSSource ModulationWheel = new(0x81);
        public static readonly DLSSource Volume = new(0x87);
        public static readonly DLSSource Pan = new(0x8a);
        public static readonly DLSSource Expression = new(0x8b);
        
        // Note: these are flipped unintentionally in DLS2 table 9. Argh!
        public static readonly DLSSource Chorus = new(0xdd);
        public static readonly DLSSource Reverb = new(0xdb);

        public static readonly DLSSource PitchWheelRange = new(0x1_00);
        public static readonly DLSSource FineTune = new(0x1_01);
        public static readonly DLSSource CoarseTune = new(0x1_02);
    }

    public readonly record struct DLSDestination(int Value)
    {
        /// <summary>No destination</summary>
        public static readonly DLSDestination None = new(0x0);              
        /// <summary>Linear gain</summary>
        public static readonly DLSDestination Gain = new(0x1);              
        public static readonly DLSDestination Reserved = new(0x2);          
        /// <summary>Pitch in cents</summary>
        public static readonly DLSDestination Pitch = new(0x3);             
        /// <summary>Pan 10ths of a percent</summary>
        public static readonly DLSDestination Pan = new(0x4);              
        /// <summary>MIDI key number</summary>
        public static readonly DLSDestination KeyNum = new(0x5);           

        // Nuh uh; the channel controllers are not supported!
        /// <summary>Chorus send level 10ths of a percent</summary>
        public static readonly DLSDestination ChorusSend = new(0x80);       
        /// <summary> Reverb send level 10ths of a percent </summary>
        public static readonly DLSDestination ReverbSend = new(0x81);       
 
        /// <summary> Modulation LFO frequency </summary>
        public static readonly DLSDestination ModLFOFreq = new(0x1_04); 
        /// <summary>Modulation LFO delay</summary>
        public static readonly DLSDestination ModLFODelay = new(0x1_05);

        /// <summary>Vibrato LFO frequency </summary>
        public static readonly DLSDestination VibLFOFreq = new(0x1_14);     
        /// <summary> Vibrato LFO delay </summary>
        public static readonly DLSDestination VibLFODelay = new(0x1_15);    
 
        /// <summary> Volume envelope attack </summary>
        public static readonly DLSDestination VolEnvAttack = new(0x2_06);    
        /// <summary> Volume envelope decay </summary>
        public static readonly DLSDestination VolEnvDecay = new(0x2_07);    
        
        public static readonly DLSDestination ReservedEG1 = new(0x2_08);    
        /// <summary> Volume envelope release </summary>
        public static readonly DLSDestination VolEnvRelease = new(0x2_09); 
        /// <summary> Volume envelope sustain </summary>
        public static readonly DLSDestination VolEnvSustain = new(0x2_0a); 
        /// <summary> Volume envelope delay </summary>
        public static readonly DLSDestination VolEnvDelay = new(0x2_0b);   
        /// <summary> Volume envelope hold </summary>
        public static readonly DLSDestination VolEnvHold = new(0x2_0c);    
 
        /// <summary> Modulation envelope attack </summary>
        public static readonly DLSDestination ModEnvAttack = new(0x3_0a);   
        /// <summary> Modulation envelope decay </summary>
        public static readonly DLSDestination ModEnvDecay = new(0x3_0b);    
        public static readonly DLSDestination ReservedEG2 = new(0x3_0c);    
        /// <summary> Modulation envelope release </summary>
        public static readonly DLSDestination ModEnvRelease = new(0x3_0d);  
        /// <summary> Modulation envelope sustain </summary>
        public static readonly DLSDestination ModEnvSustain = new(0x3_0e);  
        /// <summary> Modulation envelope delay </summary>
        public static readonly DLSDestination ModEnvDelay = new(0x3_0f);    
        /// <summary> Modulation envelope hold </summary>
        public static readonly DLSDestination ModEnvHold = new(0x3_10);     

        /// <summary> Low pass filter cutoff frequency </summary>
        public static readonly DLSDestination FilterCutOff = new(0x5_00);   
        /// <summary> Low pass filter resonance </summary>
        public static readonly DLSDestination FilterQ = new(0x5_01);        
        
        public bool IsAny(params ReadOnlySpan<DLSDestination> dests)
        {
            foreach (ref readonly var d in dests)
                if (d == this) return true;
            return false;
        }
    }

    public static ConnectionSource? FromSFSource(Modulator.Source source)
    {
        DLSSource? sourceEnum = null;
        
        if (source.IsCC)
        {
            // DLS only supports a specific set of controllers
            sourceEnum = source.SIndex.AsMidiController switch
            {
                Midi.CC.ModulationWheel => DLSSource.ModulationWheel,
                Midi.CC.MainVolume => DLSSource.Volume,
                Midi.CC.Pan => DLSSource.Pan,
                Midi.CC.Expression => DLSSource.Expression,
                Midi.CC.ChorusDepth => DLSSource.Chorus,
                Midi.CC.ReverbDepth => DLSSource.Reverb,
                _ => null,
            };
        }
        else
        {
            sourceEnum = source.SIndex.AsControllerSource switch
            {
                Modulator.Source.ControllerSource.NoController => DLSSource.None,
                Modulator.Source.ControllerSource.NoteOnKeyNum => DLSSource.KeyNum,
                Modulator.Source.ControllerSource.NoteOnVelocity => DLSSource.Velocity,
                Modulator.Source.ControllerSource.PitchWheel => DLSSource.PitchWheel,
                Modulator.Source.ControllerSource.PitchWheelRange => DLSSource.PitchWheelRange,
                Modulator.Source.ControllerSource.PolyPressure => DLSSource.PolyPressure,
                Modulator.Source.ControllerSource.ChannelPressure => DLSSource.ChannelPresure,
                _ => null,
            };
        }

        // Unable to convert into DLS
        return sourceEnum is not { } sEnum
            ? null
            : new ConnectionSource(
                sEnum, source.CurveType, source.IsBipolar, source.IsNegative);
    }

    public override string ToString() =>
        $"{Source} {Transform} {(Bipolar ? "bipolar" : "unipolar")} {
            (Invert ? "Inverted" : "Positive")}";

    public int TransformFlag =>
        ((int)Transform) |
        ((Bipolar   ? 1 : 0) << 4) |
        ((Invert    ? 1 : 0) << 5);

    public Modulator.Source? ToSFSource()
    {
        byte? sourceEnum = Source switch
        {
            var s when s == DLSSource.KeyNum => ID(Modulator.Source.ControllerSource.NoteOnKeyNum),
            var s when s == DLSSource.None => ID(Modulator.Source.ControllerSource.NoController),
            var s when s == DLSSource.ModulationWheel => (byte)Midi.CC.ModulationWheel,
            var s when s == DLSSource.Pan => (byte)Midi.CC.Pan,
            var s when s == DLSSource.Reverb => (byte)Midi.CC.ReverbDepth,
            var s when s == DLSSource.Chorus => (byte)Midi.CC.ChorusDepth,
            var s when s == DLSSource.Expression => (byte)Midi.CC.Expression,
            var s when s == DLSSource.Volume => (byte)Midi.CC.MainVolume,
            var s when s == DLSSource.Velocity => ID(Modulator.Source.ControllerSource.NoteOnVelocity),
            var s when s == DLSSource.PolyPressure => ID(Modulator.Source.ControllerSource.PolyPressure),
            var s when s == DLSSource.ChannelPresure => ID(Modulator.Source.ControllerSource.ChannelPressure),
            var s when s == DLSSource.PitchWheel => ID(Modulator.Source.ControllerSource.PitchWheel),
            var s when s == DLSSource.PitchWheelRange => ID(Modulator.Source.ControllerSource.PitchWheelRange),
            
            /*DLSSource.ModLFO or
            DLSSource.VibratoLFO or
            DLSSource.CoarseTune or
            DLSSource.FineTune or
            DLSSource.ModEnv or*/
            _ => null,// Cannot be this in sf2
        };

        if (sourceEnum == null) return null;
        
        var isCC =
            Source == DLSSource.ModulationWheel ||
            Source == DLSSource.Pan             ||
            Source == DLSSource.Reverb          ||
            Source == DLSSource.Chorus          ||
            Source == DLSSource.Expression      ||
            Source == DLSSource.Volume;

        return new Modulator.Source(
            Bipolar,
            Invert,
            Modulator.Source.Index.Of(isCC, sourceEnum.Value),
            isCC,
            Transform);

        byte ID(Modulator.Source.ControllerSource e) => (byte)Modulator.Source.ID(e);
    }
}