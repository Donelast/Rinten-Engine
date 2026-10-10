using Rinten.UI;

namespace Editor;

public class ToolSidebarWidget : Widget
{
	public ToolSidebarWidget( Widget parent = null ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;
		Layout.Margin = 8;
	}

	//A chip over the viewport: opaque, or the icon is lost in the picture.
	internal static void Plate( IconButton button )
	{
		button.Background = Theme.WindowBackground;
		button.BackgroundActive = Theme.Primary;
		button.Foreground = Theme.Ink;
		button.ForegroundActive = Theme.Ink;
	}

	public void AddTitle( string title, string icon = "people" )
	{
		var titleRow = Layout.AddRow();
		titleRow.Margin = new Margin( 0, 0, 0, 8 );
		titleRow.Spacing = 4;

		var iconLabel = titleRow.Add( new IconButton( icon ), 0 );
		iconLabel.IconSize = 18;
		Plate( iconLabel );
		iconLabel.Foreground = Theme.Primary;

		var titleLabel = titleRow.Add( new Label.Header( title ), 1 );
	}

	public Layout AddGroup( string title, SizeMode sizeMode = SizeMode.CanShrink, bool collapsible = false )
	{
		var group = CreateGroupWidget( title, sizeMode, collapsible );
		Layout.Add( group );
		return group.ContentLayout;
	}

	//Creates a group widget parented to the given widget without adding it to this sidebar's
	//layout. Use this to build groups that belong to a separately-controlled container.
	internal static SidebarGroupWidget CreateGroupWidget( string title, SizeMode sizeMode = SizeMode.CanShrink, bool collapsible = false )
	{
		var group = new SidebarGroupWidget();
		group.Title = title;
		group.VerticalSizeMode = sizeMode;
		group.Collapsible = collapsible;
		group.RestoreState();
		return group;
	}

	/// <summary>Where a group's buttons go: two to a row, each as wide as the other.</summary>
	public ButtonGrid AddButtons( Layout group, int columns = 2 )
	{
		var grid = new ButtonGrid( columns );
		group.Add( grid );
		return grid;
	}

	public SidebarButton CreateButton( string text, string icon, string keybind, Action clicked, bool enabled, ButtonGrid grid, bool active = false )
	{
		var button = CreateButton( text, icon, keybind, clicked, enabled, active );
		grid?.Add( button );
		return button;
	}

	public SidebarButton CreateButton( string text, string icon, string keybind, Action clicked, bool enabled, Layout addToLayout = null, bool active = false )
	{
		var button = CreateButton( text, icon, keybind, clicked, enabled, active );
		addToLayout?.Add( button );
		return button;
	}

	static SidebarButton CreateButton( string text, string icon, string keybind, Action clicked, bool enabled, bool active )
	{
		return new SidebarButton( text, icon, keybind, clicked )
		{
			Enabled = enabled,
			IsActive = active,
		};
	}

	/// <summary>Add a collapsible "Shortcuts" section listing the given shortcut identifiers.</summary>
	public void AddShortcuts( params string[] identifiers )
	{
		AddShortcuts( identifiers
			.Select( id => EditorShortcuts.Entries.FirstOrDefault( e => e.Identifier == id ) )
			.Where( e => e is not null )
			.Select( e => (e.Name, e.DisplayKeys) )
			.ToArray() );
	}

	/// <summary>Add a collapsible "Shortcuts" section with manual name/key pairs for shortcuts that don't have a [Shortcut] attribute (e.g. "Lasso", "Alt + Click").</summary>
	public void AddShortcuts( params (string Name, string Keys)[] shortcuts )
	{
		if ( shortcuts.Length == 0 ) return;

		var group = AddGroup( "Shortcuts", collapsible: true );
		for ( var i = 0; i < shortcuts.Length; i++ )
			group.Add( new ShortcutRow( shortcuts[i].Name, shortcuts[i].Keys, i ) );
	}
}

/// <summary>A grid of equal columns that fills left to right, top to bottom.</summary>
public sealed class ButtonGrid : Widget
{
	readonly GridLayout _grid;
	readonly int _columns;
	int _count;

