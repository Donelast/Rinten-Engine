using Rinten.Rendering;
using Rinten.UI.Construct;
using System.Globalization;

namespace Rinten.UI;

/// <summary>A <see cref="Panel"/> that the user can enter text into.</summary>
[Library( "TextEntry" )]
public partial class TextEntry : Panel, ITextCaretHost
{

	//Called when the text of this text entry is changed.
	[Parameter] public Action<string> OnTextEdited { get; set; }

	//The Label that contains the text of this text entry.
	protected Label Label { get; init; }

	bool _disabled;

	/// <summary>Is the text entry disabled? If disabled, will add a "disabled" class and prevent focus.</summary>
	[Parameter]
	public bool Disabled
	{
		get => _disabled;
		set
		{
			_disabled = value;
			AcceptsFocus = !value;
			SetClass( "disabled", value );
		}
	}

	/// <summary>Access to the raw text in the text entry.</summary>
	[Parameter]
	public string Text
	{
		get => Label.Text;
		set
		{
			if ( Label.Text == value ) return;

			Label.Text = value;

			//The placeholder comes and goes with the text, and Tick is what moves it.
			RequestTick();
		}
	}

	/// <summary>The value of the text entry. Returns <see cref="Text"/>, but does special logic when setting text.</summary>
	[Parameter]
	public string Value
	{
		get => Label.Text;
		set
		{
			//don't change the value when we're editing it
			if ( HasFocus )
				return;

			var was = Label.Text;

			Label.Text = value;
			if ( Numeric )
			{
				Label.Text = FixNumeric();
			}

			if ( Label.Text != was )
				RequestTick();
		}
	}

	public int TextLength
	{
		get => Label.TextLength;
	}

	public int CaretPosition
	{
		get => Label.CaretPosition;
		set => Label.CaretPosition = value;
	}


	/// <summary>Whether to allow automatic replacement of emoji codes with their actual unicode emoji characters. See <see cref="Emoji"/>.</summary>
	public bool AllowEmojiReplace { get; set; } = false;

	/// <summary>Allow <a href="https://en.wikipedia.org/wiki/Input_method">IME input</a> when this is focused.</summary>
	public override bool AcceptsImeInput => true;

	/// <summary>Affects formatting of the text when <see cref="Numeric"/> is enabled. Accepts any format that is supported by <see cref="float.ToString(string?)"/>. <a href="https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-numeric-format-strings">See examples here</a>.</summary>
	[Category( "Presentation" )]
	public string NumberFormat { get; set; } = null;

	/// <summary>Makes it possible to enter new lines into the text entry. (By pressing the Enter key, which no longer acts as the submit key)</summary>
	[Property, Parameter]
	public bool Multiline
	{
		get => multiline;
		set
		{
			if ( multiline == value ) return;

			multiline = value;

			//Tick mirrors this into the is-multiline class and the label.
			RequestTick();
		}
	}

	bool multiline;

	/// <summary>If we're numeric, this is the lowest numeric value allowed</summary>
	public float? MinValue { get; set; }

	/// <summary>If we're numeric, this is the highest numeric value allowed</summary>
	public float? MaxValue { get; set; }

	/// <summary>Text to display when the text entry is empty. Typically a very short description of the expected contents or function of the text entry.</summary>
	[Parameter]
	public string Placeholder
	{
		get => placeholder;
		set
		{
			if ( placeholder == value ) return;

			placeholder = value;
			RequestTick();
		}
	}

	string placeholder;

	static readonly Color PlaceholderInk = new( 0.62f, 0.62f, 0.62f );

	/// <summary>The label that shows <see cref="Placeholder"/> while the entry is empty.</summary>
	public Label PlaceholderLabel { get; protected set; }

	//Overlay for the placeholder so the text label never moves and the caret can't jump on focus
	protected Panel PlaceholderWrap { get; private set; }

