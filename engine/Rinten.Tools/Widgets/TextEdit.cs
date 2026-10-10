using System;


[Flags]
enum TextInteractionFlags
{
	NoTextInteraction = 0,
	TextSelectableByMouse = 1,
	TextSelectableByKeyboard = 2,
	LinksAccessibleByMouse = 4,
	LinksAccessibleByKeyboard = 8,
	TextEditable = 16,

	TextEditorInteraction = TextSelectableByMouse | TextSelectableByKeyboard | TextEditable,
	TextBrowserInteraction = TextSelectableByMouse | LinksAccessibleByMouse | LinksAccessibleByKeyboard
};

enum LineWrapMode
{
	NoWrap,
	WidgetWidth
};


enum QTextOptionWrapMode
{
	NoWrap,
	WordWrap,
	ManualWrap,
	WrapAnywhere,
	WrapAtWordBoundaryOrAnywhere
};

namespace Editor
{
	/// <summary>A multi-line text entry. See <see cref="LineEdit"/> for a single line version.</summary>
	public partial class TextEdit : Widget
	{
		/// <summary>The text changed, however it changed.</summary>
		public Action<string> TextChanged;

		/// <summary>Somebody started typing into it.</summary>
		public Action OnEditingStarted;

		/// <summary>And stopped.</summary>
		public Action OnEditingFinished;


		public TextEdit( Widget parent = null ) : base()
		{
			MakeSurface( parent );

			SetSizeMode( SizeMode.Flexible, SizeMode.Flexible );

			FocusMode = FocusMode.Click;
			Size = new Vector2( 200, 120 );
		}

		public bool TextSelectable
		{
			get => selectable;
			set
			{
				//Selecting inside one of ours is not there yet; the widget remembers that it was asked for.
				selectable = value;
			}
		}

		public bool LinksClickable
		{
			get => linksClickable;
			set
			{
				linksClickable = value;
			}
		}

		private bool selectable;
		private bool linksClickable;

		public bool Editable
		{
			get => editable;
			set
			{
				editable = value;
				Update();
			}
		}

		public ScrollBar VerticalScrollbar { get; init; }
		public ScrollBar HorizontalScrollbar { get; init; }

		public ScrollbarMode HorizontalScrollbarMode
		{
			get => horizontalMode;
			set
			{
				horizontalMode = value;
			}
		}

		public ScrollbarMode VerticalScrollbarMode
		{
			get => verticalMode;
			set
			{
				verticalMode = value;
			}
		}

		private ScrollbarMode horizontalMode;
		private ScrollbarMode verticalMode;

		public void ScrollToBottom()
		{
			ScrollToEnd();
			Update();
		}

		public string PlainText
		{
			get => TextManaged;
			set
			{
				TextManaged = value;
			}
		}

		public string Html
		{
			get => TextManaged;
			set
			{
				//The markup is not drawn yet; what it says is, which is what matters for the places this is used to read from.
				TextManaged = StripTags( value );
			}
		}

		public string PlaceholderText
		{
			get => placeholder;
			set
			{
				placeholder = value ?? string.Empty;
				Update();
			}
		}

		public void AppendHtml( string html )
		{
			html = System.Text.RegularExpressions.Regex.Replace( html, @"[\p{C}-[\u0020\u0009\u000A\u000C\u000D]]", "" );

			AppendManaged( StripTags( html ) );
		}

		public void AppendPlainText( string text )
		{
			AppendManaged( text );
		}

		public virtual void Clear()
		{
			ClearManaged();
		}

		public void SelectAll()
		{
			//Selecting inside one of these is not there yet.
			return;
		}

		public void CenterOnCursor()
		{
			Update();
		}

		//The words out of a piece of markup, since ours draws the words.
		private static string StripTags( string html )
		{
			if ( string.IsNullOrEmpty( html ) ) return html;

			var text = System.Text.RegularExpressions.Regex.Replace( html, "<br\\s*/?>", "\n" );

			text = System.Text.RegularExpressions.Regex.Replace( text, "<[^>]+>", "" );

			return System.Net.WebUtility.HtmlDecode( text );
		}

		public bool CenterOnScroll
		{
			get => false;
			set
			{
				return;
			}
		}

		public bool BackgroundVisible
		{
			get => true;
			set
			{
				return;
			}
		}

		public int MaximumBlockCount
		{
			get => blocks;
			set
			{
				blocks = value <= 0 ? int.MaxValue : value;
			}
		}

		public float TabSize
		{
			get => tabSize;
			set
			{
				tabSize = value;
			}
		}

		private float tabSize = 4;

		public override bool ReadOnly
		{
			get => readOnly;
			set
			{
				readOnly = value;
				Update();
			}
		}

		public void SetTextCursor( TextCursor cursor )
		{
			return;
		}

		public TextCursor GetCursorAtPosition( Vector2 position )
		{
			return CursorAtManaged( position );
		}

		public TextCursor GetTextCursor()
		{
			return new TextCursor( this, CaretLineManaged );
		}

		public Rect GetCursorRect( TextCursor cursor )
		{
			return LineRectManaged( cursor.BlockNumber );
		}

		public string GetAnchorAt( Vector2 point )
		{
			//Links in the log came in as HTML and went out as plain text, so there is nothing under the pointer to follow yet.
			return null;
		}

		public override CursorShape Cursor
		{
			get => base.Cursor;
			set
			{
				if ( base.Cursor == value )
					return;

				base.Cursor = value;

				return;
			}
		}

		public TextCursor GetCursorAtBlock( int block )
		{
			return new TextCursor( this, block );
		}

		internal void InternalTextChanged() => OnTextChanged( PlainText );

		//Called when text changed.
		protected virtual void OnTextChanged( string value )
		{
			TextChanged?.Invoke( value );
		}

		protected override void OnFocus( FocusChangeReason reason )
		{
			base.OnFocus( reason );

			OnEditingStarted?.Invoke();
		}

		protected override void OnBlur( FocusChangeReason reason )
		{
			base.OnBlur( reason );

			OnEditingFinished?.Invoke();
		}
	}
}
