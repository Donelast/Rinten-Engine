using Rinten.UI;

namespace Editor;

/// <summary>Base for a tool's settings panel; an EditorPanel so [Shortcut]s are live while the tool is</summary>
public class ToolSettingsPanel : EditorPanel
{
	public ToolSettingsPanel()
	{
		AddClass( "tool-settings" );
	}

	/// <summary>The tool's name and mark, at the top.</summary>
	public void AddTitle( string title, string icon = "people" )
	{
		var row = AddChild<Panel>( "tool-title" );

		var mark = row.AddChild<Panel>( "mark" );
		ShowMark( mark.AddChild<IconPanel>( "glyph" ), icon );

		var label = row.AddChild<Rinten.UI.Label>( "text" );
		label.Text = title;
	}

	/// <summary>A letter of the icon font.</summary>
	public static void ShowMark( IconPanel mark, string name )
	{
		mark.Text = name ?? string.Empty;
	}

	/// <summary>A named block of settings.</summary>
	public SidebarGroupPanel AddGroup( string title, bool collapsible = false )
		=> AddChild( new SidebarGroupPanel( title, collapsible ) );

	/// <summary>A row of things standing side by side, straight on the panel.</summary>
	public Panel AddRow() => AddChild<Panel>( "row gap-2" );

	/// <summary>An icon on a chip - what a tool's modes and one-shot actions are pressed by.</summary>
	public EditorToolButton AddButton( Panel into, IToolbarCommand command )
	{
		var button = new EditorToolButton( command );
		button.AddClass( "chip" );

		(into ?? this).AddChild( button );

		return button;
	}

	/// <summary>The same, said the short way: what it is called, what key does it, and when it can be pressed.</summary>
	public EditorToolButton AddButton( Panel into, string title, string icon, string keybind, Action pressed, Func<bool> enabled = null )
	{
		var tip = string.IsNullOrEmpty( keybind ) ? title : $"{title} [{EditorShortcuts.GetKeys( keybind )}]";

		return AddButton( into, new ToolbarCommand( icon, tip, pressed ) { Enabled = enabled } );
	}

	/// <summary>A grid for a group's buttons, <paramref name="columns"/> to a row - see <see cref="ButtonGridPanel"/>.</summary>
	public ButtonGridPanel AddButtons( Panel into, int columns = 2 )
		=> (into ?? this).AddChild( new ButtonGridPanel( columns ) );

	/// <summary>The same, straight into a group.</summary>
	public ButtonGridPanel AddButtons( SidebarGroupPanel group, int columns = 2 )
		=> AddButtons( group.Content, columns );

	/// <summary>A button with its picture, its word and its key, in the next cell of a grid.</summary>
	public SidebarButtonPanel AddButton( ButtonGridPanel grid, string title, string icon, string keybind, Action pressed,
		Func<bool> enabled = null, Func<bool> active = null )
	{
		var button = new SidebarButtonPanel( title, icon, keybind, pressed )
		{
			EnabledWhen = enabled,
			ActiveWhen = active,
		};

		button.Sync();

		grid.Add( button );

		return button;
	}

	/// <summary>A button with words on it - apply, cancel, the end of what a tool is doing.</summary>
	public TextButtonPanel AddTextButton( Panel into, string text, string icon, Action pressed )
	{
		var button = new TextButtonPanel { Text = text, Icon = icon, Pressed = pressed };

		(into ?? this).AddChild( button );

		return button;
	}

	/// <summary>Rows of the tool's own properties, edited the way the inspector edits them.</summary>
	public Inspectors.ControlSheet AddSheet( Panel into = null )
		=> (into ?? this).AddChild( new Inspectors.ControlSheet() );

	/// <summary>What the keyboard does, folded away under a heading.</summary>
	public void AddShortcuts( params (string Name, string Keys)[] shortcuts )
	{
		if ( shortcuts.Length == 0 ) return;

		var group = AddGroup( "Shortcuts", collapsible: true );

		group.Content.AddChild( new ShortcutListPanel { Shortcuts = shortcuts } );
	}

	/// <summary>The same, for shortcuts that are declared with <c>[Shortcut]</c> and know their own keys.</summary>
	public void AddShortcuts( params string[] identifiers )
	{
		AddShortcuts( identifiers
			.Select( id => EditorShortcuts.Entries.FirstOrDefault( e => e.Identifier == id ) )
			.Where( e => e is not null )
			.Select( e => (e.Name, e.DisplayKeys) )
			.ToArray() );
	}

	/// <summary>Fills the rest of the column, so what is above it stays at the top.</summary>
	public void AddStretch() => AddChild<Panel>( "grow" );
}
