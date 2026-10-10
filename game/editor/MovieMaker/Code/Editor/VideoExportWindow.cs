using Editor.Inspectors;
using Rinten.Internal;
using Rinten.MovieMaker;
using Rinten.UI;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static Rinten.VideoWriter;

namespace Editor.MovieMaker;

#nullable enable

public sealed class VideoExportWindow : BaseWindow
{
	private const float SlowPreviewDelaySeconds = 0.5f;

	public static VideoExportWindow Show( Session session, MovieTimeRange? timeRange )
	{
		var window = new VideoExportWindow( session );

		window.Show();

		return window;
	}

	public Session Session { get; }

	public VideoExportConfig Config { get; }

	public MovieTime PreviewTime { get; set; }
	public MovieTime RenderStartTime { get; private set; }
	public MovieTime LastRenderedTime { get; private set; }
	public Pixmap? LastRenderedFrame { get; private set; }

	private CancellationTokenSource? _cts;
	private Task? _exportTask;

	private MovieTimeRange _oldTimeRange;
	private float _slowPreviewTime;

	private Pixmap? _previewPixmap;
	private Pixmap? _renderPixmap;

	private readonly VideoExportPanel _panel;

	private bool IsOutputtingImageFile => Config.Mode is ExportMode.ImageAtlas or ExportMode.ImageSequence;

	[Feature( "General", Icon = "settings" )]
	[FilePath( "mp4,webm", AllowExternal = true, Saving = true )]
	[ShowIf( nameof( IsOutputtingImageFile ), false )]
	public string OutputVideoFile
	{
		get => Session.Cookies.ExportPath;
		set => Session.Cookies.ExportPath = value;
	}

	[Feature( "General", Icon = "settings" )]
	[FilePath( "png,jpg,jpeg", AllowExternal = true, Saving = true )]
	[ShowIf( nameof( IsOutputtingImageFile ), true )]
	public string OutputImageFile
	{
		get => Session.Cookies.ImageSequencePath;
		set => Session.Cookies.ImageSequencePath = value;
	}

	public string OutputFile => IsOutputtingImageFile ? OutputImageFile : OutputVideoFile;

	[Feature( "General", Icon = "settings" )]
	public bool PlayAfterExport { get; set; } = true;

	[Hide]
	public MovieTimeRange TimeRange => (StartTime, EndTime);

	[Feature( "General", Icon = "settings" )]
	public double StartTime
	{
		get => Session.LoopTimeRange?.Start.TotalSeconds ?? 0d;
		set
		{
			var startSeconds = Math.Clamp( value, 0d, Session.Duration.TotalSeconds );
			var endSeconds = Math.Clamp( Session.LoopTimeRange?.End.TotalSeconds ?? Session.Duration.TotalSeconds, startSeconds, Session.Duration.TotalSeconds );

			Session.LoopTimeRange = (startSeconds, endSeconds);
		}
	}

	[Feature( "General", Icon = "settings" )]
	public double EndTime
	{
		get => Session.LoopTimeRange?.End.TotalSeconds ?? Session.Duration.TotalSeconds;
		set
		{
			var endSeconds = Math.Clamp( value, 0d, Session.Duration.TotalSeconds );
			var startSeconds = Math.Clamp( Session.LoopTimeRange?.Start.TotalSeconds ?? 0d, 0d, endSeconds );

			Session.LoopTimeRange = (startSeconds, endSeconds);
		}
	}

	/// <summary>How many frames the exported video will have.</summary>
	[Property]
	public string TotalFrames => $"{TimeRange.Duration.GetFrameCount( Config.FrameRate ):N0}";

	/// <summary>How many frames will be rendered in total. Many sub-frames get merged into one frame in the exported video.</summary>
	[Property, Title( "Total Sub-Frames" )]
	public string TotalSubFrames => $"{TimeRange.Duration.GetFrameCount( Config.FrameRate ) * Config.SubFramesPerFrame:N0}";

