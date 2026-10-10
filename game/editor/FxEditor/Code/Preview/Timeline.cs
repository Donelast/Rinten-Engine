using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.FxEditor;

//Effect timeline: emitter bars (delay, duration), playhead, loop region; scrub seeks and pauses
internal sealed class TimelinePanel : Panel
{
	readonly ParticlePreviewPanel _preview;
	readonly ParticleEditorWindow _window;

	readonly Panel _ruler;
	readonly Panel _rows;
	readonly Panel _playhead;
	readonly Panel _loopA;
	readonly Panel _loopB;
	readonly Panel _loopSpan;
	readonly UILabel _clock;

	readonly List<(ParticleDefinition.Emitter Emitter, Panel Bar)> _bars = new();

	/// <summary>The region that repeats, as fractions of the whole, where <see cref="LoopOn"/>.</summary>
	public float LoopStart { get; private set; } = 0.0f;
	public float LoopEnd { get; private set; } = 1.0f;
	public bool LoopOn { get; set; }

	const float LabelWidth = 96.0f;

	public TimelinePanel( ParticlePreviewPanel preview, ParticleEditorWindow window )
	{
		_preview = preview;
		_window = window;

		AddClass( "timeline" );

		var head = AddChild<Panel>( "row ruler-row" );
		_clock = head.AddChild<UILabel>( "label clock" );
		_clock.Tokenize = false;
		_ruler = head.AddChild<Panel>( "track ruler" );

		_loopSpan = _ruler.AddChild<Panel>( "loop-span" );
		_loopA = _ruler.AddChild<Panel>( "loop-mark" );
		_loopB = _ruler.AddChild<Panel>( "loop-mark" );

		_rows = AddChild<Panel>( "rows" );

		_playhead = AddChild<Panel>( "playhead" );
	}

	/// <summary>How many seconds the strip spans.</summary>
	public float Span
	{
		get
		{
			var definition = _window.Definition;
			if ( definition is null ) return 5.0f;

			var span = definition.Duration > 0.0f ? definition.Duration : 0.0f;

			foreach ( var emitter in definition.Emitters )
			{
				if ( !emitter.Enabled ) continue;
				var end = emitter.Delay + ( emitter.Duration > 0.0f ? emitter.Duration : definition.Duration );
				span = MathF.Max( span, end );
			}

			return MathF.Max( span, 0.5f );
		}
	}

	/// <summary>Build the rows again from the effect.</summary>
	public void Rebuild()
	{
		_rows.DeleteChildren( true );
		_bars.Clear();

		var definition = _window.Definition;
		if ( definition is null ) return;

		var span = Span;

		foreach ( var emitter in definition.Emitters )
		{
			var row = _rows.AddChild<Panel>( "row" );
			row.SetClass( "dim", !emitter.Enabled );

			var label = row.AddChild<UILabel>( "label" );
			label.Tokenize = false;
			label.Text = emitter.Name;

			var track = row.AddChild<Panel>( "track" );
			var bar = track.AddChild<Panel>( "bar" );
			var grip = bar.AddChild<Panel>( "grip" );

			bar.SetClass( "selected", _window.IsSelected( emitter ) );

			var captured = emitter;
			bar.AddEventListener( "onclick", () => _window.Select( captured ) );

			_bars.Add( (emitter, bar) );
		}

		Place( span );
	}

	//Put every bar and mark where the numbers say.
	void Place( float span )
	{
		var definition = _window.Definition;
		if ( definition is null ) return;

		foreach ( var (emitter, bar) in _bars )
		{
			var duration = emitter.Duration > 0.0f ? emitter.Duration : ( definition.Duration > 0.0f ? definition.Duration : span );
			var left = ( emitter.Delay / span ).Clamp( 0.0f, 1.0f );
			var width = ( duration / span ).Clamp( 0.005f, 1.0f - left );

			bar.Style.Left = Length.Percent( left * 100.0f );
			bar.Style.Width = Length.Percent( width * 100.0f );
			bar.SetClass( "looping", definition.Looping );
		}

		_loopA.Style.Left = Length.Percent( LoopStart * 100.0f );
		_loopB.Style.Left = Length.Percent( LoopEnd * 100.0f );
		_loopSpan.Style.Left = Length.Percent( LoopStart * 100.0f );
		_loopSpan.Style.Width = Length.Percent( ( LoopEnd - LoopStart ) * 100.0f );

		_loopA.SetClass( "hidden", !LoopOn );
		_loopB.SetClass( "hidden", !LoopOn );
		_loopSpan.SetClass( "hidden", !LoopOn );
	}

