using System;
using System.IO;
using Rinten.Audio;

namespace Editor;

//Bakes an audio file's .meta settings (loop, trim, volume) into a _c wav; none if nothing changes
internal static class SoundBake
{
	//What the .meta says to do, with every key missing read as "leave it alone".
	internal readonly record struct Settings(
		bool Loop = false, float LoopStart = 0, float LoopEnd = 0,
		bool ForceMono = false, bool TrimSilence = false, bool Normalize = false, float Gain = 0,
		int Rate = 0, bool Compress = false )
	{
		/// <summary>Whether anything here makes the sound other than its source.</summary>
		public bool ChangesAnything( int sourceRate ) =>
			Loop || ForceMono || TrimSilence || Normalize || MathF.Abs( Gain ) > 0.001f || Compress
			|| ( Rate > 0 && Rate != sourceRate );
	}

	//The settings kept for one asset.
	internal static Settings Read( Asset asset )
	{
		var meta = asset?.MetaData;
		if ( meta is null ) return default;

		return new Settings(
			meta.Get( "loop", false ),
			meta.Get( "start", 0.0f ),
			meta.Get( "end", 0.0f ),
			meta.Get( "forceMono", false ),
			meta.Get( "trimSilence", false ),
			meta.Get( "normalize", false ),
			meta.Get( "gain", 0.0f ),
			meta.Get( "rate", 0 ),
			meta.Get( "compress", false ) );
	}

	//Whether the bake is missing, older than the source or its settings, or there when nothing asks for one.
	internal static bool IsStale( Asset asset, string source )
	{
		try
		{
			var baked = Rinten.CompiledPaths.Built( source );
			var meta = asset.MetaData?.FilePath;
			var hasMeta = meta is not null && File.Exists( meta );

			if ( !File.Exists( baked ) )
			{
				//Nothing to bake is the usual case, and is said without reading the sound: a sound with no settings, or settings all left alone.
				if ( !hasMeta ) return false;

				var settings = Read( asset );

				if ( settings.ChangesAnything( settings.Rate ) ) return true;

				//A rate and nothing else is only a resample where the source is at some other rate, and only the source says what its rate is.
				return settings.Rate > 0 && CoreSound.Load( source ) is { } sound && sound.SampleRate != settings.Rate;
			}

			var bakedAt = File.GetLastWriteTimeUtc( baked );

			if ( bakedAt < File.GetLastWriteTimeUtc( source ) ) return true;

			return !hasMeta || bakedAt < File.GetLastWriteTimeUtc( meta );
		}
		catch ( IOException )
		{
			return true;
		}
	}

	//Bakes one sound, or takes its bake away where its settings no longer ask for
	//one. False only where the source could not be read or the bake not written.
	internal static bool Build( Asset asset, string source )
	{
		var baked = Rinten.CompiledPaths.Built( source );
		var settings = Read( asset );

		var sound = CoreSound.Load( source );

		if ( sound is null )
		{
			Log.Warning( $"{asset.Path}: could not be read to bake its settings" );
			return false;
		}

		if ( !settings.ChangesAnything( sound.SampleRate ) )
		{
			Remove( asset, baked );
			return true;
		}

		var bytes = Bake( sound, settings );

		if ( bytes is null )
		{
			Log.Warning( $"{asset.Path}: decoded to nothing, so there is nothing to bake" );
			return false;
		}

		try
		{
			//Beside it first and moved over it, so nothing ever reads half a bake.
			var writing = baked + ".tmp";
			File.WriteAllBytes( writing, bytes );
			File.Move( writing, baked, overwrite: true );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( e, $"{asset.Path}: could not write {baked} ({e.Message})" );
			return false;
		}

		Rinten.ResourceReload.Changed( asset.Path );

		return true;
	}

	//The sound with its settings applied, as the wav the game will play - or null where it decoded to nothing.
	internal static byte[] Bake( CoreSound sound, Settings settings )
	{
		var rate = settings.Rate > 0 ? settings.Rate : sound.SampleRate;

		var channels = Decode( sound, rate );

		if ( channels is null || channels.Length == 0 || channels[0].Length == 0 ) return null;

		if ( settings.ForceMono ) channels = [Downmix( channels )];

		var lead = 0;

		if ( settings.TrimSilence ) channels = Trim( channels, out lead );

		if ( settings.Normalize ) Scale( channels, NormalizingGain( channels ) );

		if ( MathF.Abs( settings.Gain ) > 0.001f ) Scale( channels, MathF.Pow( 10.0f, settings.Gain / 20.0f ) );

		var frames = channels[0].Length;

		//The loop is set in seconds of the source, and the start of the sound may just have been trimmed off:
		//counted in the bake's own frames, less whatever went. An end of nought is the end of the sound.
		(int Start, int End)? loop = null;

		if ( settings.Loop && frames > 0 )
		{
			var start = Math.Clamp( (int)( settings.LoopStart * rate ) - lead, 0, frames - 1 );
			var end = settings.LoopEnd > 0.0f
				? Math.Clamp( (int)( settings.LoopEnd * rate ) - lead, start + 1, frames )
				: frames;

			loop = (start, end);
		}

		return settings.Compress
			? WavWriter.ImaAdpcm( channels, rate, loop )
			: WavWriter.Pcm16( channels, rate, loop );
	}

