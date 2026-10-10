using Rinten.UI;

namespace Editor.MdlEditor;

//Model editor timeline: frame ruler, clip events lane and draggable playhead, styled like MovieMaker
[StyleSheet( "ClipTimeline.cs.scss" )]
internal sealed class ClipTimeline : PaintPanel
{
	/// <summary>The preview whose clip this is.</summary>
	public Preview Preview { get; set; }

	private const float RulerHeight = 22.0f;
	private const float LaneTop = RulerHeight + 8.0f;
	private const float LaneHeight = 18.0f;

	//Room left before frame nought, so the playhead there is not cut in half.
	private const float Margin = 12.0f;

	private const float LeastPixelsPerFrame = 0.25f;
	private const float MostPixelsPerFrame = 200.0f;

	//How far apart two numbered ticks are at least, and two unnumbered ones.
	private const float MajorSpacing = 64.0f;
	private const float MinorSpacing = 6.0f;

	private static readonly int[] Steps = { 1, 2, 5 };
	private static readonly int[] Divisions = { 10, 5, 2 };

	private float pixelsPerFrame = 8.0f;

	//The frame at the left edge, past the margin.
	private float scroll;

	//The clip last fitted to the width, and whether the view has been moved since.
	private string fittedClip;
	private float fittedWidth;
	private bool needsFit = true;
	private bool moved;

	private bool scrubbing;
	private bool panning;
	private float panFrom;
	private float panScroll;

	//What the picture last showed, so it is painted again only when that moves.
	private float shownTime = -1.0f;
	private int shownEvents = -1;

	public ClipTimeline()
	{
		AddClass( "clip-timeline" );
	}

	private ClipPreview Playback => Preview?.Playback;

	private bool HasClip => Playback is { HasClip: true } playback && playback.Duration > 0.0f;

	private float Rate => Playback?.FrameRate ?? 30.0f;

	//How long the clip is, in frames.
	private float Frames => HasClip ? Playback.Duration * Rate : 0.0f;

	//The last frame the playhead can stand on: at the length itself a looping clip is back at nought.
	private float LastFrame => MathF.Max( MathF.Ceiling( Frames ) - 1.0f, 0.0f );

	//The panel in the units the picture is drawn in.
	private float Width => Box.Rect.Width * ScaleFromScreen;

	private float PointerX => MousePosition.x * ScaleFromScreen;

	private float XOf( float frame ) => Margin + ( frame - scroll ) * pixelsPerFrame;

	private float FrameAt( float x ) => ( x - Margin ) / pixelsPerFrame + scroll;

	//The whole of the clip across the width.
	private void Fit()
	{
		var width = Width;

		if ( width <= Margin * 2.0f || Frames <= 0.0f ) return;

		pixelsPerFrame = Math.Clamp( ( width - Margin * 2.0f ) / Frames, LeastPixelsPerFrame, MostPixelsPerFrame );
		scroll = 0.0f;
		fittedWidth = width;
		needsFit = false;

		Repaint();
	}

	public override void Tick()
	{
		base.Tick();

		//Another clip is the whole of that one again, and so is another width while nobody has zoomed or scrolled.
		var clip = HasClip ? Playback.ClipName : null;

		if ( clip != fittedClip )
		{
			fittedClip = clip;
			needsFit = true;
			moved = false;
		}

		if ( needsFit || ( !moved && MathF.Abs( Width - fittedWidth ) > 1.0f ) )
			Fit();

		//Followed here rather than from move events: a panel has no implicit grab, so a drag that leaves the strip -
		//which is how the ends are reached - has to be read from where the pointer is and ended from the button itself.
		if ( scrubbing )
		{
			if ( Application.MouseButtons.HasFlag( MouseButtons.Left ) ) ScrubTo( PointerX );
			else scrubbing = false;
		}

		if ( panning )
		{
			if ( Application.MouseButtons.HasFlag( MouseButtons.Middle ) )
			{
				scroll = panScroll - ( PointerX - panFrom ) / pixelsPerFrame;
				Repaint();
			}
			else
			{
				panning = false;
			}
		}

		//The playhead moved, or the events were edited somewhere else.
		var time = HasClip ? Playback.Time : 0.0f;
		var events = EventsHash();

		if ( time == shownTime && events == shownEvents ) return;

		shownTime = time;
		shownEvents = events;

		Repaint();
	}

