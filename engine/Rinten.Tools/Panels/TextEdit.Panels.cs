using Rinten;
using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor;

/// <summary>Several lines of text.</summary>
public partial class TextEdit
{
	private readonly List<string> lines = new() { string.Empty };

	private string placeholder = string.Empty;
	private bool editable = true;
	private bool readOnly;

	private int caretLine;
	private int caretColumn;

	//Where a selection was started, or a line below zero while there is none.
	private int anchorLine = -1;
	private int anchorColumn;

	//Whether the pointer is drawing a selection right now.
	private bool dragging;

	private float scroll;
	private int blocks = int.MaxValue;

	//The room around the text.
	private static readonly Margin Padding = new( 6, 4, 6, 4 );

	//The selection. A line and a column at each end, the far one anchored where it was started from - so which of
	//the two is earlier in the text depends on which way it was dragged, and everything below asks for them in order.

	private bool HasSelection => anchorLine >= 0
		&& ( anchorLine != caretLine || anchorColumn != caretColumn );

	//Whichever end of the selection comes first in the text.
	private (int Line, int Column) SelectionFrom => Earlier( anchorLine, anchorColumn, caretLine, caretColumn )
		? (anchorLine, anchorColumn) : (caretLine, caretColumn);

	//And whichever comes second.
	private (int Line, int Column) SelectionTo => Earlier( anchorLine, anchorColumn, caretLine, caretColumn )
		? (caretLine, caretColumn) : (anchorLine, anchorColumn);

	private static bool Earlier( int line, int column, int otherLine, int otherColumn )
	{
		if ( line != otherLine ) return line < otherLine;

		return column <= otherColumn;
	}

	//What is selected, newlines and all.
	private string SelectedText
	{
		get
		{
			if ( !HasSelection ) return string.Empty;

			var (fromLine, fromColumn) = SelectionFrom;
			var (toLine, toColumn) = SelectionTo;

			if ( fromLine == toLine )
				return lines[fromLine][fromColumn..toColumn];

			var parts = new List<string> { lines[fromLine][fromColumn..] };

			for ( var i = fromLine + 1; i < toLine; i++ ) parts.Add( lines[i] );

			parts.Add( lines[toLine][..toColumn] );

			return string.Join( '\n', parts );
		}
	}

	//Takes out whatever is selected, and leaves the caret where it was. False where nothing
	//was selected, so the caller can go on and do whatever it does to a single letter.
	private bool DeleteSelection()
	{
		if ( !HasSelection ) return false;

		var (fromLine, fromColumn) = SelectionFrom;
		var (toLine, toColumn) = SelectionTo;

		lines[fromLine] = lines[fromLine][..fromColumn] + lines[toLine][toColumn..];

		for ( var i = toLine; i > fromLine; i-- ) lines.RemoveAt( i );

		caretLine = fromLine;
		caretColumn = fromColumn;

		Deselect();

		return true;
	}

	//Forgets the selection, without touching the caret.
	private void Deselect()
	{
		anchorLine = -1;
		anchorColumn = 0;
	}

	//Puts the caret somewhere, extending the selection or dropping it. Everything that moves the caret
	//goes through here, so that holding shift means the same thing whichever key or click did the moving.
	private void SetCaret( int line, int column, bool selecting )
	{
		if ( selecting )
		{
			//The far end stays where it was, and where there was no selection it starts from wherever the caret is leaving.
			if ( anchorLine < 0 )
			{
				anchorLine = caretLine;
				anchorColumn = caretColumn;
			}
		}
		else
		{
			Deselect();
		}

		caretLine = Math.Clamp( line, 0, lines.Count - 1 );
		caretColumn = Math.Clamp( column, 0, lines[caretLine].Length );

		ShowCaret();
	}

	//Which letter of a line a point along it is on.
	private int ColumnAt( int line, float x )
	{
		var inside = LocalRect.Shrink( Padding );
		var wanted = x - inside.Left;
		var text = lines[Math.Clamp( line, 0, lines.Count - 1 )];

		for ( var i = 1; i <= text.Length; i++ )
		{
			var before = EditorText.Measure( text[..(i - 1)] ).x;
			var after = EditorText.Measure( text[..i] ).x;

			//Past the middle of a letter is the far side of it, which is where a caret goes when somebody clicks at the end of a word.
			if ( wanted < (before + after) * 0.5f ) return i - 1;
		}

		return text.Length;
	}

	//Which line a point down the widget is on.
	private int LineAt( float y )
	{
		var inside = LocalRect.Shrink( Padding );

		return Math.Clamp( (int)(((y - inside.Top) / LineHeight) + scroll), 0, lines.Count - 1 );
	}

