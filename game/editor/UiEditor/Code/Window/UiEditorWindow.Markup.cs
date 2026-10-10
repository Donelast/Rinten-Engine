using System.IO;
using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.UiEditor;

/// <summary>Markup edits splice the .razor on disk and mirror on the live tree until the hotload arrives</summary>
public sealed partial class UiEditorWindow
{
	//The tag a panel was written as. The document's own panel is the <root> tag - or the one tag at the
	//top of the file - since what the compiler recorded on it is where the class is, not where a tag is.
	internal RazorMarkup.Element LocateTag( Panel panel )
	{
		if ( _document is null || !panel.IsValid() ) return null;

		return _document.Locate( panel, Root );
	}

	//The tag a panel was written as, or null with a warning when it cannot be found.
	RazorMarkup.Element TagOf( Panel panel, string what )
	{
		if ( _document is null || !panel.IsValid() ) return null;

		var element = LocateTag( panel );

		if ( element is null )
			Log.Warning( $"UI editor: cannot {what} - the panel was not made by {_document.Name}.razor, or its tag was not found" );

		return element;
	}

	//Set an attribute on a panel's tag; an empty value takes it off.
	internal bool SetAttribute( Panel panel, string name, string value )
	{
		if ( TagOf( panel, $"set {name}" ) is not { } element ) return false;

		var markup = _document.Markup;
		var after = string.IsNullOrWhiteSpace( value ) ? markup.WithoutAttribute( element, name ) : markup.WithAttribute( element, name, value.Trim() );

		if ( !CommitMarkup( $"{name} on <{element.Name}>", markup.Text, after ) ) return false;

		//What the tag change means to the panel it made.
		switch ( name.ToLowerInvariant() )
		{
			case "class": panel.Classes = value?.Trim() ?? ""; break;
			case "id": panel.Id = string.IsNullOrWhiteSpace( value ) ? null : value.Trim(); break;
			default: if ( !name.StartsWith( '@' ) ) panel.SetProperty( name, value ?? "" ); break;
		}

		return true;
	}

	//Set the words between a tag's open and close.
	internal bool SetText( Panel panel, string text )
	{
		if ( TagOf( panel, "set the text" ) is not { } element ) return false;

		//Words go in place of words, never in place of tags.
		if ( element.Children.Count > 0 )
		{
			Log.Warning( $"UI editor: <{element.Name}> holds other tags, so its words are not replaced" );
			return false;
		}

		var markup = _document.Markup;
		var after = markup.WithInnerText( element, text ?? "" );

		if ( !CommitMarkup( $"text of <{element.Name}>", markup.Text, after ) ) return false;

		//The words are the label's, whether the tag is the label or holds it.
		if ( panel is UILabel label ) label.Text = text ?? "";
		else if ( panel.Children.FirstOrDefault() is UILabel inner && panel.ChildrenCount == 1 ) inner.Text = text ?? "";

		return true;
	}

	//Put a tag inside a panel's tag, as its last child.
	internal Panel AddChild( Panel parent, string tag, string classes = null, string text = null )
	{
		if ( TagOf( parent, "add a child" ) is not { } element ) return null;

		var markup = _document.Markup;
		var after = markup.WithChildAppended( element, TagMarkup( tag, classes, text ) );

		if ( !CommitMarkup( $"add <{tag}>", markup.Text, after ) ) return null;

		var made = MakePanel( tag, classes, text );
		parent.AddChild( made );

		PlaceMirrored( made, parent );

		return made;
	}

	//Put a tag beside a panel's tag, just after it.
	internal Panel AddSibling( Panel panel, string tag, string classes = null, string text = null )
	{
		if ( TagOf( panel, "add a sibling" ) is not { } element ) return null;
		if ( panel.Parent is not { } parent ) return null;

		var markup = _document.Markup;
		var after = markup.WithSiblingAfter( element, TagMarkup( tag, classes, text ) );

		if ( !CommitMarkup( $"add <{tag}>", markup.Text, after ) ) return null;

		var made = MakePanel( tag, classes, text );
		parent.AddChild( made );

		var index = IndexOf( parent, panel ) + 1;
		parent.SetChildIndex( made, index );

		PlaceMirrored( made, parent );

		return made;
	}

