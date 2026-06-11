using System.Collections;
using System.Runtime.InteropServices;

namespace SpessaSharp.MIDI;

public sealed class Track
{
    /// <summary>The name of this track.</summary>
    public string Name { get; set; } = "";
    
    /// <summary>The MIDI port number used by the track.</summary>
    public int Port { get; set; }
    /// <summary>A set that contains the MIDI channels used by the track in the sequence.</summary>
    public readonly BitArray Channels = new (16);
    /// <summary>All the MIDI messages of this track.</summary>
    internal readonly List<MidiMessage> EventList = [];
    
    public ReadOnlySpan<MidiMessage> Events =>
        CollectionsMarshal.AsSpan(EventList);

    public static Track Copy(Track track)
    {
        var t = new Track();
        t.CopyFrom(track);
        return t;
    }

    private void CopyFrom(Track track)
    {
        Name = track.Name;
        Port = track.Port;
        Channels.Or(track.Channels);
        EventList.AddRange(track.EventList);// TODO: Depth array copy
    }
    
    /// <summary>Adds an event to the track.</summary>
    /// <param name="msg">The event to add.</param>
    /// <param name="index">The index at which to add this event.</param>
    public void Add(MidiMessage msg, int index) => EventList.Insert(index, msg);
    
    /// <summary>Adds events to the track.</summary>
    /// <param name="events">The index at which to add these event.</param>
    /// <param name="index">The events to add.</param>
    public void Add(ReadOnlySpan<MidiMessage> events, int index) => 
        EventList.InsertRange(index, events);
    
    /// <summary>Removes an event from the track.</summary>
    /// <param name="index">The index of the event to remove.</param>
    public void DeleteEvent(int index) => EventList.RemoveAt(index);
}