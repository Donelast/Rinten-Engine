using System.IO;
using Rinten.UI;

namespace Editor.UiEditor;

/// <summary>An open .razor: file, stylesheet and compiled panel type; type re-resolved after every hotload</summary>
public sealed class RazorDocument
{
	/// <summary>The <c>.razor</c> file, absolute.</summary>
	public string RazorPath { get; }

	/// <summary>The <c>.razor.scss</c> beside it, absolute, whether or not it exists yet.</summary>
	public string ScssPath { get; }

	/// <summary>The name the file goes by in a title.</summary>
	public string Name => Path.GetFileNameWithoutExtension( RazorPath );

	/// <summary>The type compiled from the file - a <see cref="Panel"/> or a <see cref="PanelComponent"/> - or null while there is none.</summary>
	public TypeDescription Type { get; private set; }

	/// <summary>Whether the type is a component, which has a panel only once it is on a game object in a scene.</summary>
	public bool IsComponent => Type is not null && Type.TargetType.IsAssignableTo( typeof( PanelComponent ) );

	public RazorDocument( string razorPath )
	{
		RazorPath = Path.GetFullPath( razorPath );
		ScssPath = RazorPath + ".scss";

		Resolve();
	}

	/// <summary>Whether the stylesheet file is there to be read and written.</summary>
	public bool HasScss => File.Exists( ScssPath );

	RazorMarkup _markup;
	DateTime _markupWritten;

	/// <summary>The file's markup, scanned - read again whenever the file on disk is newer than the scan.</summary>
	public RazorMarkup Markup
	{
		get
		{
			var written = File.Exists( RazorPath ) ? File.GetLastWriteTimeUtc( RazorPath ) : DateTime.MinValue;

			if ( _markup is null || written != _markupWritten )
			{
				_markup = RazorMarkup.Load( RazorPath ) ?? RazorMarkup.Parse( "" );
				_markupWritten = written;
			}

			return _markup;
		}
	}

	/// <summary>Forget the scan; the next look reads the file again.</summary>
	public void ForgetMarkup() => _markup = null;

	/// <summary>The <c>&lt;style&gt;</c> tags in the file, in the order written.</summary>
	public List<RazorMarkup.Element> StyleTags()
		=> Markup.Elements.Where( IsStyleTag ).OrderBy( x => x.Start ).ToList();

	//Asked once a frame, to enable a menu option, so it is counted rather than listed.
	/// <summary>Whether the file dresses itself from a <c>&lt;style&gt;</c> rather than from the sheet beside it.</summary>
	public bool HasStyleTag
	{
		get
		{
			var elements = Markup.Elements;

			for ( var i = 0; i < elements.Count; i++ )
			{
				if ( IsStyleTag( elements[i] ) ) return true;
			}

			return false;
		}
	}

	/// <summary>The line the first <c>&lt;style&gt;</c> starts on, counted from 1, or 0 when there is none.</summary>
	public int StyleTagLine => StyleTags().FirstOrDefault() is { } tag ? tag.Line + 1 : 0;

	//Whether a tag is a <style> , which the engine reads as a sheet and the panel tree never has.
	internal static bool IsStyleTag( RazorMarkup.Element element )
		=> string.Equals( element.Name, "style", StringComparison.OrdinalIgnoreCase );

	/// <summary>Whether a panel was made by this file's markup, rather than by a component's it uses.</summary>
	public bool Owns( Panel panel )
	{
		if ( !panel.IsValid() || string.IsNullOrEmpty( panel.SourceFile ) ) return false;

		var source = Normalise( panel.SourceFile );
		var wanted = Normalise( RazorPath );

		if ( !wanted.EndsWith( source, StringComparison.OrdinalIgnoreCase ) ) return false;

		var start = wanted.Length - source.Length;
		return start == 0 || wanted[start - 1] == '/';
	}

	/// <summary>The tag a panel was made from: by recorded position, else by structure from a located ancestor</summary>
	public RazorMarkup.Element Locate( Panel panel, Panel root = null )
	{
		if ( !panel.IsValid() ) return null;

		if ( Exact( panel, root ) is { } exact ) return exact;

		//Up to something found outright, remembering the way down.
		var path = new List<Panel>();
		var at = panel;
		RazorMarkup.Element anchor = null;

		while ( at is not null )
		{
			path.Add( at );

			if ( at.Parent is not { } parent ) break;

			anchor = Exact( parent, root );
			at = parent;

			if ( anchor is not null ) break;
		}

		if ( anchor is null ) return null;

		//Down again: path holds the panel itself first and the child of the anchor last.
		var element = anchor;

		for ( var i = path.Count - 1; i >= 0; i-- )
		{
			var child = path[i];
			var next = ChildTagOf( element, child );

			if ( next is null ) return null;

			element = next;
		}

		return element;
	}