	//Selects the word a position is in - what a double click means.
	private void SelectWordAt( int line, int column )
	{
		var text = lines[Math.Clamp( line, 0, lines.Count - 1 )];

		if ( text.Length == 0 ) return;

		column = Math.Clamp( column, 0, text.Length );

		if ( column >= text.Length || !IsWordCharacter( text[column] ) )
		{
			if ( column > 0 && IsWordCharacter( text[column - 1] ) )
			{
				column--;
			}
			else
			{
				//Space, or past the end: the line itself, so a double click always leaves something selected.
				anchorLine = line;
				anchorColumn = 0;

				SetCaret( line, text.Length, selecting: true );
				return;
			}
		}

		var from = column;
		while ( from > 0 && IsWordCharacter( text[from - 1] ) ) from--;

		var to = column;
		while ( to < text.Length && IsWordCharacter( text[to] ) ) to++;

		anchorLine = line;
		anchorColumn = from;

		SetCaret( line, to, selecting: true );
	}

	//What counts as part of a word: an ident or a filename is one word to whoever is looking at it, underscores and all.
	private static bool IsWordCharacter( char c ) => char.IsLetterOrDigit( c ) || c == '_';

	//How tall one line is.
	private float LineHeight => MathF.Max( EditorText.Measure( "Ay" ).y, Theme.RowHeight );

	//Everything, as one string.
	internal string TextManaged
	{
		get => string.Join( '\n', lines );
		set
		{
			lines.Clear();
			lines.AddRange( (value ?? string.Empty).Replace( "\r", "" ).Split( '\n' ) );

			if ( lines.Count == 0 ) lines.Add( string.Empty );

			Trim();

			caretLine = lines.Count - 1;
			caretColumn = lines[caretLine].Length;

			Deselect();

			InternalTextChanged();
			Update();
		}
	}

	//Adds a line to the end, as a log does.
	internal void AppendManaged( string text )
	{
		if ( text is null ) return;

		foreach ( var line in text.Replace( "\r", "" ).Split( '\n' ) )
			lines.Add( line );

		Trim();

		//The caret follows what was added: text appended to one of these is text somebody is watching arrive, and typing carries on at the end.
		caretLine = lines.Count - 1;
		caretColumn = lines[caretLine].Length;

		ScrollToEnd();

		InternalTextChanged();
		Update();
	}

	//Drops old lines from the front and shifts the caret and selection to match
	private void Trim()
	{
		var removed = 0;

		while ( blocks > 0 && lines.Count > blocks )
		{
			lines.RemoveAt( 0 );
			removed++;
		}

		if ( removed == 0 ) return;

		caretLine = Math.Max( caretLine - removed, 0 );

		//A selection whose far end has scrolled off the front is no longer a selection of anything anybody made.
		if ( anchorLine >= 0 )
		{
			if ( anchorLine < removed ) Deselect();
			else anchorLine -= removed;
		}
	}

	internal void ScrollToEnd()
	{
		var shown = MathF.Max( (Size.y - Padding.EdgeSize.y) / LineHeight, 1.0f );

		scroll = MathF.Max( lines.Count - shown, 0.0f );
	}

	//The line the caret is on, for whoever asks for "the" cursor.
	internal int CaretLineManaged => caretLine;

	//The line under a point of the widget, as a cursor.
	internal TextCursor CursorAtManaged( Vector2 position )
	{
		var inside = LocalRect.Shrink( Padding );

		var line = (int)(scroll + ((position.y - inside.Top) / LineHeight));

		return new TextCursor( this, Math.Clamp( line, 0, lines.Count - 1 ) );
	}

	//Where a line of the text is on the widget.
	internal Rect LineRectManaged( int line )
	{
		var inside = LocalRect.Shrink( Padding );

		return new Rect( inside.Left, inside.Top + ((line - scroll) * LineHeight), inside.Width, LineHeight );
	}

	//Takes one line out, as removing a repeated log message does.
	internal void RemoveLineManaged( int line )
	{
		if ( line < 0 || line >= lines.Count ) return;

		lines.RemoveAt( line );

		if ( lines.Count == 0 ) lines.Add( string.Empty );

		//The lines below have moved up; whatever was selected across them is not what is there now.
		Deselect();

		caretLine = Math.Min( caretLine, lines.Count - 1 );
		caretColumn = Math.Min( caretColumn, lines[caretLine].Length );

		InternalTextChanged();
		Update();
	}

	internal void ClearManaged()
	{
		lines.Clear();
		lines.Add( string.Empty );

		caretLine = 0;
		caretColumn = 0;
		scroll = 0;

		InternalTextChanged();
		Update();
	}

