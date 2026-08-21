#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;

var test = new MidiTestMaker("Overlapping Notes Test");

test.ProgramChange(1, 0, 80)

    .Text("Note On Square")
    .NoteOn(60, 80)
    .Wait(480)

    .ProgramChange(1, 0, 81)

    .Text("Note On Saw")
    .NoteOn(60, 127)
    .Wait(480)
    .Text("Note Off Square")
    .NoteOff(60)
    .Wait(480)
    .Text("Note Off Saw")
    .NoteOff(60);

test.Wait(480);

test.Text("Multiple Note Off Test")
    .NoteOn(60, 120)
    .Wait(480)
    .NoteOff(60)
    .Wait(80)
    .NoteOff(60)
    .Wait(80)
    .NoteOff(60)
    .Wait(80)
    .NoteOff(60)
    .Wait(120)
    .NoteOn(60, 120)
    .Wait(480)
    .NoteOff(60);

test.Wait(480);

// Mono mode test
test.Text("Mono Mode Test")
    .CC(Midi.CC.MonoModeOn, 0)
    .NoteOn(60, 127)
    .Wait(480)
    .NoteOn(64, 127)
    .Wait(480)
    .NoteOn(60, 127)
    .Wait(480)
    .NoteOff(60)
    .Wait(480)
    .NoteOn(60, 127)
    .Wait(480)
    .NoteOff(60);

test.Make();
