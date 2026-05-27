using System.Runtime.CompilerServices;

namespace SpessaSharp.Utils;

public static class Params
{
    public enum Type
    {
        Int, Float, Bool,
        InterpolationType,
        MidiSystem, CC, AssignMode,
        
        OptBool,
        OptInterpolationType,
    }
    
    internal readonly record struct Data(float Value, bool HasValue)
    {
        public float AsFloat() => Value;
        public int AsInt() => BitConverter.SingleToInt32Bits(Value);
        public bool AsBool() => AsInt() == 1;
        
        public bool? AsOptBool() => HasValue ? AsBool() : null;
        public int? AsOptInt() => HasValue ? AsInt() : null;
        public float? AsOptFloat() => HasValue ? AsFloat() : null;

        public T AsEnum<T>() where T : unmanaged, Enum
        {
            var i = AsInt();
            return Unsafe.As<int, T>(ref i);
        }

        public T? AsOptEnum<T>() where T : unmanaged, Enum =>
            HasValue ? AsEnum<T>() : null;
    }
    
    internal static void Assert(Type got, Type expected)
    {
        if (got == expected) return;
        throw new ArgumentOutOfRangeException(
            $"Expected: {expected}, Got: {got}");        
    }
    
    internal static Data OfNull() => 
        new (0, false);
    internal static Data Of(float floatVal) => 
        new (floatVal, true);
    internal static Data Of(int intVal) => 
        new (BitConverter.Int32BitsToSingle(intVal), true);
    internal static Data Of(bool boolVal) => 
        new (BitConverter.Int32BitsToSingle(boolVal ? 1 : 0), true);
    // enum must be backed by int (default type)
    internal static Data Of<T>(T enumVal) where T: unmanaged, Enum => 
        new (BitConverter.Int32BitsToSingle(Unsafe.As<T, int>(ref enumVal)), true);
}