	//A copy of a panel's tag, with everything in it, just after it.
	internal bool Duplicate( Panel panel )
	{
		if ( TagOf( panel, "duplicate" ) is not { } element ) return false;

		var markup = _document.Markup;
		var after = markup.WithSiblingAfter( element, markup.Extract( element ) );

		//Not mirrored: a copy of a subtree is the compiler's to make.
		return CommitMarkup( $"duplicate <{element.Name}>", markup.Text, after );
	}

	//Take a panel's tag out of the file, with everything in it.
	internal bool Delete( Panel panel )
	{
		if ( TagOf( panel, "delete" ) is not { } element ) return false;
		if ( ReferenceEquals( panel, Root ) ) return false;

		var markup = _document.Markup;
		var after = markup.WithoutElement( element );

		if ( !CommitMarkup( $"delete <{element.Name}>", markup.Text, after ) ) return false;

		var parent = panel.Parent;

		if ( IsSelected( panel ) ) Select( parent );

		panel.Delete( true );

		return true;
	}

	//Move a panel's tag one place up or down among its siblings.
	internal bool Move( Panel panel, int direction )
	{
		if ( TagOf( panel, "move" ) is not { } element ) return false;
		if ( panel.Parent is not { } parent ) return false;

		//Among the tags written beside it, in the same block - a tag inside an @if does not trade places with one outside.
		var siblings = element.Parent?.Children ?? _document.Markup.Roots;
		var at = siblings.IndexOf( element );
		var to = at + direction;

		if ( at < 0 || to < 0 || to >= siblings.Count ) return false;

		var other = siblings[to];
		if ( other.ControlDepth != element.ControlDepth || other.Control != element.Control ) return false;

		var markup = _document.Markup;
		var after = direction < 0 ? markup.WithElementBefore( element, other ) : markup.WithElementAfter( element, other );

		if ( !CommitMarkup( $"move <{element.Name}>", markup.Text, after ) ) return false;

		var index = IndexOf( parent, panel ) + direction;
		if ( index >= 0 && index < parent.ChildrenCount ) parent.SetChildIndex( panel, index );

		//Where it is now is where the hotload will look for it.
		if ( IsSelected( panel ) ) NoteSelectionMoved();

		return true;
	}

	//Repeat a panel's tag for every item of a list member: the tag wrapped in a @foreach .
	internal bool RepeatFor( Panel panel, string collection )
	{
		if ( TagOf( panel, "repeat" ) is not { } element ) return false;
		if ( ReferenceEquals( panel, Root ) ) return false;

		//One item name that is not already a member: "item", or the singular of the list.
		var item = Singular( collection );

		var markup = _document.Markup;
		var after = markup.WithBlockAround( element, $"@foreach ( var {item} in {collection} )" );

		//Not mirrored: how many the loop makes is the compiler's to say.
		return CommitMarkup( $"repeat <{element.Name}> for {collection}", markup.Text, after );
	}

	//Show a panel's tag only when a condition holds: the tag wrapped in an @if .
	internal bool ShowWhen( Panel panel, string condition )
	{
		if ( TagOf( panel, "condition" ) is not { } element ) return false;
		if ( ReferenceEquals( panel, Root ) ) return false;

		var markup = _document.Markup;
		var after = markup.WithBlockAround( element, $"@if ( {condition} )" );

		return CommitMarkup( $"show <{element.Name}> when {condition}", markup.Text, after );
	}

	//A name for one of a list: "item" from Items, "card" from Cards, "entry" from anything else.
	static string Singular( string collection )
	{
		var name = collection.Split( '.' ).Last();
		var lower = char.ToLowerInvariant( name[0] ) + name.Substring( 1 );

		if ( lower.EndsWith( "ies" ) ) return lower.Substring( 0, lower.Length - 3 ) + "y";
		if ( lower.EndsWith( "s" ) && lower.Length > 2 ) return lower.Substring( 0, lower.Length - 1 );

		return "entry";
	}

	//What a menu offers for a panel, and the keys that do the same

	//Tags the menu offers to add, with a class each to start from.
	static readonly (string tag, string classes, string text)[] Additions =
	{
		( "div", "", null ),
		( "label", "", "Label" ),
		( "img", "", null ),
		( "button", "", "Button" ),
	};

