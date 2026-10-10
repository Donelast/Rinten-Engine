using System.IO;
using System.Text;
using Rinten.UI;

namespace Editor.UiEditor;

/// <summary>The shape of a document changed in bigger strokes than one tag: a panel drawn onto the canvas with its rule, tags grouped under a new one and let out again, a subtree made into a component of its own and a component folded back in.</summary>
public partial class UiEditorWindow
{
	//Names

	//A class no tag in the file has yet: the stem, else the stem with a number.
	internal string UniqueClass( string stem )
	{
		if ( _document is null ) return stem;

		var taken = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var element in _document.Markup.Elements )
		{
			foreach ( var cls in element.Classes.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
				taken.Add( cls );
		}

		if ( !taken.Contains( stem ) ) return stem;

		for ( var i = 2; ; i++ )
		{
			if ( !taken.Contains( $"{stem}-{i}" ) ) return $"{stem}-{i}";
		}
	}

	//What a drawn tag of this kind is called before anyone names it.
	static string StemFor( string tag ) => tag.ToLowerInvariant() switch
	{
		"div" => "box",
		"label" => "text",
		"img" => "image",
		"button" => "button",
		_ => tag.ToLowerInvariant(),
	};

	//Drawn onto the canvas

	//A tag added inside a panel with a rule placing it where a rectangle was drawn, in the parent's style
	//pixels: absolute, at that left and top, that wide and tall. One gesture: the tag and the rule together.
	internal Panel AddDrawn( Panel parent, string tag, string classes, string text, Rect rect )
	{
		if ( _document is null || !parent.IsValid() ) return null;

		var cls = string.IsNullOrWhiteSpace( classes ) ? UniqueClass( StemFor( tag ) ) : classes.Trim();

		using var gesture = BeginGesture( $"draw <{tag}>" );

		var made = AddChild( parent, tag, cls, text );
		if ( made is null ) return null;

		var declarations = new List<(string, string)>
		{
			( "position", "absolute" ),
			( "left", $"{MathF.Round( rect.Left ):0}px" ),
			( "top", $"{MathF.Round( rect.Top ):0}px" ),
			( "width", $"{MathF.Round( rect.Width ):0}px" ),
			( "height", $"{MathF.Round( rect.Height ):0}px" ),
		};

		AddRuleUnderRoot( "." + cls.Split( ' ' )[0], declarations );

		//The mirrored panel shows it at once; the sheet takes over on reload.
		made.Style.Position = PositionMode.Absolute;
		made.Style.Left = Length.Pixels( rect.Left );
		made.Style.Top = Length.Pixels( rect.Top );
		made.Style.Width = Length.Pixels( rect.Width );
		made.Style.Height = Length.Pixels( rect.Height );

		return made;
	}

	//A rule in the document's own sheet, nested under its root rule when there is one:
	//written into the block of that selector when it is already there, made otherwise.
	internal bool AddRuleUnderRoot( string selector, IReadOnlyList<(string name, string value)> declarations )
	{
		if ( _document is null ) return false;

		var sheet = ScssDocument.Load( _document.ScssPath ) ?? ScssDocument.FromText( _document.ScssPath, "" );
		var rootBlock = RootRule() is { } root ? sheet.Find( root.Block ) : null;

		var text = sheet.Text;

		if ( sheet.Blocks.Any( x => x.Selector == selector && x.Parent?.Line == rootBlock?.Line ) )
		{
			foreach ( var (name, value) in declarations )
			{
				var working = ScssDocument.FromText( sheet.Path, text );
				var at = working.Blocks.LastOrDefault( x => x.Selector == selector && x.Parent?.Line == rootBlock?.Line );
				if ( at is null ) break;

				text = string.IsNullOrWhiteSpace( value ) ? working.WithoutDeclaration( at, name ) : working.WithDeclaration( at, name, value );
			}
		}
		else
		{
			text = sheet.WithBlock( rootBlock, selector, declarations );
		}

		return Commit( sheet, $"Add {selector}", text );
	}

	//Grouped, and let out

	//The selected tags put inside a new div : siblings written in the same block, taken in the order written, the group where the first was.
	[Shortcut( "ui.wrap", "CTRL+G", ShortcutType.Window )]
	internal void WrapSelection()
	{
		if ( TypingSomewhere() || _document is null ) return;

		var elements = Selected.Select( LocateTag ).Where( x => x is not null ).Distinct().OrderBy( x => x.Start ).ToList();
		elements.RemoveAll( x => elements.Any( other => !ReferenceEquals( other, x ) && other.Start <= x.Start && x.End <= other.End ) );

		if ( elements.Count == 0 ) return;

		if ( elements.Any( x => ReferenceEquals( x, elements[0] ) is false && ( x.Parent != elements[0].Parent || x.ControlDepth != elements[0].ControlDepth ) ) )
		{
			Log.Warning( "UI editor: only tags written beside each other can be grouped" );
			return;
		}

		if ( elements[0].Parent is null )
		{
			Log.Warning( "UI editor: the root cannot be grouped" );
			return;
		}

		var first = Selected.FirstOrDefault( p => ReferenceEquals( LocateTag( p ), elements[0] ) );
		var path = first is not null ? PanelPath.Of( Root, first ) : null;

		var cls = UniqueClass( "group" );
		var markup = _document.Markup;
		var after = markup.WithWrapped( elements, $"<div class=\"{cls}\">", "</div>" );

		if ( !CommitMarkup( $"group {elements.Count} in <div>", markup.Text, after ) ) return;

		if ( path is not null ) SelectAfterBuild( path );
	}

