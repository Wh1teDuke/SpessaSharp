using System.Diagnostics;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.DLS;

internal sealed class DownloadableSoundsSample(
    int wFormatTag,
    int bytesPerSample,
    int sampleRate,
    RIFFChunk dataChunk)
{
    private WaveSample _waveSample = WaveSample.Default;
    
    public readonly int WFormatTag = wFormatTag;
    public readonly int BytesPerSample = bytesPerSample;
    public readonly int Rate = sampleRate;
    public readonly RIFFChunk DataChunk = dataChunk;
    public string Name = "Unnamed sample";
    
    public WaveSample WaveSample => _waveSample;

    public static DownloadableSoundsSample Read(RIFFChunk waveChunk)
    {
        var chunks = DLSVerifier.VerifyAndReadList(
            waveChunk, new RIFFChunk.DLSFourCC("wave"));

        var fmtChunkIdx = chunks.FindIndex(c => c.Header == "fmt ");
        if (fmtChunkIdx == -1)
            throw SpessaException.ParsingSoundBank("No fmt chunk in the wave file!");
        
        var fmtChunk = chunks[fmtChunkIdx];
        var fmtData = fmtChunk.Data;
        
        // https://github.com/tpn/winsdk-10/blob/9b69fd26ac0c7d0b83d378dba01080e93349c2ed/Include/10.0.14393.0/shared/mmreg.h#L2108
        var wFormatTag = Util.ReadLittleEndian(ref fmtData, 2);
        var channelsAmount = Util.ReadLittleEndian(ref fmtData, 2);
        if (channelsAmount != 1)
            throw SpessaException.ParsingSoundBank(
                $"Only mono samples are supported. Fmt reports {
                    channelsAmount} channels.");

        var sampleRate = Util.ReadLittleEndian(ref fmtData, 4);
        // Skip avg bytes
        Util.ReadLittleEndian(ref fmtData, 4);
        // BlockAlign
        Util.ReadLittleEndian(ref fmtData, 2);
        // It's bits per sample because one channel
        var wBitsPerSample = Util.ReadLittleEndian(ref fmtData, 2);
        var bytesPerSample = wBitsPerSample / 8;
        
        var dataChunkIdx = chunks.FindIndex(c => c.Header == "data");
        if (dataChunkIdx == -1)
            throw SpessaException.ParsingSoundBank("No data chunk in the WAVE chunk!");

        var dataChunk = chunks[dataChunkIdx];
        var sample = new DownloadableSoundsSample(
            wFormatTag, bytesPerSample, sampleRate, dataChunk);
        
        // Read sample Name
        if (RIFFChunk.FindListType(
            chunks, new RIFFChunk.FourCC("INFO")) is { } wInfo)
        {
            var wiData = wInfo.Data;
            var infoChunk = RIFFChunk.Read(ref wiData);
            while (infoChunk.Header != "INAM" && wiData.Count > 0)
                infoChunk = RIFFChunk.Read(ref wiData);

            if (infoChunk.Header == "INAM")
            {
                sample.Name = Util.ToString(
                    Util.ReadBinaryString(
                        infoChunk.Data[..infoChunk.Size])).Trim();
            } 
        }
        
        // Read wave sample
        var wsmpChunk = chunks.FindIndex(c => c.Header == "wsmp");
        if (wsmpChunk != -1)
            sample._waveSample = WaveSample.Read(chunks[wsmpChunk]);

        return sample;
    }

    public static DownloadableSoundsSample FromSF(BasicSample sample)
    {
        var raw = sample.GetRawData(false);
        var dlsSample = new DownloadableSoundsSample(
            0x01, // PCM
            2, // 2 bytes per sample
            sample.Rate,
            // Get the s16le data
            new RIFFChunk(new RIFFChunk.FourCC("data"), raw.Count, raw)
        )
        {
            Name = sample.Name,
            _waveSample = WaveSample.From(sample)
        };

        return dlsSample;
    }

    public void ToSFSample(SoundBank bank)
    {
        // DLS allows tuning to be a SHORT (32767 max), while SF uses BYTE (with 99 max -99 min)
        // Clamp it down and change root key if needed
        var originalKey = WaveSample.UnityNote;
        var pitchCorrection = WaveSample.FineTune;
        var samplePitchSemitones = (short)(pitchCorrection / 100);

        originalKey += samplePitchSemitones;
        pitchCorrection -= (short)(samplePitchSemitones * 100);

        var loopStart = 0;
        var loopEnd = 0;
        
        if (WaveSample.Loop is {} loop)
        {
            loopStart = loop.Start;
            loopEnd = loopStart + loop.Len;
        }

        var sample = new DLSSample(
            Name,
            Rate,
            originalKey,
            pitchCorrection,
            loopStart,
            loopEnd,
            DataChunk,
            WFormatTag,
            BytesPerSample);
        
        bank.Samples.Add(sample);
    }

    public ArraySegment<byte> Write()
    {
        var fmt = WriteFmt();
        var wsmp = WaveSample.Write();
        var data = RIFFChunk.Write(
            new RIFFChunk.FourCC("data"), DataChunk.Data);

        var iname = RIFFChunk.Write(
            new RIFFChunk.FourCC("INAM"), Util.GetStringBytes(Name, true));
        var info = RIFFChunk.Write(
            new RIFFChunk.FourCC("INFO"), iname, false, true);
        
        Debug.WriteLine($"Saved {Name} successfully!");

        return RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("wave"), [fmt, wsmp, data, info], true);
    }

    private ArraySegment<byte> WriteFmt()
    {
        var fmtData = (Span<byte>)stackalloc byte[18];
        var buff = fmtData;

        Util.WriteWord(ref fmtData, (short)WFormatTag); // WFormatTag
        Util.WriteWord(ref fmtData, 1); // WChannels
        Util.WriteDword(ref fmtData, Rate);
        Util.WriteDword(ref fmtData, Rate * 2); // 16-bit samples
        Util.WriteWord(ref fmtData, 2); // WBlockAlign
        Util.WriteWord(ref fmtData, (short)(BytesPerSample * 8)); // WBitsPerSample

        return RIFFChunk.Write(new RIFFChunk.FourCC("fmt "), buff);
    }
}