	public override void Tick()
	{
		base.Tick();

		var span = Span;
		var at = _preview.Elapsed;
		var definition = _window.Definition;

		//A looping effect comes round: the head runs the strip again.
		if ( definition is { Looping: true } && span > 0.0f && !LoopOn )
			at %= span;

		var x = ( at / span ).Clamp( 0.0f, 1.0f );

		//Over the tracks only: the labels take the left of every row.
		var width = Box.Rect.Width;
		var trackWidth = MathF.Max( width - LabelWidth, 1.0f );

		_playhead.Style.Left = Length.Pixels( LabelWidth + x * trackWidth );
		_clock.Text = $"{at:0.00}s / {span:0.0}s";

		Place( span );
	}

	//-- dragging -----------------------------------------------------------

	enum Grab { None, Scrub, LoopA, LoopB, Move, Resize }

	Grab _grab;
	ParticleDefinition.Emitter _dragged;
	float _dragDelay, _dragDuration, _dragFrom;

	public override bool WantsDrag => true;

	//Where along the strip a point is, nought to one.
	float Along( Vector2 local )
	{
		var width = Box.Rect.Width;
		var trackWidth = MathF.Max( width - LabelWidth, 1.0f );

		return ( ( local.x - LabelWidth ) / trackWidth ).Clamp( 0.0f, 1.0f );
	}

	protected override void OnDragStart( DragEvent e )
	{
		var local = e.LocalGrabPosition;
		var along = Along( local );
		var span = Span;

		_grab = Grab.None;

		//The ruler: the loop marks first, then a scrub.
		if ( local.y < _ruler.Parent.Box.Rect.Height )
		{
			if ( LoopOn && MathF.Abs( along - LoopStart ) < 0.015f ) _grab = Grab.LoopA;
			else if ( LoopOn && MathF.Abs( along - LoopEnd ) < 0.015f ) _grab = Grab.LoopB;
			else { _grab = Grab.Scrub; _preview.Scrub( along * span ); }

			e.StopPropagation();
			return;
		}

		//A bar: the grip at its right end resizes, the rest moves.
		foreach ( var (emitter, bar) in _bars )
		{
			var rect = bar.Box.Rect;
			var mine = Box.Rect;
			var l = rect.Left - mine.Left;
			var t = rect.Top - mine.Top;

			if ( local.x < l || local.x > l + rect.Width || local.y < t || local.y > t + rect.Height ) continue;

			_dragged = emitter;
			_dragDelay = emitter.Delay;
			_dragDuration = emitter.Duration > 0.0f ? emitter.Duration : ( _window.Definition?.Duration ?? span );
			_dragFrom = along;
			_grab = local.x > l + rect.Width - 10.0f ? Grab.Resize : Grab.Move;

			_window.PushUndo( _grab == Grab.Resize ? "Emitter Duration" : "Emitter Delay" );
			_window.Select( emitter );
			e.StopPropagation();
			return;
		}
	}

	protected override void OnDrag( DragEvent e )
	{
		if ( _grab == Grab.None ) return;

		var along = Along( e.LocalPosition );
		var span = Span;

		switch ( _grab )
		{
			case Grab.Scrub:
				_preview.Scrub( along * span );
				break;

			case Grab.LoopA:
				LoopStart = MathF.Min( along, LoopEnd - 0.01f );
				break;

			case Grab.LoopB:
				LoopEnd = MathF.Max( along, LoopStart + 0.01f );
				break;

			case Grab.Move when _dragged is not null:
				_dragged.Delay = MathF.Max( 0.0f, Snap( _dragDelay + ( along - _dragFrom ) * span ) );
				_window.NoteTimingChanged();
				break;

			case Grab.Resize when _dragged is not null:
				_dragged.Duration = MathF.Max( 0.05f, Snap( _dragDuration + ( along - _dragFrom ) * span ) );
				_window.NoteTimingChanged();
				break;
		}

		e.StopPropagation();
	}

	protected override void OnDragEnd( DragEvent e )
	{
		if ( _grab is Grab.Move or Grab.Resize )
			_window.OnEdited();

		_grab = Grab.None;
		_dragged = null;

		e.StopPropagation();
	}

	//To the nearest twentieth of a second, which is what a hand can mean.
	static float Snap( float seconds ) => MathF.Round( seconds * 20.0f ) / 20.0f;
}
