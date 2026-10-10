using Rinten.Internal;
using Rinten.UI;

namespace Editor;

/// <summary>Something being carried from one place in the editor to another.</summary>
public static class EditorDrag
{
	/// <summary>Where it was picked up from.</summary>
	public static Widget Source { get; private set; }

	/// <summary>Panel source when the drag did not start on a widget.</summary>
	public static Panel PanelSource { get; private set; }

	/// <summary>What is being carried, or nothing.</summary>
	public static DragData Data => DragData.Current;

	/// <summary>Whether something is being carried right now.</summary>
	public static bool Active => DragData.Current is not null;

	/// <summary>What the thing under the pointer says it would do with it.</summary>
	public static DropAction Action { get; private set; }

	//Whoever is under the pointer and takes drops.
	private static Widget over;

	static IEditorDropTarget panelOver;

	private static EditorOverlay layer;
	private static Panel preview;

	//Picks something up.
	internal static void Begin( Widget source, DragData data, Pixmap image )
	{
		if ( data is null ) return;
		if ( source is null || !source.IsValid ) return;

		Cancel();

		PanelSource = null;
		Source = source;
		DragData.Current = data;
		Action = DropAction.Ignore;

		layer = EditorOverlay.For( source );

		if ( layer is null )
		{
			//Nowhere to draw it, so nothing is being carried - better than a drag nobody can see and nobody can end.
			Cancel();
			return;
		}

		preview = Preview( data, image );

		layer.AddChild( preview );
	}

	/// <summary>Picks something up from a panel.</summary>
	public static void Begin( Panel source, DragData data, Pixmap image )
	{
		if ( data is null ) return;
		if ( source is null || !source.IsValid() ) return;

		Cancel();

		PanelSource = source;
		Source = null;
		DragData.Current = data;
		Action = DropAction.Ignore;

		layer = EditorOverlay.For( source );

		if ( layer is null )
		{
			Cancel();
			return;
		}

		preview = Preview( data, image );

		layer.AddChild( preview );
	}

	//Once a frame while something is being carried: the picture follows the pointer, whoever is under it hears about it, and letting go ends it.
	internal static void Tick( EditorOverlay on )
	{
		if ( !Active ) return;

		//One window's layer runs the drag - the one it started in. The others tick too and would otherwise each run their own copy of it.
		if ( layer != on ) return;

		if ( PanelSource is not null )
		{
			if ( !PanelSource.IsValid() )
			{
				Cancel();
				return;
			}
		}
		else if ( !Source.IsValid )
		{
			Cancel();
			return;
		}

		var at = Application.CursorPosition;

		if ( preview.IsValid() )
		{
			var placed = layer.Place( at + new Vector2( 14, 14 ), preview.Box.Rect.Size * layer.ScaleFromScreen );

			preview.Style.Left = placed.x;
			preview.Style.Top = placed.y;
		}

		var panelTarget = PanelTarget( at );
		var widgetTarget = panelTarget is not null ? null : WidgetTarget( at );

		if ( panelTarget != panelOver )
		{
			panelOver?.OnEditorDragLeave();
			panelOver = panelTarget;
		}

		if ( panelOver is not null )
		{
			ClearWidgetOver();

			var e = PanelEvent( (Panel)panelOver, at );
			Action = panelOver.OnEditorDragHover( e );
		}
		else
		{
			if ( widgetTarget != over )
			{
				ClearWidgetOver();
				over = widgetTarget;
			}

			if ( over is not null )
			{
				var e = Event( over, at );

				over.OnDragHover( e );

				Action = e.Action;
				over.IsBeingDroppedOn = Action != DropAction.Ignore;
			}
			else
			{
				Action = DropAction.Ignore;
			}
		}

		//Let go. Dropped on whatever is under the pointer, or on nothing.
		if ( Application.MouseButtons == MouseButtons.None ) Drop( at );
	}

	//Puts it down.
	private static void Drop( Vector2 at )
	{
		if ( panelOver is not null )
		{
			var e = PanelEvent( (Panel)panelOver, at );
			panelOver.OnEditorDragDrop( e );
			Action = e.Action;
		}
		else if ( over is not null && over.IsValid )
		{
			var e = Event( over, at );
			over.OnDragDrop( e );
			Action = e.Action;
		}

		Cancel();
	}

	//Drops it on the floor: nothing is carried and nothing happened.
	internal static void Cancel()
	{
		panelOver?.OnEditorDragLeave();
		panelOver = null;

		ClearWidgetOver();

		preview?.Delete( true );
		preview = null;

		layer = null;
		Source = null;
		PanelSource = null;

		DragData.Current = null;
	}

	static void ClearWidgetOver()
	{
		if ( over is not null && over.IsValid )
		{
			over.IsBeingDroppedOn = false;
			over.OnDragLeave();
		}

		over = null;
	}

