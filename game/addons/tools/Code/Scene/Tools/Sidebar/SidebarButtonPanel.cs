using Rinten.UI;

namespace Editor;

/// <summary>A tool's button: a picture, the word for it, and the key it is on.</summary>
public sealed class SidebarButtonPanel : Panel
{
	readonly IconPanel icon;
	readonly Rinten.UI.Label text;

	/// <summary>What pressing it does.</summary>
	public Action Pressed { get; set; }

	//A mesh tool's buttons turn on and off as the selection changes under them, and nothing tells the button when that happened - it has to look.
	/// <summary>When it can be pressed, asked again each frame. Always, where nothing was said.</summary>
	public Func<bool> EnabledWhen { get; set; }

	/// <summary>Whether it is the one that is on, where it is one of a set. Never, where nothing was said.</summary>
	public Func<bool> ActiveWhen { get; set; }

	public SidebarButtonPanel( string title, string iconName, string keybind, Action pressed )
	{
		Pressed = pressed;

		AddClass( "tool-button" );

		icon = AddChild<IconPanel>( "icon" );
		ShowIcon( iconName );

		text = AddChild<Rinten.UI.Label>( "text" );
		text.Text = title;

		var keys = string.IsNullOrEmpty( keybind ) ? null : EditorShortcuts.GetDisplayKeys( keybind );
		if ( string.IsNullOrWhiteSpace( keys ) ) keys = null;

		Tooltip = keys is null ? title : $"{title} [{keys}]";

		//Written on the button only where it fits beside the word. A chord with two modifiers is in the tooltip like everything else.
		if ( keys is { Length: <= 9 } )
		{
			var key = AddChild<Rinten.UI.Label>( "key" );
			key.Text = keys;
		}
	}

	//Two answers, read once a frame, and the panel takes its turn only when one of them moves - the same as a strip button.
	protected override int BuildHash() => HashCode.Combine( IsEnabled, IsActive );

	protected override void OnBuildHashChanged() => Sync();

	bool IsEnabled => EnabledWhen?.Invoke() ?? true;
	bool IsActive => ActiveWhen?.Invoke() ?? false;

	/// <summary>Says what the two questions answer now, rather than on the next frame.</summary>
	public void Sync()
	{
		SetClass( "disabled", !IsEnabled );
		SetClass( "active", IsActive );
	}

	//A letter of the icon font
	void ShowIcon( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
		{
			icon.SetClass( "hidden", true );
			return;
		}

		icon.Text = name;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		e.StopPropagation();

		if ( !IsEnabled ) return;

		Pressed?.Invoke();
	}
}

/// <summary>Where a group's buttons go: two to a row, each as wide as the other.</summary>
public sealed class ButtonGridPanel : Panel
{
	readonly int columns;

	Panel[] cells;
	int used;

	public ButtonGridPanel( int columns = 2 )
	{
		this.columns = Math.Max( 1, columns );

		AddClass( "button-grid" );
	}

	//Hides Panel.Add on purpose: a child added to the grid any other way would sit beside the rows rather than in a cell.
	/// <summary>Puts something in the next cell, starting a row where the last is full.</summary>
	public new T Add<T>( T panel ) where T : Panel
	{
		if ( cells is null || used == columns )
		{
			var row = AddChild<Panel>( "button-row" );

			cells = new Panel[columns];

			for ( var i = 0; i < columns; i++ )
				cells[i] = row.AddChild<Panel>( "cell" );

			used = 0;
		}

		cells[used++].AddChild( panel );

		return panel;
	}
}