	//Fill a menu with everything that can be done to a panel's tag.
	internal void FillMarkupMenu( Menu menu, Panel panel )
	{
		if ( _document is null || !panel.IsValid() ) return;

		var editable = LocateTag( panel ) is not null;
		var isRoot = ReferenceEquals( panel, Root );

		var add = menu.AddMenu( "Add Child", "add" );

		foreach ( var (tag, classes, text) in Additions )
		{
			var option = add.AddOption( $"<{tag}>", null, () => Select( AddChild( panel, tag, classes, text ) ?? panel ) );
			option.Enabled = editable;
		}

		if ( !isRoot )
		{
			var beside = menu.AddMenu( "Add After", "playlist_add" );

			foreach ( var (tag, classes, text) in Additions )
			{
				var option = beside.AddOption( $"<{tag}>", null, () => Select( AddSibling( panel, tag, classes, text ) ?? panel ) );
				option.Enabled = editable;
			}

			menu.AddSeparator();

			menu.AddOption( "Move Up", "arrow_upward", () => Move( panel, -1 ), "ui.moveup" ).Enabled = editable;
			menu.AddOption( "Move Down", "arrow_downward", () => Move( panel, 1 ), "ui.movedown" ).Enabled = editable;

			menu.AddSeparator();

			//Data-driven: the tag once per item of a list, or only when a condition holds.
			if ( editable && _document?.Type is { } type )
			{
				var repeat = menu.AddMenu( "Repeat For Each", "repeat" );
				var lists = DesignData.Members( type ).Where( x => DesignData.IsList( x.type, out _ ) ).Select( x => x.name ).ToList();

				if ( lists.Count == 0 )
					repeat.AddOption( "No list members - add one in Data", "info" ).Enabled = false;

				foreach ( var list in lists )
					repeat.AddOption( list, "data_object", () => RepeatFor( panel, list ) );

				var when = menu.AddMenu( "Show Only When", "visibility" );
				var bools = DesignData.Members( type ).Where( x => x.type == typeof( bool ) ).Select( x => x.name ).ToList();

				if ( bools.Count == 0 )
					when.AddOption( "No bool members - add one in Data", "info" ).Enabled = false;

				foreach ( var flag in bools )
					when.AddOption( flag, "toggle_on", () => ShowWhen( panel, flag ) );

				menu.AddSeparator();
			}

			menu.AddOption( "Edit Text", "edit", () => CanvasPanel?.BeginTextEdit( panel ), "ui.edittext" ).Enabled = editable && HasWords( panel );
			menu.AddSeparator();
			menu.AddOption( "Copy", "content_copy", CopySelection, "editor.copy" ).Enabled = editable;
			menu.AddOption( "Cut", "content_cut", CutSelection, "editor.cut" ).Enabled = editable;
			menu.AddOption( "Paste", "content_paste", PasteMarkup, "editor.paste" ).Enabled = LooksLikeMarkup( EditorUtility.Clipboard.Paste() );
			menu.AddOption( "Copy Style", "palette", CopyStyle, "ui.copystyle" ).Enabled = editable;
			menu.AddOption( "Paste Style", "format_paint", PasteStyle, "ui.pastestyle" ).Enabled = editable && LooksLikeStyle( EditorUtility.Clipboard.Paste() );
			//Inside a @foreach over a list of the document's: the item this panel was made from can be added beside, copied, moved, taken away.
			if ( LocateTag( panel ) is { Control: "foreach" } repeated )
			{
				menu.AddSeparator();

				var member = ListForForeach( repeated );
				var index = ItemIndexOf( panel );
				var items = member is not null;

				var item = menu.AddMenu( items ? $"Item {index + 1} of {member}" : "Item", "list" );

				if ( !items )
					item.AddOption( $"walks `{repeated.ControlHeader}` - not a list of the document's", "info" ).Enabled = false;
				else
				{
					item.AddOption( "Add Item After", "add", () => InsertItem( member, index + 1 ) );
					item.AddOption( "Duplicate Item", "content_copy", () => DuplicateItem( member, index ) );
					item.AddOption( "Move Item Up", "arrow_upward", () => MoveItem( member, index, -1 ) );
					item.AddOption( "Move Item Down", "arrow_downward", () => MoveItem( member, index, 1 ) );
					item.AddSeparator();
					item.AddOption( "Remove Item", "delete", () => RemoveItem( member, index ) );
				}
			}

			menu.AddSeparator();
			menu.AddOption( "Group", "select_all", WrapSelection, "ui.wrap" ).Enabled = editable && !isRoot;
			menu.AddOption( "Ungroup", "deselect", () => Unwrap( panel ), "ui.unwrap" ).Enabled = editable && !isRoot && LocateTag( panel )?.Children.Count > 0;

			if ( ComponentPathOf( panel ) is not null )
				menu.AddOption( "Inline Component", "call_merge", () => InlineComponent( panel ) ).Enabled = editable;
			else
				menu.AddOption( "Make Component…", "call_split", () => AskComponentName( panel ) ).Enabled = editable && !isRoot;

			menu.AddSeparator();
			menu.AddOption( "Duplicate", "difference", () => Duplicate( panel ), "ui.duplicate" ).Enabled = editable;
			menu.AddOption( "Delete", "delete", () => Delete( panel ), "ui.delete" ).Enabled = editable;
		}

		if ( HasMany )
		{
			menu.AddSeparator();

			var align = menu.AddMenu( "Align To Primary", "align_horizontal_left" );
			align.AddOption( "Left", "align_horizontal_left", () => Align( "left" ) );
			align.AddOption( "Centre", "align_horizontal_center", () => Align( "centre" ) );
			align.AddOption( "Right", "align_horizontal_right", () => Align( "right" ) );
			align.AddSeparator();
			align.AddOption( "Top", "align_vertical_top", () => Align( "top" ) );
			align.AddOption( "Middle", "align_vertical_center", () => Align( "middle" ) );
			align.AddOption( "Bottom", "align_vertical_bottom", () => Align( "bottom" ) );

			var spread = menu.AddMenu( "Distribute", "horizontal_distribute" );
			spread.AddOption( "Horizontally", "horizontal_distribute", () => Distribute( true ) );
			spread.AddOption( "Vertically", "vertical_distribute", () => Distribute( false ) );
		}

		if ( !string.IsNullOrEmpty( panel.SourceFile ) )
		{
			menu.AddSeparator();
			menu.AddOption( $"Open {Path.GetFileName( panel.SourceFile )}:{panel.SourceLine}", "code", () => InspectorPanel.OpenFile( panel.SourceFile, panel.SourceLine ) );
		}

		if ( ComponentPathOf( panel ) is { } component )
			menu.AddOption( $"Open {Path.GetFileName( component )} in UI Editor", "web", () => OpenComponent( panel ) );

		if ( panel.Parent is { } parent && !isRoot )
			menu.AddOption( "Select Parent", "north_west", () => Select( parent ) );

		//The files behind the document. There is no menu bar to hold them.
		menu.AddSeparator();
		menu.AddOption( "Open .razor in Code Editor", "code", () => OpenInCode( _document.RazorPath ) );
		menu.AddOption( "Open .scss in Code Editor", "css", () => OpenInCode( _document.ScssPath ) ).Enabled = _document.HasScss;
		menu.AddOption( "Show in Explorer", "folder_open", ShowInExplorer );
	}