	public ButtonGrid( int columns ) : base( null )
	{
		_columns = Math.Max( 1, columns );

		_grid = Layout.Grid();
		_grid.Spacing = 4;
		_grid.SetColumnStretch( Enumerable.Repeat( 1.0f, _columns ).ToArray() );
		Layout = _grid;

		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public T Add<T>( T widget ) where T : Widget
	{
		_grid.AddCell( _count % _columns, _count / _columns, widget );
		_count++;
		return widget;
	}
}

/// <summary>Tool button with picture, label and key</summary>
public sealed class SidebarButton : Widget
{
	/// <summary>Tall enough for a word and a picture, and no taller.</summary>
	public const float RowHeight = 26.0f;

	const float IconSize = 16.0f;

	public string Text { get; }
	public string Icon { get; }
	public Action Clicked { get; set; }

	readonly string _key;
	bool _active;

	/// <summary>Whether this is the one that is on, where it is one of a set.</summary>
	public bool IsActive
	{
		get => _active;
		set
		{
			if ( _active == value ) return;

			_active = value;
			Update();
		}
	}

	public SidebarButton( string text, string icon, string keybind, Action clicked ) : base( null )
	{
		Text = text;
		Icon = icon;
		Clicked = clicked;

		FixedHeight = RowHeight;
		HorizontalSizeMode = SizeMode.Flexible;
		Cursor = CursorShape.Finger;

		var keys = string.IsNullOrEmpty( keybind ) ? null : EditorShortcuts.GetDisplayKeys( keybind );
		if ( string.IsNullOrWhiteSpace( keys ) ) keys = null;

		ToolTip = keys is null ? text : $"{text} [{keys}]";

		//Written on the button only where it fits beside the word. A chord with two modifiers is in the tooltip like everything else.
		_key = keys is { Length: <= 9 } ? keys : null;
	}

	protected override Vector2 SizeHint() => new( 96, RowHeight );

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( !Enabled ) return;

		base.OnMouseClick( e );

		Clicked?.Invoke();

		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		var active = Enabled && IsActive;

		//Grey on the black of the panel, lifted under the pointer, and the picked plate when on - the words and glyph on it stay white.
		var bg = active ? Theme.SelectedBackground : Theme.SurfaceLightBackground;

		if ( Enabled && !active )
		{
			if ( Paint.HasPressed ) bg = bg.WithAlphaMultiplied( 2.0f );
			else if ( Paint.HasMouseOver ) bg = bg.WithAlphaMultiplied( 1.6f );
		}

		if ( !Enabled ) bg = bg.WithAlphaMultiplied( 0.4f );

		Paint.ClearPen();
		Paint.SetBrush( bg );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		var ink = Enabled ? Theme.Ink : Theme.InkOff;
		var rect = LocalRect.Shrink( 7, 0 );

		Paint.SetPen( ink );

		if ( !string.IsNullOrEmpty( Icon ) )
		{
			Paint.DrawIcon( new Rect( rect.Left, rect.Center.y - IconSize * 0.5f, IconSize, IconSize ), Icon, IconSize, TextFlag.Center );
			rect.Left += IconSize + 6;
		}

		var keyWidth = 0.0f;

		if ( _key is not null )
		{
			Paint.SetDefaultFont( 6 );
			keyWidth = Paint.MeasureText( _key ).x + 6;
		}

		Paint.SetDefaultFont( 7 );

		var textRect = rect;
		textRect.Right -= keyWidth;

		Paint.SetPen( ink );
		Paint.DrawText( textRect, Paint.GetElidedText( Text, textRect.Width, flags: TextFlag.LeftCenter ), TextFlag.LeftCenter | TextFlag.SingleLine );

		if ( _key is null ) return;

		Paint.SetDefaultFont( 6 );
		Paint.SetPen( ink.WithAlphaMultiplied( Enabled ? 0.5f : 0.7f ) );
		Paint.DrawText( rect, _key, TextFlag.RightCenter );
	}
}

