namespace SpessaSharp.MIDI;

public static class RMidi
{
    /// <summary>
    /// Info type represents metadata for an RMIDI file.
    /// </summary>
    /// <param name="Name">The name of the song.</param>
    /// <param name="Engineer">The engineer who worked on the sound bank file.</param>
    /// <param name="Artist">The artist of the MIDI file.</param>
    /// <param name="Album">The album of the song.</param>
    /// <param name="Genre">The genre of the song.</param>
    /// <param name="Picture">The image for the file (album cover).</param>
    /// <param name="Comment">The comment of the file.</param>
    /// <param name="CreationDate">The creation date of the file.</param>
    /// <param name="Copyright">The copyright of the file.</param>
    /// <param name="InfoEncoding">The encoding of the RMIDI info.</param>
    /// <param name="MidiEncoding">The encoding of the MIDI file's text messages.</param>
    /// <param name="Software">The software used to write the file.</param>
    /// <param name="Subject">The subject of the file.</param>
    public sealed record Info(
        string? Name = null,
        string? Engineer = null,
        string? Artist = null,
        string? Album = null,
        string? Genre = null,
        ArraySegment<byte>? Picture = null,
        string? Comment = null,
        DateTime? CreationDate = null,
        string? Copyright = null,
        string? InfoEncoding = null,
        string? MidiEncoding = null,
        string? Software = null,
        string? Subject = null)
    {
        public enum Key
        {
            Name, Engineer, Artist, Album, Genre, Picture, Comment,
            CreationDate, Copyright, InfoEncoding, MidiEncoding, Software, Subject,
        }

        public object? Get(Key key) =>
            key switch
            {
                Key.Name => Name,
                Key.Engineer => Engineer,
                Key.Artist => Artist,
                Key.Album => Album,
                Key.Genre => Genre,
                Key.Picture => Picture,
                Key.Comment => Comment,
                Key.CreationDate => CreationDate,
                Key.Copyright => Copyright,
                Key.InfoEncoding => InfoEncoding,
                Key.MidiEncoding => MidiEncoding,
                Key.Software => Software,
                Key.Subject => Subject,
                _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
            };
    }
}