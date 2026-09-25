namespace SpessaSharp.Synthesizer.Engine.Channel;

/// <summary>
/// </summary>
/// <param name="Depth">Vibrato depth, as gain.</param>
/// <param name="Delay">Vibrato delay, in seconds from the voice's start time.</param>
/// <param name="Rate">Vibrato rate in Hertz.</param>
public record struct CustomChannelVibrato(
    float Depth, float Delay, float Rate);