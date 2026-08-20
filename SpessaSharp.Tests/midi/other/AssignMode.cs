#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;
using SpessaSharp.Synthesizer.Engine.Channel;

var test = new MidiTestMaker(
    "Assign Mode Test", 
    MidiTestMaker.Options.Default with { Channel = 0 });
// Celesta (long fade)
test.ProgramChange(0, 0, 8);

const int n = 60;

test.Text("Assign Mode: Full Multi")
    .Set(MidiChannel.Assign.FullMulti)
    .Note(n, 120, 60)
    .Note(n, 1, 60)
    .Wait(960);

test.Text("Assign Mode: Limited Multi")
    .Set(MidiChannel.Assign.LimitedMulti)
    .Note(n, 120, 60)
    .Note(n, 1, 60)
    .Wait(960);

test.Text("Assign Mode: Single")
    .Set(MidiChannel.Assign.Single)
    .Note(n, 120, 60)
    .Note(n, 1, 60)
    .Wait(960);

test.Reset(Midi.System.XG).Text("XG Version testing");

// Celesta (long fade)
test.ProgramChange(0, 0, 8);

test.Text("SAME NOTE NUMBER KEY ON ASSIGN: INST")
    .XG(0x08, 0, 0x06, [2])
    .Note(n, 120, 60)
    .Note(n, 1, 60)
    .Wait(960);

test.Text("SAME NOTE NUMBER KEY ON ASSIGN: MULTI")
    .XG(0x08, 0, 0x06, [1])
    .Note(n, 120, 60)
    .Note(n, 1, 60)
    .Wait(960);

test.Text("SAME NOTE NUMBER KEY ON ASSIGN: SINGLE")
    .XG(0x08, 0, 0x06, [0])
    .Note(n, 120, 60)
    .Note(n, 1, 60)
    .Wait(960);

test.Make();