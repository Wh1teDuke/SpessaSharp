#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;

var test = new MidiTestMaker("GS Filter Test");

// MG Square, no reverb, max volume and no vibrato
test.ProgramChange(1, 1, 80)
    .CC(Midi.CC.ReverbDepth, 0)
    .CC(Midi.CC.MainVolume, 127)
    .CC(Midi.CC.VibratoDepth, 0)
    .CC(Midi.CC.VibratoRate, 0)
    .CC(Midi.CC.VibratoDelay, 127);

test.Text("MG Square").Note(60, 127, 960).Wait(480);

test.Text("CC#71 = 0")
    .NRPN(0xa1, 0)
    .Note(60, 127, 960)
    .NRPN(0xa1, 64)
    .Wait(480);

test.Text("CC#74 Max")
    .NRPN(0xa0, 127)
    .Note(60, 127, 960)
    .NRPN(0xa0, 64)
    .Wait(480);

test.Text("CC#74 Sweep, top half")
    .NoteOn(60, 127)
    .SweepNrpn(0xa0, 64, 127, 20)
    .NRPN(0xa0, 64)
    .Wait(480)
    .NoteOff(60)
    .Wait(480);

test.Text("CC#74 Sweep")
    .NoteOn(60, 127)
    .SweepNrpn(0xa0, 0, 127, 10)
    .NoteOff(60)
    .NRPN(0xa0, 64)
    .Wait(480);

test.Text("CC#74 Sweep, CC#74 = 127")
    .NRPN(0xa1, 127)
    .NoteOn(60, 127)
    .SweepNrpn(0xa0, 0, 127, 10)
    .NRPN(0xa0, 64)
    .Wait(480)
    .NoteOff(60)
    .Wait(480);

test.Text("CC#71 sweep")
    .NoteOn(60, 127)
    .SweepNrpn(0xa1, 0, 127, 10)
    .NoteOff(60);

test.Make();