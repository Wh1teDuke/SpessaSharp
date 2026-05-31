using System.Diagnostics;

namespace SpessaSharp.Utils;

/// <summary>Manage the log level of SpessaSharp</summary>
public static class SpessaLog
{
    /// <summary>The most verbose log level, prints out a lot of small details.</summary>
    public static bool InfoEnabled = false;
    /// <summary>The default log level, prints out warnings for unexpected and erroneous behavior.</summary>
    public static bool WarnEnabled = true;

    [Conditional("DEBUG")]
    public static void SetLogLevel(bool enableInfo, bool enableWarn)
    {
        InfoEnabled = enableInfo;
        WarnEnabled = enableWarn;
    }
    
    [Conditional("DEBUG")]
    public static void Info(string str)
    {
        if (InfoEnabled) Console.WriteLine(str);
    }
    
    [Conditional("DEBUG")]
    public static void Warn(string str)
    {
        if (WarnEnabled) Console.WriteLine($"[WARN] {str}");
    }

    [Conditional("DEBUG")]
    public static void Unsupported(
        string what, ReadOnlySpan<byte> syx, string? reason = null)
    {
        if (!InfoEnabled) return;
        Info($"[WARN] Unsupported {what} message: {Util.ToHexString(syx)}. {reason}");
    }
    
    [Conditional("DEBUG")]
    public static void GMInfo<T>(string what, T value, string? unit = null)
    {
        if (!InfoEnabled) return; 
        CoolInfo($"General MIDI {what}", value, unit);
    }
    
    [Conditional("DEBUG")]
    public static void GMFail(
        string what, ReadOnlySpan<byte> syx, string? reason = null)
    {
        if (!InfoEnabled) return;
        Unsupported($"General MIDI {what}", syx, reason);
    }
    
    [Conditional("DEBUG")]
    public static void GSInfo<T>(string what, T value, string? unit = null)
    {
        if (!InfoEnabled) return; 
        CoolInfo($"Roland GS {what}", value, unit);
    }
    
    [Conditional("DEBUG")]
    public static void GSFail(
        string what, ReadOnlySpan<byte> syx, string? reason = null)
    {
        if (!InfoEnabled) return;
        Unsupported($"Roland GS {what}", syx, reason);
    }
    
    [Conditional("DEBUG")]
    public static void XGInfo<T>(string what, T value, string? unit = null)
    {
        if (!InfoEnabled) return; 
        CoolInfo($"Yamaha XG {what}", value, unit);
    }
    
    [Conditional("DEBUG")]
    public static void XGFail(
        string what, ReadOnlySpan<byte> syx, string? reason = null)
    {
        if (!InfoEnabled) return;
        Unsupported($"Yamaha XG {what}", syx, reason);
    }
    
    [Conditional("DEBUG")]
    public static void CoolInfo<T>(string what, T value, string? unit = null)
    {
        if (!InfoEnabled) return;

        Info(unit != null
            ? $"{what} is now set to {value} {unit}."
            : $"{what} is now set to {value}.");
    }
}