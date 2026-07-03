using System.Buffers;
using System.Runtime.InteropServices;
using Gaiden.SFML.Audio;
using SpessaSharp.SoundBank.Utils;
using SpessaSharp.Utils;

namespace SSTool.Util;

internal static class SfmlUtil
{
    public static ArraySegment<float> DecodeVorbis(
        ArraySegment<byte> compressed)
    {
        var pool = ArrayPool<short>.Shared;
        short[]? buffer = null;

        try
        {
            unsafe
            {
                fixed (byte* data = compressed.Array!)
                {
                    var d = data;
                    d += compressed.Offset;
                    var ptr = CSFMLAudio.sfSoundBuffer_createFromMemory(
                        (IntPtr)d, (UIntPtr)(ulong)compressed.Count);

                    var len = (int)CSFMLAudio.sfSoundBuffer_getSampleCount(ptr);
                    buffer = pool.Rent(len);
                    Marshal.Copy(CSFMLAudio.sfSoundBuffer_getSamples(ptr), buffer, 0, len);
                    
                    var res = SharedSampleBuffer.New(len);
                    AudioUtil.ConvertPCM16ToFloat32(
                        MemoryMarshal.AsBytes(buffer), res, len);

                    CSFMLAudio.sfSoundBuffer_destroy(ptr);

                    return res;
                }
            }
        }
        finally
        {
            if (buffer != null) pool.Return(buffer);
        }
    }
}