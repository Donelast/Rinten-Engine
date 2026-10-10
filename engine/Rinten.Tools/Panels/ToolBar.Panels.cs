using Rinten;
using System;
using System.Collections.Generic;

namespace Editor;

//Each option is a square with its icon in it; widgets added to the bar sit between them at whatever size they asked for. The bar arranges its own contents because that is all it does.
/// <summary>A row of buttons along the top of a window.</summary>
public partial class ToolBar
{
	private readonly List<Option> options = new();
	private readonly List<Widget> widgets = new();

	//Options and widgets in the order added, so spacers and mixed items lay out correctly
	private readonly List<object> order = new();

	private readonly List<Rect> optionRects = new();

	private Vector2 iconSize = new( 16, 16 );
	private int hovered = -1;

	//The room around a button's icon.
	private const float Padding = 6.0f;

	private float ButtonSize => iconSize.y + (Padding * 2);

	protected override Vector2 SizeHint()
	{
		if ( !OnPanels ) return base.SizeHint();

		return new Vector2( Size.x, ButtonSize + 4 );
	}

	private protected override void OnArrange()
	{
		base.OnArrange();

		if ( !OnPanels ) return;

		optionRects.Clear();

		var size = ButtonSize;

		//What everything wants, so what is left over can be handed to whoever asked to grow. A widget that says it expands is a
		//spacer: it is how the buttons after it end up against the right edge, and two of them put what is between them in the middle.
		var taken = 2.0f;
		var growers = 0;

		foreach ( var item in order )
		{
			switch ( item )
			{
				case Option:
					taken += size + 2;
					break;

				case Widget widget when widget.IsValid():
					if ( Grows( widget ) ) growers++;
					else taken += widget.WantedSize().x + 2;
					break;
			}
		}

		var each = growers > 0 ? MathF.Max( 0, Size.x - taken ) / growers : 0.0f;

		var at = 2.0f;

		foreach ( var item in order )
		{
			switch ( item )
			{
				case Option:
					//Rects stay in the order the options were added, which is the order they are painted and hit-tested in.
					optionRects.Add( new Rect( at, 2, size, size ) );
					at += size + 2;
					break;

				case Widget widget when widget.IsValid():
				{
					var wanted = widget.WantedSize();
					var width = Grows( widget ) ? each : wanted.x;

					//A control that has a height of its own keeps it, centred in the bar rather than hung from the top of it.
					var height = wanted.y > 0 ? MathF.Min( size, wanted.y ) : size;

					widget.Position = new Vector2( at, 2 + ( ( size - height ) * 0.5f ) );
					widget.Size = new Vector2( width, height );

					at += width + 2;
					break;
				}
			}
		}
	}

	//Whether this widget asked for whatever width is going spare.
	private static bool Grows( Widget widget )
		=> widget.HorizontalSizeMode.HasFlag( SizeMode.Expand );

	protected override void OnPaint()
	{
		if ( !OnPanels ) { base.OnPaint(); return; }

		Paint.Antialiasing = true;

		for ( var i = 0; i < options.Count && i < optionRects.Count; i++ )
		{
			var option = options[i];
			var rect = optionRects[i];

			//Lit is the accent, under the pointer is a veil of white: the same two answers every other button in the editor gives.
			if ( option.Checked || i == hovered )
			{
				Paint.ClearPen();
				Paint.SetBrush( option.Checked ? Theme.OnFill : Theme.TileHover );
				Paint.DrawRect( rect, Theme.ControlRadius );
			}

			Paint.SetPen( option.Enabled
				? ( option.Checked || i == hovered ? Theme.Ink : Theme.InkDim )
				: Theme.InkOff );

			if ( !string.IsNullOrEmpty( option.RowIcon ) )
			{
				Paint.DrawIcon( rect, option.RowIcon, iconSize.y );
				continue;
			}

			Paint.SetDefaultFont();
			Paint.DrawText( rect, option.RowText, TextFlag.Center | TextFlag.SingleLine );
		}
	}

	//Which button is under a point, or minus one.
	private int OptionAt( Vector2 local )
	{
		for ( var i = 0; i < optionRects.Count; i++ )
		{
			if ( optionRects[i].IsInside( local ) ) return i;
		}

		return -1;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseMove( e ); return; }

		var was = hovered;

		hovered = OptionAt( e.LocalPosition );

		if ( was != hovered ) Update();
	}

	protected override void OnMouseLeave()
	{
		if ( !OnPanels ) { base.OnMouseLeave(); return; }

		hovered = -1;
		Update();
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseClick( e ); return; }

		var at = OptionAt( e.LocalPosition );

		if ( at < 0 || at >= options.Count ) return;

		options[at].Choose();

		Update();
	}
}