	/// <summary>Roughly how big the exported video will be on disk.</summary>
	[Property]
	public string EstimatedSize => $"{TimeRange.Duration.TotalSeconds * Config.Bitrate / 8d:N1} MB";

	public bool IsExporting => _exportTask is { IsCompleted: false };

	public bool SupportsTransparency => Path.GetExtension( OutputFile ).ToLower() is ".png" or ".webm";

	private CancellationTokenSource? _previewCts;

	private VideoExportWindow( Session session )
	{
		Session = session;
		Config = session.Project.ExportConfig ??= new VideoExportConfig();

		SetModal( true, true );

		Size = new Vector2( 1024, 400 );
		MinimumSize = Size;
		TranslucentBackground = true;
		NoSystemBackground = true;

		WindowTitle = "Export Video";
		SetWindowIcon( "video_file" );

		//A window is still a window, and the dock layer arranges it. Everything inside is one panel.

		Layout = Layout.Row();

		var exportSerialized = EditorTypeLibrary.GetSerializedObject( this );
		var configSerialized = EditorTypeLibrary.GetSerializedObject( Config );

		exportSerialized.OnPropertyChanged += OnExportChanged;
		configSerialized.OnPropertyChanged += OnConfigChanged;

		_panel = new VideoExportPanel( this, exportSerialized, configSerialized );

		var host = new PanelDockHost( this );

		host.SetContent( _panel );

		Layout.Add( host );

		PreviewTime = TimeRange.Start;

		_ = UpdatePreviewAsync( false );
	}

	internal void OnExportPressed() => ExportClicked();

	internal void OnCancelPressed() => CancelClicked();

	private void OnExportChanged( SerializedProperty property )
	{
		_panel.SetExportEnabled( !string.IsNullOrWhiteSpace( OutputFile ) );

		//If we change start / end time, show that time in the preview to help with seeking

		if ( _oldTimeRange.Start != TimeRange.Start )
		{
			PreviewTime = TimeRange.Start;
		}
		else if ( _oldTimeRange.End != TimeRange.End )
		{
			PreviewTime = TimeRange.End;
		}
		else
		{
			_panel.Refresh();
			return;
		}

		_oldTimeRange = TimeRange;

		_ = UpdatePreviewAsync( true, true );
	}

	private void OnConfigChanged( SerializedProperty property )
	{
		_ = UpdatePreviewAsync( false, true );

		_panel.Refresh();
	}

	private static Vector2Int FindPreviewResolution( Vector2Int configResolution, Vector2 previewSize )
	{
		//Not laid out yet: nothing smaller to go by.
		if ( previewSize.x <= 0f || previewSize.y <= 0f )
		{
			return configResolution;
		}

		if ( previewSize.x > configResolution.x && previewSize.y > configResolution.y )
		{
			return configResolution;
		}

		var configAspect = (float)configResolution.x / configResolution.y;
		var previewAspect = previewSize.x / previewSize.y;

		return previewAspect > configAspect
			? new Vector2Int( (int)(configResolution.x * previewSize.y / configResolution.y), (int)previewSize.y )
			: new Vector2Int( (int)previewSize.x, (int)(configResolution.y * previewSize.x / configResolution.x) );
	}

	public async Task UpdatePreviewAsync( bool fast, bool delay = false )
	{
		//Just in case the preview panel's resize triggers this too early

		if ( (VideoExportPanel?)_panel is null ) return;

		if ( _previewCts is { } cts )
		{
			await cts.CancelAsync();
		}

		cts = _previewCts = new CancellationTokenSource();

		var config = new VideoExportConfig
		{
			Resolution = FindPreviewResolution( Config.Resolution, _panel.PreviewSize ),
			FrameRate = Config.FrameRate,
			WarmupFrameCount = 0,
			Exposure = fast ? Exposure.Instant : Config.Exposure,
			MotionQuality = Config.MotionQuality,
		};

		if ( config.Resolution.x < 4 || config.Resolution.y < 4 || config.FrameRate <= 0 )
		{
			return;
		}

		try
		{
			_panel.IsUpdating = true;

			if ( delay )
			{
				await Task.Delay( TimeSpan.FromSeconds( 0.5 ), cts.Token );
				await MainThread.Wait();
			}

			var resolution = config.Resolution;

			await Session.Renderer.RenderAsync( PreviewTime, config, ( subFrameTime, pixels, _ ) =>
			{
				OnRenderFrame( resolution, subFrameTime, pixels.AsSpan(), ref _renderPixmap );
				return Task.CompletedTask;
			}, cts.Token );

			if ( fast && config.Exposure != Config.Exposure )
			{
				//If we did a fast preview, do a slow one if no options change within UpdateRenderDelaySeconds

				_slowPreviewTime = RealTime.Now + SlowPreviewDelaySeconds;
			}
		}
		finally
		{
			_panel.IsUpdating = false;
		}
	}

