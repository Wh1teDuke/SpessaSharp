#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;

var test = new MidiTestMaker("Stereo EQ");

test.ProgramChange(16, 3, 80)
    .CC(Midi.CC.VibratoDepth, 0)
    .CC(Midi.CC.Brightness, 127)
    .CC(Midi.CC.ReverbDepth, 0)
    .Wait(80)
    .NoteOn(60, 120)
    .Wait(480)
    .NoteOff(60);

var efx = test.EFX(0x01, 0x00);

test.Wait(80).NoteOn(60, 120);

efx.SweepParam(3, 0, 1, 840)
    .SweepParam(4, 52, 76)
    .SweepParam(5, 0, 1, 840)
    .SweepParam(6, 52, 76)
    .SweepParam(7, 0, 127, 480, 16)
    .SweepParam(8, 1, 4)
    .SweepParam(9, 52, 76)
    .SweepParam(0xa, 0, 127, 480, 16)
    .SweepParam(0xb, 1, 4)
    .SweepParam(0xc, 52, 76);

test.NoteOff(60);

test.Make();
