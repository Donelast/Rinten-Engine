using System;
using Rinten.UI;
using Rinten.UI.Construct;


namespace Editor;

/// <summary>A line of text somebody can type into.</summary>
public class TextField : Panel, IWantsRawKeys, ITextCaretHost
{
	public override bool AcceptsImeInput => true;

	//The text itself, drawn and edited by the label under it.
	private readonly Rinten.UI.Label label;

	//What to show while it is empty.
	private readonly Rinten.UI.Label hint;

	//The mark before the text, where there is one.
	private readonly Rinten.UI.Label mark;

	//Since the focus arrived - what makes the caret blink.
	private RealTimeSince focused;

	public TextField()
	{
		AcceptsFocus = true;

		mark = Add.Label( "", "mark" );
		mark.SetClass( "hidden", true );

		label = Add.Label( "", "text" );
		label.Tokenize = false;
		label.Multiline = false;

		//Not a stylesheet property, so it is set here rather than in the sheet.
		label.SelectionColor = Color.Parse( "#3b82f6" )?.WithAlpha( 0.45f ) ?? Color.Gray;

		hint = Add.Label( "", "hint" );

		EditorStyles.Apply( this, "TextField.cs.scss" );
	}

	//Setting it while somebody is typing is ignored: the value that arrives from elsewhere is older than the letter being typed right now, and taking it would eat what they wrote.
	/// <summary>The text in the field.</summary>
	public string Value
	{
		get => label.Text ?? string.Empty;
		set
		{
			if ( HasFocus ) return;
			if ( label.Text == value ) return;

			label.Text = value ?? string.Empty;

			//Tick hides the hint from the text; asked for, since the field no longer ticks every frame.
			RequestTick();
		}
	}

	/// <summary>What to show while it is empty - what the field is for, briefly.</summary>
	public string Placeholder
	{
		get => hint.Text;
		set => hint.Text = value ?? string.Empty;
	}

	/// <summary>A font icon before the text, by name.</summary>
	public string Icon
	{
		get => mark.Text;
		set
		{
			mark.Text = value ?? string.Empty;
			mark.SetClass( "hidden", string.IsNullOrEmpty( value ) );

			//The placeholder sits over the text rather than in the row, so it has to be told to move out from under the icon.
			SetClass( "marked", !string.IsNullOrEmpty( value ) );
		}
	}

	/// <summary>Called for every change, as it is typed.</summary>
	public Action<string> ValueChanged { get; set; }

	/// <summary>Called when return is pressed.</summary>
	public Action<string> Submitted { get; set; }

	/// <summary>Handed every key before the field itself takes it, for whoever owns the field.</summary>
	public Action<ButtonEvent> ButtonTyped { get; set; }

	private int Caret => label.CaretPosition;

	//Whether part of the text is selected. Asked of the selection itself rather than of the label's own HasSelection , which also answers whether the
	//selection is being drawn - true only once the field has been through a frame. What is selected and what is painted are not the same question.
	private bool HasSelection => label.SelectionStart != label.SelectionEnd;

	//Selects a run of the text, and shows that it is selected.
	private void Select( int from, int to )
	{
		label.ShouldDrawSelection = true;
		label.SetSelection( from, to );
	}

	//Public because a field is not always typed into from empty: a rename comes up holding the old name, and the first thing typed should replace it rather than be appended to it.
	/// <summary>Selects the whole of the text, as ctrl+A does.</summary>
	public void SelectAll() => Select( 0, label.TextLength );

	//Sets text from the field's own code even while typing (where Value refuses); caret goes to end
	protected void Replace( string text )
	{
		label.Text = text ?? string.Empty;
		label.SetSelection( 0, 0 );
		label.SetCaretPosition( label.TextLength );

		Changed();
	}

	protected void Changed()
	{
		hint.SetClass( "hidden", !string.IsNullOrEmpty( label.Text ) );

		ValueChanged?.Invoke( Value );
	}

	//Ticked when asked: the text asks through Value and Changed, focus and blur arrive as events.
	protected override bool TicksEveryFrameOfItsOwn => GetType() != typeof( TextField );

	public override void Tick()
	{
		base.Tick();

		hint.SetClass( "hidden", !string.IsNullOrEmpty( label.Text ) );
		SetClass( "focused", HasFocus );
	}