	//Shows the placeholder while the entry is empty and unfocused. Called on focus changes
	//as well as from Tick, so it comes and goes with the click rather than a frame after it.
	void UpdatePlaceholderState()
	{
		//Focus counts as content: the field is being typed into.
		bool isPlaceholder = string.IsNullOrEmpty( Text ) && !string.IsNullOrEmpty( Placeholder ) && !HasFocus;

		if ( isPlaceholder && PlaceholderWrap is null )
		{
			PlaceholderWrap = AddChild<Panel>( "placeholder-wrap" );
			PlaceholderWrap.Style.Position = PositionMode.Absolute;
			PlaceholderWrap.Style.Left = 0;
			PlaceholderWrap.Style.Top = 0;
			PlaceholderWrap.Style.Width = Length.Percent( 100 );
			PlaceholderWrap.Style.Height = Length.Percent( 100 );
			PlaceholderWrap.Style.FlexDirection = FlexDirection.Row;
			PlaceholderWrap.Style.AlignItems = Align.Center;
			PlaceholderWrap.Style.PointerEvents = PointerEvents.None;

			PlaceholderLabel = PlaceholderWrap.Add.Label( "", "placeholder" );
			PlaceholderLabel.Tokenize = false;
			PlaceholderLabel.Selectable = false;
			PlaceholderLabel.Style.PointerEvents = PointerEvents.None;
			PlaceholderLabel.Style.FontColor = PlaceholderInk;
		}

		if ( PlaceholderWrap.IsValid() )
		{
			PlaceholderLabel.Text = Placeholder;
			PlaceholderWrap.Style.Display = isPlaceholder ? DisplayMode.Flex : DisplayMode.None;
		}
	}

	/// <summary>The <see cref="Label"/> that shows <see cref="Prefix"/> text.</summary>
	public Label PrefixLabel { get; protected set; }

	/// <summary>If set, will display given text before the text entry box.</summary>
	public string Prefix
	{
		get => PrefixLabel?.Text;
		set
		{
			if ( string.IsNullOrWhiteSpace( value ) )
			{
				PrefixLabel?.Delete();
				SetClass( "has-prefix", false );
				return;
			}

			PrefixLabel ??= Add.Label( value, "prefix-label" );
			PrefixLabel.Text = value;

			SetClass( "has-prefix", PrefixLabel.IsValid() );
		}
	}

	/// <summary>The <see cref="Label"/> that shows <see cref="Suffix"/> text.</summary>
	public Label SuffixLabel { get; protected set; }

	/// <summary>If set, will display given text after the text entry box.</summary>
	public string Suffix
	{
		get => SuffixLabel?.Text;
		set
		{
			if ( string.IsNullOrWhiteSpace( value ) )
			{
				SuffixLabel?.Delete();
				SetClass( "has-suffix", false );
				return;
			}

			SuffixLabel ??= Add.Label( value, "suffix-label" );
			SuffixLabel.Text = value;

			SetClass( "has-suffix", SuffixLabel.IsValid() );
		}
	}

	/// <summary>The color used for text selection highlight. Defaults to cyan with transparency.</summary>
	[Category( "Appearance" ), Parameter]
	public Color SelectionColor
	{
		get => Label?.SelectionColor ?? Color.Cyan.WithAlpha( 0.39f );
		set
		{
			if ( Label is not null )
				Label.SelectionColor = value;
		}
	}

	public TextEntry()
	{
		AcceptsFocus = true;
		Style.PointerEvents = PointerEvents.All;
		AddClass( "textentry" );

		//Dragging selects text, it never scrolls
		CanDragScroll = false;

		Label = Add.Label( "", "content-label" );
		Label.Tokenize = false;
		Label.Style.WhiteSpace = WhiteSpace.Pre;
		Label.Style.PointerEvents = PointerEvents.None;
	}

