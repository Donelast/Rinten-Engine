using Rinten.UI;

namespace Editor.Gallery;

//The smallest panels the editor is made of: buttons, boxes, fields, lists to pick from.
static class AtomExamples
{
	const string Page = "Atoms";

	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Text buttons" ), Icon( "smart_button" )]
	[Description( "TextButtonPanel wears .btn from design.scss: one look for the whole editor, color only for yes, no and careful." )]
	static Panel TextButtons()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Nothing pressed yet." );
		var row = Example.Row( example );

		Example.Button( row, "Default", null, () => note.Text = "Default pressed." );
		Example.Button( row, "With icon", "save", () => note.Text = "Save pressed." );
		Example.Button( row, "Yes", "check", () => note.Text = "Yes pressed.", "good" );
		Example.Button( row, "No", "close", () => note.Text = "No pressed.", "bad" );
		Example.Button( row, "Careful", "warning", () => note.Text = "Careful pressed.", "careful" );
		Example.Button( row, "Disabled", "block", () => note.Text = "A disabled button was pressed - that is a bug." ).Disabled = true;

		var gated = Example.Button( Example.Row( example ), "Enabled while the note is short", "rule", () => note.Text = "Now it is longer, and the button goes." );
		gated.EnabledWhen = () => note.Text.Length < 40;

