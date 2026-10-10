using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

//The timeline scrolls by moving one layer rather than by being a scroll panel, so it has to say for itself how far down the list it is.
/// <summary>The bar down the right of the timeline, for when the tracks do not fit.</summary>
public sealed class TimelineScrollBarPanel : Panel
{
	private const float MinimumThumb = 24f;

	private readonly Timeline _timeline;
	private readonly Panel _thumb;

	private bool _dragging;
	private float _dragStartPointerY;
	private float _dragStartOffsetY;

	public TimelineScrollBarPanel( Timeline timeline )
	{
		_timeline = timeline;

		AddClass( "timeline-scrollbar" );

		_thumb = AddChild<Panel>( "thumb" );
	}

	public void UpdatePosition()
	{
		var view = _timeline.View;

		var height = view.Size.y;
		var content = view.ContentHeight + TimelineView.VerticalMargin;

		if ( height <= 0f || content <= height )
		{
			SetClass( "hidden", true );
			return;
		}

		SetClass( "hidden", false );

		var span = content - height;
		var scrolled = Math.Clamp( (view.Offset.y + TimelineView.VerticalMargin) / span, 0f, 1f );

		var thumbHeight = Math.Max( MinimumThumb, height * (height / content) );
		var travel = Math.Max( 0f, height - thumbHeight );

		_thumb.Style.Height = Length.Pixels( thumbHeight );
		_thumb.Style.Top = Length.Pixels( scrolled * travel );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		_dragging = true;
		_dragStartPointerY = e.PointerPosition.y;
		_dragStartOffsetY = _timeline.View.Offset.y;

		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( !_dragging ) return;

		var view = _timeline.View;
		var height = view.Size.y;
		var content = view.ContentHeight + TimelineView.VerticalMargin;

		if ( height <= 0f || content <= height ) return;

		var thumbHeight = Math.Max( MinimumThumb, height * (height / content) );
		var travel = Math.Max( 1f, height - thumbHeight );

		var moved = (e.PointerPosition.y - _dragStartPointerY) * ScaleFromScreen;

		view.Offset = view.Offset with { y = _dragStartOffsetY + moved / travel * (content - height) };

		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		_dragging = false;
	}
}