	//The selection's tag taken away, what was inside it left where it was.
	[Shortcut( "ui.unwrap", "CTRL+SHIFT+G", ShortcutType.Window )]
	internal void UnwrapSelection()
	{
		if ( TypingSomewhere() || Selection is not { } panel ) return;

		Unwrap( panel );
	}

	internal bool Unwrap( Panel panel )
	{
		if ( TagOf( panel, "ungroup" ) is not { } element ) return false;

		if ( element.Parent is null || ReferenceEquals( panel, Root ) )
		{
			Log.Warning( "UI editor: the root cannot be ungrouped" );
			return false;
		}

		if ( element.Children.Count == 0 )
		{
			Log.Warning( $"UI editor: <{element.Name}> holds no tags to let out" );
			return false;
		}

		var parentPath = panel.Parent is { } parent ? PanelPath.Of( Root, parent ) : null;

		var markup = _document.Markup;
		var after = markup.WithoutWrapper( element );

		if ( !CommitMarkup( $"ungroup <{element.Name}>", markup.Text, after ) ) return false;

		if ( parentPath is not null ) SelectAfterBuild( parentPath );

		return true;
	}

	//Components

	//The selection's tag, with everything in it, made into a razor of its own beside this one, and a tag
	//of the new component put in its place. The rules for its first class go with it into the new sheet.
	internal bool ExtractComponent( Panel panel, string name )
	{
		if ( TagOf( panel, "make a component of" ) is not { } element ) return false;

		if ( ReferenceEquals( panel, Root ) )
		{
			Log.Warning( "UI editor: the root is the component already" );
			return false;
		}

		name = ComponentName( name );
		if ( name.Length == 0 ) return false;

		var directory = Path.GetDirectoryName( _document.RazorPath );
		var razorPath = Path.Combine( directory, name + ".razor" );

		if ( File.Exists( razorPath ) )
		{
			Log.Warning( $"UI editor: {name}.razor is already there" );
			return false;
		}

		var markup = _document.Markup;
		var text = markup.Text;

		//The tag's own markup, its outer tag renamed root.
		var extracted = markup.Extract( element );
		var openLength = element.OpenEnd - element.Start;
		var open = extracted.Substring( 0, openLength );
		var body = element.SelfClosing ? "" : extracted.Substring( openLength, extracted.Length - openLength - ( element.End - element.Close ) );

		if ( element.SelfClosing ) open = open.Substring( 0, open.LastIndexOf( '/' ) ).TrimEnd() + ">";

		var rootOpen = "<root" + open.Substring( 1 + element.Name.Length );

		var header = new StringBuilder();

		foreach ( var line in text.Split( '\n' ) )
		{
			var trimmed = line.Trim();
			if ( trimmed.StartsWith( "@using " ) || trimmed.StartsWith( "@namespace " ) ) header.Append( trimmed ).Append( '\n' );
			if ( trimmed.StartsWith( '<' ) ) break;
		}

		header.Append( "@inherits Panel\n" );

		var razor = header + "\n" + rootOpen + body + "</root>\n\n@code\n{\n}\n";

		//The rules go with it: the tag's own first class, and every class written inside it that
		//nothing outside it wears - those dress what is now the component's, and nothing else.
		var firstClass = element.Classes.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault( x => !x.Contains( '@' ) );
		var sheet = ScssDocument.Load( _document.ScssPath );
		var moved = new List<string>();
		var sheetText = sheet?.Text;

		var inside = markup.Elements.Where( x => x.Start >= element.Start && x.End <= element.End ).ToList();
		var outside = markup.Elements.Except( inside ).ToList();

		var taking = new List<string>();
		if ( firstClass is not null ) taking.Add( firstClass );

		foreach ( var cls in inside.SelectMany( x => x.Classes.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) ).Where( x => !x.Contains( '@' ) ).Distinct() )
		{
			if ( taking.Contains( cls ) ) continue;
			if ( outside.Any( x => x.Classes.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Contains( cls ) ) ) continue;
			taking.Add( cls );
		}

