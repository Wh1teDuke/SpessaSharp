#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI.Utils;

var test = new MidiTestMaker("RPN Tuning Real-time Test");

// MG Square
test.ProgramChange(1, 0, 80).NoteOn(60, 127);

test.Text("Real-Time Fine Tuning");
var pitch = 0;
while (pitch < 16_383) 
{
    test.RPN(ExtendedParameters.RPN.FineTuning, pitch).Wait(2);
    pitch = Math.Min(16_383, pitch + 10);
}
test.NoteOff(60).Wait(480);

// Piano 1
test.ProgramChange(0, 0, 0).NoteOn(60, 127);

test.Text("Coarse-tune: should be treated as non-realtime (key shift)");
pitch = 64;
while (pitch < 88) 
{
    test.RPN(ExtendedParameters.RPN.CoarseTuning, pitch << 7)
        .NoteOff(60)
        .NoteOn(60, 127)
        .Wait(480);
    pitch++;
}
test.NoteOff(60);

test.Make();