#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;

var test = new MidiTestMaker("Tremolo EFX Test");

test.ProgramChange(8, 1, 80)
    .CC(Midi.CC.VibratoDepth, 0)
    .CC(Midi.CC.Brightness, 127)
    .CC(Midi.CC.ReverbDepth, 0)
    .Wait(80)
    .NoteOn(60, 120)
    .Wait(480)
    .NoteOff(60);

var efx = test.EFX(0x01, 0x25);

test.Wait(480)
    .Text("Basic note test")
    .NoteOn(60, 120)
    .Wait(2560)
    .Text("Param test");

efx.SweepParam(3, 0, 4, 960, 1)
    .SweepParam(4, 0, 127, 480, 16)
    .SetParam(3, 0)
    .SweepParam(5, 0, 127, 240, 8);

test.Wait(960).NoteOff(60);

test.Make();