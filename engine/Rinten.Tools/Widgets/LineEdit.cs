using System;

namespace Editor
{
	/// <summary>A single line text entry. See <see cref="TextEdit"/> for multi line version.</summary>
	public partial class LineEdit : Widget
	{
		/// <summary>The text changed, however it changed.</summary>
		public event Action<string> TextChanged;

		/// <summary>The text was changed by somebody typing in it.</summary>
		public event Action<string> TextEdited;

		/// <summary>Return was pressed while this had the focus.</summary>
		public event Action ReturnPressed;

		/// <summary>The field stopped being typed into.</summary>
		public event Action EditingFinished;

		/// <summary>And started being typed into.</summary>
		public event Action EditingStarted;

		/// <summary>The selection changed.</summary>
		public event Action SelectionChanged;

		/// <summary>Something was typed that the field would not take.</summary>
		public event Action InputRejected;

		/// <summary>The caret moved.</summary>
		public event Action CursorPositionChanged;

		//Said by the field's own code when the selection moves.
		internal void RaiseSelectionChanged() => SelectionChanged?.Invoke();

		//Said when the caret moves.
		internal void RaiseCursorMoved() => CursorPositionChanged?.Invoke();

		//Said when a key was refused - the field is full, or read only.
		internal void RaiseInputRejected() => InputRejected?.Invoke();

		/// <summary>A widget that gets the up, down and return keys instead of this field - the list under a search box, say.</summary>
		public Widget ForwardNavigationEvents { get; set; }


		/// <summary>Alias of <see cref="Text"/>, except disallows setting text when <see cref="Widget.IsFocused"/> is <see langword="true"/>.</summary>
		public virtual string Value
		{
			get => Text;
			set
			{
				if ( IsFocused )
					return;

				Text = value;
			}
		}

		/// <summary>The text of this text entry.</summary>
		public string Text
		{
			get => value;
			set
			{
				SetTextManaged( value, byUser: false );
			}
		}

		public string DisplayText
		{
			get => value;
		}

		/// <summary>The placeholder text, it will be displayed only when the text entry is empty. Typically used to as a short description of the expected input, or as an example input.</summary>
		public string PlaceholderText
		{
			get => placeholder;
			set
			{
				placeholder = value ?? string.Empty;
				Update();
			}
		}

		/// <summary>User entered text can never be longer than this many characters (not bytes).</summary>
		public int MaxLength
		{
			get => maxLength;
			set
			{
				maxLength = Math.Max( value, 0 );
			}
		}

		/// <summary>Whether the user has any text selected within this text entry.</summary>
		public bool HasSelectedText => SelectionFrom != SelectionTo;

		/// <summary>Character at which the text selection begins, or -1 if there is no selection.</summary>
		public int SelectionStart
		{
			get => (HasSelectedText ? SelectionFrom : -1);
		}

		/// <summary>Character at which the text selection ends, or -1 if there is no selection.</summary>
		public int SelectionEnd
		{
			get => (HasSelectedText ? SelectionTo : -1);
		}

		/// <summary>Show a button to clear the text input when it is not empty.</summary>
		public bool ClearButtonEnabled
		{
			get => false;
			set
			{
				//The little cross inside the box is not drawn yet; the field works without it rather than not working.
				return;
			}
		}

		public override bool ReadOnly
		{
			get => readOnly;
			set
			{
				readOnly = value;
				Update();
			}
		}

		/// <summary>The selected text, if any.</summary>
		public string SelectedText => HasSelectedText ? value[SelectionFrom..SelectionTo] : string.Empty;

		/// <summary>Position of the text cursor, at which newly typed letters will be inserted.</summary>
		public int CursorPosition
		{
			get => caret;
			set
			{
				MoveTo( value, selecting: false );
			}
		}

		/// <summary>Clear the text.</summary>
		public void Clear()
		{
			SetTextManaged( string.Empty, byUser: false );
		}

		/// <summary>Select all of the text.</summary>
		public void SelectAll()
		{
			SetSelection( 0, value.Length );
		}

		/// <summary>Set the selected text region.</summary>
		public void SetSelection( int start, int length )
		{
			anchor = Math.Clamp( start, 0, value.Length );
			caret = Math.Clamp( start + length, 0, value.Length );

			ScrollToCaret();
			Update();
		}

		/// <summary>De-select all of the text.</summary>
		public void Deselect()
		{
			anchor = -1;
			Update();
		}

		public void Undo()
		{
			//Nothing remembers what was typed before yet.
			return;
		}

		public void Redo()
		{
			return;
		}

		public void Cut()
		{
			Copy();
			if ( !ReadOnly ) DeleteSelection();
		}

		public void Copy()
		{
			if ( HasSelectedText ) EditorUtility.Clipboard.Copy( SelectedText );
		}

		public void Paste()
		{
			if ( !ReadOnly ) Insert( EditorUtility.Clipboard.Paste() );
		}

		public void Insert( string val )
		{
			if ( ReadOnly || string.IsNullOrEmpty( val ) ) return;

			//A newline is not a thing a line of text can hold.
			val = val.Replace( "\r", "" ).Replace( "\n", "" );

			Replace( SelectionFrom, SelectionTo, val );
		}

		public void SetValidator( string str )
		{
		}

