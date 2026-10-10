using System.IO;
using System.Text;
using Rinten.UI;

namespace Editor.UiEditor;

/// <summary>Light stylesheet scanner: spans of blocks and declarations, so one value can be spliced in place</summary>
public sealed class ScssDocument
{
	/// <summary>One <c>selector { ... }</c>, nested or not.</summary>
	public sealed class Block
	{
		/// <summary>The selector as written, trimmed, comments and all newlines collapsed.</summary>
		public string Selector;

		/// <summary>The selector with its nesting resolved, one string per comma group, as the engine strings it.</summary>
		public string[] Resolved;

		/// <summary>The block this one is written inside, if any.</summary>
		public Block Parent;

		/// <summary>The line the selector starts on, zero based.</summary>
		public int Line;

		/// <summary>Text offset of the opening brace.</summary>
		public int Open;

		/// <summary>Text offset of the closing brace.</summary>
		public int Close;

		/// <summary>The declarations written directly in this block, in order.</summary>
		public List<Declaration> Declarations = new();

		/// <summary>The blocks written directly in this block, in order.</summary>
		public List<Block> Children = new();

		/// <summary>Whether this is an at-rule (<c>@keyframes</c>, <c>@media</c>) rather than a selector.</summary>
		public bool IsAtRule => Selector.StartsWith( '@' );

		public override string ToString() => string.Join( ", ", Resolved );
	}

	/// <summary>One <c>name: value;</c>.</summary>
	public sealed class Declaration
	{
		public string Name;
		public string Value;

		/// <summary>Text offset of the first character of the name.</summary>
		public int Start;

		/// <summary>Text offset of the first character of the value.</summary>
		public int ValueStart;

		/// <summary>Text offset one past the last character of the value.</summary>
		public int ValueEnd;

		/// <summary>Text offset one past the terminating semicolon, or past the value if there was none.</summary>
		public int End;
	}

	public string Path { get; }
	public string Text { get; private set; }

	/// <summary>Every block in the file, outermost first, in the order written.</summary>
	public List<Block> Blocks { get; } = new();

	ScssDocument( string path, string text )
	{
		Path = path;
		Text = text;

		Scan();
	}

