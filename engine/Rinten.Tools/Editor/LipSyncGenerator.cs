using NativeEngine;
using Rinten;
using System;
using System.Collections.Generic;

namespace Editor;

/// <summary>Generates face lipsync visemes from audio.</summary>
public static class LipSyncGenerator
{
	//The 15 visemes, in the order of Visemes.MorphNames. MorphNames match the viseme morphs on a model.
	static readonly string[] MorphNames =
	{
		"viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH", "viseme_DD",
		"viseme_KK", "viseme_CH", "viseme_SS", "viseme_NN", "viseme_RR",
		"viseme_AA", "viseme_E", "viseme_I", "viseme_O", "viseme_U",
	};

	static readonly string[] Labels =
	{
		"sil", "PP", "FF", "TH", "DD", "KK", "CH", "SS", "NN", "RR", "AA", "E", "I", "O", "U",
	};

	//Samples of audio fed to the analyzer per step. Smaller = finer timing resolution.
	const int HopSize = 512;

	//A viseme must be at least this strong to count as active rather than silence.
	const float ActiveThreshold = 0.3f;

	/// <summary>Number of visemes.</summary>
	public static int Count => MorphNames.Length;

	/// <summary>Model morph name for a viseme, e.g. "viseme_PP".</summary>
	public static string MorphName( int viseme ) => MorphNames[viseme];

	/// <summary>Short display label for a viseme, e.g. "PP".</summary>
	public static string Label( int viseme ) => Labels[viseme];

	/// <summary>Visemes over time from mono samples spanning duration, using the realtime estimator; no silence</summary>
	public static List<VisemeFrame> Generate( short[] samples, float duration )
	{
		var frames = new List<VisemeFrame>();

		if ( samples is null || samples.Length == 0 || duration <= 0.0f )
			return frames;

		var rate = samples.Length / duration;
		var estimator = new Rinten.Audio.VisemeEstimator( rate );

		var block = new float[HopSize];
		var visemes = new float[Count];
		var steps = new List<(float Time, int Viseme)>( samples.Length / HopSize + 1 );

		for ( var start = 0; start < samples.Length; start += HopSize )
		{
			var length = Math.Min( HopSize, samples.Length - start );

			for ( var i = 0; i < length; i++ )
				block[i] = samples[start + i] / 32768.0f;

			estimator.Process( block.AsSpan( 0, length ) );
			estimator.Weights.CopyTo( visemes );

			//Stamped at the middle of the block it was heard in.
			var time = ( start + ( length * 0.5f ) ) / rate;
			steps.Add( (time, Dominant( visemes )) );
		}

		Merge( steps, frames );

		return frames;
	}

	//Loudest non-silent viseme, or silence (0) if nothing is strong enough.
	static int Dominant( float[] visemes )
	{
		int best = 0;
		float bestWeight = 0.0f;

		for ( int v = 1; v < visemes.Length; v++ )
		{
			if ( visemes[v] > bestWeight )
			{
				bestWeight = visemes[v];
				best = v;
			}
		}

		return bestWeight >= ActiveThreshold ? best : 0;
	}

	//Merge runs of the same viseme into frames, dropping silence.
	static void Merge( List<(float Time, int Viseme)> samples, List<VisemeFrame> frames )
	{
		if ( samples.Count == 0 )
			return;

		int runViseme = samples[0].Viseme;
		float runStart = samples[0].Time;

		//One past the end acts as a virtual sample that closes the final run.
		for ( int i = 1; i <= samples.Count; i++ )
		{
			var viseme = i < samples.Count ? samples[i].Viseme : -1;
			var time = i < samples.Count ? samples[i].Time : samples[^1].Time;

			if ( viseme == runViseme )
				continue;

			if ( runViseme > 0 && time > runStart )
			{
				frames.Add( new VisemeFrame
				{
					Viseme = runViseme,
					StartTime = runStart,
					EndTime = time,
				} );
			}

			runViseme = viseme;
			runStart = time;
		}
	}
}
