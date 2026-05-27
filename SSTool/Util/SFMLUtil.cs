using System.Buffers;
using System.Runtime.InteropServices;
using System.Security;
using SpessaSharp.SoundBank.Utils;
using SpessaSharp.Utils;

namespace SSTool.Util;

internal static partial class SfmlUtil
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
                    var ptr = sfSoundBuffer_createFromMemory(
                        (IntPtr)d, (UIntPtr)(ulong)compressed.Count);

                    var len = (int)sfSoundBuffer_getSampleCount(ptr);
                    buffer = pool.Rent(len);
                    Marshal.Copy(sfSoundBuffer_getSamples(ptr), buffer, 0, len);
                    
                    var res = SharedSampleBuffer.New(len);
                    AudioUtil.ConvertPCM16ToFloat32(
                        MemoryMarshal.AsBytes(buffer), res, len);

                    sfSoundBuffer_destroy(ptr);

                    return res;
                }
            }
        }
        finally
        {
            if (buffer != null) pool.Return(buffer);
        }
    }
    
    #region DLLImport
    [SuppressUnmanagedCodeSecurity]
    [LibraryImport("csfml-audio")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial IntPtr sfSoundBuffer_createFromMemory(IntPtr data, UIntPtr size);
    
    [SuppressUnmanagedCodeSecurity]
    [LibraryImport("csfml-audio")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void sfSoundBuffer_destroy(IntPtr soundBuffer);
    
    [SuppressUnmanagedCodeSecurity]
    [LibraryImport("csfml-audio")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial IntPtr sfSoundBuffer_getSamples(IntPtr soundBuffer);

    [SuppressUnmanagedCodeSecurity]
    [LibraryImport("csfml-audio")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial ulong sfSoundBuffer_getSampleCount(IntPtr soundBuffer);
    #endregion
}