using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

//The furniture around the content: docks, the overlay layer, drag and drop, and the widget chrome that is still there.
static class ChromeExamples
{
	const string Page = "Chrome";

	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Confirm on the overlay" ), Icon( "help" )]
	[Description( "EditorModal on the window's EditorOverlay: over everything in this window, nothing outside it, no second native window." )]
	static Panel Confirm()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Nothing asked yet." );

		TextButtonPanel button = null;
		button = Example.Button( Example.Row( example ), "Ask something", "help", () =>
			EditorModal.Confirm( button, "Delete everything?", "This is only a question. Nothing is deleted either way.",
				() => note.Text = "Confirmed.", "Delete", "Keep" ), "bad" );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Drag and drop" ), Icon( "drag_indicator" )]
	[Description( "EditorDrag from a panel to an IEditorDropTarget panel: the preview on the overlay, the target lighting up, the drop landing." )]
	static Panel DragAndDrop()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Drag the tile onto the box." );

		var row = Example.Row( example, "gap-3" );

		row.AddChild( new DragSourcePanel( "gallery:tile" ) );
		row.AddChild( new DropTargetPanel( text => note.Text = $"Dropped '{text}'." ) );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 2 )]
	[Title( "Docks window" ), Icon( "dashboard" )]
	[Description( "A window arranged the way the editor's own is: docks around a graph, and tabs where two share an edge. The dock layer is the widget layer that is still standing." )]
	static Panel DocksWindow()
	{
		var example = Example.Column();

		Example.Button( Example.Row( example ), "Open docks window", "open_in_new", OpenDocksWindow );
		Example.Note( example, "Drag a dock by its tab, drop it on another edge, drag a node in the middle." );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 3 )]
	[Title( "Widget chrome window" ), Icon( "widgets" )]
	[Description( "The widgets the dock chrome is still drawn with: buttons, fields, list view, splitter. Goes when the dock layer is panels." )]
	static Panel WidgetChromeWindow()
	{
		var example = Example.Column();

		Example.Button( Example.Row( example ), "Open widget window", "open_in_new", OpenWidgetWindow );

		return example;
	}

	static void OpenDocksWindow()
	{
		var window = new Window { WindowTitle = "Panel Gallery docks", Size = new Vector2( 900, 600 ), DeleteOnClose = true };
		window.SetWindowIcon( "dashboard" );

		var docks = new DockManager( null );
		window.Canvas = docks;

		//The middle is a graphics view with a few items in it, which is what every node graph in the editor is made of.
		var graph = new GraphicsView( null );

		for ( var i = 0; i < 6; i++ )
			graph.Add( new GalleryNode { Position = new Vector2( -320 + (i % 3) * 220, -100 + (i / 3) * 140 ), Text = $"Node {i + 1}" } );

		docks.SetCentralWidget( graph );

		docks.AddDock( "Scene", "list", Filled( "The scene tree" ), DockArea.Left );
		docks.AddDock( "Inspector", "tune", Filled( "What is selected" ), DockArea.Right );
		docks.AddDock( "Console", "terminal", Filled( "What the engine said" ), DockArea.Bottom );
		docks.AddDock( "Output", "receipt_long", Filled( "What the compiler said" ), DockArea.Bottom );

		window.Show();
	}

	//A dock slot with a panel in it, the way a real dock is: a well with a few list rows.
	static Widget Filled( string text )
	{
		var host = new PanelDockHost();
		var panel = new Panel();

		panel.AddClass( "column" );
		panel.AddClass( "well" );

		for ( var i = 0; i < 4; i++ )
		{
			var row = panel.AddChild<Panel>( "list-row row gap-2" );
			row.SetClass( "even", i % 2 == 1 );
			row.Style.Height = 22;
			row.Style.PaddingLeft = 8;

			var icon = row.AddChild<Rinten.UI.Label>( "material-icon" );
			icon.Text = i == 0 ? "folder" : "description";

			Example.Text( row, i == 0 ? text : $"{text}, line {i}" );
		}

		host.SetContent( panel );

		return host;
	}

	static void OpenWidgetWindow()
	{
		var window = new Window { WindowTitle = "Panel Gallery widgets", Size = new Vector2( 900, 760 ), DeleteOnClose = true };
		window.SetWindowIcon( "widgets" );

		var canvas = new Widget( null );
		var layout = Layout.Column();

		canvas.Layout = layout;
		layout.Margin = 16;
		layout.Spacing = 8;

		layout.Add( new Editor.Label( "Widgets, drawn by the editor's own renderer:" ) );

		var row = layout.AddRow();
		row.Spacing = 8;

		row.Add( new Editor.Button( "A button" ) { Clicked = () => Log.Info( "widget window: the button was clicked" ) } );
		row.Add( new Editor.Button.Primary( "The important one" ) );
		row.AddStretchCell();

		layout.Add( new Editor.Checkbox( "A check box" ) );
		layout.Add( new LineEdit( "and something typed into a field" ) );

		var combo = new ComboBox( null );
		combo.AddItem( "Vulkan", null, () => Log.Info( "widget window: vulkan" ), selected: true );
		combo.AddItem( "Direct3D" );
		combo.AddItem( "Metal", null, null, "Not on this machine", enabled: false );

		layout.Add( combo );

		var text = new TextEdit( null );
		text.PlainText = "Several lines of text,\ndrawn line by line,\nscrolled with the wheel.";
		text.MinimumHeight = 60;

		layout.Add( text );

		var list = new ListView( null );

		list.SetItems( Enumerable.Range( 1, 200 ).Select( x => (object)$"Item number {x}" ) );
		list.ItemSize = new Vector2( 0, 22 );
		list.MinimumHeight = 90;

		list.ItemPaint = item =>
		{
			if ( Paint.HasSelected ) { Paint.ClearPen(); Paint.SetBrush( Theme.Primary.WithAlpha( 0.6f ) ); Paint.DrawRect( item.Rect, 3 ); }
			else if ( Paint.HasMouseOver ) { Paint.ClearPen(); Paint.SetBrush( Theme.SurfaceBackground ); Paint.DrawRect( item.Rect, 3 ); }

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.Text );
			Paint.DrawText( item.Rect.Shrink( 8, 0 ), item.Object as string, TextFlag.LeftCenter | TextFlag.SingleLine );
		};

		layout.Add( list, 1 );

		var splitter = new Splitter( canvas ) { IsHorizontal = true };
		splitter.MinimumHeight = 80;

		var left = new Widget( null );
		left.Layout = Layout.Column();
		left.Layout.Margin = 8;
		left.Layout.Add( new Editor.Label( "Left of the splitter" ) );
		left.Layout.AddStretchCell();

		var right = new Widget( null );
		right.Layout = Layout.Column();
		right.Layout.Margin = 8;
		right.Layout.Add( new Editor.Label( "Right of it - drag the line" ) );
		right.Layout.AddStretchCell();

		splitter.AddWidget( left );
		splitter.AddWidget( right );

		layout.Add( splitter, 1 );

		window.Canvas = canvas;
		window.Show();
	}

	//A node with a name on it, for the graph in the docks window.
	sealed class GalleryNode : GraphicsItem
	{
		public string Text { get; set; } = "Node";

		public GalleryNode()
		{
			Size = new Vector2( 180, 90 );

			Movable = true;
			Selectable = true;
			HoverEvents = true;
		}

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;

			Paint.ClearPen();
			Paint.SetBrush( Paint.HasSelected ? Theme.Primary.WithAlpha( 0.8f ) : Theme.SurfaceBackground );
			Paint.DrawRect( BoundingRect, 6 );

			Paint.ClearBrush();
			Paint.SetPen( Paint.HasMouseOver ? Theme.Text : Theme.Border );
			Paint.DrawRect( BoundingRect, 6 );

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.Text );
			Paint.DrawText( BoundingRect, Text, TextFlag.Center );
		}
	}

	//A tile that starts an editor drag when pressed.
	sealed class DragSourcePanel : Panel
	{
		readonly string payload;

		public DragSourcePanel( string payload )
		{
			this.payload = payload;

			AddClass( "drop-source" );

			Example.Text( this, "Drag me" );
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			base.OnMouseDown( e );

			if ( e.MouseButton != MouseButtons.Left ) return;

			EditorDrag.Begin( this, new DragData { Text = payload }, null );

			e.StopPropagation();
		}
	}

	//A box that takes what the tile drags.
	sealed class DropTargetPanel : Panel, IEditorDropTarget
	{
		readonly Action<string> dropped;
		readonly Label label;

		public DropTargetPanel( Action<string> dropped )
		{
			this.dropped = dropped;

			AddClass( "drop-target" );

			label = Example.Text( this, "Drop here" );
		}

		public bool AcceptEditorDrops => true;

		public DropAction OnEditorDragHover( Widget.DragEvent ev )
		{
			var ok = ev.Data?.Text?.StartsWith( "gallery:" ) == true;

			SetClass( "is-drop-target", ok );

			return ok ? DropAction.Copy : DropAction.Ignore;
		}

		public void OnEditorDragDrop( Widget.DragEvent ev )
		{
			SetClass( "is-drop-target", false );
			SetClass( "has-dropped", true );

			label.Text = "Got it";
			dropped?.Invoke( ev.Data?.Text );
		}

		public void OnEditorDragLeave() => SetClass( "is-drop-target", false );
	}
}