	protected override void OnPaint()
	{
		if ( !OnPanels ) { base.OnPaint(); return; }

		Paint.Antialiasing = true;

		//Sunk into the panel, with no outline until it is being typed into.
		Paint.ClearPen();
		//A shade off the well while it is being typed into, not a card: a field that lightens into a plate stops
		//looking like a field at the moment it matters. What says it has the keyboard is the ring, in the accent.
		Paint.SetBrush( IsFocused ? Theme.Base : Theme.Sunken );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		Paint.ClearBrush();
		Paint.SetPen( IsFocused ? Theme.EdgeFocus : Paint.HasMouseOver ? Theme.EdgeLit : Theme.Edge );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		var inside = LocalRect.Shrink( Padding );

		Paint.SetDefaultFont();

		if ( lines.Count == 1 && lines[0].Length == 0 && !string.IsNullOrEmpty( placeholder ) )
		{
			Paint.SetPen( Theme.InkOff );
			Paint.DrawText( inside, placeholder, TextFlag.LeftTop | TextFlag.SingleLine );
			return;
		}

		var height = LineHeight;
		var first = (int)scroll;
		var shown = (int)MathF.Ceiling( inside.Height / height ) + 1;

		//What is selected, under the words rather than over them.
		if ( HasSelection )
		{
			var (fromLine, fromColumn) = SelectionFrom;
			var (toLine, toColumn) = SelectionTo;

			Paint.ClearPen();
			Paint.SetBrush( Theme.SelectedFill );

			for ( var i = Math.Max( first, fromLine ); i <= toLine && i < first + shown && i < lines.Count; i++ )
			{
				var text = lines[i];

				var start = i == fromLine ? fromColumn : 0;
				var end = i == toLine ? toColumn : text.Length;

				var left = inside.Left + EditorText.Measure( text[..Math.Min( start, text.Length )] ).x;
				var right = inside.Left + EditorText.Measure( text[..Math.Min( end, text.Length )] ).x;

				//A line that is selected through to its end carries the newline with it, and a run of empty lines has to show as something.
				if ( i != toLine ) right += EditorText.Measure( " " ).x;

				var at = inside.Top + ((i - scroll) * height);

				Paint.DrawRect( new Rect( left, at, MathF.Max( right - left, 1.0f ), height ), 2.0f );
			}
		}

		Paint.SetPen( ReadOnly ? Theme.InkOff : Theme.Ink );

		for ( var i = first; i < lines.Count && i < first + shown; i++ )
		{
			var at = inside.Top + ((i - scroll) * height);

			Paint.DrawText( new Rect( inside.Left, at, inside.Width, height ), lines[i],
				TextFlag.LeftTop | TextFlag.SingleLine | TextFlag.DontClip );
		}

		//The caret, where this is something being typed into.
		if ( !IsFocused || ReadOnly || !editable ) return;

		var caretAt = inside.Top + ((caretLine - scroll) * height);
		var caretX = inside.Left + EditorText.Measure( lines[caretLine][..Math.Min( caretColumn, lines[caretLine].Length )] ).x;

		Paint.SetPen( Theme.Ink, 1.0f );
		Paint.DrawLine( new Vector2( caretX, caretAt ), new Vector2( caretX, caretAt + height ) );
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		if ( !OnPanels ) { base.OnMouseWheel( e ); return; }

		var shown = (Size.y - Padding.EdgeSize.y) / LineHeight;
		var most = MathF.Max( lines.Count - shown, 0.0f );

		if ( most <= 0.0f ) return;

		scroll = Math.Clamp( scroll - (e.Delta / 120.0f * 3.0f), 0.0f, most );

		e.Accept();
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMousePress( e ); return; }

		Focus();

		var line = LineAt( e.LocalPosition.y );
		var column = ColumnAt( line, e.LocalPosition.x );

		//Shift keeps the far end where it was; anything else starts again from the click.
		SetCaret( line, column, selecting: e.HasShift );

		dragging = true;

