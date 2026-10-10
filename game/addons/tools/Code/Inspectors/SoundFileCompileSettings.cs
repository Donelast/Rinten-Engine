using WidgetControlSheet = global::Editor.ControlSheet;

namespace Editor.Inspectors;

[CanEdit( "asset:vsnd" )]
public class SoundFileCompileSettings : Widget, IAssetInspector
{
	public class Settings
	{
		[Title( "Looping Enabled" ), Header( "Looping" )]
		public bool Loop { get; set; }

		[ShowIf( nameof( Loop ), true )]
		[Description( "Start Time" )]
		public float Start { get; set; }

		[ShowIf( nameof( Loop ), true )]
		[Description( "End Time, 0 is end of sound" )]
		public float End { get; set; }

		[Title( "Force Mono" ), Header( "Processing" )]
		[Description( "Downmix every channel into a single mono channel" )]
		public bool ForceMono { get; set; }

		[Title( "Trim Silence" )]
		[Description( "Remove silence from the start and end of the sound" )]
		public bool TrimSilence { get; set; }

		[Title( "Normalize Loudness" )]
		[Description( "Normalize the sound to a standard loudness level" )]
		public bool Normalize { get; set; }

		[Title( "Gain (dB)" ), Range( -24, 24 )]
		[Description( "Fixed volume adjustment applied to the whole sound" )]
		public float Gain { get; set; } = 0.0f;

		[Title( "Sample Rate" ), Header( "Resampling" )]
		public SamplingRate Rate { get; set; } = SamplingRate.Original;

		//IMA ADPCM: four bits a sample. There is no lossy encoder in the engine to give a bitrate meaning, so there is no bitrate to set.
		[Title( "Enabled" ), Header( "Compression" )]
		[Description( "Store the sound as IMA ADPCM - a quarter of the size of uncompressed, with a little hiss" )]
		public bool Compress { get; set; }

		public enum SamplingRate
		{
			[Title( "Original" )] Original = 0,

			[Title( "8000" )] Rate8000 = 8000,
			[Title( "11025" )] Rate11025 = 11025,
			[Title( "12000" )] Rate12000 = 12000,

			[Title( "16000" )] Rate16000 = 16000,
			[Title( "22050" )] Rate22050 = 22050,
			[Title( "24000" )] Rate24000 = 24000,

			[Title( "32000" )] Rate32000 = 32000,
			[Title( "44100" )] Rate44100 = 44100
		}
	}

	//Each selected asset paired with the Settings object bound to it in the sheet. One entry for a single selection, many for multi-select.
	private readonly List<(Asset Asset, Settings Settings)> _targets = new();

	public SoundFileCompileSettings( Widget parent ) : base( parent )
	{
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void SetAsset( Asset asset )
	{
		if ( asset?.MetaData is null )
			return;

		_targets.Clear();

		var settings = Load( asset );
		_targets.Add( (asset, settings) );

		var so = EditorTypeLibrary.GetSerializedObject( settings );
		so.OnPropertyChanged += ValuesChanged;

		Layout = WidgetControlSheet.Create( so );
	}

	public bool SetAssets( Asset[] assets )
	{
		_targets.Clear();

		var mso = new MultiSerializedObject();

		foreach ( var asset in assets )
		{
			if ( asset?.MetaData is null )
				continue;

			var settings = Load( asset );
			_targets.Add( (asset, settings) );

			mso.Add( EditorTypeLibrary.GetSerializedObject( settings ) );
		}

		if ( _targets.Count == 0 )
			return false;

		mso.Rebuild();
		mso.OnPropertyChanged += ValuesChanged;

		Layout = WidgetControlSheet.Create( mso );

		return true;
	}

	//Read an asset's compile metadata into a fresh Settings object.
	private static Settings Load( Asset asset )
	{
		var meta = asset.MetaData;

		return new Settings
		{
			Loop = meta.Get( "loop", false ),
			Start = meta.Get( "start", 0.0f ),
			End = meta.Get( "end", 0.0f ),
			ForceMono = meta.Get( "forceMono", false ),
			TrimSilence = meta.Get( "trimSilence", false ),
			Normalize = meta.Get( "normalize", false ),
			Gain = meta.Get( "gain", 0.0f ),
			Rate = meta.Get( "rate", Settings.SamplingRate.Original ),
			Compress = meta.Get( "compress", false ),
		};
	}

	//Write a Settings object back to an asset's compile metadata.
	private static void Save( Asset asset, Settings settings )
	{
		var meta = asset.MetaData;
		if ( meta is null )
			return;

		meta.Set( "loop", settings.Loop );
		meta.Set( "start", settings.Start );
		meta.Set( "end", settings.End );
		meta.Set( "forceMono", settings.ForceMono );
		meta.Set( "trimSilence", settings.TrimSilence );
		meta.Set( "normalize", settings.Normalize );
		meta.Set( "gain", settings.Gain );
		meta.Set( "rate", settings.Rate );
		meta.Set( "compress", settings.Compress );

		//Written by the settings before there was nothing to spend it on.
		meta.Set<object>( "bitrate", null );
	}

	//A value changed in the sheet. For multi-select the edit has already been propagated to every target's Settings
	//by the MultiSerializedObject, so we just persist each one - untouched fields keep their own per-asset values.
	private void ValuesChanged( SerializedProperty property )
	{
		//Mark dirty instead of compiling now: multi-select and sliders fire this many times per edit
		_timeSinceChange = 0;
		_dirty = true;
	}

	private bool _dirty;
	private RealTimeSince _timeSinceChange;

	[EditorEvent.Frame]
	private void SaveAndRecompile()
	{
		if ( !_dirty )
			return;

		//let slider drags and multi-select propagation settle first
		if ( _timeSinceChange < 0.2f )
			return;

		Flush();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		//don't lose an edit that was still inside the settle window
		if ( _dirty )
		{
			Flush();
		}
	}

	private void Flush()
	{
		_dirty = false;

		//Every asset's meta first, then the bake - see SoundBake. The meta is newer than the bake now, which is what makes the compile do one.
		foreach ( var (asset, settings) in _targets )
		{
			Save( asset, settings );
		}

		foreach ( var (asset, _) in _targets )
		{
			asset.Compile( false );
		}

		Baked?.Invoke();
	}

	/// <summary>The settings were written and baked into what plays.</summary>
	public Action Baked { get; set; }
}
