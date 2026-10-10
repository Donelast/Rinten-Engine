using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.UiEditor;

/// <summary>Dock showing the document's panels as a tree; rebuilt on hotload, not walked</summary>
[SkipHotload]
public sealed class Hierarchy : PanelDockHost
{
	readonly UiEditorWindow _window;
	readonly HierarchyPanel _panel;

	//Panels somebody has folded shut.
	readonly HashSet<Panel> _shut = new();

	Panel _root;

	public Hierarchy( UiEditorWindow window ) : base( null )
	{
		_window = window;

		Name = "Hierarchy";
		WindowTitle = "Hierarchy";
		SetWindowIcon( "account_tree" );
		MinimumWidth = 180;

		_panel = new HierarchyPanel( this );

		SetContent( _panel );
	}

	internal UiEditorWindow Window => _window;
	internal Panel Root => _root.IsValid() ? _root : null;

	/// <summary>What the tree is built from.</summary>
	public void SetRoot( Panel root )
	{
		_root = root;
		_shut.Clear();

		Refresh();
	}

	/// <summary>Build the rows again, for when the tree has changed shape.</summary>
	public void Refresh() => _panel?.Rebuild();

	/// <summary>Read the selection again without rebuilding anything.</summary>
	public void NoteSelection() => _panel?.ReadSelection();

	internal bool IsShut( Panel panel ) => _shut.Contains( panel );

	//The tree opened or shut a row: remembered, so it stays so as the rows are read again.
	internal void NoteShut( Panel panel, bool shut )
	{
		if ( shut ) _shut.Add( panel );
		else _shut.Remove( panel );
	}
}

//The tree, and the name of what is being carried riding beside the pointer over it.
[StyleSheet( "Hierarchy.cs.scss" )]
//Made again on hotload with the dock it is in - see DesignCanvas.
[SkipHotload]
internal sealed class HierarchyPanel : EditorPanel
{
	readonly Hierarchy _tree;
	readonly UiHierarchyTree _rows;
	readonly UILabel _note;
	readonly UILabel _ghost;

	//What the tree looked like when the rows were last read.
	int _shape;

	public HierarchyPanel( Hierarchy tree )
	{
		_tree = tree;

		AddClass( "ui-hierarchy tree-dock" );

		//The bar the inspector's tab opens into, and the tree in a frame under it - design.scss's .tree-dock.
		var bar = AddChild<Panel>( "dock-bar" );

		var unfold = bar.AddChild<ToolbarButton>();
		unfold.Icon = "unfold_more";
		unfold.ToolTip = "Expand all";
		unfold.OnPressed = () => _rows?.OpenAll();

		var fold = bar.AddChild<ToolbarButton>();
		fold.Icon = "unfold_less";
		fold.ToolTip = "Collapse all";
		fold.OnPressed = () => _rows?.CloseAll();

		bar.AddChild<Panel>( "spacer" );

		_rows = AddChild( new UiHierarchyTree( tree ) );
		_rows.AddClass( "tree-frame" );

		_note = AddChild<UILabel>( "note hidden" );

		//What is being carried, under the pointer, while it is.
		_ghost = AddChild<UILabel>( "ghost hidden" );
		_ghost.Tokenize = false;
	}

	/// <summary>Read every row from the tree again.</summary>
	public void Rebuild()
	{
		var root = _tree.Root;

		_note.SetClass( "hidden", root is not null );
		_note.Text = root is not null ? "" : _tree.Window.Document is null ? "No file open" : "No panel - is the file compiled?";

		_shape = root is null ? 0 : ShapeOf( root );
		_rows.Refresh();
	}

	/// <summary>Mark the selected rows, and bring the primary into view.</summary>
	public void ReadSelection() => _rows.ShowSelection();

	/// <summary>A razor panel rebuilds its children whenever its state changes, and nobody tells the tree. So the tree's shape is summed every frame, and the rows are read again when it moves.</summary>
	public override void Tick()
	{
		base.Tick();

		//The carried thing follows the pointer over the tree.
		var carry = _tree.Window.Carrying;
		var over = carry is not null && HasHovered;

		_ghost.SetClass( "hidden", !over );

		if ( over )
		{
			_ghost.Text = carry.ToString();
			_ghost.Style.Left = Length.Pixels( MousePosition.x + 14 );
			_ghost.Style.Top = Length.Pixels( MousePosition.y - 10 );
		}

		var root = _tree.Root;
		var shape = root is null ? 0 : ShapeOf( root );

		if ( shape != _shape )
		{
			Rebuild();
			ReadSelection();
		}
	}

	static int ShapeOf( Panel panel )
	{
		var hash = HashCode.Combine( panel.GetHashCode(), panel.ChildrenCount );

		foreach ( var child in panel.Children )
			hash = HashCode.Combine( hash, ShapeOf( child ) );

		return hash;
	}
}

