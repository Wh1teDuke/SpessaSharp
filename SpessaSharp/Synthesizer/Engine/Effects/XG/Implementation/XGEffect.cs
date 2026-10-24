namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Implementation;

public static class XGEffect
{
    public enum Reverb
    {
        T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15,

        Type, Return, Pan, Number,
    }
    
    public enum Chorus
    {
        T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15,
        
        Type, Return, Pan, SendToReverb, Number,
    }
    
    public enum Variation
    {
        Type, Return, Pan, SendToReverb, SendToChorus, PartNumber,
        Number,
        Connection,
    }
    
    /// <summary> Variation connection mode </summary>
    public enum VariationConnection
    {
        /// <summary> Routes all channels via sends (like reverb and chorus). </summary>
        System,
        /// <summary> Routes a single <c>partNumber</c> channel straight through. </summary>
        Insertion,
    }
    
    public enum Insertion
    {
        T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15,
        
        Type, PartNumber, Number
    }
}