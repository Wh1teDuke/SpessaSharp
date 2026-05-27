using System.Reflection;

namespace SSTool.Util;

public static class DefaultSoundBank
{
    public static byte[] Get()
    {
        const string resourceName = "SSTool.assets.GM.dls";

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
            
        if (stream == null)
            throw new FileNotFoundException(
                $"Could not find embedded resource: {resourceName}");

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}