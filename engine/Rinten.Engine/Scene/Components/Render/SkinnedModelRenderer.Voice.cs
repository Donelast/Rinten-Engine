namespace Rinten;

public partial class SkinnedModelRenderer
{
	//Only for models that are actually speaking - see ModelVoice.
	ModelVoice _voice;

	ModelVoice VoiceState => _voice ??= new ModelVoice( this );

	//The sound this model is currently speaking, set by SpeakSound. While this is playing
	//we drive the model's viseme morphs from it, so the mouth moves along with the words.
	internal SoundHandle Voice
	{
		get => _voice?.Handle;
		set
		{
			//Silence on a model that has never spoken is what it already is, and the lipsync component says so every frame for every renderer it is pointed at.
			if ( value is null && _voice is null )
				return;

			VoiceState.Handle = value;
		}
	}

	internal float VoiceMorphSmoothTime
	{
		get => _voice?.MorphSmoothTime ?? 0.1f;
		set => VoiceState.MorphSmoothTime = value;
	}

	internal float VoiceRealtimeScale
	{
		get => _voice?.RealtimeScale ?? 1.5f;
		set => VoiceState.RealtimeScale = value;
	}

	internal bool VoiceActive => _voice is not null && _voice.Active;

	internal void UpdateVoice() => _voice?.Update();

	internal void ReleaseVoiceMorphs() => _voice?.ReleaseMorphs();

	/// <summary>Play a sound as this model's voice. On top of regular sound playback, the model will lipsync to it - if the sound has a viseme track (authored in the sound editor) we play that back, otherwise the audio is analyzed as it plays. Either way the mouth moves, assuming the model has viseme morphs.</summary>
	public SoundHandle SpeakSound( SoundEvent sound, GameObject mouth = null )
	{
		var handle = Sound.Play( sound, out var soundFile );

		if ( handle.IsValid() )
		{
			var host = mouth ?? GameObject;
			handle.Position = host.WorldPosition;
			handle.Parent = host;
			handle.FollowParent = true;
		}

		VoiceState.Speak( handle, soundFile );

		return Voice;
	}

	/// <summary>Play a specific sound file as this model's voice. Useful when you've picked the file yourself - to keep it in sync across the network, or because you generated it. See <see cref="SpeakSound(SoundEvent, GameObject)"/>.</summary>
	public SoundHandle SpeakSound( SoundFile sound, float volume = 1.0f, float pitch = 1.0f, GameObject mouth = null )
	{
		if ( !sound.IsValid() )
			return default;

#pragma warning disable CA2000 //Dispose objects before losing scope
		var handle = Sound.PlayFile( sound, volume, pitch );
#pragma warning restore CA2000
		if ( !handle.IsValid() )
			return default;

		var host = mouth ?? GameObject;
		handle.Position = host.WorldPosition;
		handle.Parent = host;
		handle.FollowParent = true;

		VoiceState.Speak( handle, sound );

		return Voice;
	}
}
