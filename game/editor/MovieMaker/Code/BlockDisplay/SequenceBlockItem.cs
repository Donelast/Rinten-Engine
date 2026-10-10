using Rinten;
using Rinten.MovieMaker;
using Rinten.UI;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

/// <summary>A whole movie clip laid inside another one, drawn as a strip of film.</summary>
public sealed class SequenceBlockItem : BlockItem<ProjectSequenceBlock>, IMovieDraggable, IMovieResizable, IMovieContextMenu
{
	private readonly FilmStripPanel _strip;
	private readonly ChildPool<Panel> _loopLines;
	private readonly Rinten.UI.Label _title;
	private readonly IconPanel _icon;
	private readonly Rinten.UI.Label _startTime;
	private readonly Rinten.UI.Label _endTime;

	private bool _isDragging;
	private BlockEdge? _resizeEdge;

	public ProjectSequenceTrack SequenceTrack => (ProjectSequenceTrack)Track.View.Track;

	public string BlockTitle => Block.Resource.ResourceName.ToTitleCase();

	public Rect FullSceneRect
	{
		get
		{
			var fullTimeRange = FullTimeRange;

			var min = Timeline.TimeToPixels( fullTimeRange.Start );
			var max = Timeline.TimeToPixels( fullTimeRange.End );

			return SceneRect with { Left = min, Right = max };
		}
	}

	private MovieTimeRange FullTimeRange
	{
		get
		{
			var sourceTimeRange = new MovieTimeRange( 0d, Block.Resource.GetCompiled().Duration );

			return new MovieTimeRange( Block.Transform * sourceTimeRange.Start, Block.Transform * sourceTimeRange.End );
		}
	}

	bool IMovieItem.MultiSelectable => true;

	public SequenceBlockItem()
	{
		AddClass( "sequence-block" );

		Selectable = true;

		_strip = AddChild( new FilmStripPanel() );
		_loopLines = new ChildPool<Panel>( this, "loop-line" );

		_icon = AddChild<IconPanel>( "icon" );
		_icon.Text = "movie";

		_title = AddChild<Rinten.UI.Label>( "title" );
		_startTime = AddChild<Rinten.UI.Label>( "time start" );
		_endTime = AddChild<Rinten.UI.Label>( "time end" );
	}

	protected override void OnResize() => Rebuild();

	protected override void OnBlockChanged( MovieTimeRange timeRange ) => Rebuild();

	protected override void OnSelectionChanged() => Rebuild();

	public override void Tick()
	{
		base.Tick();

		if ( _strip.Tint == Tint ) return;

		Rebuild();
	}

	private Color Tint
	{
		get
		{
			var isLocked = Track.View.IsLocked;
			var isSelected = !isLocked && Selected;
			var isHovered = !isLocked && Hovered;

			return Theme.Primary
				.Desaturate( isLocked ? 0.25f : 0f )
				.Darken( isLocked ? 0.5f : isSelected ? 0f : isHovered ? 0.1f : 0.25f );
		}
	}

	private void Rebuild()
	{
		_strip.Tint = Tint;

		RebuildLoopLines();
		RebuildText();
	}

	//A line wherever the clip inside starts over.
	private void RebuildLoopLines()
	{
		_loopLines.Begin();

		var full = FullSceneRect;
		var loopWidth = full.Width;

		if ( loopWidth > 1f )
		{
			var minLoopX = full.Left - SceneRect.Left;

			minLoopX -= MathF.Floor( minLoopX / loopWidth ) * loopWidth;

			for ( var x = minLoopX; x < SceneRect.Width; x += loopWidth )
			{
				if ( x.AlmostEqual( 0f ) ) continue;

				var line = _loopLines.Next();

				line.Style.Left = Length.Pixels( x );
				line.Style.BackgroundColor = Tint.Darken( 0.5f );
			}
		}

		_loopLines.End();
	}

	//The name of the clip, or where in it each end of the block now sits.
	private void RebuildText()
	{
		var showTimes = _resizeEdge is not null;

		_icon.SetClass( "hidden", showTimes );
		_title.SetClass( "hidden", showTimes );
		_startTime.SetClass( "hidden", !showTimes );
		_endTime.SetClass( "hidden", !showTimes );

		if ( !showTimes )
		{
			_title.Text = BlockTitle;
			return;
		}

		var fullTimeRange = FullTimeRange;
		var duration = Block.Resource.GetDuration();

		_startTime.Text = WrapTime( Block.TimeRange.Start - fullTimeRange.Start, duration ).ToString();
		_endTime.Text = WrapTime( Block.TimeRange.End - fullTimeRange.Start, duration, -MovieTime.Epsilon ).ToString();
	}

	private static MovieTime WrapTime( MovieTime time, MovieTime duration, MovieTime bias = default )
	{
		if ( !duration.IsPositive ) return time;

		return time - (time + bias).GetFrameIndex( duration ) * duration;
	}