	//A name for the component the panel's tag is to become, then the making of it.
	void AskComponentName( Panel panel )
	{
		var initial = LocateTag( panel )?.Classes.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault( x => !x.Contains( '@' ) ) ?? "";

		EditorModal.Prompt( CanvasPanel, "Make Component", "The new razor's name, beside this one:", name => ExtractComponent( panel, name ),
			"Make", "Cancel", ComponentName( initial ), 1, icon: "call_split" );
	}

	[Shortcut( "ui.delete", "DELETE", ShortcutType.Window )]
	void DeleteSelection()
	{
		if ( TypingSomewhere() ) return;

		//Each on its own: a tag gone moves the lines under it, and the next is found again by its panel, which is still there.
		using ( BeginGesture( "delete" ) )
			foreach ( var panel in Selected.ToList() ) Delete( panel );
	}

	[Shortcut( "ui.duplicate", "CTRL+D", ShortcutType.Window )]
	void DuplicateSelection()
	{
		if ( TypingSomewhere() ) return;

		using ( BeginGesture( "duplicate" ) )
			foreach ( var panel in Selected.ToList() ) Duplicate( panel );
	}

	//The clipboard: tags as markup, a rule as its lines

	//The selected tags' markup, in the order written, each with its indent taken off.
	string SelectedMarkup()
	{
		if ( _document is null ) return null;

		var markup = _document.Markup;
		var elements = Selected.Select( LocateTag ).Where( x => x is not null ).Distinct().OrderBy( x => x.Start ).ToList();

		//A tag inside another selected tag goes with its parent.
		elements.RemoveAll( x => elements.Any( other => !ReferenceEquals( other, x ) && other.Start <= x.Start && x.End <= other.End ) );

		if ( elements.Count == 0 ) return null;

		return string.Join( "\n", elements.Select( markup.Extract ) );
	}

