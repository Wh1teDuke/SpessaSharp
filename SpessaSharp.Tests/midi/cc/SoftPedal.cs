#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;

var test = new MidiTestMaker("Soft Pedal Test");

// Sav wave no extra effects
// Capital tone since variation banks seem to not be affected????
test.ProgramChange(0, 3, 81)
    .CC(Midi.CC.ReverbDepth, 0)
    .CC(Midi.CC.MainVolume, 127)
    .CC(Midi.CC.VibratoDepth, 0);

test.Text("Soft pedal OFF")
    .CC(Midi.CC.SoftPedal, 0)
    .Note(60, 127, 480)
    .Wait(480)

    .Text("Soft pedal ON")
    .CC(Midi.CC.SoftPedal, 127)
    .Note(60, 127, 480);

test.Make();