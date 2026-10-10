using Rinten.UI;

namespace Editor.MdlEditor;

//The clip preview timeline: track, frame diamonds and playhead, painted as one picture
[StyleSheet( "ClipScrubber.cs.scss" )]
internal sealed class ClipScrubber : PaintPanel
{
	//How wide the playhead's travel is inset from either end.
	private const float ThumbWidth = 6.0f;

	/// <summary>How far through the clip, from 0 to 1.</summary>
	public float Value
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			Repaint();
		}
	}

	/// <summary>How long the clip is, in seconds. Nothing to scrub through when it is zero, which is also what the transport says while there is no clip.</summary>
	public float Duration
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			Tooltip = $"Animation timeline · {FormatTime( field )}";
		}
	}

	/// <summary>How many frames the clip has, one diamond each where there is room.</summary>
	public int FrameCount
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			Repaint();
		}
	}

	/// <summary>Called when a scrub begins, so playback can be paused for it.</summary>
	public Action ScrubStarted { get; set; }

	/// <summary>Called when it ends, wherever the button came up.</summary>
	public Action ScrubEnded { get; set; }

	/// <summary>Called whenever the scrub has moved the playhead.</summary>
	public Action ValueEdited { get; set; }

	private bool _scrubbing;

	public ClipScrubber()
	{
		AddClass( "clip-scrubber" );
	}

	//The panel in the units the picture and the sheet are both written in.
	private Rect Content
	{
		get
		{
			var size = Box.Rect.Size * ScaleFromScreen;

			return new Rect( 0.0f, 0.0f, size.x, size.y );
		}
	}

	//The strip the playhead travels along, inset by half a thumb at either end.
	private static Rect TrackOf( Rect rect ) => new( ThumbWidth * 0.5f, rect.Center.y - 2.0f, rect.Width - ThumbWidth, 4.0f );

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		//Nothing to scrub through, which is the state the transport is disabled in.
		if ( e.MouseButton != MouseButtons.Left || Duration <= 0.0f )
			return;

		_scrubbing = true;
		ScrubStarted?.Invoke();

		ScrubTo( PointerX );

		e.StopPropagation();
	}

	/// <summary>The scrub is followed here rather than from a move event: a panel has no implicit grab, so dragging the playhead off the end of the strip - which is how you get to zero or to the last frame - has to be read from where the pointer is and ended from the button itself.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( !_scrubbing )
			return;

		ScrubTo( PointerX );

		if ( !Application.MouseButtons.HasFlag( MouseButtons.Left ) )
			EndScrub();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		EndScrub();
	}

	//Where the pointer is across the strip, in the picture's own units.
	private float PointerX => MousePosition.x * ScaleFromScreen;

	private void ScrubTo( float x )
	{
		var track = TrackOf( Content );
		if ( track.Width <= 0.0f )
			return;

		var value = ((x - track.Left) / track.Width).Clamp( 0.0f, 1.0f );
		if ( value == Value )
			return;

		Value = value;
		ValueEdited?.Invoke();
	}

	private void EndScrub()
	{
		if ( !_scrubbing )
			return;

		_scrubbing = false;
		ScrubEnded?.Invoke();
	}

	protected override void OnPaintContent( Rect rect )
	{
		var track = TrackOf( rect );
		var playhead = track.Left + track.Width * Value.Clamp( 0.0f, 1.0f );

		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground.WithAlpha( 0.8f ) );
		Paint.DrawRect( track, 2f );

		Paint.SetBrush( Theme.Primary.WithAlpha( 0.9f ) );
		Paint.DrawRect( new Rect( track.Left, track.Top, MathF.Max( playhead - track.Left, 0f ), track.Height ), 2f );

		PaintFrameMarkers( track );

		Paint.SetPen( Theme.Primary, 2f );
		Paint.DrawLine( new Vector2( playhead, 4f ), new Vector2( playhead, rect.Bottom - 4f ) );
		Paint.ClearPen();
		Paint.SetBrush( Theme.Primary );
		Paint.DrawPolygon(
			new Vector2( playhead - 4f, 3f ),
			new Vector2( playhead + 4f, 3f ),
			new Vector2( playhead, 8f ) );
	}

	private void PaintFrameMarkers( Rect track )
	{
		if ( FrameCount < 1 || track.Width <= 0f )
			return;

		const float minimumSpacing = 10f;
		var frameStep = Math.Max( 1, (int)MathF.Ceiling( FrameCount * minimumSpacing / track.Width ) );
		var center = track.Center.y;

		Paint.SetPen( Theme.WindowBackground.WithAlpha( 0.9f ), 1f );
		Paint.SetBrush( Theme.TextLight.WithAlpha( 0.8f ) );
		for ( var frame = frameStep; frame < FrameCount; frame += frameStep )
		{
			var x = MathX.LerpTo( track.Left, track.Right, frame / (float)FrameCount );
			Paint.DrawPolygon(
				new Vector2( x, center - 3f ),
				new Vector2( x + 3f, center ),
				new Vector2( x, center + 3f ),
				new Vector2( x - 3f, center ) );
		}
	}

	//A time as the transport reads it. Here rather than beside the labels because the tooltip on the strip says the same thing the labels do.
	internal static string FormatTime( float seconds )
	{
		seconds = MathF.Max( seconds, 0f );
		var minutes = (int)(seconds / 60f);
		var remaining = seconds - minutes * 60f;
		return $"{minutes}:{remaining:00.000}";
	}
}