	[Shortcut( "editor.copy", "CTRL+C", ShortcutType.Window )]
	internal void CopySelection()
	{
		if ( TypingSomewhere() ) return;
		if ( SelectedMarkup() is not { } text ) return;

		EditorUtility.Clipboard.Copy( text );
	}

	[Shortcut( "editor.cut", "CTRL+X", ShortcutType.Window )]
	internal void CutSelection()
	{
		if ( TypingSomewhere() ) return;
		if ( SelectedMarkup() is not { } text ) return;

		EditorUtility.Clipboard.Copy( text );

		using ( BeginGesture( "cut" ) )
			foreach ( var panel in Selected.ToList() ) Delete( panel );
	}

	//Whether text is a tag or several - what a paste can put in a file.
	static bool LooksLikeMarkup( string text )
	{
		var trimmed = text?.Trim();
		return !string.IsNullOrEmpty( trimmed ) && trimmed.StartsWith( '<' ) && trimmed.EndsWith( '>' );
	}

	//Put the clipboard's markup in the file: inside the selection when it is the root or already
	//holds tags, after it otherwise. The new tag is selected once the hotload has built it.
	[Shortcut( "editor.paste", "CTRL+V", ShortcutType.Window )]
	internal void PasteMarkup()
	{
		if ( TypingSomewhere() || _document is null ) return;

		var text = EditorUtility.Clipboard.Paste();
		if ( !LooksLikeMarkup( text ) ) return;

		var target = Selection ?? Root;
		if ( target is null || TagOf( target, "paste" ) is not { } element ) return;

		var markup = _document.Markup;
		var inside = ReferenceEquals( target, Root ) || element.Children.Count > 0;
		var after = inside ? markup.WithChildAppended( element, text.Trim() ) : markup.WithSiblingAfter( element, text.Trim() );

		CommitMarkup( "paste", markup.Text, after );

		//Not mirrored: the tree the compiler builds has the tag, and the selection follows it there.
		var path = PanelPath.Of( Root, target ).ToList();
		if ( inside ) path.Add( target.ChildrenCount );
		else path[^1]++;

		SelectAfterBuild( path );
	}

	//The declarations of the selection's own rule - what the inspector edits - as lines of property: value; .
	string SelectedStyle()
	{
		if ( _document is null || Selection is not { } panel ) return null;

		var cascade = StyleCascade.For( panel, _document.ScssPath );
		var rule = cascade.EditTarget( null, _document.ScssPath );
		if ( rule is null ) return null;

		var sheet = ScssDocument.Load( rule.Path );
		var block = sheet?.Find( rule.Block );
		if ( block is null || block.Declarations.Count == 0 ) return null;

		return string.Join( "\n", block.Declarations.Select( x => $"{x.Name}: {x.Value};" ) );
	}

	//Whether text is lines of property: value; - what a paste can write onto a rule.
	static bool LooksLikeStyle( string text ) => ParseStyle( text ).Count > 0;

	static List<(string name, string value)> ParseStyle( string text )
	{
		var found = new List<(string, string)>();
		if ( string.IsNullOrWhiteSpace( text ) || text.Contains( '<' ) || text.Contains( '{' ) ) return found;

		foreach ( var raw in text.Split( new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries ) )
		{
			var line = raw.Trim();
			var colon = line.IndexOf( ':' );
			if ( colon <= 0 ) continue;

			var name = line.Substring( 0, colon ).Trim();
			var value = line.Substring( colon + 1 ).Trim();

			if ( name.Length == 0 || value.Length == 0 || name.Any( char.IsWhiteSpace ) ) continue;
			found.Add( ( name, value ) );
		}

		return found;
	}

	[Shortcut( "ui.copystyle", "CTRL+SHIFT+C", ShortcutType.Window )]
	internal void CopyStyle()
	{
		if ( TypingSomewhere() ) return;
		if ( SelectedStyle() is not { } text ) return;

		EditorUtility.Clipboard.Copy( text );
	}

