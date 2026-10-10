using System;

namespace Editor;

/// <summary>Splitter that can be linked to move alongside others</summary>
public class LinkableSplitter : Splitter
{
	private readonly List<LinkableSplitter> linked = new();

	public LinkableSplitter( Orientation orientation, Widget parent ) : base( parent )
	{
		IsHorizontal = orientation == Orientation.Horizontal;
	}

	/// <summary>Move together with another splitter: dragging either one moves both, so two viewports side by side keep their edges lined up.</summary>
	public void LinkWith( LinkableSplitter other )
	{
		if ( other is null || other == this ) return;

		if ( !linked.Contains( other ) ) linked.Add( other );
		if ( !other.linked.Contains( this ) ) other.linked.Add( this );
	}

	private protected override void OnSharesChanged()
	{
		base.OnSharesChanged();

		foreach ( var other in linked )
		{
			if ( !other.IsValid ) continue;

			other.CopySharesFrom( this );
		}
	}
}