	public override void OnPaste( string text )
	{
		if ( Label.HasSelection() )
		{
			Label.ReplaceSelection( "" );
		}

		var pasteResult = new string( text.Where( CanEnterCharacter ).ToArray() );
		ReplaceEmojisInText( ref pasteResult );

		if ( MaxLength.HasValue )
		{
			//MaxLength and TextLength count text elements, not chars - truncate the same way so we don't cut a surrogate pair or combining sequence in half
			var remaining = Math.Max( MaxLength.Value - TextLength, 0 );
			var info = new StringInfo( pasteResult );
			if ( info.LengthInTextElements > remaining )
			{
				pasteResult = remaining > 0 ? info.SubstringByTextElements( 0, remaining ) : "";
			}
		}

		Text ??= "";
		Label.InsertText( pasteResult, CaretPosition );
		Label.MoveCaretPos( pasteResult.Length );

		OnValueChanged();
	}

	public override string GetClipboardValue( bool cut )
	{
		var value = Label.GetClipboardValue( cut );

		if ( cut )
		{
			Label.ReplaceSelection( "" );
			OnValueChanged();
		}

		return value;
	}
	public override void OnButtonEvent( ButtonEvent e )
	{
		//dont' send to parent
		e.StopPropagation = true;
	}

	protected override void OnEscape( PanelEvent e )
	{
		Cancel();
		e.StopPropagation();
	}


	/// <summary>Non-letter keys, on press only; windows also send releases, which acted on every key twice</summary>
	public override void OnButtonTyped( ButtonEvent e )
	{
		//Set for the release as well as the press: a field being typed into is deaf to everything above it for the whole of a keystroke, not half of one.
		e.StopPropagation = true;

		if ( !e.Pressed ) return;

		//Log.Info( $"OnButtonTyped {button}" );
		var button = e.Button;

		if ( Label.HasSelection() && (button == "delete" || button == "backspace") )
		{
			Label.ReplaceSelection( "" );
			OnValueChanged();

			return;
		}

		if ( button == "delete" )
		{
			if ( CaretPosition < TextLength )
			{
				if ( e.HasCtrl )
				{
					Label.MoveToWordBoundaryRight( true );
					Label.ReplaceSelection( string.Empty );
					OnValueChanged();
					return;
				}

				Label.RemoveText( CaretPosition, 1 );
				OnValueChanged();
			}

			return;
		}

		if ( button == "backspace" )
		{
			if ( CaretPosition > 0 )
			{
				if ( e.HasCtrl )
				{
					Label.MoveToWordBoundaryLeft( true );
					Label.ReplaceSelection( string.Empty );
					OnValueChanged();
					return;
				}

				Label.MoveCaretPos( -1 );
				Label.RemoveText( CaretPosition, 1 );
				OnValueChanged();
			}

			return;
		}

		if ( button == "a" && e.HasCtrl )
		{
			Label.SelectionStart = 0;
			Label.SelectionEnd = TextLength;
			return;
		}

		if ( button == "home" )
		{
			if ( !e.HasCtrl )
			{
				Label.MoveToLineStart( e.HasShift );
			}
			else
			{
				Label.SetCaretPosition( 0, e.HasShift );
			}
			return;
		}

		if ( button == "end" )
		{
			if ( !e.HasCtrl )
			{
				Label.MoveToLineEnd( e.HasShift );
			}
			else
			{
				Label.SetCaretPosition( TextLength, e.HasShift );
			}
			return;
		}

		if ( button == "left" )
		{
			if ( !e.HasCtrl )
			{
				if ( Label.HasSelection() )
					Label.SetCaretPosition( Label.SelectionStart );
				else
					Label.MoveCaretPos( -1, e.HasShift );
			}
			else
			{
				Label.MoveToWordBoundaryLeft( e.HasShift );
			}
			return;
		}

		if ( button == "right" )
		{
			if ( !e.HasCtrl )
			{
				if ( Label.HasSelection() )
					Label.SetCaretPosition( Label.SelectionEnd );
				else
					Label.MoveCaretPos( 1, e.HasShift );
			}
			else
			{
				Label.MoveToWordBoundaryRight( e.HasShift );
			}
			return;
		}

		if ( button == "down" || button == "up" )
		{
			if ( AutoCompletePanel.IsValid() )
			{
				AutoCompletePanel.MoveSelection( button == "up" ? -1 : 1 );
				AutoCompleteSelectionChanged();
				return;
			}

			//We have history items, autocomplete using those
			if ( string.IsNullOrEmpty( Text ) && !AutoCompletePanel.IsValid() && _history.Count > 0 )
			{
				UpdateAutoComplete( _history.ToArray() );

				//select last item
				AutoCompletePanel.MoveSelection( -1 );
				AutoCompleteSelectionChanged();

				return;
			}

			Label.MoveCaretLine( button == "up" ? -1 : 1, e.HasShift );
			return;
		}

		if ( button == "enter" || button == "pad_enter" )
		{
			if ( Multiline )
			{
				OnKeyTyped( '\n' );
				return;
			}

			if ( AutoCompletePanel.IsValid() && AutoCompletePanel.SelectedChild.IsValid() )
			{
				DestroyAutoComplete();
			}

			Blur();
			CreateEvent( "onsubmit", Text );
			return;
		}

		if ( button == "escape" )
		{
			Cancel();
			return;
		}

		if ( button == "tab" )
		{
			if ( AutoCompletePanel.IsValid() )
			{
				AutoCompletePanel.MoveSelection( e.HasShift ? -1 : 1 );
				AutoCompleteSelectionChanged();
				return;
			}
		}

		base.OnButtonTyped( e );
	}

