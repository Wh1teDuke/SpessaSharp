#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;

var test = new MidiTestMaker("Mono Mode Test");

const int g = 480;

void NoteOn(int note) 
{
    if (note > 110) return;

    test.NoteOn(note, 100).Wait(g / 8);
    NoteOn(note + 1);
    test.NoteOff(note).Wait(g / 8);
}

test.ProgramChange(1, 1, 80)
    .CC(Midi.CC.MonoModeOn, 0)
    .Text("Note depth tracking test");

NoteOn(30);

test.Wait(g * 2);

test.Text("Notes going down")
    .NoteOn(60, 127)
    .Wait(g)
    .NoteOn(55, 127)
    .Wait(g)
    .NoteOn(52, 127);

test.Wait(g * 2)
    .NoteOff(52)
    .Wait(g)
    .NoteOff(55)
    .Wait(g)
    .NoteOff(60);

test.Wait(g * 2);

test.Text("Notes going out of order")
    .NoteOn(60, 127)
    .Wait(g)
    .NoteOn(55, 127)
    .Wait(g)
    .NoteOn(52, 127);

test.Wait(g * 2)
    .NoteOff(60)
    .Wait(g)
    .NoteOff(52)
    .Wait(g)
    .NoteOff(55);

test.Wait(g * 2);

test.Text("Note velocity test")
    .NoteOn(60, 30)
    .Wait(g)
    .NoteOn(64, 50)
    .Wait(g)
    .NoteOn(69, 127);

test.Wait(g * 2)
    .NoteOff(69)
    .Wait(g)
    .NoteOff(64)
    .Wait(g)
    .NoteOff(60);

test.Wait(g * 2);

test.Text("Note off not in order test")
    .NoteOn(60, 100)
    .Wait(g)
    .NoteOn(64, 100)
    .Wait(g)
    .NoteOn(69, 100)
    .Wait(g)
    .NoteOn(72, 100);

test.Wait(g * 2)
    .NoteOff(72)
    .Wait(g)
    .NoteOff(64)
    .Wait(g)
    .NoteOff(69)
    .Wait(g)
    .NoteOff(60);

test.Make();