	/// <summary>Read and scan the file, or null when it is not there.</summary>
	public static ScssDocument Load( string path )
	{
		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) ) return null;

		return new ScssDocument( path, File.ReadAllText( path ) );
	}

	/// <summary>A document over text that is not on disk yet.</summary>
	public static ScssDocument FromText( string path, string text ) => new( path, text ?? "" );

	//Matching

	//By resolved selector first - the same string the engine builds, so a nested &:hover under .button is found as .button:hover - and when the file says the same thing twice, by the nearest line to where the engine says it read it.
	/// <summary>The block in this file that the engine's block was read from.</summary>
	public Block Find( StyleBlock block )
	{
		if ( block?.Selectors is null ) return null;

		var wanted = block.Selectors.Select( x => Squash( x.AsString ) ).ToArray();

		Block best = null;
		var bestDistance = int.MaxValue;

		foreach ( var candidate in Blocks )
		{
			if ( candidate.IsAtRule ) continue;
			if ( !SameSelectors( candidate.Resolved, wanted ) ) continue;

			var distance = Math.Abs( candidate.Line - block.FileLine );

			if ( distance < bestDistance )
			{
				best = candidate;
				bestDistance = distance;
			}
		}

		return best;
	}

	static bool SameSelectors( string[] a, string[] b )
	{
		if ( a.Length != b.Length ) return false;

		for ( var i = 0; i < a.Length; i++ )
		{
			if ( !string.Equals( a[i], b[i], StringComparison.OrdinalIgnoreCase ) ) return false;
		}

		return true;
	}

	//Editing

	/// <summary>Set one property in one block: the value replaced where the property is already written, or the declaration added at the end of the block where it is not. Returns the new text; nothing is saved here.</summary>
	public string WithDeclaration( Block block, string name, string value )
	{
		if ( block is null ) return Text;

		var existing = block.Declarations.LastOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase ) );

		if ( existing is not null )
			return Text.Substring( 0, existing.ValueStart ) + value + Text.Substring( existing.ValueEnd );

		return Insert( block, $"{name}: {value};" );
	}

	/// <summary>Take one property out of one block. The whole line goes when the declaration was alone on it.</summary>
	public string WithoutDeclaration( Block block, string name )
	{
		var existing = block?.Declarations.LastOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase ) );
		if ( existing is null ) return Text;

		var start = existing.Start;
		var end = existing.End;

		//Back over the indent to the line start, forward over the line end, but only when nothing else is on the line.
		var lineStart = start;
		while ( lineStart > 0 && Text[lineStart - 1] is ' ' or '\t' ) lineStart--;

		var lineEnd = end;
		while ( lineEnd < Text.Length && Text[lineEnd] is ' ' or '\t' ) lineEnd++;

		var aloneBefore = lineStart == 0 || Text[lineStart - 1] == '\n';
		var aloneAfter = lineEnd >= Text.Length || Text[lineEnd] == '\n';

		if ( aloneBefore && aloneAfter )
		{
			start = lineStart;
			end = Math.Min( Text.Length, lineEnd + 1 );
		}

		return Text.Substring( 0, start ) + Text.Substring( end );
	}

	/// <summary>A new block at the end of the file, or nested at the end of a parent, with the declarations given. Returns the new text.</summary>
	public string WithBlock( Block parent, string selector, IEnumerable<(string name, string value)> declarations )
	{
		var indent = parent is null ? "" : IndentOf( parent ) + "\t";
		var sb = new StringBuilder();

		sb.Append( indent ).Append( selector ).Append( '\n' );
		sb.Append( indent ).Append( "{\n" );

		//An empty value means "not this one" - a take-off of a property the rule never had - and is nothing to write.
		foreach ( var (name, value) in declarations )
		{
			if ( string.IsNullOrWhiteSpace( value ) ) continue;
			sb.Append( indent ).Append( '\t' ).Append( name ).Append( ": " ).Append( value ).Append( ";\n" );
		}

		sb.Append( indent ).Append( '}' );

		if ( parent is null )
		{
			var text = Text.TrimEnd();
			return text + ( text.Length > 0 ? "\n\n" : "" ) + sb + "\n";
		}

		return Insert( parent, sb.ToString(), blankLineBefore: true );
	}

	/// <summary>The text of a block from its selector to its closing brace, with the indent of its first line taken off every line.</summary>
	public string Extract( Block block )
	{
		var start = SelectorStartOf( block );
		var text = Text.Substring( start, block.Close + 1 - start );
		var indent = IndentOf( block );

		if ( indent.Length == 0 ) return text;

		var lines = text.Split( '\n' );

		for ( var i = 1; i < lines.Length; i++ )
		{
			if ( lines[i].StartsWith( indent, StringComparison.Ordinal ) )
				lines[i] = lines[i].Substring( indent.Length );
		}

		return string.Join( '\n', lines );
	}

	/// <summary>A block taken out, and the blank line before it when it had one.</summary>
	public string WithoutBlock( Block block )
	{
		var start = LineStartOf( SelectorStartOf( block ) );
		var end = block.Close + 1;

		while ( end < Text.Length && Text[end] is ' ' or '\t' ) end++;
		if ( end < Text.Length && Text[end] == '\n' ) end++;

		//The blank line that stood before it goes with it.
		while ( start > 0 && Text[start - 1] == '\n' && ( start < 2 || Text[start - 2] == '\n' ) ) start--;

		return Text.Substring( 0, start ) + Text.Substring( end );
	}

	/// <summary>Block text as written elsewhere - <see cref="Extract"/> of another sheet - put at the end of the file, or nested at the end of a parent.</summary>
	public string WithRawBlock( Block parent, string raw )
	{
		var indent = parent is null ? "" : IndentOf( parent ) + "\t";
		var lines = raw.Replace( "\r\n", "\n" ).TrimEnd().Split( '\n' );

		for ( var i = 0; i < lines.Length; i++ )
			if ( lines[i].Length > 0 ) lines[i] = indent + lines[i];

		var block = string.Join( '\n', lines );

		if ( parent is null )
		{
			var text = Text.TrimEnd();
			return text + ( text.Length > 0 ? "\n\n" : "" ) + block + "\n";
		}

		return Insert( parent, block, blankLineBefore: true );
	}

	/// <summary>Write text as the file, and scan it again.</summary>
	public void Save( string text )
	{
		Text = text;

		Directory.CreateDirectory( System.IO.Path.GetDirectoryName( Path ) );
		File.WriteAllText( Path, text );

		Blocks.Clear();
		Scan();
	}

	//A line put into a block on a line of its own at the block's inner indent: after its last declaration, so it stands with
	//the rest and not under the nested blocks that follow them - or before the closing brace when there are no declarations yet.
	string Insert( Block block, string line, bool blankLineBefore = false )
	{
		var indent = IndentOf( block ) + "\t";

		//Where the new line goes in: after the last declaration when a nested block follows it, else just before the brace.
		var close = block.Close;
		var lastDeclaration = block.Declarations.Count > 0 ? block.Declarations[^1].End : -1;
		var firstChild = block.Children.Count > 0 ? SelectorStartOf( block.Children[0] ) : int.MaxValue;

		if ( !line.Contains( '\n' ) && lastDeclaration >= 0 && lastDeclaration < firstChild && firstChild < close )
		{
			//The declaration's line ends here; the new line follows it.
			var lineEnd = lastDeclaration;
			while ( lineEnd < Text.Length && Text[lineEnd] is ' ' or '\t' ) lineEnd++;

			var sb = new StringBuilder();
			sb.Append( Text, 0, lineEnd );
			sb.Append( '\n' ).Append( indent ).Append( line );
			sb.Append( Text, lineEnd, Text.Length - lineEnd );
			return sb.ToString();
		}

		//Back over the whitespace before the brace, so the new line sits under the last declaration rather than under a blank.
		var at = close;
		while ( at > block.Open + 1 && char.IsWhiteSpace( Text[at - 1] ) ) at--;

		var empty = at == block.Open + 1;

		var tail = new StringBuilder();
		tail.Append( Text, 0, at );
		tail.Append( '\n' );

		if ( blankLineBefore && !empty ) tail.Append( '\n' );

		//A nested block brings its own indent on every line; a declaration is one line.
		if ( line.Contains( '\n' ) ) tail.Append( line );
		else tail.Append( indent ).Append( line );

		tail.Append( '\n' ).Append( IndentOf( block ) );
		tail.Append( Text, close, Text.Length - close );

		return tail.ToString();
	}

	//The whitespace the block's selector line starts with.
	string IndentOf( Block block )
	{
		//From the brace back to the start of the selector's first line.
		var lineStart = LineStartOf( SelectorStartOf( block ) );

		var end = lineStart;
		while ( end < Text.Length && Text[end] is ' ' or '\t' ) end++;

		return Text.Substring( lineStart, end - lineStart );
	}

	int SelectorStartOf( Block block )
	{
		var at = block.Open - 1;

		//Back over the selector text to the previous statement end or block edge.
		while ( at > 0 && Text[at] != ';' && Text[at] != '{' && Text[at] != '}' ) at--;

		//Past the delimiter - unless the walk reached the start of the file, where the first character is the selector's own.
		if ( Text[at] is ';' or '{' or '}' ) at++;
		while ( at < block.Open && char.IsWhiteSpace( Text[at] ) ) at++;

		return at;
	}

	int LineStartOf( int at )
	{
		while ( at > 0 && Text[at - 1] != '\n' ) at--;
		return at;
	}

	//Scanning

	void Scan()
	{
		var stack = new List<Block>();
		var text = Text;
		var i = 0;

		//Where the current statement (selector or declaration) started.
		var statementStart = 0;

		while ( i < text.Length )
		{
			var c = text[i];

			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				i = EndOfLine( i );
				if ( TrimmedEmpty( statementStart, i ) ) statementStart = i;
				continue;
			}

			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '*' )
			{
				var end = text.IndexOf( "*/", i + 2, StringComparison.Ordinal );
				i = end < 0 ? text.Length : end + 2;
				if ( TrimmedEmpty( statementStart, i ) ) statementStart = i;
				continue;
			}

			if ( c is '"' or '\'' )
			{
				i = EndOfString( i );
				continue;
			}

			if ( c == '{' )
			{
				var selector = Clean( text.Substring( statementStart, i - statementStart ) );
				var parent = stack.Count > 0 ? stack[^1] : null;

				var block = new Block
				{
					Selector = selector,
					Parent = parent,
					Line = LineOf( statementStart ),
					Open = i,
					Close = -1,
				};

				block.Resolved = Resolve( selector, parent );

				Blocks.Add( block );
				parent?.Children.Add( block );
				stack.Add( block );

				i++;
				statementStart = i;
				continue;
			}

			if ( c == '}' )
			{
				if ( stack.Count > 0 )
				{
					var block = stack[^1];

					//A last declaration with no semicolon before the brace.
					AddDeclaration( block, statementStart, i, i );

					block.Close = i;
					stack.RemoveAt( stack.Count - 1 );
				}

				i++;
				statementStart = i;
				continue;
			}

			if ( c == ';' )
			{
				if ( stack.Count > 0 )
					AddDeclaration( stack[^1], statementStart, i, i + 1 );

				i++;
				statementStart = i;
				continue;
			}

			i++;
		}

		//Unclosed blocks close at the end of the text, so a half-typed file still gives its earlier blocks back.
		foreach ( var block in stack )
			block.Close = text.Length;
	}

	void AddDeclaration( Block block, int start, int end, int statementEnd )
	{
		var text = Text;

		while ( start < end && char.IsWhiteSpace( text[start] ) ) start++;
		while ( end > start && char.IsWhiteSpace( text[end - 1] ) ) end--;

		if ( start >= end ) return;

		var colon = text.IndexOf( ':', start, end - start );

		//@include, @extend: a statement without a name and a value. Kept as its own kind by its leading @ so a write never lands on it.
		if ( text[start] == '@' || colon < 0 )
		{
			block.Declarations.Add( new Declaration
			{
				Name = text.Substring( start, end - start ),
				Value = "",
				Start = start,
				ValueStart = end,
				ValueEnd = end,
				End = statementEnd,
			} );

			return;
		}

		var valueStart = colon + 1;
		while ( valueStart < end && char.IsWhiteSpace( text[valueStart] ) ) valueStart++;

		block.Declarations.Add( new Declaration
		{
			Name = text.Substring( start, colon - start ).Trim(),
			Value = text.Substring( valueStart, end - valueStart ),
			Start = start,
			ValueStart = valueStart,
			ValueEnd = end,
			End = statementEnd,
		} );
	}

	//The engine's resolved selector strings for a block, one per comma group: a group starting with & is
	//joined onto the parent's groups, any other is a descendant of them. See StyleParser.ParseSelector .
	static string[] Resolve( string selector, Block parent )
	{
		var groups = SplitGroups( selector );

		if ( parent is null || parent.IsAtRule ) return groups;

		var parentRules = string.Join( ", ", parent.Resolved );

		for ( var i = 0; i < groups.Length; i++ )
		{
			groups[i] = groups[i].StartsWith( '&' )
				? parentRules + groups[i].Substring( 1 )
				: parentRules + " " + groups[i];
		}

		return groups;
	}

	//The comma groups of a selector, each trimmed; commas inside parentheses do not split.
	static string[] SplitGroups( string selector )
	{
		var groups = new List<string>();
		var depth = 0;
		var start = 0;

		for ( var i = 0; i < selector.Length; i++ )
		{
			var c = selector[i];

			if ( c == '(' ) depth++;
			else if ( c == ')' ) depth--;
			else if ( c == ',' && depth == 0 )
			{
				groups.Add( selector.Substring( start, i - start ).Trim() );
				start = i + 1;
			}
		}

		groups.Add( selector.Substring( start ).Trim() );

		return groups.Select( Squash ).ToArray();
	}

	//Whitespace runs to one space, and the ends trimmed - the engine's strings have their newlines gone the same way.
	static string Squash( string s )
	{
		if ( string.IsNullOrEmpty( s ) ) return "";

		var sb = new StringBuilder( s.Length );
		var space = false;

		foreach ( var c in s.Trim() )
		{
			if ( char.IsWhiteSpace( c ) )
			{
				space = true;
				continue;
			}

			if ( space ) sb.Append( ' ' );
			space = false;
			sb.Append( c );
		}

		return sb.ToString();
	}

	//A selector's text with the comments in it taken out.
	string Clean( string s )
	{
		if ( !s.Contains( '/' ) ) return s.Trim();

		var sb = new StringBuilder( s.Length );
		var i = 0;

		while ( i < s.Length )
		{
			if ( s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/' )
			{
				var end = s.IndexOf( '\n', i );
				i = end < 0 ? s.Length : end;
				continue;
			}

			if ( s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*' )
			{
				var end = s.IndexOf( "*/", i + 2, StringComparison.Ordinal );
				i = end < 0 ? s.Length : end + 2;
				continue;
			}

			sb.Append( s[i] );
			i++;
		}

		return sb.ToString().Trim();
	}

	bool TrimmedEmpty( int start, int end ) => string.IsNullOrWhiteSpace( Text.Substring( start, Math.Max( 0, end - start ) ) );

	int EndOfLine( int at )
	{
		var end = Text.IndexOf( '\n', at );
		return end < 0 ? Text.Length : end;
	}

	int EndOfString( int at )
	{
		var quote = Text[at];
		var i = at + 1;

		while ( i < Text.Length )
		{
			if ( Text[i] == '\\' ) { i += 2; continue; }
			if ( Text[i] == quote ) return i + 1;
			if ( Text[i] == '\n' ) return i;
			i++;
		}

		return Text.Length;
	}

	int LineOf( int at )
	{
		var line = 0;
		var end = Math.Min( at, Text.Length );

		for ( var i = 0; i < end; i++ )
		{
			if ( Text[i] == '\n' ) line++;
		}

		return line;
	}
}
