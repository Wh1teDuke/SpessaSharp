using System.Numerics;

namespace SpessaSharp.Synthesizer.Engine.Channel;

internal static class PortamentoTime
{
    // Tests were performed by John Novak
    // https://github.com/dosbox-staging/dosbox-staging/pull/2705

    /*
    Original table by John Novak:
    CC 5 value  Portamento time
    ----------  ---------------
         0          0.000 s
         1          0.006 s
         2          0.023 s
         4          0.050 s
         8          0.110 s
        16          0.250 s
        32          0.500 s
        64          2.060 s
        80          4.200 s
        96          8.400 s
       112         19.500 s
       116         26.700 s
       120         40.000 s
       124         80.000 s
       127        480.000 s

       This table used to be linearly interpolated, but it has been replaced by a PCHIP function created by Benjamin Rosseaux,
       the developer behind the Sobanth SF2 synthesizer.
       More info in this comment:
       https://github.com/FluidSynth/fluidsynth/issues/1722#issuecomment-3706599241
    */

    /// <summary>
    /// (PCHIP cubic spline - smooth and exact), optimized with fewer operations than full bilinear search and interpolation.
    /// Created by Benjamin Rosseaux.
    /// </summary>
    /// <param name="cc">The CC#5 value (should not be decimal)</param>
    /// <returns></returns>
    private static float ToRate(int cc)
    {
        const int PORTA_DIVISION_CONSTANT = 40;

        if (cc < 1)
            // Original code has smoothing here but since CC#5 is an integer, it is not needed.
            return 0;
        
        // PCHIP cubic spline in log-log space - smooth & exact
        // Segments: [1..2],[2..4],[4..8],[8..16],[16..32],[32..64],[64..80],[80..96],[96..112],[112..120],[120..124],[124..127]
        var x0 = (ReadOnlySpan<int>)[
            1, 2, 4, 8, 16, 32, 64, 80, 96, 112, 120, 124];
        var ih = (ReadOnlySpan<float>)[
            1f,
            0.5f,
            0.25f,
            0.125f,
            0.0625f,
            0.031_25f,
            0.0625f,
            0.0625f,
            0.0625f,
            0.125f,
            0.25f,
            1 / 3f
        ];

        var a = (ReadOnlySpan<float>)[
            -0.166_531_273_825_012_15f, 0.118_638_752_182_994_08f,
            0.029_479_047_361_245_264f, -0.005_442_312_089_231_738f,
            0.145_152_087_597_303_7f, -0.005_056_281_449_558_275f,
            -0.005_095_486_882_876_532f, 0.033_340_095_511_115_44f,
            -0.093_613_686_780_204_32f, 0.141_325_697_024_518_22f,
            -0.158_055_653_010_113_82f, -0.099_188_569_558_819_27f
        ];

        var b = (ReadOnlySpan<float>)[
            0.028_212_773_333_433_472f, -0.338_850_206_499_284_7f,
            -0.158_395_298_909_297_13f, -0.123_981_317_667_754_83f,
            -0.287_484_855_268_511_1f, 0.012_254_866_302_537_692f,
            0.005_957_797_193_345_771f, -0.037_458_993_303_473_74f,
            0.129_117_818_698_101_96f, -0.158_671_932_241_625_68f,
            0.504_406_322_732_748f, 0.378_684_513_187_545_8f
        ];
        
        var c = (ReadOnlySpan<float>)[
            0.721_895_086_125_528_3f, 0.557_453_622_634_716_8f,
            0.471_338_932_370_258_26f, 0.485_970_953_270_799_14f,
            0.443_362_763_335_188_54f, 0.607_698_631_180_155_1f,
            0.308_519_759_718_277_94f, 0.305_148_893_456_339_55f,
            0.330_251_193_382_738_4f, 0.153_822_885_219_165f,
            0.130_228_055_904_733_7f, 0.498_655_306_754_916_87f
        ];
        
        var d = (ReadOnlySpan<float>)[
            -2.221_848_749_616_356_6f, -1.638_272_163_982_407_2f,
            -1.301_029_995_663_981_3f, -0.958_607_314_841_775f,
            -0.602_059_991_327_962_4f, -0.301_029_995_663_981_2f,
            0.313_867_220_369_153_43f, 0.623_249_290_397_900_4f,
            0.924_279_286_061_881_7f, 1.290_034_611_362_518f,
            1.426_511_261_364_575_2f, 1.903_089_986_991_943_5f
        ];

        var s = cc switch
        {
            <= 64 => cc <= 2 ? 0 : BitOperations.Log2((uint)(cc - 1)),
            <= 80 => 6,
            <= 96 => 7,
            <= 112 => 8,
            <= 120 => 9,
            <= 124 => 10,
            _ => 11
        };
        
        var t = (cc - x0[s]) * ih[s];
        return float.Exp(
            2.302_585_092_994_046f *
            (((a[s] * t + b[s]) * t + c[s]) * t + d[s])
        ) / PORTA_DIVISION_CONSTANT;
    }
    
    /// <summary> Converts portamento time to seconds. </summary>
    /// <param name="time">MIDI portamento time (CC 5 value) (0-127)</param>
    /// <param name="distance">Distance in semitones (keys) to slide over.</param>
    /// <returns>The portamento time in seconds.</returns>
    public static float ToSeconds(int time, int distance) =>
        // This seems to work fine for the MIDIs I have.
        // Note: Some tests about portamento were compared to SC-VA and S-YXG50
        // PortaTimeToRate is the constant rate of the portamento, as that's how the synths work
        // We multiply it by the distance
        ToRate(time) * distance;
}