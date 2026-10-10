namespace Rinten.Audio;

//An instance of a DspPreset. The actual processor creates one of these from a DspPreset, and then uses it to process the audio buffers.
class DspInstance
{
	private readonly DspChain chain;

	internal DspInstance( DspPreset source, int channels )
	{
		chain = new DspChain( source.Processors, channels );
	}

	public void Process( MixBuffer input, MixBuffer output, int channel ) => chain.Process( input, output, channel );

	internal void Dispose()
	{
	}
}