//The document's panels as the editor's one tree - the same rows the scene's hierarchy draws: each panel by the classes its tag
//writes, its tag and id after them, quieter, folded under its parent. Made again on hotload with the dock it is in - see DesignCanvas.
[SkipHotload]
internal sealed class UiHierarchyTree : TreePanel<Panel>
{
	readonly Hierarchy _tree;

	int _revision;

	//Set while the tree's folding is put back on the rows, so it is not noted back.
	bool _seeding;

	public UiHierarchyTree( Hierarchy tree )
	{
		_tree = tree;

		AddClass( "ui-hierarchy-tree" );
		SelectionMode = TreeSelectionMode.Multi;
	}

	protected override int Revision => _revision;

	protected override bool HasRoots() => _tree.Root is not null;

	protected override IEnumerable<Panel> Roots
	{
		get
		{
			if ( _tree.Root is { } root )
				yield return root;
		}
	}

	protected override IEnumerable<Panel> GetChildren( Panel node ) => node.Children;

	protected override TreeRowPanel<Panel> CreateRow( Panel node, TreeItemInfo info, int depth )
		=> new UiHierarchyRow( this, _tree, node, info, depth );

	/// <summary>Read the document again, and open what nobody shut.</summary>
	public void Refresh()
	{
		_revision++;

		if ( _tree.Root is { } root )
		{
			_seeding = true;
			OpenUnshut( root );
			_seeding = false;
		}

		Bump();
	}

	void OpenUnshut( Panel panel )
	{
		if ( !panel.HasChildren ) return;

		if ( _tree.IsShut( panel ) )
		{
			Close( panel );
			return;
		}

		Open( panel );

		foreach ( var child in panel.Children )
			OpenUnshut( child );
	}

	protected override void SeedOpenRoots( IEnumerable<Panel> roots )
	{
		//Opened from what nobody shut - see Refresh.
	}

	protected override void OnExpandedChanged( Panel node, bool expanded )
	{
		if ( !_seeding ) _tree.NoteShut( node, !expanded );
	}

	/// <summary>Lights the rows of what the window has selected, without telling it back, and brings the primary into view.</summary>
	public void ShowSelection()
	{
		var window = _tree.Window;

		Select( window.Selected, skipEvents: true );

		if ( window.Selection is { } primary )
			ScrollTo( primary );
	}

	//A click, a ctrl-click, a shift-click: the window takes what the tree now holds, the last one clicked as the primary.
	protected override void OnSelectionChanged( IReadOnlyList<Panel> selected )
	{
		var window = _tree.Window;

		if ( selected.Count <= 1 ) window.Select( selected.FirstOrDefault() );
		else window.SelectMany( selected.Reverse() );
	}

	protected override void OnContextMenu( Panel node )
	{
		var window = _tree.Window;

		if ( !window.IsInSelection( node ) ) window.Select( node );

		var menu = new Menu( _tree );
		window.FillMarkupMenu( menu, node );

		if ( menu.HasOptions || menu.HasMenus ) menu.OpenAtCursor();
	}

	//A component opens in the editor; anything else folds.
	protected override bool OnRowActivated( Panel node )
	{
		if ( _tree.Window.ComponentPathOf( node ) is null ) return false;

		_tree.Window.OpenComponent( node );
		return true;
	}

	protected override TreeItemInfo GetInfo( Panel panel )
	{
		if ( !panel.IsValid() ) return new TreeItemInfo( "", null, false );

		var window = _tree.Window;

		//The classes the tag writes are the name a designer knows the panel by; the tag itself stands
		//after them, dimmer. A panel whose tag is not in this file shows every class it wears.
		var tag = window.LocateTag( panel );
		var classes = tag is not null ? WrittenClasses( tag ) : ClassesOf( panel );

		//The words between tags make a label of their own; its row shows the words, in quotes, rather than the tag they are in.
		var words = panel is UILabel label && tag is not null && !string.Equals( tag.Name, "label", StringComparison.OrdinalIgnoreCase ) && !string.Equals( tag.Name, panel.ElementName, StringComparison.OrdinalIgnoreCase )
			? label.Text : null;

		var element = TagOf( panel );
		var id = string.IsNullOrEmpty( panel.Id ) ? "" : $"#{panel.Id}";
		var component = window.ComponentPathOf( panel ) is not null;

		string name;
		string after;

		if ( words is not null )
		{
			name = $"\u201C{( words.Length > 24 ? words.Substring( 0, 24 ) + "…" : words )}\u201D";
			after = id;
		}
		else if ( classes.Length == 0 )
		{
			name = element;
			after = id;
		}
		else
		{
			name = classes;
			after = string.IsNullOrEmpty( id ) ? element : $"{element} {id}";
		}

		//Written inside an @if or a @foreach: a small sign over the glyph, since the panel may come and go, or come several times.
		var control = tag?.Control;

		return new TreeItemInfo( name, component ? "extension" : words is not null ? "notes" : "crop_square", panel.HasChildren )
		{
			IconColor = component ? new Color( 0.77f, 0.53f, 0.75f ) : null,
			Suffix = string.IsNullOrEmpty( after ) ? null : after,
			Overlay = control switch { null => null, "if" or "else" or "switch" => "alt_route", _ => "repeat" },
			Dim = !panel.IsVisible,
			Classes = words is not null ? "words" : null,
			Tooltip = control is null ? null : $"inside @{control}",
		};
	}

