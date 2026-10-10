using Rinten;
using Rinten.UI;
using System;

namespace Editor;

/// <summary>A line of text somebody types into.</summary>
public partial class LineEdit
{
	private string value = string.Empty;
	private string placeholder = string.Empty;
	private int maxLength = 32767;
	private bool readOnly;

	//Where the next letter goes.
	private int caret;

	//Where a selection started, or -1 while there is none.
	private int anchor = -1;

	//How far the text has been pushed left to keep the caret in view.
	private float scroll;

	private RealTimeSince sinceTyped;

	//The room left around the text inside the box.
	private static readonly Margin Padding = new( 6, 2, 6, 2 );

	private int SelectionFrom => anchor < 0 ? caret : Math.Min( anchor, caret );
	private int SelectionTo => anchor < 0 ? caret : Math.Max( anchor, caret );

	private void SetTextManaged( string wanted, bool byUser )
	{
		wanted ??= string.Empty;

		if ( wanted.Length > maxLength ) wanted = wanted[..maxLength];
		if ( value == wanted ) return;

		value = wanted;

		caret = Math.Clamp( caret, 0, value.Length );
		anchor = -1;

		InternalTextChanged( value );
		if ( byUser ) InternalTextEdited( value );

		Update();
	}

	//Drawing. The box, what is selected, the words, and the caret.

	protected override void OnPaint()
	{
		if ( !OnPanels ) { base.OnPaint(); return; }

		Paint.Antialiasing = true;

		//Sunk into the panel rather than boxed off from it: darker, and no outline at all until it is the thing being typed into.
		Paint.ClearPen();
		//A shade off the well while it is being typed into, not a card: a field that lightens into a plate stops
		//looking like a field at the moment it matters. What says it has the keyboard is the ring, in the accent.
		Paint.SetBrush( IsFocused ? Theme.Base : Theme.Sunken );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		//And a shade lighter again under the pointer. The lit edge alone is a line one pixel wide on a field that is otherwise the
		//colour of the hole it sits in, which is not enough to notice while looking for somewhere to type: the whole box answering is.
		if ( !IsFocused && Paint.HasMouseOver && !ReadOnly )
		{
			Paint.SetBrush( Theme.Veil );
			Paint.DrawRect( LocalRect, Theme.ControlRadius );
		}

		Paint.ClearBrush();
		Paint.SetPen( IsFocused ? Theme.EdgeFocus : Paint.HasMouseOver ? Theme.EdgeLit : Theme.Edge );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		var inside = LocalRect.Shrink( Padding );

		Paint.SetDefaultFont();

		//Nothing typed and nothing to say: the placeholder, in a colour that says it is not the text.
		if ( string.IsNullOrEmpty( value ) && !IsFocused )
		{
			if ( string.IsNullOrEmpty( placeholder ) ) return;

			Paint.SetPen( Theme.InkOff );
			Paint.DrawText( inside, placeholder, TextFlag.LeftCenter | TextFlag.SingleLine );
			return;
		}

		var left = inside.Left - scroll;

		if ( anchor >= 0 && SelectionFrom != SelectionTo )
		{
			var from = left + Widths( SelectionFrom );
			var to = left + Widths( SelectionTo );

			Paint.ClearPen();
			Paint.SetBrush( Theme.SelectedFill );
			Paint.DrawRect( new Rect( from, inside.Top, to - from, inside.Height ), 2.0f );
		}

		Paint.SetPen( ReadOnly ? Theme.InkOff : Theme.Ink );
		Paint.DrawText( new Rect( left, inside.Top, 4096, inside.Height ), value, TextFlag.LeftCenter | TextFlag.SingleLine | TextFlag.DontClip );

		//The caret blinks, and holds still for a moment after a key so that what was just typed can be read.
		if ( !IsFocused || ReadOnly ) return;
		if ( sinceTyped > 0.5f && (int)(RealTime.Now * 2.0f) % 2 == 0 ) return;

		var at = left + Widths( caret );

		Paint.SetPen( Theme.Ink, 1.0f );
		Paint.DrawLine( new Vector2( at, inside.Top + 1 ), new Vector2( at, inside.Bottom - 1 ) );
	}

	protected override Vector2 SizeHint()
	{
		if ( !OnPanels ) return base.SizeHint();

		var words = EditorText.Measure( string.IsNullOrEmpty( value ) ? placeholder : value );

		return new Vector2(
			Math.Max( words.x + Padding.EdgeSize.x, 60.0f ),
			Math.Max( words.y + Padding.EdgeSize.y, Theme.ControlHeight ) );
	}

	//How wide the text is up to a character.
	private float Widths( int upTo )
	{
		if ( upTo <= 0 || string.IsNullOrEmpty( value ) ) return 0.0f;

		return EditorText.Measure( value[..Math.Min( upTo, value.Length )] ).x;
	}

	//Which character is under a point.
	private int At( float x )
	{
		var inside = LocalRect.Shrink( Padding );
		var wanted = x - inside.Left + scroll;

		for ( var i = 1; i <= value.Length; i++ )
		{
			var before = Widths( i - 1 );
			var after = Widths( i );

			//Past the middle of a letter counts as the far side of it, which is where a caret goes when you click at the end of a word.
			if ( wanted < (before + after) * 0.5f ) return i - 1;
		}

		return value.Length;
	}

	//Keeps the caret inside the box by pushing the text along.
	private void ScrollToCaret()
	{
		var inside = LocalRect.Shrink( Padding );
		var at = Widths( caret );

		if ( at - scroll > inside.Width ) scroll = at - inside.Width;
		if ( at - scroll < 0 ) scroll = at;

		var whole = Widths( value.Length );

		if ( whole - scroll < inside.Width ) scroll = Math.Max( whole - inside.Width, 0.0f );
	}

