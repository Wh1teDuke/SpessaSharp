#!/usr/bin/env -S dotnet run --configuration Release
#:project ../SpessaSharp/SpessaSharp.csproj

using SpessaSharp.MIDI.Utils;

// A simple example showing how to generate a 4/4 drum pattern with the MIDI builder.

// Recommended and the default
const int TICKS_PER_BEAT = 480;

var builder = MidiBuilder.New(
    MidiBuilder.Options.Default with
    {
        Name = "Simple Drum Pattern", 
        TimeDivision = TICKS_PER_BEAT,   
    });

// Channel 9 is by default the drum channel
var chanBuilder = builder.OfTrack(0).OfChannel(9);
var drumBuilder = builder.OfTrack(0).DrumBuilder;

// Time tracking, in MIDI ticks.
// MIDI ticks are not seconds - instead, they are fractions of a beat.
// Time division specifies how many ticks are in a beat, and the duration in seconds is determined by tempo.
var ticks = 0;

// Side stick intro
for (var i = 0; i < 4; i++) 
{
    AddNote(37);
    ticks += TICKS_PER_BEAT;
}

builder.OfTrack(0).SetLoopStart(ticks);

for (var i = 0; i < 4; i++)
{
    const int HALF_BEAT = TICKS_PER_BEAT / 2;
    
    // One measure
    AddNote(36); // Kick
    ticks += HALF_BEAT;

    AddNote(42); // Hi-hat
    ticks += HALF_BEAT;

    AddNote(38); // Snare
    ticks += HALF_BEAT;

    AddNote(42); // Hi-hat
    ticks += HALF_BEAT;

    Play(InstrumentInfo.Drum.BassDrum1); // Kick
    ticks += HALF_BEAT;

    Play(InstrumentInfo.Drum.ClosedHiHat); // Hi-hat
    ticks += HALF_BEAT;

    Play(InstrumentInfo.Drum.AcousticSnare); // Snare
    ticks += HALF_BEAT;

    Play(InstrumentInfo.Drum.ClosedHiHat); // Hi-hat
    ticks += HALF_BEAT / 2;

    Play(InstrumentInfo.Drum.AcousticSnare); // Snare
    ticks += HALF_BEAT / 2;
}

builder.OfTrack(0).SetLoopEnd(ticks);

builder.Midi.Flush();
File.WriteAllBytes("drum_pattern.mid", builder.Midi.Write());
Console.WriteLine("File written to drum_pattern.mid");
return;

// A simple helper function to add drum notes
void AddNote(int midiNote)
{
    chanBuilder.NoteOn(ticks, midiNote, 120);
    chanBuilder.NoteOff(ticks, midiNote); // Drum notes can be released immediately
}

void Play(InstrumentInfo.Drum drum) => drumBuilder.Play(ticks, drum, 120);