		if ( sheet is not null && taking.Count > 0 )
		{
			var blocks = sheet.Blocks.Where( x => taking.Contains( x.Selector.TrimStart( '.' ) ) && x.Selector.StartsWith( '.' ) && x.Parent?.Parent is null ).OrderByDescending( x => x.Open ).ToList();

			foreach ( var block in blocks )
			{
				var working = ScssDocument.FromText( sheet.Path, sheetText );
				var at = working.Blocks.FirstOrDefault( x => x.Selector == block.Selector && x.Line == block.Line );
				if ( at is null ) continue;

				moved.Insert( 0, working.Extract( at ) );
				sheetText = working.WithoutBlock( at );
			}
		}

		var scss = moved.Count > 0 ? string.Join( "\n\n", moved ) + "\n" : $"// {name}: nothing dresses it yet.\n";

		using ( BeginGesture( $"make {name}" ) )
		{
			try
			{
				File.WriteAllText( razorPath, razor );
				File.WriteAllText( razorPath + ".scss", scss );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"UI editor: could not write {razorPath}: {e.Message}" );
				return false;
			}

			var path = PanelPath.Of( Root, panel );

			if ( !CommitMarkup( $"make {name}", text, markup.WithElementReplaced( element, $"<{name} />" ) ) ) return false;
			if ( sheet is not null && sheetText != sheet.Text ) Commit( sheet, $"move .{firstClass} to {name}", sheetText );

			if ( path is not null ) SelectAfterBuild( path );
		}

		Log.Info( $"UI editor: {name}.razor made from <{element.Name}> with {moved.Count} rule(s): {string.Join( ", ", taking.Select( x => "." + x ) )}" );
		return true;
	}

	//A component's markup put in place of its tag, and its top-level rules brought into this sheet.
	internal bool InlineComponent( Panel panel )
	{
		if ( TagOf( panel, "inline" ) is not { } element ) return false;

		if ( ComponentPathOf( panel ) is not { } componentPath )
		{
			Log.Warning( $"UI editor: <{element.Name}> is not a component of the project's (type {panel.GetType().Name}, source '{TypeLibrary.GetType( panel.GetType() )?.SourceFile}')" );
			return false;
		}

		string componentText;

		try
		{
			componentText = File.ReadAllText( componentPath );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"UI editor: could not read {componentPath}: {e.Message}" );
			return false;
		}

		var component = RazorMarkup.Parse( componentText );
		var root = component.Roots.FirstOrDefault( x => string.Equals( x.Name, "root", StringComparison.OrdinalIgnoreCase ) );

		if ( root is null )
		{
			Log.Warning( $"UI editor: {Path.GetFileName( componentPath )} has no <root> to inline" );
			return false;
		}

		//The classes of both, the tag's other attributes dropped: a parameter has no meaning on a div, and is said so.
		var classes = string.Join( " ", ( root.Classes + " " + element.Classes ).Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Distinct() );
		var dropped = element.Attributes.Where( x => !string.Equals( x.Name, "class", StringComparison.OrdinalIgnoreCase ) ).Select( x => x.Name ).ToList();

		if ( dropped.Count > 0 ) Log.Warning( $"UI editor: inlining <{element.Name}> drops {string.Join( ", ", dropped )} - the markup has no parameters" );

		var extracted = component.Extract( root );
		var openLength = root.OpenEnd - root.Start;
		var body = root.SelfClosing ? "" : extracted.Substring( openLength, extracted.Length - openLength - ( root.End - root.Close ) );

		var replacement = $"<div class=\"{classes}\">" + body + "</div>";

		var markup = _document.Markup;
		var path = PanelPath.Of( Root, panel );

		using ( BeginGesture( $"inline {element.Name}" ) )
		{
			if ( !CommitMarkup( $"inline <{element.Name}>", markup.Text, markup.WithElementReplaced( element, replacement ) ) ) return false;

			//Its rules: the plain ones under our root rule, the at-rules at the top.
			if ( ScssDocument.Load( componentPath + ".scss" ) is { } theirs && theirs.Blocks.Count > 0 )
			{
				var ours = ScssDocument.Load( _document.ScssPath ) ?? ScssDocument.FromText( _document.ScssPath, "" );
				var text = ours.Text;

				foreach ( var block in theirs.Blocks.Where( x => x.Parent is null ) )
				{
					var working = ScssDocument.FromText( ours.Path, text );
					var rootBlock = RootRule() is { } r ? working.Find( r.Block ) : null;

					text = working.WithRawBlock( block.IsAtRule ? null : rootBlock, theirs.Extract( block ) );
				}

				Commit( ours, $"rules of {element.Name}", text );
			}

			if ( path is not null ) SelectAfterBuild( path );
		}

		return true;
	}

	//A component name from whatever was typed: letters and digits, the first one capital.
	static string ComponentName( string typed )
	{
		var sb = new StringBuilder();
		var up = true;

		foreach ( var c in typed ?? "" )
		{
			if ( char.IsLetterOrDigit( c ) )
			{
				sb.Append( up ? char.ToUpperInvariant( c ) : c );
				up = false;
			}
			else up = true;
		}

		return sb.ToString();
	}
}