	private async Task ExportAsync()
	{
		_panel.SetExporting( true );

		if ( _previewCts is { } cts )
		{
			await cts.CancelAsync();
		}

		_cts = new CancellationTokenSource();

		try
		{
			PreviewTime = TimeRange.Start;

			switch ( Config.Mode )
			{
				case ExportMode.VideoFile:
					await ExportVideoFile( _cts.Token );
					break;

				case ExportMode.ImageSequence:
					await ExportImageSequence( _cts.Token );
					break;

				case ExportMode.ImageAtlas:
					await ExportImageAtlas( _cts.Token );
					break;
			}
		}
		finally
		{
			RenderStartTime = 0d;
			LastRenderedTime = 0d;

			_panel.SetExporting( false );
		}
	}

	private async Task ExportVideoFile( CancellationToken ct )
	{
		var resolution = Config.Resolution;

		using var writer = EditorUtility.CreateVideoWriter( OutputFile, Config.GetVideoWriterConfig( OutputFile ) );

		var timeRange = TimeRange;

		await Session.Renderer.RenderAsync( timeRange, Config, async ( time, pixels, innerCt ) =>
		{
			OnExportFrame( time, resolution, pixels );

			while ( !writer.ReadyForVideoFrame )
			{
				await Task.Delay( 10, innerCt );
			}

			await Task.Run( () => writer.AddFrame( pixels.AsSpan(), TimeSpan.FromSeconds( (time - timeRange.Start).TotalSeconds ) ), innerCt );
		}, ct );

		await writer.FinishAsync();

		if ( PlayAfterExport )
		{
			EditorUtility.OpenFile( OutputFile );
		}
	}

	private enum ImageFileFormat
	{
		Png,
		Jpeg
	}

	private static ImageFileFormat GetImageFormat( string path )
	{
		return Path.GetExtension( path ).ToLower() switch
		{
			".jpg" or ".jpeg" => ImageFileFormat.Jpeg,
			_ => ImageFileFormat.Png
		};
	}

	private static void SavePixmap( Pixmap pixmap, string path )
	{
		var fileFormat = GetImageFormat( path );

		switch ( fileFormat )
		{
			case ImageFileFormat.Png:
				pixmap.SavePng( path );
				break;

			case ImageFileFormat.Jpeg:
				pixmap.SaveJpg( path );
				break;
		}
	}

	private async Task ExportImageSequence( CancellationToken ct )
	{
		var resolution = Config.Resolution;

		var imagePathFormat = GetFrameFilePathFormat( OutputFile );
		var pixmap = new Pixmap( resolution.x, resolution.y );

		await Session.Renderer.RenderAsync( TimeRange, Config, ( time, pixels, innerCt ) =>
		{
			OnExportFrame( time, resolution, pixels );

			return Task.Run( () =>
			{
				pixmap.UpdateFromPixels( pixels, pixmap.Width, pixmap.Height, ImageFormat.RGBA8888 );

				var frameIndex = time.GetFrameIndex( Config.FrameRate );
				var framePath = string.Format( imagePathFormat, frameIndex );

				SavePixmap( pixmap, framePath );
			}, innerCt );
		}, ct );
	}

