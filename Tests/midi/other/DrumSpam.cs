#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

var test = new MidiTestMaker(
    "Drum Spam Test", 
    MidiTestMaker.Options.Default with { Channel = 9 });

// Analog (TR-808)
test.ProgramChange(0, 0, 25);

for (var i = 0; i < 120; i++)
    test.Note(36, 120, 10);

test.Make();