using Rinten.UI;
using System.Collections.Generic;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>Anything placed on the timeline at a time and a height of its own.</summary>
public abstract class TimelineItem : Panel
{
	public Timeline Timeline { get; protected set; }

	private Rect _sceneRect;
	private bool _selected;

	//A block is made by reflection before it knows its track, so this may come later.
	protected TimelineItem( Timeline? timeline )
	{
		Timeline = timeline!;

		AddClass( "timeline-item" );
	}

	/// <summary>Where this item is on the timeline, in scene pixels.</summary>
	public Rect SceneRect => _sceneRect;

	/// <summary>Whether clicking this picks it out.</summary>
	public bool Selectable { get; set; }

	//Set through the timeline rather than here, so the list of what is selected and the items that say they are can never disagree.
	/// <summary>Whether it is one of the things picked out.</summary>
	public bool Selected
	{
		get => _selected;
		set
		{
			if ( _selected == value ) return;

			Timeline?.SetSelected( this, value );
		}
	}

	internal void ApplySelected( bool value )
	{
		if ( _selected == value ) return;

		_selected = value;

		SetClass( "selected", value );

		OnSelectionChanged();
	}

	protected virtual void OnSelectionChanged() { }

	/// <summary>Which items lie in front of which.</summary>
	public int ZIndex
	{
		get => Style.ZIndex ?? 0;
		set => Style.ZIndex = value;
	}

	/// <summary>Whether the pointer is over this, said by the timeline rather than by the pointer.</summary>
	public bool Hovered
	{
		get => HasClass( "hovered" );
		internal set => SetClass( "hovered", value );
	}

	/// <summary>Whether the window cannot show this, so it is out of the layout.</summary>
	public bool IsCulled { get; private set; }

	//Takes an item out of the layout while the window cannot show it. A class rather than a style, because some items hide themselves for
	//reasons of their own - a keyframe on a locked track - and an inline display would win over that and show it again on the way back.
	internal void SetCulled( bool culled )
	{
		if ( IsCulled == culled ) return;

		IsCulled = culled;

		SetClass( "culled", culled );
	}

	/// <summary>Puts this item at a place on the timeline.</summary>
	public void SetSceneRect( Rect rect )
	{
		if ( _sceneRect == rect ) return;

		_sceneRect = rect;

		Style.Left = Length.Pixels( rect.Left );
		Style.Top = Length.Pixels( rect.Top );
		Style.Width = Length.Pixels( rect.Width );
		Style.Height = Length.Pixels( rect.Height );

		OnSceneRectChanged();
	}

	protected virtual void OnSceneRectChanged() { }

	//Its box by default. A keyframe that starts or ends a block is only half a handle and answers for that half alone.
	/// <summary>Whether a point of the timeline counts as being on this item.</summary>
	public virtual bool ContainsScenePoint( Vector2 scenePos ) => _sceneRect.IsInside( scenePos );

	/// <summary>Items placed inside this one, which the timeline can also hit and snap to.</summary>
	public virtual IEnumerable<TimelineItem> ChildItems => [];
}