	void Cancel()
	{
		if ( AutoCompletePanel.IsValid() )
		{
			AutoCompleteCancel();
			return;
		}

		Blur();
		CreateEvent( "oncancel" );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		//Stopping a press on the scrollbar here would stop its drag from starting
		if ( ScrollBar.Owns( e.Target ) ) return;

		e.StopPropagation();

		Label.ShouldDrawSelection = true;
		TimeSinceNotInFocus = 0;

		Label.SelectionStart = 0;
		Label.SelectionEnd = 0;

		if ( !string.IsNullOrEmpty( Text ) )
		{
			var pos = Label.GetLetterAtPointer( e.PointerPosition );
			if ( pos >= 0 )
				Label.SetCaretPosition( pos );
		}
		else
			Label.SetCaretPosition( 0 );

		Focus();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( ScrollBar.Owns( e.Target ) ) return;

		SelectingWords = false;

		var pos = Label.GetLetterAtPointer( e.PointerPosition );
		if ( Label.SelectionEnd > 0 ) pos = Label.SelectionEnd;
		Label.CaretPosition = pos.Clamp( 0, TextLength );

		Label.ScrollToCaret();
		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( ScrollBar.Owns( e.Target ) ) return;

		e.StopPropagation();
	}

	protected override void OnFocus( PanelEvent e )
	{
		//Now rather than on the next tick, so the caret of this very click is drawn against a label that is already laid out where the text goes.
		UpdatePlaceholderState();

		UpdateAutoComplete();
		TimeSinceNotInFocus = 0;
		Label.MarkRenderDirty();
	}

	protected override void OnBlur( PanelEvent e )
	{
		//UpdateAutoComplete();

		if ( Numeric )
		{
			Text = FixNumeric();

			//Typed text that never came to a number was never sent; what it is fixed to now is.
			if ( numericEditUnsent )
				OnValueChanged();
		}

		UpdatePlaceholderState();
	}

	private bool SelectingWords = false;
	protected override void OnDoubleClick( MousePanelEvent e )
	{
		if ( string.IsNullOrEmpty( Text ) )
			return;

		if ( e.Button == "mouseleft" )
		{
			Label.SelectWord( Label.GetLetterAtPointer( e.PointerPosition ) );
			SelectingWords = true;
		}
	}

	public override void OnKeyTyped( char k )
	{
		if ( !CanEnterCharacter( k ) )
			return;

		if ( MaxLength.HasValue && TextLength >= MaxLength )
			return;

		if ( Label.HasSelection() )
		{
			Label.ReplaceSelection( k.ToString() );
		}
		else
		{
			Text ??= "";
			Label.InsertText( k.ToString(), CaretPosition );
			Label.MoveCaretPos( 1 );
		}

		if ( k == ':' )
		{
			RealtimeEmojiReplace();
		}

		OnValueChanged();
	}