	//Write the clipboard's property: value; lines onto every selected panel's rule.
	[Shortcut( "ui.pastestyle", "CTRL+SHIFT+V", ShortcutType.Window )]
	internal void PasteStyle()
	{
		if ( TypingSomewhere() ) return;

		var declarations = ParseStyle( EditorUtility.Clipboard.Paste() );
		if ( declarations.Count == 0 ) return;

		using ( BeginGesture( "paste style" ) )
			foreach ( var panel in Selected.ToList() )
				WriteStyles( panel, declarations );
	}

	//Whether a panel's tag holds words and no tags - what can be edited in place.
	internal bool HasWords( Panel panel )
		=> LocateTag( panel ) is { } element && element.Children.Count == 0 && !element.SelfClosing;

	[Shortcut( "ui.edittext", "F2", ShortcutType.Window )]
	void EditSelectionText()
	{
		if ( TypingSomewhere() || Selection is not { } panel || !HasWords( panel ) ) return;

		CanvasPanel?.BeginTextEdit( panel );
	}

	//Several at once: lined up, or spread out

	//Line the selected panels up on an edge or a middle of the primary, writing left
	//or top on each absolute one. side is left, centre, right, top, middle or bottom.
	internal void Align( string side )
	{
		if ( Selection is not { } primary || Selected.Count < 2 ) return;

		var target = primary.Box.Rect;

		using var gesture = BeginGesture( $"align {side}" );

		foreach ( var panel in Selected.ToList() )
		{
			if ( ReferenceEquals( panel, primary ) || panel.ComputedStyle?.Position is not PositionMode.Absolute ) continue;
			if ( !ReferenceEquals( panel.Parent, primary.Parent ) ) continue;

			var box = panel.Box.Rect;
			var dx = side switch { "left" => target.Left - box.Left, "centre" => target.Left + target.Width * 0.5f - ( box.Left + box.Width * 0.5f ), "right" => target.Right - box.Right, _ => 0 };
			var dy = side switch { "top" => target.Top - box.Top, "middle" => target.Top + target.Height * 0.5f - ( box.Top + box.Height * 0.5f ), "bottom" => target.Bottom - box.Bottom, _ => 0 };

			MoveBy( panel, new Vector2( dx, dy ) * panel.ScaleFromScreen );
		}
	}

	//Spread the selected panels evenly between the first and the last along an axis.
	internal void Distribute( bool horizontal )
	{
		var panels = Selected.Where( x => x.ComputedStyle?.Position is PositionMode.Absolute ).ToList();
		if ( panels.Count < 3 ) return;

		panels.Sort( ( a, b ) => horizontal ? a.Box.Rect.Left.CompareTo( b.Box.Rect.Left ) : a.Box.Rect.Top.CompareTo( b.Box.Rect.Top ) );

		var first = panels[0].Box.Rect;
		var last = panels[^1].Box.Rect;
		var span = horizontal ? last.Left - first.Left : last.Top - first.Top;
		var step = span / ( panels.Count - 1 );

		using var gesture = BeginGesture( "distribute" );

		for ( var i = 1; i < panels.Count - 1; i++ )
		{
			var box = panels[i].Box.Rect;
			var want = ( horizontal ? first.Left : first.Top ) + step * i;
			var delta = horizontal ? new Vector2( want - box.Left, 0 ) : new Vector2( 0, want - box.Top );

			MoveBy( panels[i], delta * panels[i].ScaleFromScreen );
		}
	}

	//Move a panel by so many style pixels, as a drag would, writing what it means.
	void MoveBy( Panel panel, Vector2 delta )
	{
		if ( CanvasPanel is not { } canvas || LocateTag( panel ) is null ) return;
		if ( MathF.Abs( delta.x ) < 0.5f && MathF.Abs( delta.y ) < 0.5f ) return;

		var drag = CanvasDrag.Move( canvas, panel, Vector2.Zero );
		drag.MoveTo( delta * canvas.Fit );
		drag.Finish();
	}

	[Shortcut( "ui.moveup", "CTRL+UP", ShortcutType.Window )]
	void MoveSelectionUp()
	{
		if ( Selection is { } panel && !TypingSomewhere() ) Move( panel, -1 );
	}

