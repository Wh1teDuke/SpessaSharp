using System.Globalization;
using System.Runtime.CompilerServices;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Channel;

/// <summary>Represents a single drum instrument's XG/GS parameters.</summary>
public record struct DrumParameter
{
    public enum Type
    {
        /// <summary>
        /// Pitch offset in semitones. Relative value
        /// May be floating point! (GS half-semitone coarse tune resolution)
        /// </summary>
        PitchCoarse,
        /// <summary>Pitch offset in cents. Relative value.</summary>
        PitchFine,
        /// <summary>Level in 0 - 127 range.</summary>
        Level,
        /// <summary>Exclusive class override.</summary>
        AssignGroup,
        /// <summary>Pan, 1-64-127, 0 is random. This adds to the channel pan!</summary>
        Pan,
        /// <summary>Reverb send level 0-127.</summary>
        ReverbSend,
        /// <summary>Chorus send level 0-127.</summary>
        ChorusSend,
        /// <summary>Variation/delay send level 0-127.</summary>
        VariationSend,
        /// <summary>If note on should be received.</summary>
        RxNoteOn,
        /// <summary>
        /// If note off should be received.
        /// Note: Due to the way sound banks implement drums (as 100s release time),
        /// this means killing the voice on note off, not releasing it.</summary>
        RxNoteOff,
    }
    
    [InlineArray(10)] // Type.len
    private struct Buffer { private float _element0; }

    private Buffer _buffer;

    public Entry this[Type type]
    {
        get => new(type, Params.Of(_buffer[(int)type]));
        set => _buffer[(int)value.Type] = value.Data.Value;
    }
    
    /// <summary>Pitch offset in semitones. Relative value.</summary>
    public float PitchCoarse
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetFloat(Type.PitchCoarse);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.PitchCoarse, value);
    }
    
    /// <summary>Pitch offset in cents. Relative value.</summary>
    public int PitchFine
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetInt(Type.PitchFine);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.PitchFine, value);
    }
            
    /// <summary>Level in 0 - 127 range.</summary>
    public int Level
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetInt(Type.Level);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.Level, value);
    }
    
    /// <summary>Exclusive class override.</summary>
    public int AssignGroup
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetInt(Type.AssignGroup);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.AssignGroup, value);
    }
    
    /// <summary>Pan, 1-64-127, 0 is random. This adds to the channel pan!</summary>
    public int Pan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetInt(Type.Pan);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.Pan, value);
    }
    
    /// <summary>Reverb send level 0-127.</summary>
    public int ReverbSend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetInt(Type.ReverbSend);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.ReverbSend, value);
    }
    
    /// <summary>Chorus send level 0-127.</summary>
    public int ChorusSend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetInt(Type.ChorusSend);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.ChorusSend, value);
    }
    
    /// <summary>Variation/delay send level 0-127.</summary>
    public int VariationSend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetInt(Type.VariationSend);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.VariationSend, value);
    }
    
    /// <summary>If note on should be received.</summary>
    public bool RxNoteOn
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetBool(Type.RxNoteOn);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.RxNoteOn, value);
    }
    
    /// <summary>If note off should be received. Note: Due to the way sound banks implement drums (as 100s release time), this means killing the voice on note off, not releasing it.</summary>
    public bool RxNoteOff
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
            GetBool(Type.RxNoteOff);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] set =>
            Set(Type.RxNoteOff, value);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float GetFloat(Type type)
    {
        Assert(type, Params.Type.Float);
        return _buffer[(int)type];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetInt(Type type)
    {
        Assert(type, Params.Type.Int);
        return BitConverter.SingleToInt32Bits(_buffer[(int)type]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool GetBool(Type type)
    {
        Assert(type, Params.Type.Bool);
        return BitConverter.SingleToInt32Bits(_buffer[(int)type]) == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Set(Type type, float value)
    {
        Assert(type, Params.Type.Float);
        _buffer[(int)type] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Set(Type type, int value)
    {
        Assert(type, Params.Type.Int);
        _buffer[(int)type] = BitConverter.Int32BitsToSingle(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Set(Type type, bool value)
    {
        Assert(type, Params.Type.Bool);
        _buffer[(int)type] = BitConverter.Int32BitsToSingle(value ? 1 : 0);
    }

    public static DrumParameter GetDefault(int i) =>
        new()
        {
            PitchCoarse     = 0,
            PitchFine       = 0,
            Level           = 120,
            AssignGroup     = 0,
            Pan             = 64,
            ReverbSend      = Reset.DefaultDrumReverb[i],
            ChorusSend      = 127,
            VariationSend   = 127,
            RxNoteOn        = true,
            RxNoteOff       = false,
        };
    
    public static Params.Type TypeOf(Type type) => type switch
    {
        Type.PitchCoarse => Params.Type.Float,
        Type.PitchFine => Params.Type.Int,
        Type.Level => Params.Type.Int,
        Type.AssignGroup => Params.Type.Int,
        Type.Pan => Params.Type.Int,
        Type.ReverbSend => Params.Type.Int,
        Type.ChorusSend => Params.Type.Int,
        Type.VariationSend => Params.Type.Int,
        Type.RxNoteOn => Params.Type.Bool,
        Type.RxNoteOff => Params.Type.Bool,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
    
    private static void Assert(Type type, Params.Type value) =>
        Params.Assert(TypeOf(type), value);

    public readonly record struct Entry
    {
        public readonly Type Type;
        internal readonly Params.Data Data;

        internal Entry(Type type, Params.Data data)
        {
            Type = type;
            Data = data;
        }
        
        public bool AsBool
        {
            get
            {
                Assert(Type, Params.Type.Bool);
                return Data.AsBool();
            }
        }

        public int AsInt
        {
            get
            {
                Assert(Type, Params.Type.Int);
                return Data.AsInt();
            }
        }

        public float AsFloat
        {
            get
            {
                Assert(Type, Params.Type.Float);
                return Data.AsFloat();
            }
        }

        public int ToInt() =>
            TypeOf(Type) switch
            {
                Params.Type.Int => Data.AsInt(),
                Params.Type.Float => (int)Data.AsFloat(),
                Params.Type.Bool => Data.AsBool() ? 1 : 0,
                Params.Type.InterpolationType or
                Params.Type.MidiSystem or
                Params.Type.CC or
                Params.Type.AssignMode or
                Params.Type.OptBool or
                Params.Type.OptInterpolationType or
                Params.Type.DrumParameters or
                _ => throw new ArgumentOutOfRangeException()
            };
        
        public string ValueToString() =>
            // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
            TypeOf(Type) switch
            {
                Params.Type.Int => Data.AsInt().ToString(),
                Params.Type.Float => $"{Data.AsFloat():F2}",
                Params.Type.Bool => Data.AsBool().ToString(),
                _ => throw new ArgumentOutOfRangeException()
            };

        private static Entry Of(Type type, float data) => 
            new(type, Params.Of(data));
        
        private static Entry Of(Type type, int data) =>
            new(type, Params.Of(data));
        
        private static Entry Of(Type type, bool data) =>
            new(type, Params.Of(data));

        public static implicit operator Entry((Type Type, float Data) entry) =>
            Of(entry.Type, entry.Data);
        
        public static implicit operator Entry((Type Type, int Data) entry) =>
            Of(entry.Type, entry.Data);
        
        public static implicit operator Entry((Type Type, bool Data) entry) =>
            Of(entry.Type, entry.Data);
    }
}