	public override void OnDraw()
	{
		Label.ShouldDrawSelection = HasFocus;
	}

	public bool ShouldDrawCaret( Label label )
	{
		if ( label != Label || !HasFocus || label.HasSelection() )
			return false;

		const float blinkRate = 0.8f;
		if ( (TimeSinceNotInFocus * blinkRate) % blinkRate >= blinkRate * 0.5f )
		{
			label.MarkRenderDirty();
			return false;
		}

		return true;
	}

	public int GetCaretIndex( Label label ) => CaretPosition;

	public Color GetCaretColor( Label label ) =>
		ComputedStyle.CaretColor ?? ComputedStyle.FontColor ?? Color.White;

	void RealtimeEmojiReplace()
	{
		if ( !AllowEmojiReplace )
			return;

		if ( CaretPosition == 0 )
			return;

		string lookup = null;
		var arr = StringInfo.ParseCombiningCharacters( Text );
		var caretStringPosition = arr[CaretPosition - 1];

		for ( int i = caretStringPosition - 2; i >= 0; i-- )
		{
			var c = Text[i];

			if ( char.IsWhiteSpace( c ) )
				return;

			if ( c == ':' )
			{
				lookup = Text.Substring( i, caretStringPosition - i + 1 );
				break;
			}

			if ( i == 0 )
				return;
		}

		if ( lookup == null )
			return;

		var replace = Emoji.FindEmoji( lookup );
		if ( replace == null )
			return;

		CaretPosition -= lookup.Length - 1; //set this first so we don't get abused by CaretSanity
		Text = Text.Replace( lookup, replace );
	}

	void ReplaceEmojisInText( ref string text )
	{
		if ( !AllowEmojiReplace || string.IsNullOrEmpty( text ) )
			return;

		text = System.Text.RegularExpressions.Regex.Replace( text, @":\w+:", match =>
		{
			string lookup = match.Value;
			string replace = Emoji.FindEmoji( lookup );
			return replace ?? lookup; //Use the emoji if found; otherwise, keep the original
		} );
	}


	//The text was typed into and did not come to a number, so nothing has been sent for it yet.
	bool numericEditUnsent;

	/// <summary>Called when the text entry's value changes.</summary>
	public virtual void OnValueChanged()
	{
		UpdateAutoComplete();
		UpdateValidation();

		if ( Numeric )
		{
			//with numberic, we don't ever want to send out invalid values to binds - nor the zero a half-typed "96*" would be fixed to
			numericEditUnsent = !NumericExpression.TryEvaluate( Text, out _ );

			if ( !numericEditUnsent )
			{
				var text = FixNumeric();
				CreateEvent( "onchange" );
				CreateValueEvent( "value", text );
				OnTextEdited?.Invoke( text );
			}
		}
		else
		{
			CreateEvent( "onchange" );
			CreateValueEvent( "value", Text );
			OnTextEdited?.Invoke( Text );
		}

		EmptyStateChanged();
	}

	//Keep tabs of when we were focused so we can flash the caret relative to that time. We want the caret to be visible immediately on focus
	protected RealTimeSince TimeSinceNotInFocus;

	//Ticked when asked, not every frame: everything Tick copies - the text, the placeholder, the multiline flag, focus - asks for the tick
	//where it changes, and a focused entry asks for the next frame itself below. A subclass with a Tick of its own keeps ticking every frame.
	protected override bool TicksEveryFrameOfItsOwn => GetType() != typeof( TextEntry );

