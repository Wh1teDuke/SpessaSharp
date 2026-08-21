#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;

var test = new MidiTestMaker("Velocity Sense Depth + Offset");

test.CC(Midi.CC.ReverbDepth, 0).ProgramChange(8, 1, 80);

void VelocitySweep() 
{
    test.Note(60, 1, 240)
        .Wait(120)
        .Note(60, 16, 240)
        .Wait(120)
        .Note(60, 32, 240)
        .Wait(120)
        .Note(60, 48, 240)
        .Wait(120)
        .Note(60, 64, 240)
        .Wait(120)
        .Note(60, 80, 240)
        .Wait(120)
        .Note(60, 96, 240)
        .Wait(120)
        .Note(60, 112, 240)
        .Wait(120)
        .Note(60, 127, 240)
        .Wait(480);
}

void SenseTest(int depth, int offset) 
{
    test.Text($"Depth = {depth}, Offset = {offset}")
        .GS(0x40, 0x11, 0x1a, [depth])
        .GS(0x40, 0x11, 0x1b, [offset]);
    VelocitySweep();
}

test.Text("Regular velocity sweep");
VelocitySweep();
test.Wait(480);

test.Text("Velocity Sense Depth Test");
SenseTest(0, 64);
SenseTest(32, 64);
SenseTest(96, 64);
SenseTest(127, 64);
test.Wait(480);

test.Text("Velocity Sense Offset Test");
SenseTest(64, 0);
SenseTest(64, 32);
SenseTest(64, 96);
SenseTest(64, 127);
test.Wait(480);

test.Text("Testing both");
SenseTest(32, 54);
SenseTest(92, 13);
SenseTest(120, 3);

test.Make();