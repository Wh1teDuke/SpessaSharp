using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.DLS;

internal static class DLSVerifier
{
    public static void VerifyHeader(
        RIFFChunk chunk, params ReadOnlySpan<RIFFChunk.FourCC> expected)
    {
        foreach (var expect in expected) 
        {
            if (chunk.Header.EqualsIgnoreCase(expect))
                return;
        }

        var expectedStr = string.Join(
            ", or ",
            expected.ToArray().Select(h => h.Str));

        ParsingError(
            $"Invalid DLS chunk header! Expected '{expectedStr}' got '{
                chunk.Header}'");
    }

    public static void VerifyText(
        ReadOnlySpan<byte> text, 
        params ReadOnlySpan<RIFFChunk.DLSFourCC> expected) 
    {
        foreach (var expect in expected)
        {
            if (Ascii.EqualsIgnoreCase(text, expect.Str))
                return;
        }

        var expectedStr = string.Join(
            ", or ",
            expected.ToArray().Select(h => h.Str));
        var txt = Util.ToString(text);
        
        ParsingError(
            $"FourCC error: Expected {expectedStr} got {txt.ToLower()}");
    }
    
    [DoesNotReturn]
    public static void ParsingError(string error) => 
        throw SpessaException.ParsingSoundBank(
            $"DLS parse error: {error} The file may be corrupted.");
    
    public static List<RIFFChunk> VerifyAndReadList(
        RIFFChunk chunk,
        params ReadOnlySpan<RIFFChunk.DLSFourCC> type) 
    {
        VerifyHeader(chunk, new RIFFChunk.FourCC("LIST"));
        var data = chunk.Base;
        VerifyText(Util.ReadBinaryString(ref data, 4), type);
        var chunks = new List<RIFFChunk>();
        while (data.Count > 0)
            chunks.Add(RIFFChunk.Read(ref data));
        return chunks;
    }

    public static void PrintInfo(DownloadableSounds dls) => 
        Debug.WriteLine(dls.Info);
}