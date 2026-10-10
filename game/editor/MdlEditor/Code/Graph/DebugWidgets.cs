using Rinten.UI;

namespace Editor.MdlEditor;

/// <summary>Self-repainting panel for the Debug section's live readouts while the graph plays</summary>
public abstract class LiveCanvas : PaintPanel
{
	private readonly bool _square;
	private readonly float _maxHeight;

	protected LiveCanvas( float height, bool square = false )
	{
		_square = square;
		_maxHeight = height;

		AddClass( "live-canvas" );

		Style.Height = height;
		Style.FlexShrink = 0;
	}

	public override void Tick()
	{
		if ( _square )
			Style.Height = MathF.Min( Box.Rect.Width * ScaleFromScreen, _maxHeight );

		//Live: nothing edits it, and it is different every frame anyway.
		Repaint();

		base.Tick();
	}
}

/// <summary>Paints a small readout box beside a live debug marker, kept inside the given area. Shared by preview nodes (e.g. blend spaces) to annotate their live marker.</summary>
public static class DebugMarker
{
	public static void PaintReadout( Rect area, Vector2 marker, string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;

		Paint.SetDefaultFont( 8 );
		var size = Paint.MeasureText( text );
		var pos = new Vector2(
			MathF.Min( marker.x + 10.0f, area.Right - size.x - 4.0f ),
			Math.Clamp( marker.y - size.y * 0.5f, area.Top + 2.0f, area.Bottom - size.y - 2.0f ) );

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground.WithAlpha( 0.85f ) );
		Paint.DrawRect( new Rect( pos, size ).Grow( 4.0f, 2.0f ), 3.0f );

		Paint.SetPen( Theme.Text );
		Paint.DrawText( new Rect( pos, size ), text, TextFlag.LeftTop );
	}
}
