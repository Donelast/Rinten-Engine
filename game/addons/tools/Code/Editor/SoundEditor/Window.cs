
namespace Editor.SoundEditor;

[EditorForAssetType( "wav" ), EditorForAssetType( "mp3" ), EditorForAssetType( "ogg" ), EditorForAssetType( "flac" )]
public class Window : DockWindow, IAssetEditor, AssetSystem.IEventListener
{
	public bool CanOpenMultipleAssets => true;

	private Preview Preview;
	private Timeline Timeline;
	private Properties Properties;
	private Asset Asset;
	private SoundFile SoundFile;
	private short[] Samples;
	private float Duration;
	private string Sound;

	public Window()
	{
		DeleteOnClose = true;

		Title = "Sound Editor";
		Size = new Vector2( 1000, 800 );

		CreateUI();
		Show();
		StateCookie = "SoundEditor";
	}

	public void AssetOpen( Asset asset )
	{
		if ( Asset != null )
			return;

		if ( asset == null )
			return;

		Asset = asset;
		SoundFile = SoundFile.Load( asset.Path );
		Title = $"Sound Editor - {asset.Name}";
		Timeline.SetAsset( Asset );
		Properties.SetAsset( Asset );

		ReloadSound();
	}

	//Generic multicast editor event - fires whenever any asset recompiles. Refresh when it's ours.
	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( asset != Asset )
			return;

		ReloadSound();
	}

	private async void ReloadSound()
	{
		if ( !IsValid )
			return;

		if ( !SoundFile.IsValid() )
			return;

		if ( !await SoundFile.LoadAsync() )
			return;

		Samples = await SoundFile.GetSamplesAsync();
		Duration = SoundFile.Duration;
		Sound = SoundFile.ResourcePath;
		Timeline.SetSamples( Samples, Duration, Sound );
	}

	[EditorEvent.Frame]
	protected void OnFrame()
	{
		if ( Timeline.Frames == null )
			return;

		Preview.AddVisemes( Timeline.Frames, Timeline.Time, 0.08f );
	}

	public void CreateUI()
	{
		if ( Asset != null )
			Title = $"Sound Editor - {Asset.Name}";

		Preview = new Preview( this );
		Properties = new Properties( this );
		Properties.SetAsset( Asset );
		Timeline = new Timeline( this );
		Timeline.SetSamples( Samples, Duration, Sound );
		Timeline.SetAsset( Asset );

		DockManager.AddDock( "Preview", "photo", Preview, DockArea.Center );
		DockManager.AddDock( "Properties", "edit", Properties, DockArea.Right );
		DockManager.AddDock( "Timeline", "timeline", Timeline, DockArea.Bottom );
	}

	protected override void BuildDefaultLayout()
	{
		var preview = DockManager.OpenDock( "Preview", DockArea.Center );
		DockManager.OpenDock( "Properties", DockArea.Right );
		DockManager.SetSplitterProportions( preview, 0.72f, 0.28f );

		var timeline = DockManager.OpenDock( "Timeline", DockArea.Bottom, preview );
		DockManager.SetSplitterProportions( timeline, 0.72f, 0.28f );
	}

	private void Save()
	{
		if ( Asset == null )
			return;

		//Setting null removes the key, so deleting every item on a track and saving clears it from the .meta - otherwise stale
		//data would keep compiling into the sound forever. Null lists mean "never loaded", so leave whatever is there alone.
		if ( Timeline.Frames != null )
		{
			Asset.MetaData.Set( "visemes", Timeline.Frames.Count > 0 ? Timeline.Frames : null );
		}

		if ( Timeline.Words != null )
		{
			Asset.MetaData.Set( "subtitles", Timeline.Words.Count > 0 ? Timeline.Words : null );
		}
	}

	//Analyze the loaded audio offline and replace the timeline's visemes.
	private void GenerateLipSync()
	{
		if ( SoundFile == null || Samples == null )
			return;

		Timeline.SetVisemes( LipSyncGenerator.Generate( Samples, Duration ) );
	}

	protected override void OnClosed()
	{
		base.OnClosed();

		SoundFile = null;
		Save();
	}
	void IAssetEditor.SelectMember( string memberName )
	{
		throw new System.NotImplementedException();
	}
}