	private int EventsHash()
	{
		if ( Preview?.PickedClip?.Events is not { } events ) return 0;

		var hash = events.Count;

		foreach ( var marked in events )
		{
			if ( marked is null ) continue;

			hash = HashCode.Combine( hash, marked.StartFrame, marked.EndFrame, marked.Id, (int)marked.Kind );
		}

		return hash;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton == MouseButtons.Left && HasClip )
		{
			scrubbing = true;
			Preview.PauseForScrub();
			ScrubTo( PointerX );
			e.StopPropagation();
		}
		else if ( e.MouseButton == MouseButtons.Middle )
		{
			panning = true;
			moved = true;
			panFrom = PointerX;
			panScroll = scroll;
			e.StopPropagation();
		}
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( e.MouseButton == MouseButtons.Left ) scrubbing = false;
		else if ( e.MouseButton == MouseButtons.Middle ) panning = false;
	}

	public override void OnMouseWheel( Vector2 value )
	{
		if ( value.y == 0.0f )
		{
			base.OnMouseWheel( value );
			return;
		}

		moved = true;

		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			//About the pointer: the frame under it stays under it.
			var anchor = FrameAt( PointerX );

			pixelsPerFrame = Math.Clamp( pixelsPerFrame * ( value.y < 0.0f ? 1.25f : 0.8f ), LeastPixelsPerFrame, MostPixelsPerFrame );
			scroll = anchor - ( PointerX - Margin ) / pixelsPerFrame;
		}
		else
		{
			scroll += value.y * 60.0f / pixelsPerFrame;
		}

		scroll = Math.Clamp( scroll, -Frames, MathF.Max( Frames, 1.0f ) );

		Repaint();
	}

	//The playhead to the whole frame under the pointer.
	private void ScrubTo( float x )
	{
		if ( !HasClip ) return;

		var frame = Math.Clamp( MathF.Round( FrameAt( x ) ), 0.0f, LastFrame );
		var time = frame / Rate;

		if ( MathF.Abs( Playback.Time - time ) < 0.0001f ) return;

		Playback.Time = time;
	}

	protected override void OnPaintContent( Rect rect )
	{
		Paint.Antialiasing = true;

		//The ruler's band.
		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( new Rect( 0.0f, 0.0f, rect.Width, RulerHeight ) );

		if ( !HasClip )
		{
			Paint.SetDefaultFont( 8 );
			Paint.SetPen( Theme.TextLight.WithAlpha( 0.6f ) );
			Paint.DrawText( new Rect( 0.0f, RulerHeight, rect.Width, rect.Height - RulerHeight ), "Pick a clip to see its timeline.", TextFlag.Center );
			return;
		}

		var frames = Frames;
		var start = XOf( 0.0f );
		var end = XOf( frames );

		//Either side of the clip: there, but not it.
		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground.WithAlpha( 0.5f ) );

		if ( start > 0.0f ) Paint.DrawRect( new Rect( 0.0f, RulerHeight, start, rect.Height - RulerHeight ) );
		if ( end < rect.Width ) Paint.DrawRect( new Rect( end, RulerHeight, rect.Width - end, rect.Height - RulerHeight ) );

		PaintTicks( rect, frames );
		PaintEvents( rect );
		PaintPlayhead( rect );
	}

	//The smallest step of 1, 2 or 5 times a power of ten that leaves room for a number.
	private int MajorStep()
	{
		for ( var power = 1; power < 1_000_000; power *= 10 )
		{
			foreach ( var step in Steps )
			{
				if ( step * power * pixelsPerFrame >= MajorSpacing ) return step * power;
			}
		}

		return 1_000_000;
	}

	//The densest whole division of the major step that is still far enough apart to see.
	private int MinorStep( int major )
	{
		foreach ( var parts in Divisions )
		{
			if ( major % parts != 0 ) continue;

			var step = major / parts;

			if ( step * pixelsPerFrame >= MinorSpacing ) return step;
		}

		return major;
	}

	private void PaintTicks( Rect rect, float frames )
	{
		var major = MajorStep();
		var minor = MinorStep( major );

		var first = Math.Max( (int)MathF.Floor( FrameAt( 0.0f ) / minor ) * minor, 0 );
		var last = MathF.Min( FrameAt( rect.Width ), frames );

		Paint.SetDefaultFont( 7 );

		for ( var frame = first; frame <= last; frame += minor )
		{
			var x = XOf( frame );
			var numbered = frame % major == 0;

			Paint.SetPen( Theme.TextLight.WithAlpha( numbered ? 0.6f : 0.25f ), 1.0f );
			Paint.DrawLine( new Vector2( x, RulerHeight - ( numbered ? 9.0f : 4.0f ) ), new Vector2( x, RulerHeight ) );

			if ( !numbered ) continue;

			//A faint rule down through the lanes, and the frame's number by the tick.
			Paint.SetPen( Theme.TextLight.WithAlpha( 0.08f ), 1.0f );
			Paint.DrawLine( new Vector2( x, RulerHeight ), new Vector2( x, rect.Height ) );

			Paint.SetPen( Theme.TextLight.WithAlpha( 0.7f ) );
			Paint.DrawText( new Rect( x + 3.0f, 1.0f, 60.0f, RulerHeight - 9.0f ), $"{frame}", TextFlag.LeftCenter );
		}
	}

	private void PaintEvents( Rect rect )
	{
		var lane = new Rect( 0.0f, LaneTop, rect.Width, LaneHeight );

		//The lane's own ground, so a clip with no events still shows where they go.
		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground.WithAlpha( 0.35f ) );
		Paint.DrawRect( lane );

		if ( Preview?.PickedClip?.Events is not { Count: > 0 } events ) return;

		Paint.SetDefaultFont( 7 );

		var middle = lane.Center.y;

		foreach ( var marked in events )
		{
			if ( marked is null ) continue;

			var colour = KindColour( (int)marked.Kind );
			var x = XOf( marked.StartFrame );

			if ( marked.EndFrame >= marked.StartFrame )
			{
				//A span: a bar from its first frame to its last.
				var span = new Rect( x, lane.Top + 3.0f, MathF.Max( XOf( marked.EndFrame ) - x, 2.0f ), lane.Height - 6.0f );

				Paint.ClearPen();
				Paint.SetBrush( colour.WithAlpha( 0.35f ) );
				Paint.DrawRect( span, 2.0f );

				Paint.ClearBrush();
				Paint.SetPen( colour, 1.0f );
				Paint.DrawRect( span, 2.0f );
			}
			else
			{
				//A moment: a diamond on its frame.
				Paint.SetPen( Theme.WindowBackground, 1.0f );
				Paint.SetBrush( colour );
				Paint.DrawPolygon(
					new Vector2( x, middle - 5.0f ),
					new Vector2( x + 5.0f, middle ),
					new Vector2( x, middle + 5.0f ),
					new Vector2( x - 5.0f, middle ) );
			}

			if ( string.IsNullOrEmpty( marked.Id ) ) continue;

			Paint.SetPen( colour.WithAlpha( 0.9f ) );
			Paint.DrawText( new Rect( x + 6.0f, lane.Bottom + 1.0f, 160.0f, 12.0f ), marked.Id, TextFlag.LeftCenter );
		}
	}

	private void PaintPlayhead( Rect rect )
	{
		var x = XOf( Playback.Time * Rate );

		if ( x < -8.0f || x > rect.Width + 8.0f ) return;

		Paint.SetPen( Theme.Primary, 2.0f );
		Paint.DrawLine( new Vector2( x, 4.0f ), new Vector2( x, rect.Height ) );

		Paint.ClearPen();
		Paint.SetBrush( Theme.Primary );
		Paint.DrawPolygon(
			new Vector2( x - 5.0f, 2.0f ),
			new Vector2( x + 5.0f, 2.0f ),
			new Vector2( x, 9.0f ) );
	}

	//An event's colour by what kind it is: foot, sync, curve, transition, tag, or anything else.
	private static Color KindColour( int kind ) => kind switch
	{
		1 => Theme.Green,
		2 => Theme.Blue,
		3 => Theme.Yellow,
		4 => Theme.Pink,
		6 => Theme.Primary,
		_ => Theme.TextLight,
	};
}
