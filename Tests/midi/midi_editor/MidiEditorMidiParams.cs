#!/usr/bin/env -S dotnet run

#:project ../../../SpessaSharp/SpessaSharp.csproj

using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;

using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;

using SpessaSharp.Utils;

if (args.Length != 2)
{
    Console.WriteLine("Usage: dotnet run MidiEditorMidiParams.cs -- <midi input path> <midi output path>");
    Environment.Exit(1);
}

var mid = new FileInfo(args[0]);
var midi = Midi.From(mid);

SpessaLog.SetLogLevel(true, true);

var channels = new Dictionary<
    int, 
    MidiEditor.Parameter<MidiEditor.ChannelModification>>
{
    [0] = MidiEditor
        .Parameter<MidiEditor.ChannelModification>.OfReplace(
        new MidiEditor.ChannelModification
        {
            // Test these as they are relative
            KeyShift = -1,
            FineTune = 20,
            MidiParameters = InitChanMods(
                (ChannelMidiParameter.Type.VelocitySenseOffset, 100),
                (ChannelMidiParameter.Type.VelocitySenseDepth, 0),
                (ChannelMidiParameter.Type.PitchWheel, 6_000),
                (ChannelMidiParameter.Type.PitchWheelRange, 14f),
                (ChannelMidiParameter.Type.FineTune, -67f)),
        }
    )
};

var random = new Random();
for (var i = 1; i < 16; i++)
{
    // -100 to 99 cents
    var tune = float.Floor(random.NextSingle() * 200) - 100;
    Console.WriteLine($"Testing relative tuning ONLY on {i}. Cents: {tune}");
    channels[i] = MidiEditor
        .Parameter<MidiEditor.ChannelModification>.OfReplace(
        new MidiEditor.ChannelModification
        {
            KeyShift = 0,
            FineTune = tune,
        });
}

midi.Modify(new MidiEditor.Options
{
    MidiParams = InitGlobalMods(
        (GlobalMidiParameter.Type.KeyShift, -2),
        (GlobalMidiParameter.Type.FineTune, 30f),
        (GlobalMidiParameter.Type.Gain, 0.7f),
        (GlobalMidiParameter.Type.Pan, -0.7f)),
    Channels = channels,
});

File.WriteAllBytes(args[1], midi.Write());

return;

Dictionary<
    ChannelMidiParameter.Type,
    MidiEditor.Parameter<ChannelMidiParameter>> InitChanMods(
    params ReadOnlySpan<ChannelMidiParameter> args)
{
    var result = new Dictionary<
        ChannelMidiParameter.Type, 
        MidiEditor.Parameter<ChannelMidiParameter>>();

    foreach (var arg in args)
        result[arg.PType] = MidiEditor.Parameter<
            ChannelMidiParameter>.OfReplace(arg);
    
    return result;
}

Dictionary<
    GlobalMidiParameter.Type,
    MidiEditor.Parameter<GlobalMidiParameter>> InitGlobalMods(
    params ReadOnlySpan<GlobalMidiParameter> args)
{
    var result = new Dictionary<
        GlobalMidiParameter.Type, 
        MidiEditor.Parameter<GlobalMidiParameter>>();

    foreach (var arg in args)
        result[arg.PType] = MidiEditor.Parameter<
            GlobalMidiParameter>.OfReplace(arg);
    
    return result;
}