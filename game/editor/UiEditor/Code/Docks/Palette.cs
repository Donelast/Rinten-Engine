using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.UiEditor;

/// <summary>Palette dock: plain tags and panel types to add; click adds inside the selection, Shift beside it</summary>
[SkipHotload]
public sealed class Palette : PanelDockHost
{
	readonly PalettePanel _panel;

	public Palette( UiEditorWindow window ) : base( null )
	{
		Name = "Palette";
		WindowTitle = "Palette";
		SetWindowIcon( "dashboard_customize" );
		MinimumWidth = 180;

		_panel = new PalettePanel( window );

		SetContent( _panel );
	}

	/// <summary>Read the types again - after a hotload, a project may have new panels.</summary>
	public void Refresh() => _panel.Rebuild();

	/// <summary>Another document can see other types.</summary>
	public void SetDocument( RazorDocument document ) => _panel.Rebuild();
}

//A filter, and the entries under it in their groups.
[StyleSheet( "Palette.cs.scss" )]
internal sealed class PalettePanel : EditorPanel
{
	//One thing the palette can add.
	sealed record Entry( string Title, string Tag, string Classes, string Text, string Icon, string Tooltip );

	static readonly Entry[] Plain =
	{
		new( "div", "div", "", null, "crop_square", "A box: the tag everything else is built out of" ),
		new( "label", "label", "", "Label", "text_fields", "Words" ),
		new( "img", "img", "", null, "image", "A picture, from its src" ),
		new( "button", "button", "", "Button", "smart_button", "A button with words on it" ),
	};

	internal readonly UiEditorWindow _window;
	readonly Panel _groups;

	string _filter = "";

	public PalettePanel( UiEditorWindow window )
	{
		_window = window;

		AddClass( "ui-palette" );

		var bar = AddChild<PanelToolbar>();
		bar.AddFilter( "Filter", text => { _filter = text ?? ""; Rebuild(); } );

		var well = AddChild<Panel>( "well" );
		_groups = well.AddChild<Panel>( "groups" );

		Rebuild();
	}

	public void Rebuild()
	{
		_groups.DeleteChildren( true );

		AddGroup( "Tags", Plain );

		//Panel types the document can use, only from assemblies it can see (engine, base, own project)
		var own = new List<Entry>();
		var library = new List<Entry>();
		var visible = VisibleAssemblies();
		var ownAssembly = _window.Document?.Type?.TargetType.Assembly;

		foreach ( var type in TypeLibrary.GetTypes<Panel>() )
		{
			if ( type.IsAbstract || type.IsGenericType ) continue;
			if ( type.TargetType == typeof( Panel ) || type.TargetType == typeof( RootPanel ) ) continue;
			if ( !type.TargetType.IsPublic && !type.TargetType.IsNestedPublic ) continue;
			if ( type.HasAttribute<HideAttribute>() ) continue;
			if ( !visible.Contains( type.TargetType.Assembly ) ) continue;

			var title = type.Title ?? type.Name;
			var tooltip = string.IsNullOrEmpty( type.Description ) ? type.FullName : type.Description;
			var entry = new Entry( title, type.Name, "", null, type.Icon ?? "widgets", tooltip );

			//The project's own panels first and apart; the library's after.
			if ( type.TargetType.Assembly == ownAssembly ) own.Add( entry );
			else library.Add( entry );
		}

		own.Sort( ( a, b ) => string.Compare( a.Title, b.Title, StringComparison.OrdinalIgnoreCase ) );
		library.Sort( ( a, b ) => string.Compare( a.Title, b.Title, StringComparison.OrdinalIgnoreCase ) );

		AddGroup( "Project", own );
		AddGroup( "Library", library );
	}

	//The assemblies a tag in the document may name a type from.
	HashSet<System.Reflection.Assembly> VisibleAssemblies()
	{
		var set = new HashSet<System.Reflection.Assembly> { typeof( Panel ).Assembly };

		//The base library, by a panel everyone has from it.
		if ( TypeLibrary.GetType<Panel>( "Button" ) is { } button ) set.Add( button.TargetType.Assembly );

		if ( _window.Document?.Type is { } own ) set.Add( own.TargetType.Assembly );

		return set;
	}