	private async Task ExportImageAtlas( CancellationToken ct )
	{
		var resolution = Config.Resolution;
		var frameCount = TimeRange.Duration.GetFrameCount( Config.FrameRate );
		var cols = (int)Math.Ceiling( Math.Sqrt( frameCount ) );
		var rows = (int)Math.Ceiling( (double)frameCount / cols );

		var atlasResolution = resolution * new Vector2Int( cols, rows );

		const int warnSize = 10_000;

		if ( atlasResolution.x >= warnSize || atlasResolution.y >= warnSize )
		{
			var confirmTcs = new TaskCompletionSource<bool>();

			Dialog.AskConfirm( () => confirmTcs.SetResult( true ), () => confirmTcs.SetResult( false ),
				$"The atlas will be {atlasResolution.x:N0}x{atlasResolution.y:N0}! Are you sure?" );

			if ( !await confirmTcs.Task )
			{
				return;
			}
		}

		var atlasPixels = new byte[atlasResolution.x * atlasResolution.y * 4];

		await Session.Renderer.RenderAsync( TimeRange, Config, ( time, pixels, innerCt ) =>
		{
			OnExportFrame( time, resolution, pixels );

			return Task.Run( () =>
			{
				var frameIndex = time.GetFrameIndex( Config.FrameRate );
				var col = frameIndex % cols;
				var row = frameIndex / cols;

				for ( var y = 0; y < resolution.y; ++y )
				{
					var srcLine = pixels.AsSpan( y * resolution.x * 4, resolution.x * 4 );
					var dstLine = atlasPixels.AsSpan( (y + row * resolution.y) * atlasResolution.x * 4, atlasResolution.x * 4 );

					srcLine.CopyTo( dstLine[(col * resolution.x * 4)..((col + 1) * resolution.x * 4)] );
				}

			}, innerCt );
		}, ct );

		var pixmap = new Pixmap( atlasResolution.x, atlasResolution.y );

		pixmap.UpdateFromPixels( atlasPixels, atlasResolution, ImageFormat.RGBA8888 );

		SavePixmap( pixmap, OutputFile );
	}

	private static Regex ImageSequenceFilePathRegex { get; } =
		new( @"^(?<path>.+[^0-9])(?:[0-9]+)?\.(?<ext>[a-z0-9]+)$" );

	private static string GetFrameFilePathFormat( string filePath )
	{
		if ( ImageSequenceFilePathRegex.Match( filePath ) is { Success: true } match )
		{
			return $"{match.Groups["path"].Value}{{0}}.{match.Groups["ext"]}";
		}

		return $"{filePath}{{0}}.png";
	}

	private void OnExportFrame( MovieTime time, Vector2Int resolution, byte[] pixels )
	{
		PreviewTime = time;
		LastRenderedTime = time;

		OnRenderFrame( resolution, time, pixels.AsSpan(), ref _previewPixmap );
	}

	private void ExportClicked()
	{
		if ( IsExporting ) return;
		if ( string.IsNullOrWhiteSpace( OutputFile ) ) return;

		RenderStartTime = TimeRange.Start;
		LastRenderedTime = TimeRange.Start;

		_exportTask = ExportAsync();
	}

	private void CancelClicked()
	{
		_cts?.Cancel();
	}

	[EditorEvent.Frame]
	private void Frame()
	{
		//Do a full preview render if we've not edited any options in a while

		if ( Application.MouseButtons != MouseButtons.None ) return;
		if ( _slowPreviewTime <= 0f ) return;
		if ( RealTime.Now < _slowPreviewTime ) return;

		_slowPreviewTime = 0f;
		_ = UpdatePreviewAsync( false );
	}

	private void OnRenderFrame( Vector2Int resolution, MovieTime time, ReadOnlySpan<byte> pixels, ref Pixmap? pixmap )
	{
		if ( pixmap?.Size != resolution )
		{
			pixmap = new Pixmap( resolution );
		}

		lock ( pixmap )
		{
			pixmap.UpdateFromPixels( pixels, resolution, ImageFormat.RGBA8888 );
		}

		LastRenderedFrame = pixmap;

		_panel.Refresh();
	}
}