	private void OnSplit( MovieTime time )
	{
		if ( time <= TimeRange.Start ) return;
		if ( time >= TimeRange.End ) return;

		using ( Session.History.Push( $"Split Sequence ({BlockTitle})" ) )
		{
			SequenceTrack.AddBlock( (time, Block.TimeRange.End), Block.Transform, Block.Resource );

			Block.TimeRange = (Block.TimeRange.Start, time);
		}

		Layout();
		Track.View.MarkValueChanged();
	}

	public void ShowContextMenu( EditMode.ContextMenuEvent ev )
	{
		ev.Accepted = true;

		Timeline.SetSelected( this, true );

		var time = Session.PlayheadTime;

		ev.Menu.AddHeading( "Sequence Block" );
		ev.Menu.AddOption( "Edit", "edit", OnEdit );

		if ( time > TimeRange.Start && time < TimeRange.End )
		{
			ev.Menu.AddOption( "Split", "carpenter", () => OnSplit( time ) );
		}

		ev.Menu.AddOption( "Delete", "delete", OnDelete );
	}

	private void OnDelete()
	{
		using ( Session.History.Push( "Sequence Deleted" ) )
		{
			SequenceTrack.RemoveBlock( Block );

			if ( SequenceTrack.IsEmpty )
			{
				Track.View.Remove();
			}
		}

		Track.View.MarkValueChanged();
	}

	private void OnEdit()
	{
		if ( Block.Resource is { } resource )
		{
			Session.Editor.EnterSequence( resource, Block.Transform, Block.Transform.Inverse * Block.TimeRange );
		}
	}

	MovieTimeRange IMovieItem.TimeRange => Block.TimeRange;

	void IMovieItem.DoubleClick() => OnEdit();

	void IMovieDraggable.StartDrag() => _isDragging = true;

	void IMovieDraggable.Drag( MovieTime delta )
	{
		Block.TimeRange += delta;
		Block.Transform += delta;

		Track.View.MarkValueChanged();

		Layout();
	}

	void IMovieDraggable.EndDrag() => _isDragging = false;

	void IMovieResizable.StartResize( BlockEdge edge )
	{
		_resizeEdge = edge;

		RebuildText();
	}

	void IMovieResizable.Resize( BlockEdge edge, MovieTime delta )
	{
		Block.TimeRange =
			edge == BlockEdge.Start
				? Block.TimeRange with { Start = Block.TimeRange.Start + delta }
				: Block.TimeRange with { End = Block.TimeRange.End + delta };

		if ( Block.Resource.GetDuration() is { IsPositive: true } duration )
		{
			var localStartTime = Block.Transform.Inverse * Block.TimeRange.Start;

			Block.Transform += localStartTime.GetFrameIndex( duration ) * duration;
		}

		Track.View.MarkValueChanged();

		Layout();
	}

	void IMovieResizable.EndResize()
	{
		_resizeEdge = null;

		RebuildText();
	}

	public override IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary )
	{
		if ( _isDragging ) yield break;

		if ( _resizeEdge is null )
		{
			yield return Block.TimeRange.Start;
			yield return Block.TimeRange.End;
		}

		if ( Block.Resource.GetDuration() is not { IsPositive: true } duration ) yield break;

		var localTime = Block.Transform.Inverse * sourceTime;
		var nearestLoopPoint = Block.Transform * localTime.Round( duration );

		//If nearest loop point is outside the block, only snap if we're resizing this block

		if ( !Block.TimeRange.Contains( nearestLoopPoint ) && _resizeEdge is null ) yield break;

		yield return nearestLoopPoint;
	}

	IEnumerable<MovieTime> IMovieDraggable.SnapSources
	{
		get
		{
			if ( Block.Resource.GetDuration() is not { IsPositive: true } duration )
			{
				return [Block.TimeRange.Start, Block.TimeRange.End];
			}

			//If source clip has a duration, snap on loop points

			return
			[
				Block.TimeRange.Start,
				..(Block.Transform * (0, duration))
					.Repeat( Block.TimeRange )
					.Select( x => x.Range.End )
			];
		}
	}
}

//A tile three pixels wide, repeated across the block. The colour is painted into it rather than laid over it, so the whole thing is one texture and one box on the GPU whatever the block is doing.
/// <summary>The strip of film a sequence block is drawn as.</summary>
public sealed class FilmStripPanel : BitmapPanel
{
	private const int TileWidth = 3;

	private Color _tint = Color.White;

	public Color Tint
	{
		get => _tint;
		set
		{
			if ( _tint == value ) return;

			_tint = value;

			MarkDirty();
		}
	}

	public FilmStripPanel()
	{
		AddClass( "film-strip" );
	}

	protected override bool StretchToFit => false;

	protected override Vector2Int BitmapSize => new( TileWidth, MovieLayout.BlockHeight );

	protected override void OnDrawBitmap( Bitmap bitmap )
	{
		bitmap.SetFill( _tint );
		bitmap.DrawRect( new Rect( 0f, 0f, TileWidth, MovieLayout.BlockHeight ) );

		bitmap.SetFill( _tint.Darken( 0.5f ) );
		bitmap.DrawRect( new Rect( 1f, 1f, 2f, 3f ) );
		bitmap.DrawRect( new Rect( 1f, MovieLayout.BlockHeight - 4f, 2f, 3f ) );
	}
}
