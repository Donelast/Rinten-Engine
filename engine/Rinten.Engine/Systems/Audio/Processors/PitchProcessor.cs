namespace Rinten.Audio;

[Expose]
public sealed class PitchProcessor : AudioProcessor<PitchProcessor.State>
{
	/// <summary>How much higher, one being as it was.</summary>
	[Range( 0.5f, 2.0f )]
	public float Pitch { get; set; } = 1.0f;

	public class State : ListenerState
	{
		internal float Pitch { get; set; } = 1.0f;

		private readonly PitchEffect effect = new();

		internal void Process( MultiChannelBuffer input, MultiChannelBuffer output )
			=> effect.Apply( Pitch, input, output );
	}

	internal override void Process( MultiChannelBuffer input, MultiChannelBuffer output )
	{
		CurrentState.Pitch = Pitch;
		CurrentState.Process( input, output );
	}
}