	//Takes a bake away, so the source plays again.
	private static void Remove( Asset asset, string baked )
	{
		try
		{
			if ( !File.Exists( baked ) ) return;

			File.Delete( baked );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( e, $"{asset.Path}: could not remove {baked} ({e.Message})" );
			return;
		}

		Rinten.ResourceReload.Changed( asset.Path );
	}

	//The whole sound, one array a channel, at the rate asked for - through a sampler, which is how the game plays it and which resamples on the way.
	private static unsafe float[][] Decode( CoreSound sound, int rate )
	{
		var sampler = new CoreAudioSampler( sound, rate, looping: false );

		try
		{
			if ( !sampler.IsValid ) return null;

			var count = sampler.ChannelCount;
			var chunks = new System.Collections.Generic.List<float[]>[count];
			for ( var c = 0; c < count; c++ ) chunks[c] = new();

			const int Chunk = 4096;

			var buffers = new float[count][];
			for ( var c = 0; c < count; c++ ) buffers[c] = GC.AllocateArray<float>( Chunk, pinned: true );

			var pointers = stackalloc float*[count];

			for ( var c = 0; c < count; c++ )
				pointers[c] = (float*)System.Runtime.CompilerServices.Unsafe.AsPointer( ref buffers[c][0] );

			var total = 0;

			while ( true )
			{
				var read = sampler.Read( pointers, count, Chunk, 1.0f );
				if ( read <= 0 ) break;

				for ( var c = 0; c < count; c++ ) chunks[c].Add( buffers[c].AsSpan( 0, read ).ToArray() );

				total += read;

				if ( sampler.Finished ) break;
			}

			//Drop the resampler's one-frame startup delay so loop points don't land a frame early
			var delay = Math.Min( total, (int)Math.Round( rate / (double)sound.SampleRate ) );

			var result = new float[count][];

			for ( var c = 0; c < count; c++ )
			{
				var whole = new float[total];
				var at = 0;

				foreach ( var chunk in chunks[c] )
				{
					chunk.CopyTo( whole, at );
					at += chunk.Length;
				}

				result[c] = whole.AsSpan( delay ).ToArray();
			}

			return result;
		}
		finally
		{
			sampler.Dispose();
		}
	}

	private static float[] Downmix( float[][] channels )
	{
		var frames = channels[0].Length;
		var mono = new float[frames];

		foreach ( var channel in channels )
		{
			for ( var i = 0; i < frames; i++ ) mono[i] += channel[i];
		}

		for ( var i = 0; i < frames; i++ ) mono[i] /= channels.Length;

		return mono;
	}

	//Anything quieter than this, in every channel, is silence: -60 dBFS.
	private const float SilenceThreshold = 0.001f;

	//The sound from its first sound to its last, and how many frames went off the front.
	private static float[][] Trim( float[][] channels, out int lead )
	{
		var frames = channels[0].Length;

		bool Loud( int frame )
		{
			foreach ( var channel in channels )
			{
				if ( MathF.Abs( channel[frame] ) > SilenceThreshold ) return true;
			}

			return false;
		}

		var first = 0;
		while ( first < frames && !Loud( first ) ) first++;

		//All of it silence: kept as it is rather than trimmed to nothing, which is a sound that cannot be played at all.
		if ( first == frames )
		{
			lead = 0;
			return channels;
		}

		var last = frames - 1;
		while ( last > first && !Loud( last ) ) last--;

		lead = first;

		var result = new float[channels.Length][];

		for ( var c = 0; c < channels.Length; c++ )
			result[c] = channels[c].AsSpan( first, last - first + 1 ).ToArray();

		return result;
	}

	//Where normalizing aims: -18 dBFS of average loudness.
	private const float TargetRms = 0.126f;

	//And how loud the loudest moment may come out of it: -1 dBFS.
	private const float PeakCeiling = 0.891f;

	//The gain that brings the sound's average loudness to a standard level, held back where that would push its peaks past the ceiling.
	private static float NormalizingGain( float[][] channels )
	{
		double sum = 0;
		long count = 0;
		var peak = 0.0f;

		foreach ( var channel in channels )
		{
			foreach ( var sample in channel )
			{
				sum += sample * (double)sample;
				peak = MathF.Max( peak, MathF.Abs( sample ) );
			}

			count += channel.Length;
		}

		if ( count == 0 || peak <= 0.0f ) return 1.0f;

		var rms = (float)Math.Sqrt( sum / count );
		if ( rms <= 0.0f ) return 1.0f;

		return MathF.Min( TargetRms / rms, PeakCeiling / peak );
	}

	private static void Scale( float[][] channels, float gain )
	{
		foreach ( var channel in channels )
		{
			for ( var i = 0; i < channel.Length; i++ ) channel[i] *= gain;
		}
	}
}
