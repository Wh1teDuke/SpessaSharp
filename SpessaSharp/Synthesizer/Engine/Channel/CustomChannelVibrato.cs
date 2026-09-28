namespace SpessaSharp.Synthesizer.Engine.Channel;

/// <summary>
/// Represents the custom channel vibrato of the channel.
/// </summary>
/// <param name="Depth">Vibrato depth, in cents.</param>
/// <param name="Delay">Vibrato delay, in seconds from the voice's start time.</param>
/// <param name="Rate">Vibrato rate in Hertz.</param>
public record struct CustomChannelVibrato(
    float Depth, float Delay, float Rate);