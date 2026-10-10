using Rinten.UI;

namespace Editor;

/// <summary>A widget standing in a razor layout.</summary>
public class WidgetPanel : Panel
{
	private Widget widget;

	//The size the widget last asked for, so it is only written when it changes.
	private Vector2 wanted;

	public WidgetPanel()
	{
		//So a stylesheet can say how big the hole is where the widget says nothing about it
		AddClass( "widgetpanel" );
	}

	/// <summary>The widget standing here. It belongs to whoever made it - this only borrows it.</summary>
	public Widget Widget
	{
		get => widget;
		set
		{
			if ( widget == value ) return;

			Release();

			widget = value;
			wanted = default;
		}
	}

	/// <summary>Whether this box is as big as the widget wants to be, rather than however big the layout around it decides.</summary>
	public bool AutoSize { get; set; } = true;

	/// <summary>Whether the width comes from the layout around it while the height still comes from the widget.</summary>
	public bool WidthFromLayout { get; set; }

	public override void OnDeleted()
	{
		base.OnDeleted();

		Release();
	}

	//Lets the widget go without destroying it - a tool's toolbar outlives the bar it was shown in, and is put back the next time its tool is picked.
	private void Release()
	{
		if ( widget is not null && widget.IsValid ) widget.Parent = null;

		widget = null;
	}

	//The widget this markup is inside of, which is the one the borrowed widget is parented to.
	private Widget Host()
	{
		for ( var panel = Parent; panel is not null; panel = panel.Parent )
		{
			if ( panel is WidgetSurface surface ) return surface.Widget;
		}

		return null;
	}

	public override void Tick()
	{
		base.Tick();

		if ( widget is null || !widget.IsValid ) return;

		if ( !widget.Visible ) return;

		var host = Host();

		if ( host is null || !host.IsValid || host.Surface is null ) return;

		if ( widget.Parent != host ) widget.Parent = host;

		if ( AutoSize )
		{
			//Measure like a layout cell; asking the widget returns whatever size was last written here
			var cell = new Layout.Cell { Widget = widget };
			var size = new Vector2( Layout.WantsOf( cell, false ), Layout.WantsOf( cell, true ) );

			//Inspector SizeHint is a thousand pixels; FixedWidth on the bar is the hole.
			size.x = System.Math.Clamp( size.x, widget.MinimumWidth, widget.MaximumWidth );
			size.y = System.Math.Clamp( size.y, widget.MinimumHeight, widget.MaximumHeight );

			//Only when it actually changed. Not because assigning would cost anything - a style setter compares before it dirties - but because the
			//branches below decide which dimensions to write at all, and on almost every frame the widget has not moved and there is nothing to decide.
			if ( size != wanted )
			{
				wanted = size;

				//A widget that says nothing about one of its dimensions - most of them only fix a width - leaves it to the
				//stylesheet rather than being written down as nothing, which is a hole of no height in the middle of a row.
				if ( size.x > 0 && !WidthFromLayout ) Style.Width = size.x;
				if ( size.y > 0 ) Style.Height = size.y;
			}
		}

		//Where the box ended up, said in the widget's own coordinates - which are relative to the widget this markup belongs to,
		//because that is what the widget's panel is a child of. Panel boxes are in screen pixels and widgets are not, hence the scale.
		var scale = ScaleFromScreen;
		var box = Box.Rect;

		widget.Position = (box.Position - host.Surface.Box.Rect.Position) * scale;
		widget.Size = box.Size * scale;
	}
}
