using Rinten;
using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

//Five corners and a point, which is not a box, so it is painted - once, into a picture the size of the flag, and again only if its colour changes.
/// <summary>The little flag at the end of a time cursor.</summary>
public sealed class BookmarkPanel : BitmapPanel
{
	private readonly bool _pointsDown;
	private readonly int _width;
	private readonly int _height;
	private readonly float _arrow;

	private Color _color;

	public Color Color
	{
		get => _color;
		set
		{
			if ( _color == value ) return;

			_color = value;

			MarkDirty();
		}
	}

	public BookmarkPanel( Color color, bool pointsDown, int width = 9, int height = 12, float arrow = 4f )
	{
		_color = color;
		_pointsDown = pointsDown;
		_width = width;
		_height = height;
		_arrow = arrow;

		AddClass( "bookmark" );
		AddClass( pointsDown ? "top" : "bottom" );
	}

	protected override Vector2Int BitmapSize => new( _width, _height );

	protected override void OnDrawBitmap( Bitmap bitmap )
	{
		var half = _width * 0.5f;

		Vector2[] points = _pointsDown
			?
			[
				new Vector2( half, _height ),
				new Vector2( _width, _height - _arrow ),
				new Vector2( _width, 0f ),
				new Vector2( 0f, 0f ),
				new Vector2( 0f, _height - _arrow )
			]
			:
			[
				new Vector2( half, 0f ),
				new Vector2( _width, _arrow ),
				new Vector2( _width, _height ),
				new Vector2( 0f, _height ),
				new Vector2( 0f, _arrow )
			];

		bitmap.SetAntialias( true );
		bitmap.SetFill( _color );
		bitmap.DrawPolygon( points );
	}
}
