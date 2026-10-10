using Rinten;
using System;
using System.Collections.Generic;

namespace Editor;

//The list itself is a Menu, so it is the same rows, the same hover and the same click as everywhere else in the editor - a drop-down is only a menu that remembers which line you took.
/// <summary>A drop-down: what is chosen, drawn as a box, and a menu of the rest when it is clicked.</summary>
public partial class ComboBox
{
	private readonly struct Item
	{
		public string Text { get; init; }
		public string Icon { get; init; }
		public string Description { get; init; }
		public bool Enabled { get; init; }
	}

	private readonly List<Item> items = new();

	private int currentIndex = -1;
	private string editText;
	private bool editable;

	private Menu dropdown;

	private protected override void OnArrange()
	{
		base.OnArrange();

		//The field, where there is one, is the whole of the box less the room the arrow needs.
		if ( LineEdit is null ) return;

		LineEdit.Visible = editable;

		if ( !editable ) return;

		LineEdit.Position = new Vector2( 1, 1 );
		LineEdit.Size = new Vector2( MathF.Max( Size.x - Arrow - 2, 0 ), MathF.Max( Size.y - 2, 0 ) );
	}

	//How much room the arrow at the end takes.
	private const float Arrow = 18.0f;

	protected override Vector2 SizeHint()
	{
		if ( !OnPanels ) return base.SizeHint();

		var widest = 0.0f;

		foreach ( var item in items )
			widest = MathF.Max( widest, EditorText.Measure( item.Text ).x );

		return new Vector2( widest + Arrow + 20.0f, Theme.ControlHeight );
	}

	protected override void OnPaint()
	{
		if ( !OnPanels ) { base.OnPaint(); return; }

		Paint.Antialiasing = true;

		//A veil of white rather than a box: it is a thing to press, and what says so is that it answers the pointer.
		Paint.ClearPen();
		Paint.SetBrush( Paint.HasMouseOver ? Theme.Tile : Theme.Quiet );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		Paint.ClearBrush();
		Paint.SetPen( Paint.HasMouseOver ? Theme.EdgeLit : Theme.Edge );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		Paint.SetDefaultFont();
		Paint.SetPen( Enabled ? ( Paint.HasMouseOver ? Theme.Ink : Theme.InkDim ) : Theme.InkOff );

		//The arrow that says there is a list behind this.
		Paint.DrawIcon( new Rect( LocalRect.Right - Arrow, LocalRect.Top, Arrow, LocalRect.Height ), "expand_more", 14 );

		if ( editable ) return;

		var text = CurrentText;

		if ( string.IsNullOrEmpty( text ) ) return;

		Paint.DrawText( LocalRect.Shrink( 8, 0, Arrow, 0 ), text, TextFlag.LeftCenter | TextFlag.SingleLine );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseClick( e ); return; }
		if ( !Enabled || items.Count == 0 ) return;

		//The press this click began with already took the list away. Clicking the box a second time
		//closes the list, rather than closing it on the way down and opening it again on the way up.
		if ( Menu.ClosedByPress( this ) ) return;

		OpenList();
	}

	//Drops the list down, under the box.
	private void OpenList()
	{
		dropdown = new Menu( this ) { DeleteOnClose = true, Owner = this };

		for ( var i = 0; i < items.Count; i++ )
		{
			var index = i;
			var item = items[i];

			var option = dropdown.AddOption( item.Text, item.Icon, () => Choose( index ) );

			option.Enabled = item.Enabled;
			option.Checkable = true;
			option.Checked = index == currentIndex;
			option.ToolTip = item.Description;
		}

		var below = Position + new Vector2( 0, Size.y );

		for ( var widget = Parent; widget is not null; widget = widget.Parent )
			below += widget.Position;

		dropdown.OpenAt( below );
	}

	//Somebody took one of the lines.
	private void Choose( int index )
	{
		if ( index < 0 || index >= items.Count ) return;

		CurrentIndex = index;

		InternalIndexChanged();

		Update();
	}
}
