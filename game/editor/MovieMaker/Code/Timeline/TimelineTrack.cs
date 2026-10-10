using Editor.MovieMaker.BlockDisplays;
using Rinten.MovieMaker;
using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>One track's row on the timeline, and the blocks laid along it.</summary>
public sealed partial class TimelineTrack : TimelineItem
{
	//TODO: should this be in DisplayInfo?
	private static Dictionary<Type, Color> HandleColors { get; } = new()
	{
		{ typeof(Vector3), Theme.Blue },
		{ typeof(Rotation), Theme.Green },
		{ typeof(Color), Theme.Pink },
		{ typeof(float), Theme.Yellow },
	};

	//How wide a track row is drawn, at the least - a movie is usually longer than the view.
	private const float MinimumWidth = 50_000f;

	public Session Session { get; }
	public TrackView View { get; }

	private readonly SynchronizedList<ITrackBlock, BlockItem> _blockItems;
	private readonly SynchronizedList<ITrackBlock, BlockItem> _previewBlockItems;

	public Color HandleColor { get; }

	public TimelineTrack( Timeline timeline, TrackView view )
		: base( timeline )
	{
		Session = timeline.Session;
		View = view;

		AddClass( "timeline-track" );

		Tooltip = view.Description;

		HandleColor = HandleColors.TryGetValue( view.Track.TargetType, out var color ) ? color : Color.Gray;

		_blockItems = new SynchronizedList<ITrackBlock, BlockItem>(
			AddBlockItem, RemoveBlockItem, UpdateBlockItem );

		_previewBlockItems = new SynchronizedList<ITrackBlock, BlockItem>(
			AddPreviewBlockItem, RemoveBlockItem, UpdateBlockItem );

		View.Changed += View_Changed;
		View.ValueChanged += View_ValueChanged;

		UpdateLayout();
	}

	public override void OnDeleted()
	{
		Session.EditMode?.ClearTimelineItems( this );

		View.Changed -= View_Changed;
		View.ValueChanged -= View_ValueChanged;

		_blockItems.Clear();
		_previewBlockItems.Clear();

		Timeline.Forget( this );

		base.OnDeleted();
	}

	private void View_Changed( TrackView view ) => UpdateItems();

	private void View_ValueChanged( TrackView view ) => UpdateItems();

	/// <summary>Puts this row where the track list says it goes, and lays out its blocks.</summary>
	public void UpdateLayout()
	{
		var width = Math.Max( MinimumWidth,
			Timeline.TimeToPixels( Session.Duration ) + Timeline.View.Size.x );

		SetSceneRect( new Rect( 0f, Timeline.HeaderHeight + View.Position, width, MovieLayout.TrackHeight ) );

		UpdateItems();
	}

	//Called when the pointer is level with this row, or has left it.
	internal void SetHovered( bool hovered )
	{
		if ( View.IsHovered == hovered ) return;

		View.IsHovered = hovered;
	}

	//Clicked in the timeline, on this row but not on anything in it.
	internal void OnPressed() => View.InspectProperty();

	public void UpdateItems()
	{
		_blockItems.Update( View.Blocks );
		_previewBlockItems.Update( View.PreviewBlocks );

		Session.EditMode?.UpdateTimelineItems( this );

		UpdateBackground();
	}

	private Color? _litColor;

	//Only when it changes: writing a style marks the panel dirty whether or not the value is new, and this runs on every track every frame.
	private void UpdateBackground()
	{
		var lit = View.IsHovered || View.IsSelected;
		var color = lit ? View.BackgroundColor.WithAlphaMultiplied( 0.25f ) : (Color?)null;

		if ( _litColor == color ) return;

		_litColor = color;

		SetClass( "lit", lit );

		Style.BackgroundColor = color;
	}

	public override void Tick()
	{
		base.Tick();

		UpdateBackground();
	}

	private BlockItem AddBlockItem( ITrackBlock src ) => Timeline.AddItem( BlockItem.Create( this, src, default ) );

	private BlockItem AddPreviewBlockItem( ITrackBlock src )
	{
		var item = AddBlockItem( src );

		item.IsPreview = true;

		return item;
	}

	private void RemoveBlockItem( BlockItem dst ) => Timeline.Remove( dst );

	private bool UpdateBlockItem( ITrackBlock src, ref BlockItem dst )
	{
		if ( dst.Block.GetType() != src.GetType() )
		{
			var isPreview = dst.IsPreview;

			Timeline.Remove( dst );

			dst = AddBlockItem( src );
			dst.IsPreview = isPreview;
		}

		dst.Block = src;
		dst.Offset = default;
		dst.Layout();

		return true;
	}
}