	//The tag a panel's recorded position points at, when it points at one exactly.
	RazorMarkup.Element Exact( Panel panel, Panel root )
	{
		if ( root is not null && ReferenceEquals( panel, root ) )
		{
			var roots = Markup.Roots;

			//A <style> is written at the top like any other tag but makes no panel - see StyleTags - so the fallback steps past it. A
			//file that opens with one would otherwise have its root panel point at the tag, and every edit of the root go into the css.
			return roots.FirstOrDefault( x => string.Equals( x.Name, "root", StringComparison.OrdinalIgnoreCase ) )
				?? roots.FirstOrDefault( x => !IsStyleTag( x ) );
		}

		//A column of -1 is the render tree saying the position is only near.
		if ( !Owns( panel ) || panel.SourceColumn < 0 ) return null;

		return Markup.FindExact( panel.SourceLine, panel.SourceColumn );
	}

	//Walks tags and child panels in step, allowing for @if (no panel) and @foreach (many panels)
	static RazorMarkup.Element ChildTagOf( RazorMarkup.Element element, Panel child )
	{
		var tags = TagsOf( element );
		var j = 0;

		foreach ( var sibling in child.Parent.Children )
		{
			//Past the optional tags this panel is not, to the first it could be.
			while ( j < tags.Count && !Matches( tags[j], sibling ) && tags[j].Control is not null )
				j++;

			var tag = j < tags.Count ? tags[j] : null;
			var matches = tag is not null && Matches( tag, sibling );

			if ( !matches && sibling is Rinten.UI.Label )
			{
				//Words, not a tag: the element they are in.
				if ( ReferenceEquals( sibling, child ) ) return element;
				continue;
			}

			if ( ReferenceEquals( sibling, child ) ) return tag;

			//A repeating tag stays for the next panel; any other is spent.
			if ( tag is not null && !Repeats( tag ) ) j++;
			else if ( tag is not null && j + 1 < tags.Count && !Matches( tag, NextSibling( child.Parent, sibling ) ) ) j++;
		}

		return null;
	}

	//Child tags minus <style>, which makes no panel and would throw the walk out of step
	static List<RazorMarkup.Element> TagsOf( RazorMarkup.Element element )
	{
		var children = element.Children;

		for ( var i = 0; i < children.Count; i++ )
		{
			if ( !IsStyleTag( children[i] ) ) continue;

			var kept = new List<RazorMarkup.Element>( children.Count - 1 );

			foreach ( var tag in children )
				if ( !IsStyleTag( tag ) ) kept.Add( tag );

			return kept;
		}

		return children;
	}

	//Whether a tag could have made a panel: the same element, and every class the tag writes out
	//on the panel - which may wear more, since a Label gives itself label and a Button button .
	static bool Matches( RazorMarkup.Element tag, Panel panel )
	{
		if ( tag is null || panel is null ) return false;
		if ( !string.Equals( tag.Name, panel.ElementName, StringComparison.OrdinalIgnoreCase ) ) return false;

		var classes = tag.Classes;
		if ( classes.Contains( '@' ) ) return true;

		var has = panel.Class.ToHashSet( StringComparer.OrdinalIgnoreCase );

		foreach ( var c in classes.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( !has.Contains( c ) ) return false;
		}

		return true;
	}

	static bool Repeats( RazorMarkup.Element tag ) => tag.Control is "foreach" or "for" or "while" or "do";

	static Panel NextSibling( Panel parent, Panel panel )
	{
		var seen = false;

		foreach ( var each in parent.Children )
		{
			if ( seen ) return each;
			if ( ReferenceEquals( each, panel ) ) seen = true;
		}

		return null;
	}

	/// <summary>Find the panel type for the file, in whichever project compiled it.</summary>
	public bool Resolve()
	{
		var wanted = Normalise( RazorPath );

		TypeDescription best = null;
		var bestLength = 0;

		foreach ( var type in TypeLibrary.GetTypes<Panel>().Concat( TypeLibrary.GetTypes<PanelComponent>() ) )
		{
			if ( string.IsNullOrEmpty( type.SourceFile ) ) continue;
			if ( type.IsAbstract || type.IsGenericType ) continue;

			var source = Normalise( type.SourceFile );

			if ( !source.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( source.Length <= bestLength ) continue;
			if ( !wanted.EndsWith( source, StringComparison.OrdinalIgnoreCase ) ) continue;

			//A tail that starts mid-name - "Hud.razor" matching "MyHud.razor" - is not the same file.
			var start = wanted.Length - source.Length;
			if ( start > 0 && wanted[start - 1] != '/' ) continue;

			best = type;
			bestLength = source.Length;
		}

		Type = best;
		return Type is not null;
	}

	/// <summary>A fresh panel of the file's type, or null while the type is missing or is a component - see <see cref="IsComponent"/>.</summary>
	public Panel Instantiate()
	{
		if ( Type is null || IsComponent ) return null;

		try
		{
			return Type.Create<Panel>();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"UI editor: could not create {Type.FullName}: {e.Message}" );
			return null;
		}
	}

	static string Normalise( string path ) => path.Replace( '\\', '/' ).TrimStart( '/' );
}