	void AddGroup( string title, IEnumerable<Entry> entries )
	{
		var shown = entries.Where( Passes ).ToList();
		if ( shown.Count == 0 ) return;

		var group = _groups.AddChild<Panel>( "group" );

		var heading = group.AddChild<UILabel>( "heading" );
		heading.Tokenize = false;
		heading.Text = title;

		var rows = group.AddChild<Panel>( "rows" );

		foreach ( var entry in shown )
			rows.AddChild( new PaletteRow( this, entry ) );
	}

	bool Passes( Entry entry )
	{
		if ( string.IsNullOrWhiteSpace( _filter ) ) return true;

		return entry.Title.Contains( _filter, StringComparison.OrdinalIgnoreCase )
			|| entry.Tag.Contains( _filter, StringComparison.OrdinalIgnoreCase );
	}

	//Add an entry into the selection - or beside it, with Shift - and select what was added.
	void AddEntry( Entry entry, bool beside )
	{
		var target = _window.Selection ?? _window.Root;

		if ( target is null )
		{
			Log.Warning( "UI editor: nothing to add into - open a file first" );
			return;
		}

		var made = beside && !ReferenceEquals( target, _window.Root )
			? _window.AddSibling( target, entry.Tag, entry.Classes, entry.Text )
			: _window.AddChild( target, entry.Tag, entry.Classes, entry.Text );

		if ( made is not null ) _window.Select( made );
	}

	//One entry: an icon and a name.
	sealed class PaletteRow : Panel
	{
		readonly PalettePanel _palette;
		readonly Entry _entry;

		public PaletteRow( PalettePanel palette, Entry entry )
		{
			_palette = palette;
			_entry = entry;

			AddClass( "palette-row" );
			Tooltip = entry.Tooltip;

			var icon = AddChild<UILabel>( "material-icon icon" );
			icon.Tokenize = false;
			icon.Text = entry.Icon;

			var title = AddChild<UILabel>( "title" );
			title.Tokenize = false;
			title.Text = entry.Title;

			var tag = AddChild<UILabel>( "tag" );
			tag.Tokenize = false;
			tag.Text = entry.Tag == entry.Title ? "" : $"<{entry.Tag}>";
		}

		bool _dragged;

		protected override void OnMouseDown( MousePanelEvent e )
		{
			base.OnMouseDown( e );

			if ( e.Button != "mouseleft" ) return;

			e.StopPropagation();
			_dragged = false;
		}

		//A click arms the tag: the next press on the canvas draws it where the pointer goes. Shift adds it beside
		//the selection at once, a double click inside it. A drag that ended somewhere else has already landed.
		protected override void OnMouseUp( MousePanelEvent e )
		{
			base.OnMouseUp( e );

			if ( e.Button != "mouseleft" || _dragged ) return;

			if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			{
				_palette.AddEntry( _entry, beside: true );
				return;
			}

			_palette._window.Arm( new Carry { Tag = _entry.Tag, Classes = _entry.Classes, Text = _entry.Text } );
		}

		protected override void OnDoubleClick( MousePanelEvent e )
		{
			base.OnDoubleClick( e );

			if ( e.Button != "mouseleft" ) return;

			_palette._window.Disarm();
			_palette.AddEntry( _entry, beside: false );
		}

		public override void Tick()
		{
			base.Tick();

			var armed = _palette._window.Armed;
			SetClass( "armed", armed is not null && armed.Tag == _entry.Tag && armed.Classes == _entry.Classes );
		}

		public override bool WantsDrag => true;

		protected override void OnDragStart( DragEvent e )
		{
			_dragged = true;
			_palette._window.BeginCarry( new Carry { Tag = _entry.Tag, Classes = _entry.Classes, Text = _entry.Text } );
		}

		protected override void OnDragEnd( DragEvent e )
		{
			//The drop, if there was one, came first; what is left is let go.
			_palette._window.EndCarry();
		}
	}
}