	[Shortcut( "ui.movedown", "CTRL+DOWN", ShortcutType.Window )]
	void MoveSelectionDown()
	{
		if ( Selection is { } panel && !TypingSomewhere() ) Move( panel, 1 );
	}

	//Whether a text field has the keyboard - a Delete there is a character, not a panel.
	static bool TypingSomewhere() => InputFocus.Current is TextField or Rinten.UI.TextEntry;

	//The keys: moving the selection about the canvas and the tree, and the canvas's own toggles. Every one is idle while a field has the keyboard.

	//Move the selection by so many stage pixels, as a drag would - the grid's step with the grid on, ten with Shift.
	void Nudge( int dx, int dy )
	{
		if ( TypingSomewhere() || CanvasPanel is not { } canvas ) return;

		var by = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) ? 10.0f : canvas.Grid ? DesignCanvasPanel.GridStep : 1.0f;

		using var gesture = BeginGesture( "nudge" );

		foreach ( var panel in Selected.ToList() )
		{
			if ( LocateTag( panel ) is null ) continue;

			var drag = CanvasDrag.Move( canvas, panel, Vector2.Zero );
			drag.MoveTo( new Vector2( dx * by, dy * by ) * canvas.Fit );
			drag.Finish();
		}
	}

	[Shortcut( "ui.nudgeleft", "LEFT", ShortcutType.Window )] void NudgeLeft() => Nudge( -1, 0 );
	[Shortcut( "ui.nudgeright", "RIGHT", ShortcutType.Window )] void NudgeRight() => Nudge( 1, 0 );
	[Shortcut( "ui.nudgeup", "UP", ShortcutType.Window )] void NudgeUp() => Nudge( 0, -1 );
	[Shortcut( "ui.nudgedown", "DOWN", ShortcutType.Window )] void NudgeDown() => Nudge( 0, 1 );
	[Shortcut( "ui.nudgeleftfar", "SHIFT+LEFT", ShortcutType.Window )] void NudgeLeftFar() => Nudge( -1, 0 );
	[Shortcut( "ui.nudgerightfar", "SHIFT+RIGHT", ShortcutType.Window )] void NudgeRightFar() => Nudge( 1, 0 );
	[Shortcut( "ui.nudgeupfar", "SHIFT+UP", ShortcutType.Window )] void NudgeUpFar() => Nudge( 0, -1 );
	[Shortcut( "ui.nudgedownfar", "SHIFT+DOWN", ShortcutType.Window )] void NudgeDownFar() => Nudge( 0, 1 );

	[Shortcut( "ui.selectparent", "ALT+UP", ShortcutType.Window )]
	void SelectParent()
	{
		if ( TypingSomewhere() ) return;
		if ( Selection?.Parent is { } parent && !ReferenceEquals( Selection, Root ) ) Select( parent );
	}

	[Shortcut( "ui.selectchild", "ALT+DOWN", ShortcutType.Window )]
	void SelectChild()
	{
		if ( TypingSomewhere() ) return;
		if ( Selection?.Children.FirstOrDefault() is { } child ) Select( child );
	}

	[Shortcut( "ui.selectprevious", "[", ShortcutType.Window )]
	void SelectPrevious() => SelectSibling( -1 );

	[Shortcut( "ui.selectnext", "]", ShortcutType.Window )]
	void SelectNext() => SelectSibling( 1 );

	void SelectSibling( int direction )
	{
		if ( TypingSomewhere() || Selection is not { } panel || panel.Parent is not { } parent ) return;

		var index = IndexOf( parent, panel ) + direction;
		if ( index >= 0 && index < parent.ChildrenCount ) Select( parent.Children.ElementAt( index ) );
	}

	//Out of its parent: the selection put after the parent, one level up.
	[Shortcut( "ui.outdent", "CTRL+LEFT", ShortcutType.Window )]
	void Outdent()
	{
		if ( TypingSomewhere() || Selection is not { } panel || panel.Parent is not { } parent ) return;
		if ( ReferenceEquals( parent, Root ) || ReferenceEquals( panel, Root ) ) return;

		Reparent( panel, parent, DropPlace.After );
	}

	//Into the sibling before it: the selection made the last child of the panel above it.
	[Shortcut( "ui.indent", "CTRL+RIGHT", ShortcutType.Window )]
	void Indent()
	{
		if ( TypingSomewhere() || Selection is not { } panel || panel.Parent is not { } parent ) return;

		var index = IndexOf( parent, panel );
		if ( index <= 0 ) return;

		var before = parent.Children.ElementAt( index - 1 );
		if ( before is Rinten.UI.Label ) return;

		Reparent( panel, before, DropPlace.Into );
	}

	[Shortcut( "ui.deselect", "ESCAPE", ShortcutType.Window )]
	void Deselect()
	{
		if ( TypingSomewhere() ) return;
		Select( null );
	}

	[Shortcut( "ui.grid", "CTRL+G", ShortcutType.Window )] void ToggleGrid() => CanvasPanel?.ToggleGrid();
	[Shortcut( "ui.rulers", "CTRL+R", ShortcutType.Window )] void ToggleRulers() => CanvasPanel?.ToggleRulers();
	[Shortcut( "ui.interact", "CTRL+I", ShortcutType.Window )] void ToggleInteract() => CanvasPanel?.ToggleInteract();

	//The pieces

	static string TagMarkup( string tag, string classes, string text )
	{
		var attributes = string.IsNullOrWhiteSpace( classes ) ? "" : $" class=\"{classes.Trim()}\"";

		return string.IsNullOrEmpty( text ) ? $"<{tag}{attributes}></{tag}>" : $"<{tag}{attributes}>{text}</{tag}>";
	}

	//The panel the compiler would make from a tag, near enough to stand in until it does.
	static Panel MakePanel( string tag, string classes, string text )
	{
		var panel = string.Equals( tag, "label", StringComparison.OrdinalIgnoreCase ) || !string.IsNullOrEmpty( text )
			? new UILabel { Text = text ?? "" }
			: TypeLibrary.Create<Panel>( tag, false ) ?? new Panel();

		panel.ElementName = tag;

		if ( !string.IsNullOrWhiteSpace( classes ) ) panel.Classes = classes.Trim();

		return panel;
	}

	//A mirrored panel is found in the file by structure - the same path through the tags as through the panels - so it needs
	//the file and nothing else. A position guessed here could land on another tag exactly, and then an edit would go there.
	void PlaceMirrored( Panel made, Panel parent )
	{
		made.SourceFile = parent.SourceFile ?? _document.RazorPath;
		made.SourceLine = parent.SourceLine;
		made.SourceColumn = -1;
	}

	static int IndexOf( Panel parent, Panel child )
	{
		var i = 0;

		foreach ( var candidate in parent.Children )
		{
			if ( ReferenceEquals( candidate, child ) ) return i;
			i++;
		}

		return -1;
	}

	//Save new text for the razor, remember how to take it back, and move the panels' lines to match.
	bool CommitMarkup( string title, string before, string after )
	{
		if ( _document is null || before == after ) return false;

		var path = _document.RazorPath;

		try
		{
			File.WriteAllText( path, after );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"UI editor: could not write {path}: {e.Message}" );
			return false;
		}

		_document.ForgetMarkup();

		Record( title, () => RestoreMarkup( path, before ), () => RestoreMarkup( path, after ) );

		ShiftSourceLines( before, after );

		return true;
	}

	void RestoreMarkup( string path, string text )
	{
		Restore( path, text );
		_document?.ForgetMarkup();
	}

	//The lines under a change moved by as many lines as it added or took away; the panels
	//built from them are told, so a second edit before the hotload still finds its tag.
	void ShiftSourceLines( string before, string after )
	{
		var root = Root;
		if ( root is null ) return;

		var common = 0;
		var max = Math.Min( before.Length, after.Length );
		while ( common < max && before[common] == after[common] ) common++;

		var changedLine = 0;
		for ( var i = 0; i < common; i++ ) if ( before[i] == '\n' ) changedLine++;

		var delta = Count( after, '\n' ) - Count( before, '\n' );
		if ( delta == 0 ) return;

		var offset = _document.Markup.CompilerLineOffset;

		foreach ( var panel in root.Descendants.Prepend( root ) )
		{
			if ( !_document.Owns( panel ) ) continue;

			var line = panel.SourceLine - 1 - offset;
			if ( line > changedLine ) panel.SourceLine += delta;
		}
	}

	static int Count( string text, char c )
	{
		var n = 0;
		foreach ( var x in text ) if ( x == c ) n++;
		return n;
	}
}
