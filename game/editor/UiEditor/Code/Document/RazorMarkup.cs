using System.Text;

namespace Editor.UiEditor;

/// <summary>Light .razor scanner: spans of tags and attributes, skipping C# blocks, for in-place edits</summary>
public sealed class RazorMarkup
{
	/// <summary>One <c>&lt;tag ...&gt;...&lt;/tag&gt;</c>.</summary>
	public sealed class Element
	{
		public string Name;

		/// <summary>The line the tag starts on, counted from 0.</summary>
		public int Line;

		/// <summary>The column the <c>&lt;</c> is at, counted from 0.</summary>
		public int Column;

		/// <summary>Text offset of the <c>&lt;</c>.</summary>
		public int Start;

		/// <summary>Text offset one past the <c>&gt;</c> of the opening tag.</summary>
		public int OpenEnd;

		/// <summary>Text offset of the <c>&lt;/</c> of the closing tag, or -1 when the tag closes itself.</summary>
		public int Close = -1;

		/// <summary>Text offset one past the end of the element, closing tag included.</summary>
		public int End;

		public bool SelfClosing;

		public Element Parent;
		public List<Element> Children = new();
		public List<Attribute> Attributes = new();

		/// <summary>The keyword of the nearest <c>@if</c>, <c>@foreach</c> and the like this tag is written inside, or null when it is plain.</summary>
		public string Control;

		/// <summary>How many control blocks deep this tag is, within its parent element.</summary>
		public int ControlDepth;

		/// <summary>What the nearest control block says in its parentheses - <c>var item in Items</c> for a foreach - or null.</summary>
		public string ControlHeader;

		/// <summary>The member a <c>@foreach</c> walks, when it walks a plain name: <c>Items</c> from <c>var item in Items</c>.</summary>
		public string ForEachMember
		{
			get
			{
				if ( Control != "foreach" || ControlHeader is null ) return null;

				var at = ControlHeader.IndexOf( " in ", StringComparison.Ordinal );
				if ( at < 0 ) return null;

				var source = ControlHeader.Substring( at + 4 ).Trim();
				return source.Length > 0 && source.All( c => char.IsLetterOrDigit( c ) || c == '_' ) ? source : null;
			}
		}

		/// <summary>The class attribute's value, or an empty string.</summary>
		public string Classes => Attribute( "class" )?.Value ?? "";

