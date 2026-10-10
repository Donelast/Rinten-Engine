using Native;
using System;

namespace Editor
{
	/// <summary>A simple text label.</summary>
	public partial class Label : Widget
	{

		/// <summary>The text of this label.</summary>
		public string Text
		{
			get => text;
			set
			{
				if ( text == value ) return;

				text = value;

				//A label is as big as its words unless something else has already decided; a layout asks for this next time round.
				AdjustSize();
				Update();
			}
		}

		private string text = string.Empty;
		private bool wordWrap;
		private TextFlag alignment = TextFlag.LeftCenter;
		private float indent;
		private float labelMargin;

		public bool WordWrap
		{
			get => wordWrap;
			set
			{
				wordWrap = value;
				AdjustSize();
				Update();
			}
		}

		/// <summary>How big this label's letters are, in pixels. The editor's own size unless something says otherwise.</summary>
		public float TextSize
		{
			get => textSize;
			set
			{
				if ( textSize == value ) return;

				textSize = value;
				AdjustSize();
				Update();
			}
		}

		private float textSize = EditorText.Size;

		/// <summary>Measure height at the current width so wrapped text fits; off by default to avoid layout fights</summary>
		public bool WrapsToWidth { get; set; }

		public bool TextSelectable
		{
			get => false;
			set
			{
				//Selecting the text of a label is not there yet; a label that asks for it simply cannot be selected rather than throwing.
				return;
			}
		}

		/// <summary>If true, clicking a html link on this label will open the website. This is true by default.</summary>
		public bool OpenExternalLinks
		{
			get => openExternalLinks;
			set
			{
				if ( openExternalLinks == value ) return;

				openExternalLinks = value;
				Update();
			}
		}

		private bool openExternalLinks = true;

		public float Indent
		{
			get => indent;
			set
			{
				indent = value;
				Update();
			}
		}

		public float Margin
		{
			get => labelMargin;
			set
			{
				labelMargin = value;
				AdjustSize();
				Update();
			}
		}

		Color _color = new Color( 0.6f, 0.6f, 0.6f );

		public Color Color
		{
			get => _color;
			set
			{
				if ( _color == value ) return;

				_color = value;
				SetStyles( $"color: {Color.Hex};" );
			}
		}

		public TextFlag Alignment
		{
			get => alignment;
			set
			{
				alignment = value;
				Update();
			}
		}

		//The words, in the label's own colour, where the label's own class says they go.
		protected override void OnPaint()
		{
			if ( !Paint.Snapshot ) return;

			if ( string.IsNullOrEmpty( Text ) ) return;

			Paint.SetDefaultFont( TextSize, sizeInPixels: true );
			Paint.SetPen( Enabled ? Color : Color.WithAlpha( 0.5f ) );

			var rect = LocalRect.Shrink( labelMargin + indent, labelMargin, labelMargin, labelMargin );

			Paint.DrawText( rect, VisibleText, WordWrap ? Alignment : Alignment | TextFlag.SingleLine );
		}

		protected override Vector2 SizeHint()
		{
			//What a reader sees, not what the string says: a label measured with its own markup in it asks for room for
			//characters it never draws, and an error whose code is wrapped in a link asked for twice the width of the message.
			var measured = WrapWidth() is { } column
				? EditorText.MeasureWrapped( VisibleText, column, TextSize )
				: EditorText.Measure( VisibleText, TextSize );

			var height = measured.y + (labelMargin * 2);

			//As tall as its words and no taller, where it is a label that wraps to the room it has. The row-height floor is there so a label lines
			//up with the field beside it; a paragraph has no field beside it, and the floor is then a band of nothing under every line of it.
			if ( !WrapsToWidth ) height = MathF.Max( height, Theme.RowHeight );

			return new Vector2( measured.x + indent + (labelMargin * 2), height );
		}

		//The column this label's words are laid into, where it is one that wraps to the room it has and has been given some.
		private float? WrapWidth()
		{
			if ( !WrapsToWidth || !WordWrap ) return null;

			var column = Size.x - indent - (labelMargin * 2);

			return column > 1.0f ? column : null;
		}

		public Label( Widget parent = null ) : this( null, parent )
		{
		}

		public Label( string text, Widget parent = null ) : base()
		{
			MakeSurface( parent );

			Indent = 0;
			Text = text ?? string.Empty;
		}

		public class Title : Label
		{
			public Title( Widget parent = null ) : this( null, parent ) { }

			public Title( string title, Widget parent = null ) : base( title, parent )
			{
				SetProperty( "type", "title" );
				WordWrap = true;
			}
		}

		public class Subtitle : Label
		{
			public Subtitle( Widget parent = null ) : this( null, parent ) { }

			public Subtitle( string title, Widget parent = null ) : base( title, parent )
			{
				SetProperty( "type", "subtitle" );
				WordWrap = true;
			}
		}

		public class Small : Label
		{
			public Small( Widget parent = null ) : this( null, parent ) { }

			public Small( string title, Widget parent = null ) : base( title, parent )
			{
				SetProperty( "type", "small" );
				WordWrap = true;
			}
		}

		public class Body : Label
		{
			public Body( Widget parent = null ) : this( null, parent ) { }

			public Body( string title, Widget parent = null ) : base( title, parent )
			{
				SetProperty( "type", "body" );
				WordWrap = true;
			}
		}

		public class Header : Label
		{
			public Header( Widget parent = null ) : this( null, parent ) { }

			public Header( string title, Widget parent = null ) : base( title, parent )
			{
				SetStyles( "font-weight: bold;" );
				FixedHeight = Theme.RowHeight;
				WordWrap = true;
			}
		}
	}
}
