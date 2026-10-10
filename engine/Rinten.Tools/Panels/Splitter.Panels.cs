using Rinten;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Editor;

/// <summary>A splitter: widgets side by side with a handle between each pair.</summary>
public partial class Splitter
{
	//How thick the thing you grab is: the theme's one separator width.
	private float handleWidth = Theme.SeparatorWidth;

	private readonly List<Widget> children = new();
	private readonly List<float> shares = new();

	private bool vertical;
	private int dragging = -1;

	//Which handle the pointer is on, or minus one.
	private int hovered = -1;

	//Which handle is under a point, or minus one.
	private int HandleAt( Vector2 local )
	{
		var at = vertical ? local.y : local.x;

		for ( var i = 0; i < children.Count - 1; i++ )
		{
			var edge = EdgeAfter( i );

			if ( MathF.Abs( at - edge ) <= handleWidth ) return i;
		}

		return -1;
	}

	//Where the gap after a child is, along the splitter.
	private float EdgeAfter( int index )
	{
		var length = Length;
		var at = 0.0f;

		for ( var i = 0; i <= index; i++ )
			at += shares[i] * length;

		return at + (handleWidth * (index + 0.5f));
	}

	//How much room there is for the children, less the handles.
	private float Length
	{
		get
		{
			var whole = vertical ? Size.y : Size.x;

			return MathF.Max( whole - (handleWidth * MathF.Max( children.Count - 1, 0 )), 1.0f );
		}
	}

	//How big the splitter would like to be: everything in it, end to end, plus the handles between them.
	protected override Vector2 SizeHint()
	{
		if ( !OnPanels ) return base.SizeHint();

		var along = 0.0f;
		var across = 0.0f;

		foreach ( var child in children )
		{
			if ( child is null || !child.IsValid || !child.Visible ) continue;

			var wanted = child.WantedSize();

			if ( vertical )
			{
				along += MathF.Max( wanted.y, child.MinimumHeight );
				across = MathF.Max( across, MathF.Max( wanted.x, child.MinimumWidth ) );
			}
			else
			{
				along += MathF.Max( wanted.x, child.MinimumWidth );
				across = MathF.Max( across, MathF.Max( wanted.y, child.MinimumHeight ) );
			}
		}

		along += handleWidth * MathF.Max( children.Count - 1, 0 );

		return vertical
			? new Vector2( across, along )
			: new Vector2( along, across );
	}

	private protected override void OnArrange()
	{
		if ( children.Count == 0 ) return;

		Even();

		var length = Length;
		var across = vertical ? Size.x : Size.y;

		var at = 0.0f;
		var whole = vertical ? Size.y : Size.x;

		for ( var i = 0; i < children.Count; i++ )
		{
			var size = shares[i] * length;
			var leftover = whole - at;

			if ( i == children.Count - 1 || size > leftover )
				size = MathF.Max( leftover, 0.0f );

			children[i].Position = vertical ? new Vector2( 0, at ) : new Vector2( at, 0 );
			children[i].Size = vertical ? new Vector2( across, size ) : new Vector2( size, across );

			at += size + handleWidth;
		}
	}

	//The shares moved. A splitter linked to this one follows it.
	private protected virtual void OnSharesChanged()
	{
	}

	//Takes another splitter's shares, for a linked pair.
	internal void CopySharesFrom( Splitter other )
	{
		if ( other is null || other.shares.Count != shares.Count ) return;

		for ( var i = 0; i < shares.Count; i++ ) shares[i] = other.shares[i];

		MarkNeedsArrange();
	}

	//Shares that do not add up are made to.
	private void Even()
	{
		while ( shares.Count < children.Count ) shares.Add( 0.0f );
		while ( shares.Count > children.Count ) shares.RemoveAt( shares.Count - 1 );

		var total = shares.Sum();

		if ( total > 0.001f )
		{
			for ( var i = 0; i < shares.Count; i++ ) shares[i] /= total;
			return;
		}

		for ( var i = 0; i < shares.Count; i++ ) shares[i] = 1.0f / shares.Count;
	}

	//The handles: a seam each, and the one that can be moved right now says so. The seam is a hairline until the pointer is on it, and
	//the accent while it is being dragged - a splitter that looks the same whether or not it can be grabbed is one nobody knows is there.
	protected override void OnPaint()
	{
		if ( !OnPanels ) { base.OnPaint(); return; }

		Paint.ClearPen();

		for ( var i = 0; i < children.Count - 1; i++ )
		{
			var edge = EdgeAfter( i );
			var busy = dragging == i || ( dragging < 0 && hovered == i );

			//The whole strip, in the theme's separator colour, and the hover colour while it is
			//the thing being pointed at or dragged: the same seam as the gap between two docks.
			Paint.SetBrush( busy ? Theme.SeparatorHoverColor : Theme.SeparatorColor );

			var at = edge - (handleWidth * 0.5f);

			Paint.DrawRect( vertical
				? new Rect( 0, at, Size.x, handleWidth )
				: new Rect( at, 0, handleWidth, Size.y ) );
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMousePress( e ); return; }

		dragging = HandleAt( e.LocalPosition );
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseMove( e ); return; }

		//Over a handle, say so, so it is clear it can be dragged.
		var under = HandleAt( e.LocalPosition );

		Cursor = under >= 0 || dragging >= 0
			? (vertical ? CursorShape.SizeV : CursorShape.SizeH)
			: CursorShape.None;

		//Repainting only when the answer changed: a splitter is under the pointer for as long as
		//somebody is moving across the window, and a repaint per frame of that is a repaint for nothing.
		if ( under != hovered )
		{
			hovered = under;
			Update();
		}

		if ( dragging < 0 ) return;

		var length = Length;
		if ( length <= 1.0f ) return;

		//What the drag takes from one side it gives to the other, so the rest of the splitter does not move.
		var at = vertical ? e.LocalPosition.y : e.LocalPosition.x;
		var before = EdgeAfter( dragging );

		var moved = (at - before) / length;

		var first = shares[dragging] + moved;
		var second = shares[dragging + 1] - moved;

		const float smallest = 0.02f;

		if ( first < smallest || second < smallest ) return;

		shares[dragging] = first;
		shares[dragging + 1] = second;

		OnSharesChanged();

		MarkNeedsArrange();
		Update();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseReleased( e ); return; }

		dragging = -1;

		Update();
	}

	protected override void OnMouseLeave()
	{
		base.OnMouseLeave();

		if ( !OnPanels || hovered < 0 ) return;

		hovered = -1;

		Update();
	}
}
