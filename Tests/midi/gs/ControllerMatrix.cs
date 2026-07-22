#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;

// TODO: Finish
var test = new MidiTestMaker("GS Controller matrix comparison");

// Sine wave, no reverb, max volume and no vibrato
test.ProgramChange(8, 1, 80)
    .CC(Midi.CC.ReverbDepth, 0)
    .CC(Midi.CC.MainVolume, 127)
    .CC(Midi.CC.VibratoDepth, 0);

void SweepGSMatrix(string name, int a3, int v) 
{
    test.Text(name)
        .GS(0x40, 0x21, a3, [v])
        .NoteOn(60, 127)
        .SweepCC((Midi.CC)16, 0, 127, 30)
        .NoteOff(60)
        .GS(0x40, 0x21, a3, [64])
        .CC((Midi.CC)16, 0)
        .Wait(480);
}

test.Text("PITCH CONTROL Test");
test.Text("Pitch Wheel - baseline")
    .RPN(0, 24 << 7)
    .NoteOn(60, 127)
    .SweepPitch(0, 8192, 1, 2)
    .NoteOff(60)
    .Wait(480)
    .NoteOn(60, 127)
    .SweepPitch(8192, 16_383, 1, 2)
    .NoteOff(60)
    .Pitch(8192)
    .RPN(ExtendedParameters.RPN.PitchWheelRange, 2 << 7)
    .Wait(480);
SweepGSMatrix("CC1 PITCH CONTROL -24 [semitones]", 0x40, 0x28);
SweepGSMatrix("CC1 PITCH CONTROL +24 [semitones]", 0x40, 0x58);
SweepGSMatrix("CC1 PITCH CONTROL +64 [semitones]", 0x40, 127);

// Square wave
test.ProgramChange(1, 1, 80);

test.Text("TVF CONTROL Test");
test.Text("CC#74 - baseline (filter, lower half)")
    .NoteOn(60, 127)
    .SweepCC(Midi.CC.Brightness, 64, 0, 60)
    .NoteOff(60)
    .CC(Midi.CC.Brightness, 64)
    .Wait(480);
SweepGSMatrix("CC1 TVF CONTROL -9600 [cent]", 0x41, 0);
SweepGSMatrix("CC1 TVF CONTROL +9600 [cent]", 0x41, 127);

test.Text("CC#74 at lowest")
    .CC(Midi.CC.Brightness, 0)
    .Note(60, 127, 480)
    .CC(Midi.CC.Brightness, 64)
    .Wait(480);

test.Text("CC1 TVF CONTROL -9600 [cent] at highest")
    .GS(0x40, 0x21, 0x41, [0])
    .CC((Midi.CC)16, 127)
    .Note(60, 127, 480)
    .CC((Midi.CC)16, 0)
    .GS(0x40, 0x21, 0x41, [64])
    .Wait(480);

// Back to sine wave
test.ProgramChange(8, 1, 80);
test.Text("AMPLITUDE CONTROL Test");
test.Text("CC#7 - baseline (square gain)")
    .NoteOn(60, 127)
    .SweepCC(Midi.CC.MainVolume, 0, 127, 60)
    .NoteOff(60)
    .Wait(480);

SweepGSMatrix("CC1 AMPLITUDE CONTROL -100.0 [%]", 0x42, 0);
SweepGSMatrix("CC1 AMPLITUDE CONTROL +100.0 [%]", 0x42, 127);

test.CC(Midi.CC.MainVolume, 0);
SweepGSMatrix("CC1 AMPLITUDE CONTROL +100.0 [%], CC#7 = 0", 0x42, 127);

test.Make();