	public override void OnKeyTyped( char k )
	{
		if ( char.IsControl( k ) ) return;

		if ( HasSelection )
		{
			label.ReplaceSelection( k.ToString() );
		}
		else
		{
			label.InsertText( k.ToString(), Caret );
			label.MoveCaretPos( 1 );
		}

		Changed();
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		//The owner first, where there is one: once the field has the key nothing else can be given it, so this is the only place a key can be taken away.
		ButtonTyped?.Invoke( e );

		if ( e.StopPropagation ) return;

		//Whatever a field is typed into is the field's, not the window's: the editor's shortcuts do not fire while somebody is writing in one.
		e.StopPropagation = true;

		if ( !e.Pressed ) return;

		switch ( e.Button )
		{
			case "backspace":
				Backspace( e.HasCtrl );
				return;

			case "delete":
				Forward( e.HasCtrl );
				return;

			case "left":
				if ( e.HasCtrl ) label.MoveToWordBoundaryLeft( e.HasShift );
				else if ( HasSelection && !e.HasShift ) label.SetCaretPosition( label.SelectionStart );
				else label.MoveCaretPos( -1, e.HasShift );
				return;

			case "right":
				if ( e.HasCtrl ) label.MoveToWordBoundaryRight( e.HasShift );
				else if ( HasSelection && !e.HasShift ) label.SetCaretPosition( label.SelectionEnd );
				else label.MoveCaretPos( 1, e.HasShift );
				return;

			case "home":
				label.SetCaretPosition( 0, e.HasShift );
				return;

			case "end":
				label.SetCaretPosition( label.TextLength, e.HasShift );
				return;

			case "enter":
			case "return":
				Submitted?.Invoke( Value );
				return;

			case "escape":
				//Out of the field rather than out of the window: escape in a search box is how you stop searching.
				if ( !string.IsNullOrEmpty( label.Text ) )
				{
					label.Text = string.Empty;
					label.SetCaretPosition( 0 );
					Changed();
					return;
				}

				Blur();
				return;

			case "a" when e.HasCtrl:
				SelectAll();
				return;

			//Copy, cut and paste are not keys here: the window turns them into the panel system's
			//own events, answered by GetClipboardValue and OnPaste below, as every other field's are.
		}
	}

	private void Backspace( bool word )
	{
		if ( HasSelection )
		{
			label.ReplaceSelection( string.Empty );
			Changed();
			return;
		}

		if ( Caret <= 0 ) return;

		if ( word )
		{
			label.MoveToWordBoundaryLeft( true );
			label.ReplaceSelection( string.Empty );
		}
		else
		{
			label.MoveCaretPos( -1 );
			label.RemoveText( Caret, 1 );
		}

		Changed();
	}

	private void Forward( bool word )
	{
		if ( HasSelection )
		{
			label.ReplaceSelection( string.Empty );
			Changed();
			return;
		}

		if ( Caret >= label.TextLength ) return;

		if ( word )
		{
			label.MoveToWordBoundaryRight( true );
			label.ReplaceSelection( string.Empty );
		}
		else
		{
			label.RemoveText( Caret, 1 );
		}

		Changed();
	}

	private void Paste( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return;

		//One line: what was copied out of a file arrives with its newlines, and they would be drawn as boxes in a field that has no second line.
		text = text.Replace( "\r", "" ).Replace( "\n", " " );

		if ( HasSelection ) label.ReplaceSelection( text );
		else
		{
			label.InsertText( text, Caret );
			label.MoveCaretPos( text.Length );
		}

		Changed();
	}

	public override string GetClipboardValue( bool cut )
	{
		var value = label.GetClipboardValue( cut );

		if ( cut )
		{
			label.ReplaceSelection( string.Empty );
			Changed();
		}

		return value;
	}

	public override void OnPaste( string text ) => Paste( text );

	protected override void OnMouseDown( MousePanelEvent e )
	{
		e.StopPropagation();

		Select( 0, 0 );

		var at = label.GetLetterAtPointer( e.PointerPosition );

		if ( at >= 0 ) label.SetCaretPosition( at );
		else label.SetCaretPosition( label.TextLength );
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		if ( string.IsNullOrEmpty( label.Text ) ) return;

		label.ShouldDrawSelection = true;
		label.SelectWord( label.GetLetterAtPointer( e.PointerPosition ) );
	}

	protected override void OnDragSelect( SelectionEvent e )
	{
		var from = label.GetLetterAtScreenPosition( e.StartPoint );
		var to = label.GetLetterAtScreenPosition( e.EndPoint );

		Select( from, to );
		label.CaretPosition = to.Clamp( 0, label.TextLength );
	}

	protected override void OnFocus( PanelEvent e )
	{
		focused = 0;
		label.MarkRenderDirty();
	}

	protected override void OnBlur( PanelEvent e )
	{
		label.SetSelection( 0, 0 );
	}

	public override void OnDraw()
	{
		label.ShouldDrawSelection = HasFocus;
	}

	public bool ShouldDrawCaret( Rinten.UI.Label label )
	{
		if ( label != this.label || !HasFocus || HasSelection )
			return false;

		const float rate = 1.0f;
		if ( focused % rate >= rate * 0.5f )
		{
			label.MarkRenderDirty();
			return false;
		}

		return true;
	}

	public int GetCaretIndex( Rinten.UI.Label label ) => Caret;

	public Color GetCaretColor( Rinten.UI.Label label ) =>
		ComputedStyle?.CaretColor ?? ComputedStyle?.FontColor ?? Color.White;
}
