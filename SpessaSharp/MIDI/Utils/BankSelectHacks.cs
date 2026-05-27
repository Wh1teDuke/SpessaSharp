namespace SpessaSharp.MIDI.Utils;

/// <summary>A class for handling various ways of selecting patches (GS, XG, GM2)</summary>
internal static class BankSelectHacks
{
    private const int XG_SFX_VOICE = 64;
    private const int GM2_DEFAULT_BANK = 121;

    /// <summary>GM2 has a different default bank number</summary>
    /// <param name="sys"></param>
    /// <returns></returns>
    public static int GetDefaultBank(Midi.System sys) =>
        sys == Midi.System.GM2 ? GM2_DEFAULT_BANK : 0;

    public static int GetDrumBank(Midi.System sys) =>
        sys switch
        {
            Midi.System.GM2 => 120,
            Midi.System.XG => 127,
            Midi.System.GM or
            Midi.System.GS or
            _ => throw new ArgumentException(
                $"{sys} doesn't have a bank MSB for drums.")
        };

    /// <summary>Checks if this bank number is XG drums.</summary>
    /// <param name="bankMSB"></param>
    /// <returns></returns>
    public static bool IsXGDrum(int bankMSB)
    {
        /*
        Note: we omit 126 (XG SFX Drums) here, as they are unwanted most of the time.
        If they are really wanted, the direct match will match them anyway.
        Testcase: Timbres of heaven, selecting 0:127:30 picked XG SFX.
        */
        return bankMSB is 120 or 127;
    }
    
    /// <summary>Checks if this MSB is a valid XG MSB</summary>
    /// <param name="bankMSB"></param>
    /// <returns></returns>
    public static bool IsValidXGMSB(int bankMSB) =>
        IsXGDrum(bankMSB) ||
        bankMSB == XG_SFX_VOICE || bankMSB == GM2_DEFAULT_BANK;
    
    public static bool IsSystemXG(Midi.System system) =>
        system is Midi.System.GM2 or Midi.System.XG;
    
    public static int AddBankOffset(
        int bankMSB, int bankOffset, bool isXG) =>
        // Do not change XG drums (120, 126 or 127)
        IsXGDrum(bankMSB) && isXG
            ? bankMSB 
            : Math.Min(bankMSB + bankOffset, 127);

    public static int SubtractBankOffset(
        int bankMSB, int bankOffset, bool isXG) =>
        // Do not change XG drums (120, 126 or 127)
        IsXGDrum(bankMSB) && isXG
            ? bankMSB 
            : Math.Max(0, bankMSB - bankOffset);
}