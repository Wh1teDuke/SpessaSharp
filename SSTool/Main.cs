using System.Runtime;
using SSTool.Util;
using SpessaSharp.SoundBank;
using SSTool.Cmd;

GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
Thread.CurrentThread.Name = "Main";
SoundBank.Vorbis.Decoder = Etc.DecodeVorbis;
//SoundBank.Vorbis.Encoder = Etc.EncodeVorbis;

try
{ ArgCommands.Eval(args); }
finally
{ Console.CursorVisible = true; }