	//The mouse: where the caret goes, and what is dragged over.

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMousePress( e ); return; }

		Focus();

		var was = caret;

		caret = At( e.LocalPosition.x );

		//Shift keeps the far end of the selection where it was: the anchor it already had, or the caret as it was before this press. Taking the
		//anchor after the caret has moved anchors it on the click itself, which is a selection of nothing - which is what shift-clicking did.
		if ( e.HasShift ) anchor = anchor < 0 ? was : anchor;
		else anchor = -1;

		RaiseCursorMoved();
		RaiseSelectionChanged();

		dragging = true;
		sinceTyped = 0;

		Update();
	}

	private bool dragging;

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseMove( e ); return; }
		if ( !dragging ) return;

		if ( anchor < 0 ) anchor = caret;

		caret = At( e.LocalPosition.x );

		RaiseCursorMoved();
		RaiseSelectionChanged();

		ScrollToCaret();
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

		SelectWordAt( At( e.LocalPosition.x ) );
	}

	//Double click selects the word at a position; on space or past the end, the word before or the line
	private void SelectWordAt( int at )
	{
		if ( string.IsNullOrEmpty( value ) ) return;

		at = Math.Clamp( at, 0, value.Length );

		if ( at >= value.Length || !IsWordCharacter( value[at] ) )
		{
			if ( at > 0 && IsWordCharacter( value[at - 1] ) )
			{
				at--;
			}
			else
			{
				SelectAll();
				RaiseSelectionChanged();
				return;
			}
		}

		var from = at;
		while ( from > 0 && IsWordCharacter( value[from - 1] ) ) from--;

		var to = at;
		while ( to < value.Length && IsWordCharacter( value[to] ) ) to++;

		SetSelection( from, to - from );

		RaiseCursorMoved();
		RaiseSelectionChanged();
	}

	//What counts as part of a word, for selecting one. Underscores included: an ident, a variable and a filename are all one
	//word to whoever is looking at them, and cutting them in half at every underscore is not what a double click is asked for.
	private static bool IsWordCharacter( char c ) => char.IsLetterOrDigit( c ) || c == '_';

	//The keyboard. What each key means to a line of text, which is the whole of what a text field is.

	//What a key means to a line of text. Called from the widget's own key event, after whatever the history and the autocomplete made of it.
	private void HandleKey( KeyEvent e )
	{
		sinceTyped = 0;

		switch ( e.Key )
		{
			case KeyCode.Left: Move( -1, e.HasShift ); return;
			case KeyCode.Right: Move( 1, e.HasShift ); return;
			case KeyCode.Home: MoveTo( 0, e.HasShift ); return;
			case KeyCode.End: MoveTo( value.Length, e.HasShift ); return;

			case KeyCode.Backspace:
				if ( ReadOnly ) return;

				//Replace already leaves the caret where the removed text began. Moving it again here put it a letter further left
				//than it should be, so the next backspace ate the wrong letter and the caret walked away from what was being typed.
				if ( !DeleteSelection() && caret > 0 ) Replace( caret - 1, caret, string.Empty );
				return;

			case KeyCode.Delete:
				if ( ReadOnly ) return;
				if ( !DeleteSelection() && caret < value.Length ) Replace( caret, caret + 1, string.Empty );
				return;

			case KeyCode.Return:
			case KeyCode.Enter:
				InternalReturnPressed();
				InternalEditingFinished();
				return;

			case KeyCode.Escape:
				Blur();
				return;

			case KeyCode.A when e.HasCtrl:
				SelectAll();
				return;

			case KeyCode.C when e.HasCtrl:
				Copy();
				return;

			case KeyCode.X when e.HasCtrl:
				Cut();
				return;

			case KeyCode.V when e.HasCtrl:
				Paste();
				return;
		}

		if ( ReadOnly ) return;
		if ( e.HasCtrl || e.HasAlt ) return;
		if ( string.IsNullOrEmpty( e.Text ) ) return;

		Insert( e.Text );
	}

	private void Move( int by, bool selecting )
	{
		MoveTo( Math.Clamp( caret + by, 0, value.Length ), selecting );
	}

	private void MoveTo( int where, bool selecting )
	{
		if ( selecting && anchor < 0 ) anchor = caret;
		if ( !selecting ) anchor = -1;

		caret = Math.Clamp( where, 0, value.Length );

		RaiseCursorMoved();
		RaiseSelectionChanged();

		ScrollToCaret();
		Update();
	}

	//Takes out whatever is selected. False where nothing was.
	private bool DeleteSelection()
	{
		if ( anchor < 0 || SelectionFrom == SelectionTo ) return false;

		var from = SelectionFrom;

		Replace( from, SelectionTo, string.Empty );
		caret = from;

		return true;
	}

	//Puts something in place of a stretch of the text.
	private void Replace( int from, int to, string with )
	{
		from = Math.Clamp( from, 0, value.Length );
		to = Math.Clamp( to, from, value.Length );

		var wanted = value[..from] + with + value[to..];

		//More than the field will hold: what fits goes in and the rest is dropped. Nothing
		//is told about it - the event that said so is commented out in this engine.
		if ( wanted.Length > maxLength ) wanted = wanted[..maxLength];

		value = wanted;
		anchor = -1;
		caret = Math.Clamp( from + with.Length, 0, value.Length );

		ScrollToCaret();

		InternalTextChanged( value );
		InternalTextEdited( value );

		Update();
	}
}