	//The element name the markup used, or the type when code made it.
	static string TagOf( Panel panel )
	{
		if ( !string.IsNullOrEmpty( panel.ElementName ) && panel.ElementName != "panel" )
			return panel.ElementName;

		var type = panel.GetType().Name;

		return string.Equals( type, "Panel", StringComparison.Ordinal ) ? "div" : type;
	}

	//The classes as the tag writes them - not the label a Label gives itself - each with its dot;
	//a class that is code, @(open ? "on" : "") , is one sign, since the expression is not a name.
	static string WrittenClasses( RazorMarkup.Element tag )
	{
		var written = tag.Classes;
		if ( string.IsNullOrWhiteSpace( written ) ) return "";

		var parts = new List<string>();
		var i = 0;

		while ( i < written.Length )
		{
			if ( char.IsWhiteSpace( written[i] ) ) { i++; continue; }

			if ( written[i] == '@' )
			{
				//To the end of the expression: balanced parentheses, or the word.
				var start = i;
				i++;

				if ( i < written.Length && written[i] == '(' )
				{
					var depth = 0;
					for ( ; i < written.Length; i++ )
					{
						if ( written[i] == '(' ) depth++;
						if ( written[i] == ')' && --depth == 0 ) { i++; break; }
					}
				}
				else
				{
					while ( i < written.Length && ( char.IsLetterOrDigit( written[i] ) || written[i] is '_' or '.' ) ) i++;
				}

				parts.Add( "ƒ" );
				continue;
			}

			var wordStart = i;
			while ( i < written.Length && !char.IsWhiteSpace( written[i] ) ) i++;
			parts.Add( "." + written.Substring( wordStart, i - wordStart ) );
		}

		return string.Join( " ", parts );
	}

	static string ClassesOf( Panel panel )
	{
		if ( string.IsNullOrWhiteSpace( panel.Classes ) ) return "";

		return string.Join( " ", panel.Class.Select( x => "." + x ) );
	}

}

//One hierarchy row; accepts the window's carried drag before, after or into the panel
[SkipHotload]
internal sealed class UiHierarchyRow : TreeRowPanel<Panel>
{
	readonly Hierarchy _tree;

	public UiHierarchyRow( TreePanel<Panel> tree, Hierarchy hierarchy, Panel node, TreeItemInfo info, int depth )
		: base( tree, node, info, depth, 0 )
	{
		_tree = hierarchy;

		AddClass( "ui-hierarchy-row" );
	}

	//The lit edge follows the pointer while something hovers here.
	protected override bool TickWhenAsked => false;

	public override bool WantsDrag => !ReferenceEquals( Node, _tree.Root );

	protected override void OnDragStart( DragEvent e )
	{
		_tree.Window.BeginCarry( new Carry { Panel = Node } );
	}

	protected override void OnDragEnd( DragEvent e )
	{
		_tree.Window.EndCarry();
	}

	//Where on the row the pointer is: the top edge is before, the bottom after, the middle into.
	DropPlace PlaceUnderPointer()
	{
		if ( Box is null || Box.Rect.Height <= 0 ) return DropPlace.Into;

		var y = MousePosition.y / ( Box.Rect.Height * ScaleFromScreen );

		return y < 0.25f ? DropPlace.Before : y > 0.75f ? DropPlace.After : DropPlace.Into;
	}

	protected override void OnDragEnter( PanelEvent e )
	{
		SetClass( "carry-target", _tree.Window.Carrying is not null );
	}

	protected override void OnDragLeave( PanelEvent e )
	{
		SetClass( "carry-target", false );
		SetClass( "carry-before", false );
		SetClass( "carry-after", false );
	}

	protected override void OnDrop( PanelEvent e )
	{
		SetClass( "carry-target", false );
		SetClass( "carry-before", false );
		SetClass( "carry-after", false );

		e.StopPropagation();
		_tree.Window.Drop( Node, PlaceUnderPointer() );
	}

	public override void Tick()
	{
		base.Tick();

		if ( !HasClass( "carry-target" ) ) return;

		var place = PlaceUnderPointer();
		SetClass( "carry-before", place == DropPlace.Before );
		SetClass( "carry-after", place == DropPlace.After );
	}
}
