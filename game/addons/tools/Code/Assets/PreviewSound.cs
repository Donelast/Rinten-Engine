using Editor.Assets;

namespace Editor;


[AssetPreview( "sound" )]
class PreviewSound : AssetPreview
{
	public PreviewSound( Asset asset ) : base( asset ) { }

	/// <summary>The event's reach, to be clicked around in under its properties - see <see cref="Editor.Inspectors.SoundRadiusPanel"/>.</summary>
	public override Rinten.UI.Panel CreatePanel()
	{
		if ( !Asset.TryLoadResource<SoundEvent>( out var soundEvent ) ) return null;

		return new Editor.Inspectors.SoundRadiusPanel( soundEvent );
	}

	short[] samples;

	/// <summary>The event's first sound, read for its waveform. There is no world to render for a sound event - the empty preview scene drew every one of them as a black square.</summary>
	public override async Task InitializeAsset()
	{
		if ( !Asset.TryLoadResource<SoundEvent>( out var soundEvent ) ) return;
		if ( soundEvent.Sounds?.FirstOrDefault( x => x is not null ) is not { } soundFile ) return;

		await soundFile.LoadAsync();
		samples = await soundFile.GetSamplesInterleavedAsync();
	}

	public override Task RenderToBitmap( Bitmap bitmap )
	{
		var rect = new Rect( 0, bitmap.Size );

		bitmap.SetAntialias( true );
		bitmap.SetPen( Theme.Green, 3 );
		PreviewSoundFile.DrawWavLines( bitmap, rect.Shrink( 8, 0 ), samples );

		return Task.CompletedTask;
	}
}