		Update();
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseMove( e ); return; }
		if ( !dragging ) return;

		var line = LineAt( e.LocalPosition.y );

		SetCaret( line, ColumnAt( line, e.LocalPosition.x ), selecting: true );

		Update();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseReleased( e ); return; }

		dragging = false;
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnDoubleClick( e ); return; }

		var line = LineAt( e.LocalPosition.y );

		SelectWordAt( line, ColumnAt( line, e.LocalPosition.x ) );

		Update();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( !OnPanels ) { base.OnKeyPress( e ); return; }

		var selecting = e.HasShift;

		switch ( e.Key )
		{
			//Up and down keep the column, clamped to the line they land on - without the clamp the caret
			//sits past the end of a shorter line, and the next letter typed is written outside the string.
			case KeyCode.Up:
				SetCaret( caretLine - 1, caretColumn, selecting );
				Update();
				return;

			case KeyCode.Down:
				SetCaret( caretLine + 1, caretColumn, selecting );
				Update();
				return;

			//Left and right walk over the ends of lines rather than stopping at them, which is what a caret in several lines of text does.
			case KeyCode.Left:
				if ( caretColumn > 0 ) SetCaret( caretLine, caretColumn - 1, selecting );
				else if ( caretLine > 0 ) SetCaret( caretLine - 1, lines[caretLine - 1].Length, selecting );
				Update();
				return;

			case KeyCode.Right:
				if ( caretColumn < lines[caretLine].Length ) SetCaret( caretLine, caretColumn + 1, selecting );
				else if ( caretLine < lines.Count - 1 ) SetCaret( caretLine + 1, 0, selecting );
				Update();
				return;

			case KeyCode.Home:
				SetCaret( caretLine, 0, selecting );
				Update();
				return;

			case KeyCode.End:
				SetCaret( caretLine, lines[caretLine].Length, selecting );
				Update();
				return;

			case KeyCode.A when e.HasCtrl:
				anchorLine = 0;
				anchorColumn = 0;

				SetCaret( lines.Count - 1, lines[^1].Length, selecting: true );
				Update();
				return;

			case KeyCode.C when e.HasCtrl:
				if ( HasSelection ) EditorUtility.Clipboard.Copy( SelectedText );
				return;
		}

		if ( ReadOnly || !editable ) return;

		switch ( e.Key )
		{
			case KeyCode.X when e.HasCtrl:
				if ( HasSelection )
				{
					EditorUtility.Clipboard.Copy( SelectedText );
					DeleteSelection();
					Changed();
				}
				return;

			case KeyCode.V when e.HasCtrl:
				Insert( EditorUtility.Clipboard.Paste() );
				return;

			case KeyCode.Return:
			case KeyCode.Enter:
			{
				var line = lines[caretLine];
				var rest = line[Math.Min( caretColumn, line.Length )..];

				lines[caretLine] = line[..Math.Min( caretColumn, line.Length )];
				lines.Insert( ++caretLine, rest );

				caretColumn = 0;

				Changed();
				return;
			}

			case KeyCode.Backspace:
			{
				if ( DeleteSelection() )
				{
					Changed();
					return;
				}

				if ( caretColumn > 0 )
				{
					var line = lines[caretLine];

					lines[caretLine] = line[..(caretColumn - 1)] + line[caretColumn..];
					caretColumn--;
				}
				else if ( caretLine > 0 )
				{
					//The start of a line: what is left of it joins the one above.
					caretColumn = lines[caretLine - 1].Length;
					lines[caretLine - 1] += lines[caretLine];
					lines.RemoveAt( caretLine-- );
				}

				Changed();
				return;
			}

			case KeyCode.Delete:
			{
				if ( DeleteSelection() )
				{
					Changed();
					return;
				}

				var line = lines[caretLine];

				if ( caretColumn < line.Length )
				{
					lines[caretLine] = line[..caretColumn] + line[(caretColumn + 1)..];
				}
				else if ( caretLine < lines.Count - 1 )
				{
					lines[caretLine] += lines[caretLine + 1];
					lines.RemoveAt( caretLine + 1 );
				}

				Changed();
				return;
			}
		}

		if ( e.HasCtrl || e.HasAlt || string.IsNullOrEmpty( e.Text ) ) return;

		Insert( e.Text );
	}

	//Puts text in at the caret, in place of whatever was selected. Everything that writes goes through here - a typed letter, a
	//paste of several lines - so that a selection is replaced rather than left behind with the new text pushed into the middle of it.
	private void Insert( string text )
	{
		if ( ReadOnly || !editable ) return;
		if ( string.IsNullOrEmpty( text ) ) return;

		DeleteSelection();

		var current = lines[caretLine];
		var at = Math.Min( caretColumn, current.Length );

		var incoming = text.Replace( "\r", "" ).Split( '\n' );

		if ( incoming.Length == 1 )
		{
			lines[caretLine] = current[..at] + incoming[0] + current[at..];
			caretColumn = at + incoming[0].Length;

			Changed();
			return;
		}

		//Several lines: the first joins what was before the caret, the last joins what was after it, and the rest go in between.
		var tail = current[at..];

		lines[caretLine] = current[..at] + incoming[0];

		for ( var i = 1; i < incoming.Length; i++ )
			lines.Insert( caretLine + i, incoming[i] );

		caretLine += incoming.Length - 1;
		caretColumn = incoming[^1].Length;

		lines[caretLine] += tail;

		Changed();
	}

	//Keeps the caret's line on the screen.
	private void ShowCaret()
	{
		var shown = MathF.Max( (Size.y - Padding.EdgeSize.y) / LineHeight, 1.0f );

		if ( caretLine < scroll ) scroll = caretLine;
		if ( caretLine > scroll + shown - 1 ) scroll = caretLine - shown + 1;

		Update();
	}

	private void Changed()
	{
		ShowCaret();

		InternalTextChanged();
		Update();
	}

}
