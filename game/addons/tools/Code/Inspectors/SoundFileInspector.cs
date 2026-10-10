namespace Editor.Inspectors;

/// <summary>Inspector for raw sound files: waveform, player, and .meta settings; plays when picked</summary>
[CanEdit( "asset:wav" )]
[CanEdit( "asset:mp3" )]
[CanEdit( "asset:ogg" )]
[CanEdit( "asset:flac" )]
public class SoundFileInspector : Widget, IAssetInspector
{
	readonly SoundPlayer _player;
	readonly SoundFileCompileSettings _settings;

	public SoundFileInspector( Widget parent ) : base( parent )
	{
		VerticalSizeMode = SizeMode.CanGrow;

		Layout = Layout.Column();
		Layout.Spacing = 4;

		_player = Layout.Add( new SoundPlayer( this ) );
		_player.FixedHeight = 160;

		_settings = Layout.Add( new SoundFileCompileSettings( this ) );
	}

	public void SetAsset( Asset asset )
	{
		_settings.SetAsset( asset );

		//What plays is the bake, so the waveform is drawn again from it once a setting
		//has been baked - trimmed, louder, mono - rather than showing the sound as it was.
		_settings.Baked = () => _ = Load( asset, play: false );

		_ = Load( asset, play: true );
	}

	public bool SetAssets( Asset[] assets )
	{
		_player.Visible = false;
		return _settings.SetAssets( assets );
	}

	async Task Load( Asset asset, bool play )
	{
		var soundFile = SoundFile.Load( asset.Path );
		if ( soundFile is null ) return;

		await soundFile.LoadAsync();

		//Interleaved so a stereo sound draws both channels; anything else comes back as one downmixed channel - see PreviewSoundFile.
		var samples = await soundFile.GetSamplesInterleavedAsync();

		if ( !IsValid ) return;

		_player.SetSamples( samples, soundFile.Duration, soundFile.ResourcePath, soundFile.Channels == 2 ? 2 : 1 );

		//Picking a sound is asking to hear it, unless the preview was told not to - the same switch the asset preview's toolbar has.
		if ( play && EditorCookie.Get( "SoundPreview.AutoPlay", true ) )
			_player.Play();
	}
}
