using Rinten;
using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The shape drawn for one keyframe.</summary>
public sealed class KeyframeGlyphPanel : BitmapPanel
{
	private readonly KeyframeHandle _handle;

	private bool _lastSelected;
	private bool _lastHovered;

	public KeyframeGlyphPanel( KeyframeHandle handle )
	{
		_handle = handle;

		AddClass( "glyph" );
	}

	/// <summary>Repaints when the pointer or the selection changes what colour it should be.</summary>
	public void SyncState()
	{
		if ( _lastSelected == _handle.Selected && _lastHovered == _handle.Hovered ) return;

		_lastSelected = _handle.Selected;
		_lastHovered = _handle.Hovered;

		MarkDirty();
	}

	protected override Vector2Int BitmapSize => new(
		Math.Max( 1, _handle.SceneRect.Width.CeilToInt() ),
		MovieLayout.TrackHeight );

	protected override void OnDrawBitmap( Bitmap bitmap )
	{
		if ( _handle.View.IsLocked ) return;

		var width = bitmap.Width;
		var height = bitmap.Height;

		var centre = new Vector2( width * _handle.HandleBias, height * 0.5f );
		var connection = _handle.Keyframe.Connection;
		var isStartEnd = connection is KeyframeConnection.StartBlock or KeyframeConnection.EndBlock;

		bitmap.SetAntialias( true );

		//The glow, so a handle reads over whatever the track is showing.
		bitmap.SetRadialGradient( centre, width * 0.5f, new Gradient(
			new Gradient.ColorFrame( 0f, MovieColors.ChannelBackground ),
			new Gradient.ColorFrame( 1f, Color.Transparent ) ) );

		FillRect( bitmap, new Rect( 0f, 0f, width, height ), centre, connection );

		var color = SelectColor();

		bitmap.SetFill( color );

		switch ( _handle.Keyframe.Interpolation )
		{
			case KeyframeInterpolation.Step:
				FillRect( bitmap, new Rect( centre, 0f ).Grow( 4f ), centre, connection );
				break;

			case KeyframeInterpolation.Linear:
				FillDiamond( bitmap, centre, 10f, connection );
				break;

			case KeyframeInterpolation.Quadratic:
				FillCircle( bitmap, centre, 4f, connection );
				break;

			case KeyframeInterpolation.Cubic:
				FillCircle( bitmap, centre, 3f, connection );

				bitmap.SetPen( color, 1f );
				StrokeCircle( bitmap, centre, 5f, connection );
				break;
		}

		bitmap.SetPen( color.WithAlphaMultiplied( isStartEnd ? 1f : 0.3f ), 1f );
		bitmap.DrawLine( new Vector2( centre.x, 0f ), new Vector2( centre.x, height - 1f ) );
	}

	private Color SelectColor()
	{
		if ( _handle.Selected ) return MovieColors.HandleSelected;
		if ( _handle.Hovered ) return _handle.Track.HandleColor;

		return _handle.Track.HandleColor.WithAlpha( 0.5f );
	}

	//A whole rect, or the half of it the connection mode keeps.
	private static void FillRect( Bitmap bitmap, Rect rect, Vector2 centre, KeyframeConnection connection )
	{
		bitmap.DrawRect( connection switch
		{
			KeyframeConnection.StartBlock => rect with { Left = centre.x },
			KeyframeConnection.EndBlock => rect with { Right = centre.x },
			_ => rect
		} );
	}

	private static void FillCircle( Bitmap bitmap, Vector2 centre, float radius, KeyframeConnection connection )
	{
		var box = new Rect( centre - radius, radius * 2f );

		switch ( connection )
		{
			case KeyframeConnection.StartBlock:
				bitmap.DrawArc( box, -90f, 180f, useCenter: true );
				return;

			case KeyframeConnection.EndBlock:
				bitmap.DrawArc( box, 90f, 180f, useCenter: true );
				return;

			default:
				bitmap.DrawCircle( centre, radius );
				return;
		}
	}

	private static void StrokeCircle( Bitmap bitmap, Vector2 centre, float radius, KeyframeConnection connection )
	{
		var box = new Rect( centre - radius, radius * 2f );

		switch ( connection )
		{
			case KeyframeConnection.StartBlock:
				bitmap.DrawArc( box, -90f, 180f, useCenter: false );
				return;

			case KeyframeConnection.EndBlock:
				bitmap.DrawArc( box, 90f, 180f, useCenter: false );
				return;

			default:
				bitmap.DrawCircle( centre, radius );
				return;
		}
	}

	private static void FillDiamond( Bitmap bitmap, Vector2 centre, float size, KeyframeConnection connection )
	{
		var x = new Vector2( size * 0.5f, 0f );
		var y = new Vector2( 0f, size * 0.5f );

		switch ( connection )
		{
			case KeyframeConnection.StartBlock:
				bitmap.DrawPolygon( [centre - y, centre + x, centre + y] );
				return;

			case KeyframeConnection.EndBlock:
				bitmap.DrawPolygon( [centre - x, centre - y, centre + y] );
				return;

			default:
				bitmap.DrawPolygon( [centre - x, centre - y, centre + x, centre + y] );
				return;
		}
	}
}
