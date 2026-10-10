using Rinten.UI;

namespace Editor.Gallery;

//Toolbars and tool sidebars: the strips of icons every editor view wears.
static class ToolbarExamples
{
	const string Page = "Toolbars";

	//A command with a light on it, for strips that need something to press.
	sealed class ToggleCommand : IToolbarCommand
	{
		readonly Action<ToggleCommand> pressed;

		public ToggleCommand( string icon, string tooltip, Action<ToggleCommand> pressed = null, bool enabled = true )
		{
			Icon = icon;
			ToolTip = tooltip;
			IsEnabled = enabled;

			this.pressed = pressed;
		}

		public string Icon { get; }
		public string ToolTip { get; }
		public bool IsActive { get; set; }
		public bool IsEnabled { get; }

		public void Execute()
		{
			IsActive = !IsActive;
			pressed?.Invoke( this );
		}
	}

	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Toolbar strip" ), Icon( "handyman" )]
	[Description( "ToolbarStrip of EditorToolButtons over IToolbarCommand: the strip a tool hangs in the viewport bar." )]
	static Panel Strip()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Press one." );

		var strip = example.AddChild<ToolbarStrip>();

		strip.AddButton( new ToggleCommand( "near_me", "Select", c => note.Text = $"Select is {(c.IsActive ? "on" : "off")}." ) { IsActive = true } );
		strip.AddButton( new ToggleCommand( "open_with", "Move", c => note.Text = $"Move is {(c.IsActive ? "on" : "off")}." ) );
		strip.AddButton( new ToggleCommand( "360", "Rotate", c => note.Text = $"Rotate is {(c.IsActive ? "on" : "off")}." ) );
		strip.AddButton( new ToggleCommand( "zoom_out_map", "Scale", c => note.Text = $"Scale is {(c.IsActive ? "on" : "off")}." ) );
		strip.AddButton( new ToggleCommand( "block", "Disabled", enabled: false ) );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Toolbar buttons" ), Icon( "smart_button" )]
	[Description( "ToolbarButton, the razor brick behind every icon on the viewport toolbar, in its states." )]
	static Panel Buttons()
	{
		var example = Example.Row();

		example.AddChild( new ToolbarButton { Icon = "save", ToolTip = "At rest" } );
		example.AddChild( new ToolbarButton { Icon = "grid_on", ToolTip = "Active", Active = true } );
		example.AddChild( new ToolbarButton { Icon = "block", ToolTip = "Disabled", Disabled = true } );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 2 )]
	[Title( "Tool settings" ), Icon( "tune" )]
	[Description( "ToolSettingsPanel: the column a scene tool puts beside the viewport - a title, groups, rows of buttons." )]
	static Panel ToolSettings()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Press a shape." );

		var settings = example.AddChild<ToolSettingsPanel>();
		settings.AddTitle( "Brush", "brush" );

		var shape = settings.AddGroup( "Shape", collapsible: true );
		var row = shape.AddRow();

		settings.AddButton( row, "Circle", "circle", "1", () => note.Text = "Circle." );
		settings.AddButton( row, "Square", "square", "2", () => note.Text = "Square." );
		settings.AddButton( row, "Never", "block", "3", () => note.Text = "Should not happen.", () => false );

		var falloff = settings.AddGroup( "Falloff" );
		Example.Text( falloff.Content, "Anything can stand in a group.", "micro" );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 3 )]
	[Title( "Sidebar group" ), Icon( "expand_more" )]
	static Panel SidebarGroups()
	{
		var example = Example.Column();

		var open = example.AddChild( new SidebarGroupPanel( "Collapsible, open", collapsible: true ) );
		Example.Text( open.Content, "Click the header to fold it." );

		var closed = example.AddChild( new SidebarGroupPanel( "Collapsible, closed", collapsible: true ) );
		Example.Text( closed.Content, "You found it." );
		closed.Collapsed = true;

		var fixedGroup = example.AddChild( new SidebarGroupPanel( "Not collapsible", collapsible: false ) );
		Example.Text( fixedGroup.Content, "Always open." );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 4 )]
	[Title( "Tool strips" ), Icon( "view_column" )]
	[Description( "ToolStripPanel over a ToolGroup of IToolCommands: the modes down the side of the scene view, and beside them what the mode that is on can do. Picking, not toggling." )]
	static Panel ToolStrips()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Pick a mode, then one of its options. The lit one stays lit when pressed again." );

		var mode = "objects";
		var option = "move";

		//One exclusive set: what a click picks. The badge on the last is the shape tool's cube-and-plus.
		var modes = new ToolGroup( "Modes", ToolButtonSize.Mode )
			.Add( Mode( "highlight_alt", "Select Objects", "Whole objects, moved by a gizmo.", "objects" ) )
			.Add( Mode( "crop_square", "Select Faces", "The flats between those lines.", "faces" ) )
			.Add( Mode( "timeline", "Select Edges", "The lines between those corners.", "edges" ) )
			.Add( Mode( "scatter_plot", "Select Vertices", "The corners a mesh is made of.", "vertices" ) )
			.Add( new ToolCommand( "view_in_ar", "Create Shape", () => Pick( "shape" ) )
			{
				Description = "Drag one out in the view.",
				Badge = "add",
				Active = () => mode == "shape",
			} );

		//The smaller column beside them: what the mode does to what it picked.
		var options = new ToolGroup( "Move", ToolButtonSize.Option )
			.Add( Option( "open_with", "Move", "move" ) )
			.Add( Option( "360", "Rotate", "rotate" ) )
			.Add( Option( "aspect_ratio", "Resize", "resize" ) )
			.Add( Option( "zoom_out_map", "Scale", "scale" ) )
			.Add( new ToolCommand( "block", "Disabled", () => { } ) { Enabled = () => false } );

		var strips = Example.Row( example, "gap-1" );

		strips.AddChild( new ToolStripPanel( modes ) );
		strips.AddChild( new ToolStripPanel( options ) );

		return example;

		void Pick( string picked )
		{
			mode = picked;
			note.Text = $"{picked}, {option}.";
		}

		IToolCommand Mode( string icon, string title, string about, string key )
			=> new ToolCommand( icon, title, () => Pick( key ) ) { Description = about, Active = () => mode == key };

		IToolCommand Option( string icon, string title, string key )
			=> new ToolCommand( icon, title, () => { option = key; note.Text = $"{mode}, {key}."; } ) { Active = () => option == key };
	}
}
