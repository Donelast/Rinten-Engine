namespace Rinten;

//Lipsync state of a speaking model (voice handle, viseme track, morph slots); only made when speaking
sealed class ModelVoice
{
	/// <summary>Drive viseme morphs from speaking voices. Turn off to check whether facial movement is coming from lipsync or something else.</summary>
	[ConVar( "snd_voice_lipsync" )]
	internal static bool LipSyncEnabled { get; set; } = true;

	//Realtime analysis is noisy - ignore viseme weights below this, so silence and the noise floor don't twitch the face
	const float RealtimeDeadzone = 0.15f;

	readonly SkinnedModelRenderer _renderer;

	SoundHandle _handle;
	SoundFile _soundFile;

	//The model whose viseme morphs we're driving, so we can let go of them when the voice stops or the
	//model changes. The morph weights themselves live on Model.Morphs, shared by everything that lipsyncs.
	Model _morphModel;

	//How fast morphs chase their target weight. The LipSync component pipes its smoothing knob through here.
	internal float MorphSmoothTime = 0.1f;

	//Realtime analysis produces weaker weights than authored tracks, scale them up. The LipSync component pipes its scale knob through here.
	internal float RealtimeScale = 1.5f;

	internal ModelVoice( SkinnedModelRenderer renderer )
	{
		_renderer = renderer;
	}

	//The sound being spoken. While this is playing we drive the model's viseme morphs from it, so the mouth moves along with the words.
	internal SoundHandle Handle
	{
		get => _handle;
		set
		{
			if ( _handle == value )
				return;

			_handle?.Stop();
			_handle?.Dispose();
			_handle = default;

			_handle = value;
			_soundFile = null;
		}
	}

	//True if there's a voice to lipsync, or morphs we're still driving that need letting go of. The animation system skips us entirely otherwise.
	internal bool Active => _handle is not null || _morphModel is not null;

	//Start speaking handle, whose visemes - if any were authored - come from soundFile. Said in one call rather than as two writes because setting the
	//handle forgets the file: a new voice is a new sound, and the track belonging to the last one would be sampled against this one's playback position.
	internal void Speak( SoundHandle handle, SoundFile soundFile )
	{
		Handle = handle;
		_soundFile = soundFile;
	}

	//Drive viseme morphs from the current voice. Called on the main thread each frame before animation
	//updates, so the weights we set here get composited in the same frame's bone/morph update.
	internal void Update()
	{
		//Voice stopped - ease the mouth back to the animation and let go
		if ( !_handle.IsValid() || _handle.Finished )
		{
			Handle = null;

			if ( _morphModel is not null )
			{
				var morphModel = _renderer.SceneModel;
				if ( morphModel.IsValid() )
					ClearMorphs( morphModel );
				else
					_morphModel = null;
			}

			return;
		}

		var sceneModel = _renderer.SceneModel;
		if ( !sceneModel.IsValid() )
			return;

		//Lipsync turned off - let go of the face but keep the voice, and stop paying for realtime analysis on the mix thread
		if ( !LipSyncEnabled )
		{
			_handle.LipSync.Enabled = false;
			ClearMorphs( sceneModel );
			return;
		}

		var model = _renderer.Model;
		if ( model is null || model.MorphCount == 0 )
			return;

		Span<float> weights = stackalloc float[Visemes.Count];
		var scale = 1.0f;

		var soundFile = _soundFile;
		var track = soundFile?.Visemes;

		if ( track is not null )
		{
			//The sound has an authored viseme track, sample it at the playback position
			track.Sample( _handle.Time, weights );
		}
		else if ( soundFile is null || soundFile.IsValidForPlayback )
		{
			//The sound is loaded and has no viseme track, analyze the audio as it plays
			_handle.LipSync.Enabled = true;

			var visemes = _handle.LipSync.VisemeWeights;
			for ( var i = 0; i < weights.Length && i < visemes.Length; i++ )
			{
				//Gate out analysis noise, so silence doesn't twitch the face
				var weight = visemes[i];
				weights[i] = weight > RealtimeDeadzone ? weight : 0.0f;
			}

			scale = RealtimeScale;
		}
		else
		{
			//The sound is still loading, so we don't yet know if it has a track - and it isn't
			//audible yet either. Hold off rather than kicking off realtime analysis we'd abandon.
			return;
		}

		ApplyMorphs( sceneModel, weights, scale );
	}

	void ApplyMorphs( SceneModel sceneModel, ReadOnlySpan<float> visemeWeights, float scale )
	{
		_morphModel = _renderer.Model;

		sceneModel.Morphs.ApplyVisemes( visemeWeights, scale, MorphSmoothTime );
	}

	//Fade the morphs we've been driving back to their animated values
	void ClearMorphs( SceneModel sceneModel )
	{
		if ( _morphModel is null )
			return;

		foreach ( var morphIndex in _morphModel.Morphs.VisemeMorphIndices )
		{
			sceneModel.Morphs.Reset( morphIndex, MorphSmoothTime );
		}

		_morphModel = null;
	}

	//Let go of any voice morphs instantly. Called when the model is about to change - our morph indices are in the old model's morph space, and the
	//scene object keeps its override slots across the swap, so anything we leave behind (even a fade) would drive random morphs on the new model.
	internal void ReleaseMorphs()
	{
		if ( _morphModel is null || _morphModel == _renderer.Model )
			return;

		var sceneModel = _renderer.SceneModel;
		if ( sceneModel.IsValid() )
		{
			foreach ( var morphIndex in _morphModel.Morphs.VisemeMorphIndices )
			{
				sceneModel.Morphs.Reset( morphIndex );
			}
		}

		_morphModel = null;
	}
}
