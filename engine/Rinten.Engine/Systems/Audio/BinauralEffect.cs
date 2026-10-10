namespace Rinten.Audio;

//Per-listener binaural handle for a sound; HeadEffect does the work, nothing here needs freeing
class BinauralEffect : IDisposable
{
	private readonly HeadEffect head = new();

	public void Dispose()
	{
	}

	internal void Apply( Vector3 direction, float spatialBlend, bool useNearestInterpolation, MultiChannelBuffer input, MultiChannelBuffer output )
	{
		output.Silence();
		head.Apply( direction, spatialBlend, input, output );
	}
}
