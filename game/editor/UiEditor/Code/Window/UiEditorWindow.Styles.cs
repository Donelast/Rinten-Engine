using System.IO;

namespace Editor.UiEditor;

/// <summary>Moves a razor's &lt;style&gt; contents into its .razor.scss so they can be edited; one undo step</summary>
public partial class UiEditorWindow
{
	//Whether the open file has styles in a <style> to move out.
	internal bool CanMoveStyles => _document is not null && _document.HasStyleTag;

	//Every <style> of the open file emptied into its .razor.scss and taken out of the markup.
	internal bool MoveStylesToSheet()
	{
		if ( _document is null ) return false;

		//Both from the one scan: the offsets spliced below are offsets into this text, and the file is read again whenever it is newer.
		var markup = _document.Markup;
		var tags = markup.Elements.Where( RazorDocument.IsStyleTag ).OrderBy( x => x.Start ).ToList();

		if ( tags.Count == 0 )
		{
			Log.Info( $"UI editor: {_document.Name}.razor has no <style> to move" );
			return false;
		}

		//What the tags say, in the order written. A tag whose css is built by code is not text to move, and
		//stops the whole move: half the styles in one place and half in another is worse than none of them moved.
		var moving = new List<string>();

		foreach ( var tag in tags )
		{
			if ( StyleTextOf( markup, tag ) is not { } css ) return false;
			if ( css.Length > 0 ) moving.Add( css );
		}

		var sheet = ScssDocument.Load( _document.ScssPath ) ?? ScssDocument.FromText( _document.ScssPath, "" );

		//After whatever the sheet already says, so a file with both keeps the order the engine read them in.
		var was = sheet.Text.TrimEnd();
		var added = string.Join( "\n\n", moving );
		var text = was.Length == 0 ? added + "\n" : was + "\n\n" + added + "\n";

		//The tags out of the markup, the last one first so that the ones before it are still where they were found.
		var razor = markup.Text;

		foreach ( var tag in tags.OrderByDescending( x => x.Start ) )
		{
			var again = RazorMarkup.Parse( razor );
			var found = again.Elements.FirstOrDefault( x => x.Start == tag.Start && RazorDocument.IsStyleTag( x ) );

			if ( found is null ) continue;

			razor = again.WithoutElement( found );
		}

		var name = Path.GetFileName( _document.ScssPath );

		using ( BeginGesture( $"move styles into {name}" ) )
		{
			if ( moving.Count > 0 && !Commit( sheet, $"styles of {_document.Name}", text ) ) return false;
			if ( !CommitMarkup( $"take <style> out of {_document.Name}", markup.Text, razor ) ) return false;
		}

		//The sheet is new to the engine, and the docks reading it were told nothing when there were no rules to write.
		_inspector?.NoteSheetWritten( _document.ScssPath );
		_animation?.SetDocument( _document );

		Log.Info( $"UI editor: {tags.Count} <style> block(s) moved into {name}" );

		return true;
	}

	//The css a tag holds, at a sheet's own indent, with razor's doubled @@ put back to one. Null when the tag holds a C# expression.
	static string StyleTextOf( RazorMarkup markup, RazorMarkup.Element tag )
	{
		var inner = tag.Inner( markup.Text );

		//Razor reads a lone @ as a step into C#, so an at-rule in a tag is written @@media and reaches the parser as @media. One that
		//is not doubled is an expression: the css is built as the panel renders, and there is no one text of it to put in a file.
		if ( HasLoneAt( inner ) )
		{
			Log.Warning( $"UI editor: the <style> at line {tag.Line + 1} builds its css from code, so it cannot be moved - take it out by hand" );
			return null;
		}

		//The break after the opening tag, and the indent before the closing one, belong to the tag and not to the sheet.
		return Dedent( inner.Replace( "@@", "@" ) ).TrimStart( '\n', '\r' ).TrimEnd();
	}

	//Whether the text has an @ that is not one of a doubled pair.
	static bool HasLoneAt( string text )
	{
		for ( var i = 0; i < text.Length; i++ )
		{
			if ( text[i] != '@' ) continue;

			//A pair stands for one @ and is stepped over whole, so that @@@x is a pair and then a lone one.
			if ( i + 1 < text.Length && text[i + 1] == '@' )
			{
				i++;
				continue;
			}

			return true;
		}

		return false;
	}

	//Every line pulled out by the smallest indent any of them has.
	static string Dedent( string text )
	{
		var lines = text.Replace( "\r\n", "\n" ).Split( '\n' );
		var least = int.MaxValue;

		foreach ( var line in lines )
		{
			if ( line.Trim().Length == 0 ) continue;

			var at = 0;
			while ( at < line.Length && line[at] is ' ' or '\t' ) at++;

			least = Math.Min( least, at );
		}

		if ( least is 0 or int.MaxValue ) return text;

		for ( var i = 0; i < lines.Length; i++ )
			if ( lines[i].Length >= least ) lines[i] = lines[i].Substring( least );

		return string.Join( '\n', lines );
	}
}
