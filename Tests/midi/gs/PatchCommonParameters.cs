#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;

var test = new MidiTestMaker("GS Patch Common Parameters");

// Sine wave, no reverb
test.ProgramChange(8, 1, 80).CC(Midi.CC.ReverbDepth, 0);

test.Text("MASTER TUNE");

for (var tune = -100; tune <= 100; tune += 20) 
{
    test.Set((GlobalMidiParameter.Type.FineTune, (float)tune))
        .Text($"Fine tune = {tune} cents")
        .Note(60, 120, 120)
        .Wait(120);
}
test.Set((GlobalMidiParameter.Type.FineTune, 0f));

test.Wait(960).Text("MASTER VOLUME");

test.NoteOn(60, 127);

for (var volume = 0; volume <= 127; volume += 1) 
{
    test.Set((GlobalMidiParameter.Type.Volume, volume / 127f))
        .Text($"Master volume = {volume}")
        .Wait(20);
}
test.NoteOff(60);
test.Set((GlobalMidiParameter.Type.Volume, 1f));

test.Wait(960).Text("MASTER KEY-SHIFT");

for (var shift = -24; shift <= 24; shift += 6) 
{
    test.Set((GlobalMidiParameter.Type.KeyShift, shift))
        .Text($"Key shift = {shift} semitones")
        .Note(60, 120, 120)
        .Wait(120);
}
test.Set((GlobalMidiParameter.Type.KeyShift, 0));

test.Wait(960).Text("MASTER PAN");

for (var pan = -100; pan <= 100; pan += 10) 
{
    test.Set((GlobalMidiParameter.Type.Pan, pan / 100f))
    .Text($"Pan = {pan}")
    .Note(60, 120, 120)
    .Wait(120);
}
test.Set((GlobalMidiParameter.Type.Pan, 0f));

test.Make();