	static IEditorDropTarget PanelTarget( Vector2 at )
	{
		var window = WindowAt( at );
		if ( window?.Shell?.Root is not RootPanel root )
			return null;

		var local = root.ScreenPositionToPanelPosition( at );
		var hit = ((IPanel)root).GetPanelAt( local, visibleOnly: true, needPointerEvents: true ) as Panel;

		for ( var panel = hit; panel is not null; panel = panel.Parent )
		{
			if ( panel is IEditorDropTarget { AcceptEditorDrops: true } target )
				return target;
		}

		return null;
	}

	static Window WindowAt( Vector2 pos )
	{
		foreach ( var window in Window.All )
		{
			if ( window.ScreenRect.IsInside( pos ) )
				return window;
		}

		return null;
	}

	static Widget.DragEvent PanelEvent( Panel panel, Vector2 at )
	{
		return new Widget.DragEvent
		{
			LocalPosition = panel.ScreenPositionToPanelPosition( at ),
			Data = Data,
			KeyboardModifiers = Application.KeyboardModifiers
		};
	}

	//The nearest widget under the pointer that takes drops.
	private static Widget WidgetTarget( Vector2 at )
	{
		var root = Source;

		if ( (root is null || !root.IsValid) && PanelSource is not null )
			root = WidgetRoot( PanelSource );

		if ( root is null || !root.IsValid ) return null;

		while ( root.Parent is not null ) root = root.Parent;

		var under = Under( root, at );

		//Whoever is under the pointer, or the nearest thing above it that said it takes drops - a row in a list is usually not the one listening, the list is.
		while ( under is not null && !under.AcceptDrops ) under = under.Parent;

		return under;
	}

	//Deepest widget at a point, topmost child first; walked because the pressed widget holds the pointer
	internal static Widget Under( Widget widget, Vector2 at )
	{
		if ( widget is null || !widget.IsValid ) return null;
		if ( !widget.Visible ) return null;

		if ( !new Rect( widget.ScreenPosition, widget.Size ).IsInside( at ) ) return null;

		for ( var i = widget.childWidgets.Count - 1; i >= 0; i-- )
		{
			if ( Under( widget.childWidgets[i], at ) is { } found ) return found;
		}

		//A widget that lets ordinary clicks pass through it - an overlay left sized to the whole
		//window, say - should let a drag pass through it too, rather than catching every drop itself.
		if ( widget.TransparentForMouseEvents ) return null;

		return widget;
	}

	static Widget WidgetRoot( Panel panel )
	{
		for ( var p = panel; p is not null; p = p.Parent )
		{
			if ( p is WidgetSurface surface && surface.Widget.IsValid() )
				return surface.Widget;
		}

		var root = panel.FindRootPanel();
		if ( root is null ) return null;

		foreach ( var child in root.Children )
		{
			if ( child is WidgetSurface surface && surface.Widget.IsValid() )
				return surface.Widget;
		}

		return null;
	}

	//What a receiver is told.
	private static Widget.DragEvent Event( Widget widget, Vector2 at )
	{
		return new Widget.DragEvent
		{
			LocalPosition = at - widget.ScreenPosition,
			Data = Data,
			KeyboardModifiers = Application.KeyboardModifiers
		};
	}

	//The thing that follows the pointer. A picture where whoever started the drag drew one, and what it is called otherwise.
	private static Panel Preview( DragData data, Pixmap image )
	{
		var panel = new Panel();

		panel.AddClass( "drag-preview" );

		var picture = image?.picture?.ToTexture( mips: false );

		if ( picture is not null )
		{
			panel.AddClass( "picture" );
			panel.Style.BackgroundImage = picture;
			panel.Style.Width = image.Width;
			panel.Style.Height = image.Height;

			return panel;
		}

		var label = panel.AddChild<Rinten.UI.Label>();

		label.Text = Name( data );
		label.Tokenize = false;

		return panel;
	}

	//What is being carried, in a few words: the last part of the first path, or the text itself where it is not a path.
	internal static string Name( DragData data )
	{
		var text = data?.Text;

		if ( string.IsNullOrWhiteSpace( text ) ) return "…";

		var lines = text.Split( '\n', System.StringSplitOptions.RemoveEmptyEntries );
		var first = lines.Length > 0 ? lines[0].Trim() : text.Trim();

		var cut = first.LastIndexOfAny( new[] { '/', '\\' } );

		if ( cut >= 0 && cut < first.Length - 1 ) first = first[(cut + 1)..];

		//A drag of several things says how many, because the name of the first one is not what is being carried.
		return lines.Length > 1 ? $"{first} +{lines.Length - 1}" : first;
	}
}
