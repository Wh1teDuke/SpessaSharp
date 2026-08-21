#!/usr/bin/env dotnet

#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

#:project ../../../SpessaSharp/SpessaSharp.csproj
#:include ../MidiTestMaker.cs

using SpessaSharp.MIDI.Utils;

var test = new MidiTestMaker("RPN Fine Tuning Test");

// SC-55 Sine
test.ProgramChange(8, 0, 80);

test.Text("Fine Tuning");
var pitch = 0;
while (pitch < 16_383) 
{
    test.RPN(1, pitch).Note(60, 120, 120).Wait(120);
    pitch = Math.Min(16_383, pitch + 250);
}

test.Flush();

test.Modify(new MidiEditor.Options(
    Channels: MidiEditor.ChannelModifications(
        // Test handling relative tuning editing
        (0, new MidiEditor.ChannelModification { FineTune = -56 }.Replace())
    )
));

test.Make();