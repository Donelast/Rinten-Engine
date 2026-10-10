using Rinten;
using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>Shows a preview render of one frame.</summary>
public sealed class VideoExportPreviewPanel : Panel
{
	private readonly VideoExportWindow _window;
	private readonly CheckerPanel _checker;
	private readonly Image _frame;
	private readonly Panel _spinner;

	private Texture? _texture;
	private Pixmap? _shown;
	private Vector2 _laidOutSize;

	public bool IsUpdating
	{
		get => !_spinner.HasClass( "hidden" );
		set => _spinner.SetClass( "hidden", !value );
	}

	public VideoExportPreviewPanel( VideoExportWindow window )
	{
		_window = window;

		AddClass( "export-preview" );

		_checker = AddChild( new CheckerPanel() );
		_frame = AddChild<Image>( "frame" );

		_spinner = AddChild<Panel>( "spinner" );
		_spinner.AddChild<IconPanel>().Text = "autorenew";
		_spinner.AddClass( "hidden" );
	}

	public override void Tick()
	{
		base.Tick();

		_checker.SetClass( "hidden", !_window.SupportsTransparency );

		//The preview renders no bigger than it is drawn, so a new size wants a new render.
		var size = Box.Rect.Size;
		if ( size != _laidOutSize && size.x > 0f && size.y > 0f )
		{
			_laidOutSize = size;

			_ = _window.UpdatePreviewAsync( true, true );
		}

		Refresh();
	}

	/// <summary>Puts the last frame that was rendered on the panel.</summary>
	public void Refresh()
	{
		var frame = _window.LastRenderedFrame;

		if ( ReferenceEquals( _shown, frame ) && _texture is not null ) return;

		_shown = frame;

		_texture?.Dispose();
		_texture = null;

		if ( frame?.picture is { } picture )
		{
			lock ( frame )
			{
				_texture = picture.ToTexture( mips: false );
			}
		}

		_frame.Texture = _texture;
		_frame.SetClass( "hidden", _texture is null );

		var resolution = _window.Config.Resolution;

		if ( resolution.y > 0 )
		{
			_frame.Style.AspectRatio = (float)resolution.x / resolution.y;
		}
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		_texture?.Dispose();
		_texture = null;
	}

	//The chequerboard behind a frame that can be see-through.
	private sealed class CheckerPanel : BitmapPanel
	{
		private const int Size = 16;

		public CheckerPanel()
		{
			AddClass( "checker" );
		}

		protected override bool StretchToFit => false;

		protected override Vector2Int BitmapSize => new( Size, Size );

		protected override void OnDrawBitmap( Bitmap bitmap )
		{
			const int half = Size / 2;

			bitmap.SetFill( Color.Black.LerpTo( Color.White, 0.5f ) );
			bitmap.DrawRect( new Rect( 0f, 0f, half, half ) );
			bitmap.DrawRect( new Rect( half, half, half, half ) );

			bitmap.SetFill( Color.Black.LerpTo( Color.White, 0.625f ) );
			bitmap.DrawRect( new Rect( 0f, half, half, half ) );
			bitmap.DrawRect( new Rect( half, 0f, half, half ) );
		}
	}
}