		public Attribute Attribute( string name )
			=> Attributes.FirstOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase ) );

		/// <summary>The text between the tags, or empty when the tag closes itself.</summary>
		public string Inner( string text ) => Close < 0 ? "" : text.Substring( OpenEnd, Close - OpenEnd );

		public override string ToString() => $"<{Name}> at {Line + 1}:{Column + 1}";
	}

	/// <summary>One <c>name="value"</c> on a tag.</summary>
	public sealed class Attribute
	{
		public string Name;
		public string Value;

		/// <summary>Text offset of the first character of the name.</summary>
		public int Start;

		/// <summary>Text offset one past the last character of the attribute, closing quote included.</summary>
		public int End;

		/// <summary>Text offset of the first character of the value, inside the quotes.</summary>
		public int ValueStart;

		/// <summary>Text offset one past the last character of the value.</summary>
		public int ValueEnd;

		/// <summary>Whether there was an <c>=</c> and a value at all.</summary>
		public bool HasValue;

		/// <summary>The quote the value is in, or nothing when it was bare.</summary>
		public char Quote;
	}

	public string Text { get; }

	/// <summary>Every element, in the order written.</summary>
	public List<Element> Elements { get; } = new();

	/// <summary>The elements written at the top, outside any tag.</summary>
	public List<Element> Roots { get; } = new();

	/// <summary>Text offset of the <c>{</c> of the <c>@code</c> block, or -1 when the file has none.</summary>
	public int CodeOpen { get; private set; } = -1;

	/// <summary>Text offset of the <c>}</c> that closes the <c>@code</c> block, or -1.</summary>
	public int CodeClose { get; private set; } = -1;

	/// <summary>A member put into the <c>@code</c> block, on lines of its own before its closing brace at the block's inner indent - the block made at the end of the file when there is none.</summary>
	public string WithCodeMember( string member )
	{
		if ( CodeOpen < 0 )
		{
			var text = Text.TrimEnd();
			return text + "\n\n@code\n{\n" + Indented( member, "\t" ) + "\n}\n";
		}

		var close = CodeClose;

		//Back over the whitespace before the brace, so the member sits under the last one and not under a blank.
		var at = close;
		while ( at > CodeOpen + 1 && char.IsWhiteSpace( Text[at - 1] ) ) at--;

		var empty = at == CodeOpen + 1;
		var sb = new StringBuilder();

		sb.Append( Text, 0, at );
		sb.Append( empty ? "\n" : "\n\n" );
		sb.Append( Indented( member, "\t" ) );
		sb.Append( '\n' );
		sb.Append( Text, close, Text.Length - close );

		return sb.ToString();
	}

	/// <summary>Whether the file says its own <c>@namespace</c>. When it does not, the compiler puts a line in front, and every line it reports is one further down.</summary>
	public bool HasNamespace { get; private set; }

	/// <summary>The line the compiler adds at the top when the file has no <c>@namespace</c>.</summary>
	public int CompilerLineOffset => HasNamespace ? 0 : 1;

	readonly int[] _lineStarts;

	RazorMarkup( string text )
	{
		Text = text ?? "";
		_lineStarts = LineStarts( Text );

		Scan();
	}

	public static RazorMarkup Parse( string text ) => new( text );

	public static RazorMarkup Load( string path )
	{
		if ( string.IsNullOrEmpty( path ) || !System.IO.File.Exists( path ) ) return null;

		return new RazorMarkup( System.IO.File.ReadAllText( path ) );
	}

	//Finding

	/// <summary>The element whose tag starts exactly at a line and column the compiler reported - the lines it reports count from 1, and from one further down when the file has no <c>@namespace</c> of its own.</summary>
	public Element FindExact( int compilerLine, int column )
	{
		var line = compilerLine - 1 - CompilerLineOffset;

		if ( line < 0 || line >= _lineStarts.Length ) return null;

		var offset = _lineStarts[line] + Math.Max( 0, column );

		foreach ( var element in Elements )
		{
			if ( element.Start == offset ) return element;
		}

		return null;
	}

	/// <summary>The line an offset is on, counted from 0.</summary>
	public int LineOf( int offset )
	{
		var line = Array.BinarySearch( _lineStarts, offset );
		return line >= 0 ? line : ~line - 1;
	}

	/// <summary>The column an offset is at on its line, counted from 0.</summary>
	public int ColumnOf( int offset ) => offset - _lineStarts[LineOf( offset )];

	//Editing - each gives back the whole text with one change in it

	/// <summary>Set an attribute on a tag: the value replaced where the attribute is there, the attribute added at the end of the tag where it is not.</summary>
	public string WithAttribute( Element element, string name, string value )
	{
		var existing = element.Attribute( name );

		if ( existing is not null && existing.HasValue )
			return Text.Substring( 0, existing.ValueStart ) + value + Text.Substring( existing.ValueEnd );

		if ( existing is not null )
			return Text.Substring( 0, existing.End ) + $"=\"{value}\"" + Text.Substring( existing.End );

		//Before the > or the />, after whatever is there.
		var at = element.OpenEnd - 1;
		if ( element.SelfClosing && at > 0 && Text[at - 1] == '/' ) at--;

		while ( at > element.Start && char.IsWhiteSpace( Text[at - 1] ) ) at--;

		var tail = element.SelfClosing ? " " : "";

		return Text.Substring( 0, at ) + $" {name}=\"{value}\"" + tail + Text.Substring( at ).TrimStart( ' ' );
	}

	/// <summary>Take an attribute off a tag.</summary>
	public string WithoutAttribute( Element element, string name )
	{
		var existing = element.Attribute( name );
		if ( existing is null ) return Text;

		var start = existing.Start;
		while ( start > element.Start + 1 && char.IsWhiteSpace( Text[start - 1] ) ) start--;

		return Text.Substring( 0, start ) + Text.Substring( existing.End );
	}

	/// <summary>Put markup inside an element, as its last child, on a line of its own at one indent more than the element. A tag that closed itself is opened for it.</summary>
	public string WithChildAppended( Element element, string markup )
	{
		var indent = IndentOf( element );
		var inner = indent + "\t";

		if ( element.SelfClosing )
		{
			//<div ... /> becomes <div ...>\n\tchild\n</div>
			var open = Text.Substring( element.Start, element.OpenEnd - element.Start );
			var slash = open.LastIndexOf( '/' );
			var reopened = open.Substring( 0, slash ).TrimEnd() + ">";

			return Text.Substring( 0, element.Start ) + reopened + "\n" + Indented( markup, inner ) + "\n" + indent + $"</{element.Name}>" + Text.Substring( element.End );
		}

		//Before the closing tag, and before the whitespace that leads to it.
		var at = element.Close;
		while ( at > element.OpenEnd && ( Text[at - 1] == ' ' || Text[at - 1] == '\t' ) ) at--;

		var onOwnLine = at > 0 && Text[at - 1] == '\n';
		var empty = string.IsNullOrWhiteSpace( Text.Substring( element.OpenEnd, element.Close - element.OpenEnd ) );

		var sb = new StringBuilder();

		if ( empty )
		{
			//<div></div> becomes <div>\n\tchild\n</div>
			sb.Append( Text, 0, element.OpenEnd );
			sb.Append( '\n' ).Append( Indented( markup, inner ) ).Append( '\n' ).Append( indent );
			sb.Append( Text, element.Close, Text.Length - element.Close );
			return sb.ToString();
		}

		if ( !onOwnLine )
		{
			//Children written inline: the new one goes after them, and the closing tag moves to a line of its own.
			sb.Append( Text, 0, at );
			sb.Append( '\n' ).Append( Indented( markup, inner ) ).Append( '\n' ).Append( indent );
			sb.Append( Text, element.Close, Text.Length - element.Close );
			return sb.ToString();
		}

		sb.Append( Text, 0, at );
		sb.Append( Indented( markup, inner ) ).Append( '\n' );
		sb.Append( Text, at, Text.Length - at );
		return sb.ToString();
	}

	/// <summary>Put markup beside an element, just before it, at the same indent.</summary>
	public string WithSiblingBefore( Element element, string markup )
	{
		var indent = IndentOf( element );
		var at = element.Start;

		//Back to the start of the element's line, so the new lines come whole.
		var lineStart = at;
		while ( lineStart > 0 && Text[lineStart - 1] is ' ' or '\t' ) lineStart--;

		if ( lineStart == 0 || Text[lineStart - 1] == '\n' )
			return Text.Substring( 0, lineStart ) + Indented( markup, indent ) + "\n" + Text.Substring( lineStart );

		return Text.Substring( 0, at ) + markup + "\n" + indent + Text.Substring( at );
	}

	/// <summary>An element moved inside another, as its last child, lines and all.</summary>
	public string WithElementInto( Element element, Element parent )
	{
		var moving = Extract( element );
		var (a, b) = WholeLines( element.Start, element.End );

		var without = Text.Substring( 0, a ) + Text.Substring( b );

		//The parent in the text without the element: the same tag, found again by its offset, moved when the element was before it.
		var parentStart = parent.Start > a ? parent.Start - ( b - a ) : parent.Start;
		var again = Parse( without );
		var target = again.Elements.FirstOrDefault( x => x.Start == parentStart );

		return target is null ? Text : again.WithChildAppended( target, moving );
	}

	/// <summary>Put markup beside an element, just after it, at the same indent.</summary>
	public string WithSiblingAfter( Element element, string markup )
	{
		var indent = IndentOf( element );
		var at = element.End;

		return Text.Substring( 0, at ) + "\n" + Indented( markup, indent ) + Text.Substring( at );
	}

	/// <summary>Set the words between a tag's open and close, in place of whatever was there.</summary>
	public string WithInnerText( Element element, string text )
	{
		if ( element.SelfClosing )
		{
			var open = Text.Substring( element.Start, element.OpenEnd - element.Start );
			var slash = open.LastIndexOf( '/' );
			var reopened = open.Substring( 0, slash ).TrimEnd() + ">";

			return Text.Substring( 0, element.Start ) + reopened + text + $"</{element.Name}>" + Text.Substring( element.End );
		}

		return Text.Substring( 0, element.OpenEnd ) + text + Text.Substring( element.Close );
	}

	/// <summary>An element moved to just before a sibling of it, lines and all.</summary>
	public string WithElementBefore( Element element, Element sibling ) => Moved( element, sibling, before: true );

	/// <summary>An element moved to just after a sibling of it, lines and all.</summary>
	public string WithElementAfter( Element element, Element sibling ) => Moved( element, sibling, before: false );

	string Moved( Element element, Element sibling, bool before )
	{
		var (a, b) = WholeLines( element.Start, element.End );
		var (c, d) = WholeLines( sibling.Start, sibling.End );

		var moving = Text.Substring( a, b - a );

		//A span that is not whole lines is put on lines of its own.
		if ( !moving.EndsWith( '\n' ) ) moving += "\n";

		var without = Text.Substring( 0, a ) + Text.Substring( b );

		//The sibling's place in the text without the element.
		var at = before ? c : d;
		if ( at > a ) at -= b - a;

		if ( !before && at > 0 && without[at - 1] != '\n' ) moving = "\n" + moving;

		return without.Substring( 0, at ) + moving + without.Substring( at );
	}

	/// <summary>An element wrapped in a block - <c>@foreach ( var item in Items )</c>, <c>@if ( Open )</c> - on lines of its own around it, the element pushed in by one indent.</summary>
	public string WithBlockAround( Element element, string header )
	{
		var indent = IndentOf( element );
		var (a, b) = WholeLines( element.Start, element.End );

		var inner = Text.Substring( a, b - a );
		if ( !inner.EndsWith( '\n' ) ) inner += "\n";

		//Every line of the element one deeper.
		var lines = inner.TrimEnd( '\n' ).Split( '\n' );
		for ( var i = 0; i < lines.Length; i++ )
			if ( lines[i].Length > 0 ) lines[i] = "\t" + lines[i];

		var sb = new StringBuilder();
		sb.Append( Text, 0, a );
		sb.Append( indent ).Append( header ).Append( '\n' );
		sb.Append( indent ).Append( "{\n" );
		sb.Append( string.Join( '\n', lines ) ).Append( '\n' );
		sb.Append( indent ).Append( "}\n" );
		sb.Append( Text, b, Text.Length - b );

		return sb.ToString();
	}

	/// <summary>Elements wrapped in a tag of their own: the span from the first to the last, pushed in by one indent, between an open tag and a close tag on lines of their own. The elements are siblings, in the order written.</summary>
	public string WithWrapped( IReadOnlyList<Element> elements, string open, string close )
	{
		var first = elements.OrderBy( x => x.Start ).First();
		var last = elements.OrderBy( x => x.End ).Last();
		var indent = IndentOf( first );

		var (a, b) = WholeLines( first.Start, last.End );

		var inner = Text.Substring( a, b - a ).TrimEnd( '\n' );
		var lines = inner.Split( '\n' );
		for ( var i = 0; i < lines.Length; i++ )
			if ( lines[i].Length > 0 ) lines[i] = "\t" + lines[i];

		var sb = new StringBuilder();
		sb.Append( Text, 0, a );
		sb.Append( indent ).Append( open ).Append( '\n' );
		sb.Append( string.Join( '\n', lines ) ).Append( '\n' );
		sb.Append( indent ).Append( close ).Append( '\n' );
		sb.Append( Text, b, Text.Length - b );

		return sb.ToString();
	}

	/// <summary>An element's tags taken away and what was inside left where it was, pulled out by one indent.</summary>
	public string WithoutWrapper( Element element )
	{
		if ( element.SelfClosing || element.Close < 0 ) return Text;

		var (a, b) = WholeLines( element.Start, element.End );

		var inner = element.Inner( Text ).Trim( '\n', '\r' );
		var lines = inner.Split( '\n' );
		for ( var i = 0; i < lines.Length; i++ )
			if ( lines[i].StartsWith( '\t' ) ) lines[i] = lines[i].Substring( 1 );

		var body = string.Join( '\n', lines );
		if ( body.Length > 0 && !body.EndsWith( '\n' ) ) body += "\n";

		return Text.Substring( 0, a ) + body + Text.Substring( b );
	}

	/// <summary>An element replaced by other markup, at its indent.</summary>
	public string WithElementReplaced( Element element, string markup )
	{
		var indent = IndentOf( element );
		var (a, b) = WholeLines( element.Start, element.End );

		var alone = a < element.Start || b > element.End;

		return alone
			? Text.Substring( 0, a ) + Indented( markup, indent ) + "\n" + Text.Substring( b )
			: Text.Substring( 0, element.Start ) + markup + Text.Substring( element.End );
	}

	/// <summary>Take an element out, and its line with it when it stood alone on one.</summary>
	public string WithoutElement( Element element )
	{
		var (start, end) = WholeLines( element.Start, element.End );

		return Text.Substring( 0, start ) + Text.Substring( end );
	}

	/// <summary>The text of an element, from its first tag to its last, with the indent of its first line taken off every line.</summary>
	public string Extract( Element element )
	{
		var text = Text.Substring( element.Start, element.End - element.Start );
		var indent = IndentOf( element );

		if ( indent.Length == 0 ) return text;

		var lines = text.Split( '\n' );

		for ( var i = 1; i < lines.Length; i++ )
		{
			if ( lines[i].StartsWith( indent, StringComparison.Ordinal ) )
				lines[i] = lines[i].Substring( indent.Length );
		}

		return string.Join( '\n', lines );
	}

	//The span of whole lines an element takes, when it starts and ends its lines; else the element's own span.
	(int start, int end) WholeLines( int start, int end )
	{
		var lineStart = start;
		while ( lineStart > 0 && Text[lineStart - 1] is ' ' or '\t' ) lineStart--;

		var lineEnd = end;
		while ( lineEnd < Text.Length && Text[lineEnd] is ' ' or '\t' ) lineEnd++;

		var aloneBefore = lineStart == 0 || Text[lineStart - 1] == '\n';
		var aloneAfter = lineEnd >= Text.Length || Text[lineEnd] == '\n';

		if ( aloneBefore && aloneAfter )
			return ( lineStart, Math.Min( Text.Length, lineEnd + 1 ) );

		return ( start, end );
	}

	/// <summary>The whitespace the element's line starts with.</summary>
	public string IndentOf( Element element )
	{
		var lineStart = _lineStarts[element.Line];
		var end = lineStart;

		while ( end < Text.Length && Text[end] is ' ' or '\t' ) end++;

		return Text.Substring( lineStart, end - lineStart );
	}

	static string Indented( string markup, string indent )
	{
		var lines = markup.Replace( "\r\n", "\n" ).Split( '\n' );

		for ( var i = 0; i < lines.Length; i++ )
			lines[i] = indent + lines[i];

		return string.Join( '\n', lines );
	}

	//Scanning

	//Where the scan is: reading tags, or getting past C#.
	int _i;

	//The element being read, whose children are being found.
	Element _open;

	//The control keywords the scan is inside, innermost last.
	readonly List<string> _controls = new();
	readonly List<string> _controlHeaders = new();
	string _lastHeader;

	void Scan()
	{
		HasNamespace = Text.Contains( "@namespace" );

		_i = 0;
		_open = null;

		ScanMarkup( untilClose: null );
	}

	//Read markup until the end of the text, or until the closing tag of the element
	//given is read. Tags become elements under _open; a @ hands over to ScanCode.
	void ScanMarkup( Element untilClose )
	{
		var text = Text;

		while ( _i < text.Length )
		{
			var c = text[_i];

			if ( c == '@' )
			{
				ScanTransition();
				continue;
			}

			if ( c == '<' )
			{
				//A comment, a closing tag, an opening tag, or a stray <.
				if ( Starts( "<!--" ) )
				{
					var end = text.IndexOf( "-->", _i + 4, StringComparison.Ordinal );
					_i = end < 0 ? text.Length : end + 3;
					continue;
				}

				if ( _i + 1 < text.Length && text[_i + 1] == '/' )
				{
					ReadCloseTag();

					//By its own tag, or by an ancestor's that shut it on the way.
					if ( untilClose is not null && untilClose.Close >= 0 )
						return;

					continue;
				}

				if ( _i + 1 < text.Length && ( char.IsLetter( text[_i + 1] ) || text[_i + 1] == '_' ) )
				{
					ReadElement();
					continue;
				}
			}

			_i++;
		}
	}

	//Read </name> at the scan position, and say which name it closed.
	string ReadCloseTag()
	{
		var text = Text;
		var start = _i;

		_i += 2;
		var nameStart = _i;
		while ( _i < text.Length && IsNameChar( text[_i] ) ) _i++;
		var name = text.Substring( nameStart, _i - nameStart );

		var end = text.IndexOf( '>', _i );
		_i = end < 0 ? text.Length : end + 1;

		//The tag being read closes here - or an ancestor does, and everything left open under it ends where this tag begins.
		for ( var at = _open; at is not null; at = at.Parent )
		{
			if ( !string.Equals( at.Name, name, StringComparison.OrdinalIgnoreCase ) ) continue;

			for ( var closing = _open; closing is not null && closing != at; closing = closing.Parent )
			{
				closing.Close = start;
				closing.End = start;
			}

			at.Close = start;
			at.End = _i;

			_open = at.Parent;
			return name;
		}

		return name;
	}

	//Read an opening tag and, unless it closes itself, everything inside it.
	void ReadElement()
	{
		var text = Text;

		var element = new Element
		{
			Start = _i,
			Line = LineOf( _i ),
			Column = ColumnOf( _i ),
			Parent = _open,
			Control = _controls.Count > 0 ? _controls[^1] : null,
			ControlHeader = _controlHeaders.Count > 0 ? _controlHeaders[^1] : null,
			ControlDepth = _controls.Count,
		};

		_i++;
		var nameStart = _i;
		while ( _i < text.Length && IsNameChar( text[_i] ) ) _i++;
		element.Name = text.Substring( nameStart, _i - nameStart );

		ReadAttributes( element );

		//At the > or the />
		if ( _i < text.Length && text[_i] == '/' )
		{
			element.SelfClosing = true;
			_i++;
		}

		if ( _i < text.Length && text[_i] == '>' ) _i++;

		element.OpenEnd = _i;
		element.End = _i;

		Elements.Add( element );

		if ( element.Parent is null ) Roots.Add( element );
		else element.Parent.Children.Add( element );

		if ( element.SelfClosing ) return;

		//The control depth of the children is counted from this element.
		var controlsAbove = _controls.Count;
		var wasOpen = _open;

		_open = element;
		ScanMarkup( element );

		//Whatever the close tag did; an unclosed tag ends where the text does.
		if ( element.Close < 0 )
		{
			element.Close = _i;
			element.End = _i;
		}

		_open = wasOpen;

		while ( _controls.Count > controlsAbove ) { _controls.RemoveAt( _controls.Count - 1 ); _controlHeaders.RemoveAt( _controlHeaders.Count - 1 ); }
	}

	void ReadAttributes( Element element )
	{
		var text = Text;

		while ( _i < text.Length )
		{
			while ( _i < text.Length && char.IsWhiteSpace( text[_i] ) ) _i++;
			if ( _i >= text.Length ) return;

			var c = text[_i];
			if ( c == '>' || c == '/' ) return;

			//@attributes="..." and @onclick="..." start with the at; a bare @expr in a tag is one too, and is read as a name.
			var attribute = new Attribute { Start = _i };

			var nameStart = _i;
			while ( _i < text.Length && !char.IsWhiteSpace( text[_i] ) && text[_i] != '=' && text[_i] != '>' && !( text[_i] == '/' && _i + 1 < text.Length && text[_i + 1] == '>' ) ) _i++;

			attribute.Name = text.Substring( nameStart, _i - nameStart );

			if ( attribute.Name.Length == 0 )
			{
				_i++;
				continue;
			}

			var afterName = _i;
			while ( _i < text.Length && char.IsWhiteSpace( text[_i] ) ) _i++;

			if ( _i < text.Length && text[_i] == '=' )
			{
				_i++;
				while ( _i < text.Length && char.IsWhiteSpace( text[_i] ) ) _i++;

				attribute.HasValue = true;

				if ( _i < text.Length && ( text[_i] == '"' || text[_i] == '\'' ) )
				{
					attribute.Quote = text[_i];
					_i++;
					attribute.ValueStart = _i;

					//A quoted value can hold @(expr) with quotes of its own inside the parentheses; those are stepped over.
					while ( _i < text.Length && text[_i] != attribute.Quote )
					{
						if ( text[_i] == '@' && _i + 1 < text.Length && text[_i + 1] == '(' )
						{
							_i = SkipBalanced( _i + 1, '(', ')' );
							continue;
						}

						_i++;
					}

					attribute.ValueEnd = _i;
					if ( _i < text.Length ) _i++;
				}
				else
				{
					//Bare: up to the next space - unless it is @( ... ), which razor allows unquoted and which has spaces of its own.
					attribute.ValueStart = _i;

					if ( _i + 1 < text.Length && text[_i] == '@' && text[_i + 1] == '(' )
						_i = SkipBalanced( _i + 1, '(', ')' );
					else
						while ( _i < text.Length && !char.IsWhiteSpace( text[_i] ) && text[_i] != '>' ) _i++;

					attribute.ValueEnd = _i;
				}

				attribute.Value = text.Substring( attribute.ValueStart, attribute.ValueEnd - attribute.ValueStart );
				attribute.End = _i;
			}
			else
			{
				_i = afterName;
				attribute.End = afterName;
				attribute.Value = null;
			}

			element.Attributes.Add( attribute );
		}
	}

	static readonly string[] BlockKeywords = { "if", "else", "foreach", "for", "while", "do", "switch", "using", "lock", "try", "catch", "finally" };
	static readonly string[] CodeKeywords = { "code", "functions" };
	static readonly string[] Directives = { "using", "inherits", "namespace", "attribute", "inject", "typeparam", "implements", "layout", "page", "model", "preservewhitespace" };

	//At a @ in markup: a comment, a code block, a control block, an expression, a directive, or an escaped @@ . The scan is left after it.
	void ScanTransition()
	{
		var text = Text;
		var at = _i;

		if ( Starts( "@@" ) ) { _i += 2; return; }

		if ( Starts( "@*" ) )
		{
			var end = text.IndexOf( "*@", _i + 2, StringComparison.Ordinal );
			_i = end < 0 ? text.Length : end + 2;
			return;
		}

		if ( Starts( "@{" ) )
		{
			_i++;
			ScanCodeBlock( "code" );
			return;
		}

		if ( Starts( "@(" ) )
		{
			_i = SkipBalanced( _i + 1, '(', ')' );
			return;
		}

		if ( Starts( "@:" ) )
		{
			_i = EndOfLine( _i );
			return;
		}

		//A word: a keyword, a directive, or the start of an implicit expression.
		var wordStart = at + 1;
		var wordEnd = wordStart;
		while ( wordEnd < text.Length && ( char.IsLetterOrDigit( text[wordEnd] ) || text[wordEnd] == '_' ) ) wordEnd++;
		var word = text.Substring( wordStart, wordEnd - wordStart );

		if ( Array.IndexOf( CodeKeywords, word ) >= 0 )
		{
			var brace = text.IndexOf( '{', wordEnd );
			if ( brace < 0 ) { _i = text.Length; return; }

			//Pure C#: nothing in it is markup, whatever it looks like.
			_i = SkipCSharp( brace );

			if ( word == "code" && CodeOpen < 0 )
			{
				CodeOpen = brace;
				CodeClose = Math.Max( brace + 1, _i - 1 );
			}

			return;
		}

		if ( Array.IndexOf( BlockKeywords, word ) >= 0 && IsBlockStart( word, wordEnd ) )
		{
			_i = wordEnd;
			ScanControl( word );
			return;
		}

		if ( Array.IndexOf( Directives, word ) >= 0 && ( at == 0 || text[at - 1] == '\n' || text[at - 1] == '\r' ) )
		{
			_i = EndOfLine( at );
			return;
		}

		//An implicit expression: @Name.Member(args)[index], no whitespace.
		_i = wordEnd;

		while ( _i < text.Length )
		{
			var c = text[_i];

			if ( c == '.' && _i + 1 < text.Length && ( char.IsLetter( text[_i + 1] ) || text[_i + 1] == '_' ) )
			{
				_i++;
				while ( _i < text.Length && ( char.IsLetterOrDigit( text[_i] ) || text[_i] == '_' ) ) _i++;
				continue;
			}

			if ( c == '(' ) { _i = SkipBalanced( _i, '(', ')' ); continue; }
			if ( c == '[' ) { _i = SkipBalanced( _i, '[', ']' ); continue; }
			if ( c == '?' && _i + 1 < text.Length && text[_i + 1] == '.' ) { _i++; continue; }

			break;
		}
	}

	//Whether a keyword at this point is followed by its header or its brace - @if ( , @try { ,
	//@else if - and not by a member, as @for.Each would be, nor by a name, as @using Rinten; is.
	bool IsBlockStart( string word, int after )
	{
		var text = Text;
		var i = after;

		while ( i < text.Length && char.IsWhiteSpace( text[i] ) ) i++;
		if ( i >= text.Length ) return false;

		if ( text[i] == '(' || text[i] == '{' ) return true;

		return word == "else" && char.IsLetter( text[i] );
	}

	//A control block: its header in parentheses, then its body in braces, which is C# with markup
	//where a statement starts with a tag. After the body, an else or a catch continues it.
	void ScanControl( string keyword )
	{
		var text = Text;

		while ( true )
		{
			while ( _i < text.Length && char.IsWhiteSpace( text[_i] ) ) _i++;

			//Anything up to the brace: (condition), "else if (x)", "catch (e)".
			while ( _i < text.Length && text[_i] != '{' )
			{
				if ( text[_i] == '(' )
				{
					var open = _i;
					_i = SkipBalanced( _i, '(', ')' );
					_lastHeader = text.Substring( open + 1, Math.Max( 0, _i - open - 2 ) ).Trim();
					continue;
				}
				if ( text[_i] == '<' || text[_i] == '@' ) return;
				_i++;
			}

			if ( _i >= text.Length ) return;

			ScanCodeBlock( keyword );

			//What may follow the closing brace of this block on the same statement.
			var save = _i;
			while ( _i < text.Length && char.IsWhiteSpace( text[_i] ) ) _i++;

			if ( Starts( "else" ) || Starts( "catch" ) || Starts( "finally" ) || ( keyword == "do" && Starts( "while" ) ) )
			{
				var wordEnd = _i;
				while ( wordEnd < text.Length && char.IsLetter( text[wordEnd] ) ) wordEnd++;

				if ( keyword == "do" && Starts( "while" ) )
				{
					//do { } while (x); - the tail is a statement, no body.
					_i = SkipBalanced( text.IndexOf( '(', wordEnd ), '(', ')' );
					if ( _i < text.Length && text[_i] == ';' ) _i++;
					return;
				}

				_i = wordEnd;
				continue;
			}

			_i = save;
			return;
		}
	}

	//A brace-delimited body of C#, entered at its { , in which a tag at the start of a
	//statement is markup and belongs to the element that is open. The scan is left after the } .
	void ScanCodeBlock( string keyword )
	{
		var text = Text;

		if ( _i >= text.Length || text[_i] != '{' ) return;

		_controls.Add( keyword );
		_controlHeaders.Add( _lastHeader );
		_lastHeader = null;

		_i++;
		var depth = 1;

		//The last thing seen that was not whitespace: a tag right after it is markup.
		var statementStart = true;

		while ( _i < text.Length && depth > 0 )
		{
			var c = text[_i];

			if ( c == '/' && _i + 1 < text.Length && text[_i + 1] == '/' ) { _i = EndOfLine( _i ); continue; }
			if ( c == '/' && _i + 1 < text.Length && text[_i + 1] == '*' )
			{
				var end = text.IndexOf( "*/", _i + 2, StringComparison.Ordinal );
				_i = end < 0 ? text.Length : end + 2;
				continue;
			}

			if ( c == '"' || c == '\'' ) { _i = SkipString( _i ); statementStart = false; continue; }

			if ( c == '@' )
			{
				//A nested transition inside code: @if within an if, @: a text line, @* a comment.
				if ( Starts( "@*" ) || Starts( "@:" ) || Starts( "@(" ) || Starts( "@{" ) )
				{
					ScanTransition();
					statementStart = true;
					continue;
				}

				var wordEnd = _i + 1;
				while ( wordEnd < text.Length && char.IsLetter( text[wordEnd] ) ) wordEnd++;
				var word = text.Substring( _i + 1, wordEnd - _i - 1 );

				if ( Array.IndexOf( BlockKeywords, word ) >= 0 )
				{
					_i = wordEnd;
					ScanControl( word );
					statementStart = true;
					continue;
				}

				_i++;
				continue;
			}

			if ( c == '<' && statementStart && _i + 1 < text.Length && ( char.IsLetter( text[_i + 1] ) || text[_i + 1] == '_' ) )
			{
				//Markup inside code: the element and everything in it, then back to C#.
				ReadElement();
				continue;
			}

			if ( c == '<' && statementStart && Starts( "<!--" ) )
			{
				var end = text.IndexOf( "-->", _i + 4, StringComparison.Ordinal );
				_i = end < 0 ? text.Length : end + 3;
				continue;
			}

			if ( c == '{' ) { depth++; statementStart = true; _i++; continue; }
			if ( c == '}' ) { depth--; statementStart = true; _i++; continue; }
			if ( c == ';' ) { statementStart = true; _i++; continue; }

			if ( !char.IsWhiteSpace( c ) ) statementStart = false;

			_i++;
		}

		_controls.RemoveAt( _controls.Count - 1 );
		_controlHeaders.RemoveAt( _controlHeaders.Count - 1 );
	}

	//Past a brace-delimited body of pure C#, entered at its { : strings and comments stepped over, braces balanced.
	int SkipCSharp( int at )
	{
		var text = Text;
		var i = at + 1;
		var depth = 1;

		while ( i < text.Length && depth > 0 )
		{
			var c = text[i];

			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '/' ) { i = EndOfLine( i ); continue; }
			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '*' )
			{
				var end = text.IndexOf( "*/", i + 2, StringComparison.Ordinal );
				i = end < 0 ? text.Length : end + 2;
				continue;
			}

			if ( c == '"' || c == '\'' ) { i = SkipString( i ); continue; }

			if ( c == '{' ) depth++;
			if ( c == '}' ) depth--;

			i++;
		}

		return i;
	}

	//Past a C# string starting at the quote: plain, verbatim, interpolated, raw, or a char.
	int SkipString( int at )
	{
		var text = Text;
		var quote = text[at];

		//A raw string: three or more quotes, closed by as many.
		if ( quote == '"' && at + 2 < text.Length && text[at + 1] == '"' && text[at + 2] == '"' )
		{
			var count = 0;
			while ( at + count < text.Length && text[at + count] == '"' ) count++;

			var closer = new string( '"', count );
			var end = text.IndexOf( closer, at + count, StringComparison.Ordinal );
			return end < 0 ? text.Length : end + count;
		}

		var verbatim = at > 0 && ( text[at - 1] == '@' || ( at > 1 && text[at - 1] == '$' && text[at - 2] == '@' ) || ( at > 1 && text[at - 1] == '@' && text[at - 2] == '$' ) );
		var i = at + 1;

		while ( i < text.Length )
		{
			var c = text[i];

			if ( verbatim )
			{
				if ( c == quote && i + 1 < text.Length && text[i + 1] == quote ) { i += 2; continue; }
				if ( c == quote ) return i + 1;
				i++;
				continue;
			}

			if ( c == '\\' ) { i += 2; continue; }
			if ( c == quote ) return i + 1;
			if ( c == '\n' ) return i;
			i++;
		}

		return text.Length;
	}

	//Past a balanced pair of brackets starting at the opener, strings inside stepped over.
	int SkipBalanced( int at, char open, char close )
	{
		var text = Text;

		if ( at < 0 || at >= text.Length ) return text.Length;

		var i = at + 1;
		var depth = 1;

		while ( i < text.Length && depth > 0 )
		{
			var c = text[i];

			if ( c == '"' || c == '\'' ) { i = SkipString( i ); continue; }
			if ( c == open ) depth++;
			if ( c == close ) depth--;

			i++;
		}

		return i;
	}

	bool Starts( string s ) => string.CompareOrdinal( Text, _i, s, 0, s.Length ) == 0;

	int EndOfLine( int at )
	{
		var end = Text.IndexOf( '\n', at );
		return end < 0 ? Text.Length : end;
	}

	static bool IsNameChar( char c ) => char.IsLetterOrDigit( c ) || c == '-' || c == '_' || c == ':' || c == '.';

	static int[] LineStarts( string text )
	{
		var starts = new List<int> { 0 };

		for ( var i = 0; i < text.Length; i++ )
		{
			if ( text[i] == '\n' ) starts.Add( i + 1 );
		}

		return starts.ToArray();
	}
}
