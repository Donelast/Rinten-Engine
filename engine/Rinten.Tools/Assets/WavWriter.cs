using System;
using System.IO;

namespace Editor;

//Writes 16-bit PCM or IMA ADPCM WAV, with the loop in a "smpl" chunk the loader already reads
internal static class WavWriter
{
	private const short FormatPcm = 1;
	private const short FormatImaAdpcm = 0x11;

	/// <summary>16 bit PCM, interleaved.</summary>
	public static byte[] Pcm16( float[][] channels, int rate, (int Start, int End)? loop )
	{
		var count = channels.Length;
		var frames = channels[0].Length;

		var data = new byte[(long)frames * count * 2];
		var at = 0;

		for ( var frame = 0; frame < frames; frame++ )
		{
			for ( var c = 0; c < count; c++ )
			{
				var sample = ToShort( channels[c][frame] );
				data[at++] = (byte)sample;
				data[at++] = (byte)( sample >> 8 );
			}
		}

		var blockAlign = (short)( count * 2 );

		return Riff( w =>
		{
			Chunk( w, "fmt ", f =>
			{
				f.Write( FormatPcm );
				f.Write( (short)count );
				f.Write( rate );
				f.Write( rate * blockAlign );
				f.Write( blockAlign );
				f.Write( (short)16 );
			} );

			Chunk( w, "data", d => d.Write( data ) );

			if ( loop is { } l ) Sampler( w, rate, l );
		} );
	}

	/// <summary>IMA ADPCM: four bits a sample, in blocks the decoder can seek between.</summary>
	public static byte[] ImaAdpcm( float[][] channels, int rate, (int Start, int End)? loop )
	{
		var count = channels.Length;
		var frames = channels[0].Length;

		//The usual block: 512 bytes a channel. Four of them are each channel's header - its
		//first sample and where the step starts - and every byte after holds two samples.
		var blockAlign = 512 * count;
		var perBlock = ( ( blockAlign - ( 4 * count ) ) * 8 / ( 4 * count ) ) + 1;
		var blocks = ( frames + perBlock - 1 ) / perBlock;

		var data = new byte[(long)blocks * blockAlign];

		var predictors = new int[count];
		var indices = new int[count];

		//Pre-run the opening to pick the initial ADPCM step, so the start doesn't swell in from silence
		for ( var c = 0; c < count; c++ )
		{
			var warm = (int)Sample( channels[c], 0 );

			for ( var frame = 1; frame < Math.Min( frames, 64 ); frame++ )
				Encode( Sample( channels[c], frame ), ref warm, ref indices[c] );
		}

		for ( var block = 0; block < blocks; block++ )
		{
			var first = block * perBlock;
			var at = block * blockAlign;

			//The header: the block's first sample, as it is, and the step the rest are coded from.
			for ( var c = 0; c < count; c++ )
			{
				var sample = Sample( channels[c], first );

				predictors[c] = sample;

				data[at++] = (byte)sample;
				data[at++] = (byte)( sample >> 8 );
				data[at++] = (byte)indices[c];
				data[at++] = 0;
			}

			//Then eight samples a channel at a time, four bytes each, the channels taking turns - low nibble first.
			for ( var group = 0; group < ( perBlock - 1 ) / 8; group++ )
			{
				for ( var c = 0; c < count; c++ )
				{
					for ( var pair = 0; pair < 4; pair++ )
					{
						var frame = first + 1 + ( group * 8 ) + ( pair * 2 );

						var low = Encode( Sample( channels[c], frame ), ref predictors[c], ref indices[c] );
						var high = Encode( Sample( channels[c], frame + 1 ), ref predictors[c], ref indices[c] );

						data[at++] = (byte)( low | ( high << 4 ) );
					}
				}
			}
		}

		return Riff( w =>
		{
			Chunk( w, "fmt ", f =>
			{
				f.Write( FormatImaAdpcm );
				f.Write( (short)count );
				f.Write( rate );
				f.Write( (int)( (long)rate * blockAlign / perBlock ) );
				f.Write( (short)blockAlign );
				f.Write( (short)4 );
				f.Write( (short)2 );
				f.Write( (short)perBlock );
			} );

			//How many frames there really are: the last block is padded out with silence, and this is how the decoder knows where to stop.
			Chunk( w, "fact", f => f.Write( frames ) );

			Chunk( w, "data", d => d.Write( data ) );

			if ( loop is { } l ) Sampler( w, rate, l );
		} );
	}

