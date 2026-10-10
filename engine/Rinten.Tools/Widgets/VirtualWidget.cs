namespace Editor;

public class VirtualWidget
{
	public Rect Rect;
	public object Object;
	public bool Hovered;
	public bool Selected;
	public bool Pressed;
	public bool Dropping;
	public bool Dragging;
	public int Row;
	public int Column;

	//for tree
	public float Indent;
	public bool HasChildren;
	public bool IsOpen;
	public Rect ChildrenRect;

	/// <summary>Generically paint a background for this item</summary>
	public void PaintBackground( Color background, float radius )
	{
		var c = background;
		var r = Rect;
		Paint.ClearPen();

		//Under the pointer a step of white, and picked the picked plate - no colour pulled towards the accent and no line of it round the edge.
		if ( Hovered )
		{
			r = r.Shrink( 1 );
			c = Color.Lerp( c, Color.White, 0.08f );
		}

		if ( Selected )
		{
			c = Theme.SelectedBackground;
		}

		if ( c.a > 0 )
		{
			Paint.SetBrush( c );
			Paint.DrawRect( r, radius );
		}
	}

	public Color GetForegroundColor()
	{
		return Theme.Text;
	}
}
