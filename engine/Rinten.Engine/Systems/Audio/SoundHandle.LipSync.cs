using System.Threading;
using System.Collections.ObjectModel;

namespace Rinten;

public partial class SoundHandle
{
	/// <summary>Access lipsync processing.</summary>
	public LipSyncAccessor LipSync { get; private init; } = new();

	//Viseme weights from the mixing thread, double-buffered so readers always see one whole set
	public class LipSyncAccessor
	{
		/// <summary>A list of 15 lipsync viseme weights. Requires <see cref="Enabled"/> to be true.</summary>
		public IReadOnlyList<float> Visemes => Volatile.Read( ref _live )?.Front.ReadOnly ?? (IReadOnlyList<float>)Array.Empty<float>();

		//The viseme weights as a raw array for the engine's per-frame readers - no allocation, no interface dispatch. Null until Enabled is set.
		internal float[] VisemeWeights => Volatile.Read( ref _live )?.Front.Weights;

		/// <summary>Count from start of recognition.</summary>
		public int FrameNumber { get; private set; }

		/// <summary>Frame delay in milliseconds.</summary>
		public int FrameDelay { get; private set; }

		//A guess from rhythm and breath - syllables bursting out evenly a few times a second with air in them - rather than a trained model of a laugh, so fast breathy speech scores a little too.
		/// <summary>Laughter score for the current audio frame.</summary>
		public float LaughterScore { get; private set; }

		/// <summary>Enables lipsync processing.</summary>
		public bool Enabled
		{
			get => _enabled;
			set
			{
				if ( _enabled == value )
					return;

				if ( value )
				{
					EnableLipSync();
				}
				else
				{
					DisableLipSync();
				}
			}
		}

		//Volatile so the mix thread's ProcessLipSync guard fires without a lock.
		volatile bool _enabled;

		//One set of weights as the main thread sees it, with the read-only view of it made once rather than on every ask.
		sealed class Published
		{
			public readonly float[] Weights = new float[Rinten.Visemes.Count];
			public readonly ReadOnlyCollection<float> ReadOnly;

			public Published() => ReadOnly = Array.AsReadOnly( Weights );
		}

		//Everything one run of lip sync works with, replaced whole when it is switched on. The mixing thread reads the reference once per
		//buffer, so turning lip sync off and on while it is halfway through a buffer leaves it finishing with the old set rather than a mixture.
		sealed class Live
		{
			public readonly Audio.VisemeEstimator Estimator = new( Audio.AudioEngine.SamplingRate );
			public Published Front = new();
			public Published Back = new();
		}

		Live _live;

		internal LipSyncAccessor()
		{
		}

		private void EnableLipSync()
		{
			if ( _enabled ) return;

			//A fresh estimator for every run, made here on the main thread: the voice it learned the loudness
			//of last time may not be this one, and the mixing thread must not be the one allocating it.
			var live = new Live();
			live.Front.Weights[0] = 1.0f;

			Volatile.Write( ref _live, live );

			FrameNumber = 0;
			FrameDelay = live.Estimator.DelayMilliseconds;
			LaughterScore = 0;

			//Last, so the mixing thread never sees it on without the state above.
			_enabled = true;
		}

		internal void DisableLipSync()
		{
			if ( !_enabled ) return;

			//The mix thread's guard reads this first; whatever it is halfway through finishes against the Live
			//it already holds, which nothing frees - so there is nothing to wait for before letting go here.
			_enabled = false;

			//The last weights stay readable, as they were when it stopped.
			FrameNumber = 0;
			FrameDelay = 0;
			LaughterScore = 0;
		}

		//Listen to one buffer of this sound. On the mixing thread, once per mix buffer; allocates nothing.
		internal void ProcessLipSync( Audio.MixBuffer buffer )
		{
			if ( !_enabled )
				return;

			if ( buffer is null )
				return;

			var live = Volatile.Read( ref _live );
			if ( live is null )
				return;

			var estimator = live.Estimator;
			estimator.Process( buffer.Buffer );

			var back = live.Back;
			estimator.Weights.CopyTo( back.Weights );

			live.Back = live.Front;
			Volatile.Write( ref live.Front, back );

			FrameNumber++;
			LaughterScore = estimator.Laughter;
		}
	}
}