	private static short ToShort( float sample ) => (short)Math.Clamp( (int)MathF.Round( sample * 32767.0f ), short.MinValue, short.MaxValue );

	//A sample, or silence past the end - what pads the last block out.
	private static short Sample( float[] channel, int frame ) => frame < channel.Length ? ToShort( channel[frame] ) : (short)0;

	private static readonly int[] StepTable =
	{
		7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
		50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230,
		253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
		1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327,
		3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
		11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
		32767,
	};

	private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8 };

	//One sample as a nibble, moving the predictor and the step exactly as the decoder will - so the two stay in step for the whole block.
	private static int Encode( int sample, ref int predictor, ref int index )
	{
		var step = StepTable[index];
		var diff = sample - predictor;

		var nibble = 0;

		if ( diff < 0 )
		{
			nibble = 8;
			diff = -diff;
		}

		var delta = step >> 3;

		if ( diff >= step ) { nibble |= 4; diff -= step; delta += step; }
		step >>= 1;
		if ( diff >= step ) { nibble |= 2; diff -= step; delta += step; }
		step >>= 1;
		if ( diff >= step ) { nibble |= 1; delta += step; }

		predictor = Math.Clamp( ( nibble & 8 ) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue );
		index = Math.Clamp( index + IndexTable[nibble & 7], 0, StepTable.Length - 1 );

		return nibble;
	}

	//The loop, the way a sampler writes it: one loop, forward, from its start to its last frame inclusive.
	private static void Sampler( BinaryWriter w, int rate, (int Start, int End) loop )
	{
		Chunk( w, "smpl", s =>
		{
			s.Write( 0 );                               //manufacturer
			s.Write( 0 );                               //product
			s.Write( (int)( 1_000_000_000L / rate ) );  //sample period, in nanoseconds
			s.Write( 60 );                              //unity note: middle C
			s.Write( 0 );                               //pitch fraction
			s.Write( 0 );                               //SMPTE format
			s.Write( 0 );                               //SMPTE offset
			s.Write( 1 );                               //loops
			s.Write( 0 );                               //sampler data

			s.Write( 0 );                               //cue point id
			s.Write( 0 );                               //type: forward
			s.Write( loop.Start );
			s.Write( loop.End - 1 );
			s.Write( 0 );                               //fraction
			s.Write( 0 );                               //play count: for ever
		} );
	}

	private static byte[] Riff( Action<BinaryWriter> body )
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter( stream );

		w.Write( "RIFF"u8 );
		w.Write( 0 );
		w.Write( "WAVE"u8 );

		body( w );

		w.Flush();

		stream.Position = 4;
		w.Write( (int)( stream.Length - 8 ) );
		w.Flush();

		return stream.ToArray();
	}

	private static void Chunk( BinaryWriter w, string id, Action<BinaryWriter> body )
	{
		w.Write( System.Text.Encoding.ASCII.GetBytes( id ) );

		var sizeAt = w.BaseStream.Position;
		w.Write( 0 );

		body( w );
		w.Flush();

		var size = w.BaseStream.Position - sizeAt - 4;

		//Every chunk starts on an even byte.
		if ( ( size & 1 ) != 0 ) w.Write( (byte)0 );

		var end = w.BaseStream.Position;

		w.BaseStream.Position = sizeAt;
		w.Write( (int)size );
		w.BaseStream.Position = end;
	}
}
