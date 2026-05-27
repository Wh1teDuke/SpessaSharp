using System.Collections.Frozen;
using System.Runtime.InteropServices;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank;

public readonly struct Generator
{
    public const int ByteSize = 4;
    
    /// <summary> All SoundFont2 Generator enumerations. </summary>
    public enum Type: byte
    {
        /// <summary>Sample control - moves sample start point</summary>
        StartAddrsOffset,            
        /// <summary>Sample control - moves sample end point</summary>
        EndAddrsOffset,      
        /// <summary>Loop control - moves loop start point</summary>
        StartLoopAddrsOffset,
        /// <summary>Loop control - moves loop end point</summary>
        EndLoopAddrsOffset,
        /// <summary>Sample control - moves sample start point in, increments</summary>
        StartAddrsCoarseOffset, 
        /// <summary> Pitch modulation - modulation lfo pitch modulation in cents </summary>
        ModLFOToPitch,               
        /// <summary> Pitch modulation - vibrato lfo pitch modulation in cents </summary>
        VibLFOToPitch,               
        /// <summary> Pitch modulation - modulation envelope pitch modulation in cents </summary>
        ModEnvToPitch,               
        /// <summary>Filter - lowpass filter cutoff in cents</summary>
        InitialFilterFc,             
        /// <summary> Filter - lowpass filter resonance </summary>
        InitialFilterQ,              
        /// <summary> Filter modulation - modulation lfo lowpass filter cutoff in cents </summary>
        ModLFOToFilterFc,            
        /// <summary>Filter modulation - modulation envelope lowpass filter cutoff in cents</summary>
        ModEnvToFilterFc,            
        /// <summary> Sample control - move sample end point in, increments </summary>
        EndAddrsCoarseOffset,       
        /// <summary>Modulation lfo - volume (tremolo), where  = dB </summary>
        ModLFOToVolume,             
        Unused1,                    
        /// <summary>Effect send - how much is sent to chorus -</summary>
        ChorusEffectsSend,           
        /// <summary> Effect send - how much is sent to reverb -</summary>
        ReverbEffectsSend,           
        /// <summary> Panning - where - = left,  = center,  = right </summary>
        Pan,                         
        Unused2,                     
        Unused3,                     
        Unused4,                     
        /// <summary> Mod lfo - delay for mod lfo to start from zero </summary>
        DelayModLFO,                 
        /// <summary> Mod lfo - frequency of mod lfo,  = . Hz, units f => log(f/.) </summary>
        FreqModLFO,                  
        /// <summary> Vib lfo - delay for vibrato lfo to start from zero </summary>
        DelayVibLFO,                 
        /// <summary> Vib lfo - frequency of vibrato lfo,  = .Hz, unit f => log(f/.) </summary>
        FreqVibLFO,                  
        /// <summary> Mod env -  =  s decay till mod env starts </summary>
        DelayModEnv,                 
        /// <summary> Mod env - attack of mod env </summary>
        AttackModEnv,                
        /// <summary> Mod env - hold of mod env </summary>
        HoldModEnv,                  
        /// <summary> Mod env - decay of mod env </summary>
        DecayModEnv,                 
        /// <summary> Mod env - sustain of mod env </summary>
        SustainModEnv,               
        /// <summary> Mod env - release of mod env </summary>
        ReleaseModEnv,               
        /// <summary> Mod env - also modulating mod envelope hold with key number </summary>
        KeyNumToModEnvHold,          
        /// <summary> Mod env - also modulating mod envelope decay with key number </summary>
        KeyNumToModEnvDecay,         
        /// <summary> Vol env - delay of envelope from zero (weird scale) </summary>
        DelayVolEnv,                 
        /// <summary> Vol env - attack of envelope </summary>
        AttackVolEnv,                
        /// <summary> Vol env - hold of envelope </summary>
        HoldVolEnv,                  
        /// <summary> Vol env - decay of envelope </summary>
        DecayVolEnv,                 
        /// <summary> Vol env - sustain of envelope </summary>
        SustainVolEnv,               
        /// <summary> Vol env - release of envelope </summary>
        ReleaseVolEnv,               
        /// <summary> Vol env - key number to volume envelope hold </summary>
        KeyNumToVolEnvHold,          
        /// <summary> Vol env - key number to volume envelope decay </summary>
        KeyNumToVolEnvDecay,         
        /// <summary> Zone - instrument index to use for preset zone </summary>
        Instrument,                  
        Reserved1,                   
        /// <summary> Zone - key range for which preset / instrument zone is active </summary>
        KeyRange,                    
        /// <summary>Zone - velocity range for which preset / instrument zone is active  </summary>
        VelRange,                    
        /// <summary>Sample control - moves sample loop start point in, increments  </summary>
        StartLoopAddrsCoarseOffset,  
        /// <summary>Zone - instrument only always use this midi number (ignore what's pressed) </summary>
        KeyNum,                      
        /// <summary>Zone - instrument only always use this velocity (ignore what's pressed) </summary>
        Velocity,                    
        /// <summary> Zone - allows turning down the volume,  = -dB </summary>
        InitialAttenuation,          
        Reserved2,                   
        /// <summary> Sample control - moves sample loop end point in, increments </summary>
        EndLoopAddrsCoarseOffset,    
        /// <summary>Tune - pitch offset in semitones </summary>
        CoarseTune,                  
        /// <summary>Tune - pitch offset in cents </summary>
        FineTune,                    
        /// <summary>Sample - instrument zone only which sample to use </summary>
        SampleID,                    
        /// <summary>Sample -  = no loop,  = loop,  = start on release,  = loop and play till the end in release phase </summary>
        SampleModes,                 
        Reserved3,                   
        /// <summary>Sample - the degree to which MIDI key number influences pitch,  = default</summary>
        ScaleTuning,                
        /// <summary>Sample - = cut = choke group </summary>
        ExclusiveClass,             
        /// <summary>Sample - can override the sample's original pitch</summary>
        OverridingRootKey,           
        Unused5,                     
        /// <summary>End marker</summary>
        EndOper,                     

        // Additional generators that are used in system exclusives and will not be saved (controller matrix)
        /// <summary>[-1000;1000] -> 1/10%</summary>
        Amplitude,
        /// <summary>[-1000;1000] -> Hz/100</summary>
        VibLFORate,                  
        /// <summary>[0;1000] -> 1/10%</summary>
        VibLFOAmplitudeDepth,        
        /// <summary>Like modLfoToFilterFc</summary>
        VibLFOToFilterFc,            
        /// <summary>[-1000;1000] -> Hz/100</summary>
        ModLFORate,                 
        /// <summary> [0;1000] -> 1/10%</summary>
        ModLFOAmplitudeDepth,        
        
        /// <summary> Invalid generator </summary>
        Invalid,
    }

    public static readonly Type[] List = Enum.GetValues<Type>();
    public static readonly int Amount = List.Length;
    public static readonly short Max = (short)List[^1];

    // Min: minimum value, max: maximum value, def: default value, nrpn: nrpn scale
    public static readonly FrozenDictionary<
            Type, (
            short Min,  // Minimum value for this generator type. 
            int Max,    // Maximum allowed value for this generator type.
            short Def,  // Default value for this generator type.
            int NRPN    // SoundFont2 NRPN scale factor for this generator type.
        )> Limits =
        new Dictionary<Type,                    (short Min, int Max, short Def, int NRPN)>
        {
            // Non-value generators
            {Type.Invalid,                      (0,         0,      0,          0)},
            {Type.EndOper,                      (0,         0,      0,          0)},
            {Type.Instrument,                   (0,         0,      0,          0)},
            {Type.SampleID,                     (0,         0,      0,          0)},
            {Type.KeyRange,                     (0,         0,      0,          0)},
            {Type.VelRange,                     (0,         0,      0,          0)},
            
            // Offsets
            {Type.StartAddrsOffset,             (0,         32_767, 0,          1)},
            {Type.EndAddrsOffset,               (-32_768,   32_767, 0,          1)},
            {Type.StartLoopAddrsOffset,         (-32_768,   32_767, 0,          1)},
            {Type.EndLoopAddrsOffset,           (-32_768,   32_767, 0,          1)},
            {Type.StartAddrsCoarseOffset,       (0,         32_767, 0,          1)},
            
            // Pitch influence
            {Type.ModLFOToPitch,                (-12_000,   12_000, 0,          2)},
            {Type.VibLFOToPitch,                (-12_000,   12_000, 0,          2)},
            {Type.ModEnvToPitch,                (-12_000,   12_000, 0,          2)},
            
            // Lowpass
            {Type.InitialFilterFc,              (1_500,     13_500, 13_500,     2)},
            {Type.InitialFilterQ,               (0,         960,    0,          1)},
            {Type.ModLFOToFilterFc,             (-12_000,   12_000, 0,          2)},
            {Type.ModEnvToFilterFc,             (-12_000,   12_000, 0,          2)},

            {Type.EndAddrsCoarseOffset,         (-32_768,   32_767, 0,          1)},
            
            // Volume modulation
            {Type.ModLFOToVolume,               (-960,      960,    0,          1)},
            
            // Effects / pan
            {Type.ChorusEffectsSend,            (0,         1_000,  0,          1)},
            {Type.ReverbEffectsSend,            (0,         1_000,  0,          1)},
            {Type.Pan,                          (-500,      500,    0,          1)},
            
            // LFO
            {Type.DelayModLFO,                  (-12_000,   5_000,  -12_000,    2)},
            {Type.FreqModLFO,                   (-16_000,   4_500,  0,          4)},
            {Type.DelayVibLFO,                  (-12_000,   5_000,  -12_000,    2)},
            {Type.FreqVibLFO,                   (-16_000,   4_500,  0,          4)},
            
            // Mod envelope
            {Type.DelayModEnv,                  (-32_768,   5_000,  -32_768,    2)}, // -32768 = instant, this is done to prevent click for lowpass
            {Type.AttackModEnv,                 (-32_768,   8_000,  -32_768,    2)},
            {Type.HoldModEnv,                   (-12_000,   5_000,  -12_000,    2)},
            {Type.DecayModEnv,                  (-12_000,   8_000,  -12_000,    2)},
            {Type.SustainModEnv,                (0,         1_000,  0,          1)},
            {Type.ReleaseModEnv,                (-12_000,   8_000,  -12_000,    2)},
            {Type.KeyNumToModEnvHold,           (-1_200,    1_200,  0,          1)},
            {Type.KeyNumToModEnvDecay,          (-1_200,    1_200,  0,          1)},
            
            // Volume envelope
            {Type.DelayVolEnv,                  (-12_000,   5_000,  -12_000,    2)},
            {Type.AttackVolEnv,                 (-12_000,   8_000,  -12_000,    2)},
            {Type.HoldVolEnv,                   (-12_000,   5_000,  -12_000,    2)},
            {Type.DecayVolEnv,                  (-12_000,   8_000,  -12_000,    2)},
            {Type.SustainVolEnv,                (0,         1_440,  0,          1)},
            {Type.ReleaseVolEnv,                (-12_000,   8_000,  -12_000,    2)},
            {Type.KeyNumToVolEnvHold,           (-1_200,    1_200,  0,          1)},
            {Type.KeyNumToVolEnvDecay,          (-1_200,    1_200,  0,          1)},
            
            // Misc
            {Type.StartLoopAddrsCoarseOffset,   (-32_768,   32_767, 0,          1)},
            {Type.KeyNum,                       (-1,        127,    -1,         1)},
            {Type.Velocity,                     (-1,        127,    -1,         1)},
            {Type.InitialAttenuation,           (0,         1_440,  0,          1)},
            {Type.EndLoopAddrsCoarseOffset,     (-32_768,   32_767, 0,          1)},
            {Type.CoarseTune,                   (-120,      120,    0,          1)},
            {Type.FineTune,                     (-12_700,   12_700, 0,          1)},
            {Type.ScaleTuning,                  (0,         1_200,  100,        1)},
            {Type.ExclusiveClass,               (0,         99_999, 0,          0)},
            {Type.OverridingRootKey,            (-1,        127,    -1,         0)},
            {Type.SampleModes,                  (0,         3,      0,          0)},
            
            // Non-standard
            {Type.Amplitude,                    (-1_000,    1_000,  0,          1)},
            {Type.VibLFORate,                   (-1_000,    1_000,  0,          1)},
            {Type.VibLFOToFilterFc,             (-12_000,   12_000, 0,          2)},
            {Type.VibLFOAmplitudeDepth,         (0,         1_000,  0,          1)},
            {Type.ModLFORate,                   (-1_000,    1_000,  0,          1)},
            {Type.ModLFOAmplitudeDepth,         (0,         1_000,  0,          1)},

        }.ToFrozenDictionary();
    
    //

    /// <summary>The generator's SF2 type.</summary>
    public readonly Type GType;
    /// <summary>The generator's 16-bit value.</summary>
    public readonly short Value;

    /// <summary> Constructs a new generator </summary>
    /// <param name="type">Generator type</param>
    /// <param name="value">Generator value</param>
    /// <param name="validate">If the limits should be validated and clamped.</param>
    public Generator(Type type, int value, bool validate = true)
    {
        GType = type;
        Value = (short)(!validate || !Limits.TryGetValue(type, out var limit)
            ? value
            : Math.Clamp(value, limit.Min, limit.Max));
    }

    public void Write(ref Span<byte> genData)
    {
        Util.WriteWord(ref genData, (short)GType);
        Util.WriteWord(ref genData, Value);
    }
    
    public static bool HasDefaultValue(Generator g) =>
        Limits.TryGetValue(g.GType, out var limit) && g.Value == limit.Def;

    public static List<Generator> Copy(List<Generator> target, List<Generator> source)
    {
        foreach (ref readonly var g in CollectionsMarshal.AsSpan(source))
            // Validate
            target.Add(new Generator(g.GType, g.Value));
        return target;
    }
}