		public LineEdit( Widget parent = null ) : base()
		{
			MakeSurface( parent );

			FocusMode = FocusMode.Click;
			Size = new Vector2( 120, Theme.ControlHeight );

			SetSizeMode( SizeMode.Default, 0 );
		}

		public LineEdit( string title, Widget parent = null ) : this( parent )
		{
			Text = title;
		}

		//Called when the input text changes.
		protected virtual void OnTextChanged( string value )
		{
			AutoComplete?.OnAutoComplete( value, ScreenPosition );
			TextChanged?.Invoke( value );
		}

		//Called when the text was edited.
		protected virtual void OnTextEdited( string value )
		{
			if ( _focusedButNotEdited )
			{
				EditingStarted?.Invoke();
				_focusedButNotEdited = false;
			}
			TextEdited?.Invoke( value );

			SignalValuesChanged();
		}

		//Called when the user presses the return (Enter) key.
		protected virtual void OnReturnPressed()
		{
			ReturnPressed?.Invoke();
		}

		//The text entry lost keyboard focus.
		protected virtual void OnEditingFinished()
		{
			EditingFinished?.Invoke();
		}

		/*
		protected virtual void OnSelectionChanged()
		{
			SelectionChanged?.Invoke();
		}

		protected virtual void OnInputRejected()
		{
			InputRejected?.Invoke();
		}

		protected virtual void OnCursorPositionChanged()
		{
			CursorPositionChanged?.Invoke();
		}*/

		internal void InternalTextChanged( string value ) => OnTextChanged( value );
		internal void InternalTextEdited( string value ) => OnTextEdited( value );
		internal void InternalReturnPressed() => OnReturnPressed();
		internal void InternalEditingFinished() => OnEditingFinished();


		public void SetAutoComplete( Action<Menu, string> func )
		{
			AutoComplete = new AutoComplete( this );
			AutoComplete.OnOptionSelected = ( o ) => Text = o;
			AutoComplete.OnBuildOptions = func;
		}

		public AutoComplete AutoComplete { get; set; }


		protected override void OnBlur( FocusChangeReason reason )
		{
			AutoComplete?.OnParentBlur();

			base.OnBlur( reason );
		}

		protected override void OnFocus( FocusChangeReason reason )
		{
			//EditingStarted?.Invoke();
			_focusedButNotEdited = true;

			base.OnFocus( reason );
		}

		private bool _focusedButNotEdited = false;

		/// <summary>Whether the <see cref="AutoComplete">auto complete</see> <see cref="Menu"/> is visible or not.</summary>
		public bool AutoCompleteVisible => AutoComplete?.Visible ?? false;

		//If we have our menus open, let use tab/shift tab to navigate instead of switching to next control
		protected override bool FocusNext() => HistoryVisible || AutoCompleteVisible;

		//If we have our menus open, let use tab/shift tab to navigate instead of switching to next control
		protected override bool FocusPrevious() => HistoryVisible || AutoCompleteVisible;

		protected override void OnKeyPress( KeyEvent e )
		{
			if ( e.Key == KeyCode.Escape || e.Key == KeyCode.Enter )
			{
				if ( e.Key == KeyCode.Enter )
				{
					InternalReturnPressed();
					InternalEditingFinished();
				}

				Blur();
				return;
			}

			if ( !AutoCompleteVisible && !HistoryVisible && MaxHistoryItems > 0 && (e.Key == KeyCode.Up || e.Key == KeyCode.Down) )
			{
				//Into the field, or into a list to pick from - see HistoryCycles.
				if ( HistoryCycles )
				{
					if ( CycleHistory( e.Key == KeyCode.Up ) )
					{
						e.Accepted = true;
						return;
					}
				}
				else
				{
					OpenHistory();
				}
			}

			if ( HistoryVisible || AutoCompleteVisible )
			{
				if ( e.Key == KeyCode.Tab ) e.Key = KeyCode.Down;
				if ( e.Key == KeyCode.Backtab ) e.Key = KeyCode.Up;
			}

			if ( HistoryVisible )
			{
				historyMenu?.OnParentKeyPress( e );
			}
			else
			{
				AutoComplete?.OnParentKeyPress( e );
			}

			if ( HistoryVisible && !e.Accepted )
			{
				historyMenu.Visible = false;
			}

			if ( ForwardNavigationEvents != null && (e.Key == KeyCode.Up || e.Key == KeyCode.Down || e.Key == KeyCode.Enter) )
			{
				ForwardNavigationEvents.PanelKeyPress( e );
			}

			if ( !e.Accepted ) HandleKey( e );

			base.OnKeyPress( e );
		}

		public Option AddOptionToFront( Option option )
		{
			Assert.NotNull( option );
			option.SetParent( this );
			return option;
		}

		public Option AddOptionToEnd( Option option )
		{
			Assert.NotNull( option );
			option.SetParent( this );
			return option;
		}

		/// <summary>Where the words sit in the field.</summary>
		public TextFlag Alignment { get; set; } = TextFlag.LeftCenter;

		public string RegexValidator
		{
			set
			{
			}
		}

		/// <summary>Where the caret is, for whatever wants to sit under it.</summary>
		public Rect CursorRect
		{
			get
			{
				//Where the caret is drawn, which is what a completion list hangs itself under.
				var inside = LocalRect.Shrink( 6, 0 );

				return new Rect( inside.Left, inside.Top, 1, inside.Height );
			}
		}

	}
}
