#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;
using SpessaSharp.Utils;

var test = new MidiTestMaker("Velocity Sense Depth Bug");

test.Text($"Depth = {1000}")
    .GS(0x40, 0x11, 0x1a, [100])
    .CC(Midi.CC.ReverbDepth, 0);

for (var j = 40; j < 100; j++)
    test.Note(j, (int)(28 + SpessaUtil.Rand() * 100), 120);

for (var j = 100; j > 40; j--)
    test.Note(j, (int)(28 + SpessaUtil.Rand() * 100), 120);

test.Make();
