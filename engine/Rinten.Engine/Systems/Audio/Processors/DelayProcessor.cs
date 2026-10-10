namespace Rinten.Audio;

[Expose]
public sealed class DelayProcessor : AudioProcessor<DelayProcessor.State>
{
	/// <summary>How long after the sound its echo comes, in seconds.</summary>
	[Range( 0, 1 )]
	public float Delay { get; set; } = 0.2f;

	/// <summary>How loud the echo is against the sound.</summary>
	[Range( 0, 1 )]
	public float Volume { get; set; } = 0.5f;

	public class State : ListenerState
	{
		internal float Delay { get; set; } = 0.2f;
		internal float Volume { get; set; } = 0.5f;

		private readonly DelayEffect effect = new();

		internal void Process( MultiChannelBuffer input, MultiChannelBuffer output )
			=> effect.Apply( Delay, Volume, input, output );
	}

	internal override void Process( MultiChannelBuffer input, MultiChannelBuffer output )
	{
		//The processor's settings, into the state that carries the echo's tail - which is the listener's own, and outlives any one buffer.
		CurrentState.Delay = Delay;
		CurrentState.Volume = Volume;
		CurrentState.Process( input, output );
	}
}