		return example;
	}

	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Base buttons" ), Icon( "radio_button_checked" )]
	[Description( "Rinten.UI.Button and ButtonGroup from the base addon, which a game gets too." )]
	static Panel BaseButtons()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Nothing pressed yet." );

		var row = Example.Row( example );
		row.AddChild( new Rinten.UI.Button( "Plain", "touch_app", () => note.Text = "Plain pressed." ) );
		row.AddChild( new Rinten.UI.Button( "Disabled", "block", () => note.Text = "Disabled pressed - a bug." ) { Disabled = true } );

		var group = example.AddChild<Rinten.UI.ButtonGroup>();
		group.AddButton( "Left", () => note.Text = "Left." );
		group.AddButton( "Middle", () => note.Text = "Middle." );
		group.AddButton( "Right", () => note.Text = "Right." );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 2 )]
	[Title( "Checkbox" ), Icon( "check_box" )]
	static Panel Checkboxes()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Toggle one." );

		var on = example.AddChild<Rinten.UI.Checkbox>();
		on.LabelText = "Starts checked";
		on.Checked = true;
		on.ValueChanged = v => note.Text = $"First is {(v ? "on" : "off")}.";

		var off = example.AddChild<Rinten.UI.Checkbox>();
		off.LabelText = "Starts clear";
		off.ValueChanged = v => note.Text = $"Second is {(v ? "on" : "off")}.";

		return example;
	}

	[PanelGallery, Category( Page ), Order( 3 )]
	[Title( "Text entry" ), Icon( "edit" )]
	[Description( "TextEntry in its modes: a placeholder, numbers only, several lines, an autocomplete." )]
	static Panel TextEntries()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Type into one." );

		var plain = example.AddChild<TextEntry>();
		plain.Placeholder = "Type here";
		plain.OnTextEdited = t => note.Text = $"Plain: {t}";

		var numeric = example.AddChild<TextEntry>();
		numeric.Numeric = true;
		numeric.Text = "42";
		numeric.OnTextEdited = t => note.Text = $"Number: {t}";

		var multi = example.AddChild<TextEntry>();
		multi.Multiline = true;
		multi.Text = "Several lines of text,\nedited in place,\nscrolled with the wheel.";
		multi.AddClass( "fixed-height" );

		var complete = example.AddChild<TextEntry>();
		complete.Placeholder = "Type a color";
		complete.AutoComplete = query => new[] { "red", "green", "blue", "cyan", "magenta", "yellow" }
			.Where( x => x.StartsWith( query ?? "", StringComparison.OrdinalIgnoreCase ) )
			.Cast<object>()
			.ToArray();

		return example;
	}

	//TextEntry with a HistoryCookie: press Enter to remember a line, the up arrow to get it back. On its own, and only
	//inside a window: the history is kept in the editor's cookie store, which a headless self-test does not have.
	[PanelGallery( RequiresWindow = true ), Category( Page ), Order( 3 )]
	[Title( "Text entry with history" ), Icon( "history" )]
	static Panel TextEntryHistory()
	{
		var example = Example.Column();

		var history = example.AddChild<TextEntry>();
		history.Placeholder = "Press Enter, then the up arrow";
		history.HistoryCookie = "panelgallery.history";

		return example;
	}

	[PanelGallery, Category( Page ), Order( 4 )]
	[Title( "Drop-down" ), Icon( "arrow_drop_down_circle" )]
	static Panel DropDowns()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Pick one." );

		var dropdown = example.AddChild<DropDown>();
		dropdown.Options.Add( new Rinten.UI.Option( "Vulkan", "memory", "vulkan" ) );
		dropdown.Options.Add( new Rinten.UI.Option( "Direct3D", "window", "d3d" ) );
		dropdown.Options.Add( new Rinten.UI.Option( "Metal", "apple", "metal" ) );
		dropdown.Selected = dropdown.Options[0];
		dropdown.ValueChanged = v => note.Text = $"Picked {v}.";

		return example;
	}

	[PanelGallery, Category( Page ), Order( 5 )]
	[Title( "Form" ), Icon( "list_alt" )]
	[Description( "Rinten.UI.Form: headed rows of a title and a control, for a dialog that is not a property sheet." )]
	static Panel Forms()
	{
		var example = Example.Column();

		var form = example.AddChild<Form>();
		form.AddHeader( "Account", "person" );
		form.AddRow( "Name", new TextEntry { Text = "garry" } );
		form.AddRow( "Notify", new Rinten.UI.Checkbox { Checked = true } );
		form.AddHeader( "Rendering", "monitor" );
		form.AddRow( "Backend", new TextEntry { Text = "Vulkan" } );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 6 )]
	[Title( "Split container" ), Icon( "vertical_split" )]
	static Panel SplitContainers()
	{
		var example = Example.Column();

		var split = example.AddChild<SplitContainer>();
		split.AddClass( "fixed-height" );
		Example.Tile( split.Left, "Left of the splitter", "well" );
		Example.Tile( split.Right, "Right of it - drag the line", "well" );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 7 )]
	[Title( "Icons and labels" ), Icon( "label" )]
	static Panel IconsAndLabels()
	{
		var example = Example.Column();

		var row = Example.Row( example, "gap-3" );
		row.AddChild( new IconPanel( "home" ) );
		row.AddChild( new IconPanel( "settings" ) );
		row.AddChild( new IconPanel( "delete", "material-icon" ) );

		Example.Text( example, "A plain label." );
		Example.Text( example, "A micro label - the smallest words the editor writes.", "micro" );

		var tip = Example.Text( example, "Hover this for a tooltip." );
		tip.Tooltip = "Tooltips are the engine's own TooltipSystem, styled by .tooltip in design.scss.";

		return example;
	}

	[PanelGallery, Category( Page ), Order( 8 )]
	[Title( "Menu popup" ), Icon( "more_vert" )]
	[Description( "MenuPanel from the base addon, opened under a button." )]
	static Panel Menus()
	{
		var example = Example.Column();
		var note = Example.Note( example, "Open the menu." );

		TextButtonPanel button = null;

		button = Example.Button( Example.Row( example ), "Open menu", "menu", () =>
		{
			var menu = MenuPanel.Open( button );
			menu.AddOption( "add", "New", () => note.Text = "New." );
			menu.AddOption( "folder_open", "Open", () => note.Text = "Open." );
			menu.AddSpacer();
			menu.AddOption( "delete", "Delete", () => note.Text = "Delete." );
		} );

		return example;
	}
}