//A single shortcut row: name on the left, keys on the right, with alternating background.
file class ShortcutRow : Widget
{
	readonly string _name;
	readonly string _keys;
	readonly int _index;

	public ShortcutRow( string name, string keys, int index )
	{
		_name = name;
		_keys = keys;
		_index = index;
		FixedHeight = 20;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( _index % 2 == 1 ? Theme.WidgetBackground.Darken( 0.1f ) : Theme.WidgetBackground );
		Paint.DrawRect( LocalRect, 2 );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;
		Paint.SetDefaultFont( 7 );

		var rect = LocalRect.Shrink( 8, 0 );
		Paint.SetPen( Theme.Text.WithAlpha( 0.7f ) );
		Paint.DrawText( rect, _name, TextFlag.LeftCenter );
		Paint.SetPen( Theme.Text.WithAlpha( 0.4f ) );
		Paint.DrawText( rect, _keys, TextFlag.RightCenter );
	}
}

internal class SidebarGroupWidget : Widget
{
	public readonly Layout ContentLayout;

	public string Title { get; set; }
	public bool Collapsible { get; set; }

	Widget _contentWidget;
	bool _collapsed;
	bool _titleHovered;

	bool Collapsed
	{
		get => _collapsed;
		set
		{
			_collapsed = value;
			_contentWidget.Visible = !value;
			Layout.Margin = value ? new Margin( 8, 16, 8, 0 ) : new Margin( 8, 16, 8, 8 );
			if ( Collapsible ) EditorCookie.Set( $"SidebarGroup.{Title}", value );

			//The frame is drawn only while open, and the chevron turns.
			Update();
		}
	}

	public SidebarGroupWidget( Widget parent = null ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;
		Layout.Margin = new Margin( 8, 16, 8, 8 );

		_contentWidget = new Widget( this );
		_contentWidget.Layout = Layout.Column();
		_contentWidget.Layout.Spacing = 4;
		Layout.Add( _contentWidget );

		ContentLayout = _contentWidget.Layout;

		HorizontalSizeMode = SizeMode.Expand | SizeMode.CanGrow;
		VerticalSizeMode = SizeMode.CanShrink;
		MouseTracking = true;

		//Assigned, not overridden: widgets with children skip canvas drawing otherwise
		OnPaintOverride = PaintGroup;
	}

	public void RestoreState()
	{
		if ( !Collapsible ) return;
		_collapsed = EditorCookie.Get( $"SidebarGroup.{Title}", false );
		_contentWidget.Visible = !_collapsed;
		Layout.Margin = _collapsed ? new Margin( 8, 16, 8, 0 ) : new Margin( 8, 16, 8, 8 );
	}

	protected override Vector2 SizeHint()
	{
		return 0;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		var wasHovered = _titleHovered;
		_titleHovered = Collapsible && e.LocalPosition.y < 14;
		if ( wasHovered != _titleHovered ) Update();
	}

	protected override void OnMouseLeave()
	{
		if ( _titleHovered )
		{
			_titleHovered = false;
			Update();
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( !Collapsible ) return;
		if ( e.LocalPosition.y > 14 ) return;

		Collapsed = !Collapsed;
		e.Accepted = true;
	}

	private bool PaintGroup()
	{
		var controlRect = Paint.LocalRect;
		controlRect.Top += 6;
		controlRect = controlRect.Shrink( 0, 0, 1, 1 );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		if ( !Collapsed )
		{
			Paint.SetBrushAndPen( Theme.Text.WithAlpha( 0.01f ), Theme.Text.WithAlpha( 0.1f ) );
			Paint.DrawRect( controlRect, 4 );
		}

		var textAlpha = _titleHovered ? 1.0f : 0.6f;
		Paint.SetPen( Theme.TextControl.WithAlpha( textAlpha ) );
		Paint.SetDefaultFont( 7, 500 );

		if ( Collapsible )
		{
			var icon = Collapsed ? "chevron_right" : "expand_more";
			Paint.DrawIcon( new Rect( 2, -2, 12, 14 ), icon, 10 );
			Paint.DrawText( new Vector2( 16, 0 ), Title );
			Cursor = CursorShape.Finger;
		}
		else
		{
			Paint.DrawText( new Vector2( 12, 0 ), Title );
		}

		return true;
	}
}