	public override void Tick()
	{
		base.Tick();

		SetClass( "is-multiline", Multiline );

		UpdatePlaceholderState();

		if ( Label.IsValid() )
		{
			Label.ShouldDrawSelection = HasFocus;
			Label.Multiline = Multiline;
		}

		if ( HasFocus && Label.IsValid() )
			Label.MarkRenderDirty();

		if ( !HasFocus )
			TimeSinceNotInFocus = 0;

		//The focused entry keeps its every-frame tick - the caret and the selection are drawn from it.
		if ( HasFocus )
			RequestTick();
	}

	public override void SetProperty( string name, string value )
	{
		base.SetProperty( name, value );

		if ( name == "placeholder" )
		{
			Placeholder = value;
		}

		if ( name == "numeric" )
		{
			Numeric = value.ToBool();
		}

		if ( name == "format" )
		{
			NumberFormat = value;
		}

		if ( name == "value" && !HasFocus )
		{
			//When setting tha value, and we're numeric, convert it to a number
			if ( Numeric )
			{
				if ( !float.TryParse( value, out var floatValue ) )
					return;

				Text = floatValue.ToString( NumberFormat );
				return;
			}

			Text = value;
		}

		if ( name == "disabled" )
		{
			Disabled = value.ToBool();
		}
	}

	/// <summary>Called to ensure the <see cref="Text"/> is absolutely in the correct format, in this case - a valid number format. Arithmetic in it is worked out: <c>96*2</c> becomes <c>192</c>.</summary>
	public virtual string FixNumeric()
	{
		if ( !NumericExpression.TryEvaluate( Text, out var number ) )
		{
			var val = 0.0f.Clamp( MinValue ?? 0.0f, MaxValue ?? 0.0f );
			return val.ToString();
		}

		var floatValue = (float)number;
		floatValue = floatValue.Clamp( MinValue ?? floatValue, MaxValue ?? floatValue );
		return floatValue.ToString( NumberFormat );
	}

	protected override void OnDragSelect( SelectionEvent e )
	{
		if ( string.IsNullOrEmpty( Text ) )
			return;

		if ( ScrollBar.Owns( e.Target ) )
			return;

		TimeSinceNotInFocus = 0;
		Label.ShouldDrawSelection = true;

		var tl = new Vector2( e.SelectionRect.Left, e.SelectionRect.Top );
		var br = new Vector2( e.SelectionRect.Right, e.SelectionRect.Bottom );
		Label.SelectionStart = Label.GetLetterAtScreenPosition( tl );
		Label.SelectionEnd = Label.GetLetterAtScreenPosition( br );

		if ( SelectingWords )
		{
			var boundaries = Label.GetWordBoundaryIndices();

			var left = boundaries.LastOrDefault( x => x < Label.SelectionStart );
			var right = boundaries.FirstOrDefault( x => x > Label.SelectionEnd );

			left = Math.Min( left, Label.SelectionStart );
			right = Math.Max( right, Label.SelectionEnd );

			Label.SelectionStart = left;
			Label.SelectionEnd = right;
		}

		var pos = Label.GetLetterAtPanelPosition( Label.MousePosition );
		Label.CaretPosition = pos.Clamp( 0, TextLength );
		Label.ScrollToCaret();
	}

	int? ImeInputPos;
	string ImeInputStart;

	protected override void OnEvent( PanelEvent e )
	{
		//Ime input started
		if ( e.Name == "onimestart" )
		{
			ImeInputStart = Label.Text;
			ImeInputPos = CaretPosition;
		}

		//Ime input ended
		if ( e.Name == "onimeend" )
		{
			ImeInputStart = default;
			ImeInputPos = default;
		}

		//ime input changed
		if ( e.Name == "onime" )
		{
			if ( ImeInputPos == null ) return;

			var str = (string)e.Value;
			var info = new StringInfo( str );

			Label.Text = ImeInputStart;
			Label.InsertText( str, ImeInputPos.Value );
			CaretPosition = ImeInputPos.Value + info.LengthInTextElements;
		}

		base.OnEvent( e );
	}

	//The TextEntry has the :empty style when the text is unset
	protected override bool IsPanelEmpty()
	{
		return TextLength == 0;
	}

}
