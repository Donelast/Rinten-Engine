using Microsoft.AspNetCore.Components;
using Rinten.Html;
using Rinten.Rendering;
using System.Globalization;

namespace Rinten.UI
{
	/// <summary>A generic text label. Can be made editable.</summary>
	[Library( "label" ), Alias( "text" ), Expose]
	public partial class Label : Panel
	{
		//Information about the Text on a per-element scale. It handles multi-character Unicode units (graphemes) correctly.
		protected StringInfo StringInfo = new();

		internal string _textToken;
		internal string _text;
		internal Rect _textRect;
		internal TextBlock _textBlock;

		int layoutStateHash;
		bool sizeFinalized;
		Vector2 availableSpace;

		//The selection as it was set before the first layout, when there is no text block yet to hold it - handed to the block the moment it is made. Dropped
		//instead, a field that selects its text as it opens (a rename, a prompt) never had it selected, and what was typed went in front of the old name.
		int earlySelectionStart;
		int earlySelectionEnd;
		bool earlyDrawSelection;
		Color? earlySelectionColor;

		[Category( "Selection" )]
		public bool ShouldDrawSelection
		{
			get => _textBlock?.ShouldDrawSelection ?? earlyDrawSelection;
			set
			{
				//Compared as one value: read as (drawn == Selectable) && value, an unfocused field re-laid out every frame.
				var draw = Selectable && value;

				if ( _textBlock is null )
				{
					earlyDrawSelection = draw;
					return;
				}

				if ( _textBlock.ShouldDrawSelection == draw )
					return;

				_textBlock.ShouldDrawSelection = draw;
				SetNeedsPreLayout();
			}
		}

		/// <summary>Can be selected</summary>
		[Category( "Selection" )]
		public bool Selectable { get; set; } = true;

		/// <summary>If true and the text starts with #, it will be treated as a language token.</summary>
		public bool Tokenize { get; set; } = true;

		[Hide]
		public int SelectionStart
		{
			get => _textBlock?.SelectionStart ?? earlySelectionStart;
			set
			{
				if ( _textBlock == null ) { earlySelectionStart = value; return; }
				if ( _textBlock.SelectionStart == value ) return;

				_textBlock.SelectionStart = value;
				SetNeedsPreLayout();
			}
		}

		[Hide]
		public int SelectionEnd
		{
			get => _textBlock?.SelectionEnd ?? earlySelectionEnd;
			set
			{
				if ( _textBlock == null ) { earlySelectionEnd = value; return; }
				if ( _textBlock.SelectionEnd == value ) return;

				_textBlock.SelectionEnd = value;
				SetNeedsPreLayout();
			}
		}

		/// <summary>The color used for text selection highlight</summary>
		[Category( "Selection" )]
		public Color SelectionColor
		{
			get => _textBlock?.SelectionColor ?? earlySelectionColor ?? Color.Cyan.WithAlpha( 0.39f );
			set
			{
				if ( _textBlock == null ) { earlySelectionColor = value; return; }
				if ( _textBlock.SelectionColor == value ) return;

				_textBlock.SelectionColor = value;
			}
		}

		public Label()
		{
			AddClass( "label" );
			LayoutTree.SetMeasureFunction( MeasureText );
			LayoutTree.SetBaselineFunction( TextBaseline );
		}

		public Label( string text, string classname = null ) : this()
		{
			Text = text;
			AddClass( classname );
		}

		Vector2 MeasureText( float width, Rinten.Layout.MeasureMode widthMode, float height, Rinten.Layout.MeasureMode heightMode )
		{
			try
			{
				if ( _textBlock == null ) return new Vector2( 2, 10 );

				availableSpace = new Vector2( width, height );

				//Measure the full text, not the truncated size, or a label truncated once would stay narrow
				return _textBlock.Measure( width, height );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, "label measure" );
				return new Vector2( 2, 10 );
			}
		}

		//The first line's baseline, so a row that aligns on baselines lines the text up rather than the boxes.
		float TextBaseline( float width, float height )
		{
			try
			{
				if ( _textBlock == null ) return height;
				return _textBlock.FirstBaseline( width, height );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, "label baseline" );
				return height;
			}
		}

		public override void OnDeleted()
		{
			base.OnDeleted();

			_textBlock?.Dispose();
			_textBlock = null;
		}

		/// <summary>Text to display on the label.</summary>
		[Parameter]
		public virtual string Text
		{
			get => _text;
			set
			{
				value ??= "";

				if ( Tokenize && value != null && value.Length > 1 && value[0] == '#' )
				{
					if ( _textToken == value ) return;
					_textToken = value;

					value = Language.GetPhrase( _textToken[1..] );
				}

				if ( _text == value )
					return;

				_text = value;
				StringInfo.String = value ?? string.Empty;
				CaretSantity();
				SetNeedsPreLayout();
			}
		}

		/// <summary>Set to true if this is rich text. This means it can support some inline html elements.</summary>
		[Parameter]
		public bool IsRich
		{
			get => _isRich;
			set
			{
				if ( _isRich == value ) return;

				_isRich = value;

				//The block is handed the text in PreLayout, and which of the two ways it is handed over is decided
				//there too. Text set before this was turned on is text already given to the block as words.
				SetNeedsPreLayout();
			}
		}

		bool _isRich;

		public override void SetProperty( string name, string value )
		{
			if ( name == "text" )
			{
				Text = value;
				return;
			}

			if ( name == "selectable" )
			{
				//Selectable = value.ToBool();
				return;
			}

			base.SetProperty( name, value );
		}

		public override void SetContent( string value )
		{
			//alex: This value gets trimmed inside TextBlock based on the WhiteSpace style value for this label
			Text = value ?? "";
		}

		/// <summary>Position of the text cursor/caret within the text, at which newly typed characters are inserted.</summary>
		public int CaretPosition { get; set; }

		/// <summary>Amount of characters in the text of the text entry. Not bytes.</summary>
		public int TextLength => StringInfo.LengthInTextElements;

		//Ensure the text caret and selection are in sane positions, that is, not outside of the text bounds.
		protected void CaretSantity()
		{
			//Nothing to clamp on a label nobody is editing, and counting text elements walks
			//the whole string - every text change on every label paid it.
			if ( CaretPosition == 0 && SelectionStart == 0 && SelectionEnd == 0 )
				return;

			if ( CaretPosition > TextLength )
			{
				CaretPosition = TextLength;
				ScrollToCaret();
			}
			if ( SelectionStart > TextLength )
			{
				SelectionStart = TextLength;
				ScrollToCaret();
			}
			if ( SelectionEnd > TextLength )
			{
				SelectionEnd = TextLength;
				ScrollToCaret();
			}
		}

		/// <summary>Returns the selected text.</summary>
		public string GetSelectedText()
		{
			if ( TextLength == 0 ) return "";
			if ( !HasSelection() ) return "";

			CaretSantity();

			var s = Math.Min( SelectionStart, SelectionEnd );
			var e = Math.Max( SelectionStart, SelectionEnd );

			return StringInfo.SubstringByTextElements( s, e - s );
		}

		public override string GetClipboardValue( bool cut )
		{
			if ( !HasSelection() )
				return null;

			var txt = GetSelectedText();

			return txt;
		}

		public Rect GetCaretRect( int i )
		{
			var rect = _textBlock.CaretRect( i );
			rect.Position += _textRect.Position - caretScroll;
			rect.Width = 2;

			return rect;
		}

		//What was last handed to the text block, to notice when it goes stale.
		string _drawnText;

		//The text this label should be drawing: the content style where one is set, otherwise Text.
		string ResolveText()
		{
			var styleContent = ComputedStyle?.Content;

			if ( styleContent is not null && styleContent.Length > 1 && styleContent[0] == '#' )
			{
				styleContent = Language.GetPhrase( styleContent[1..] );
			}

			return styleContent ?? Text ?? string.Empty;
		}

		/// <summary>Ask for another pass where the style now says to draw something else.</summary>
		public override void Tick()
		{
			base.Tick();

			if ( _textBlock is not null && ResolveText() != _drawnText )
				SetNeedsPreLayout();
		}

		//Everything that can move the text asks for a tick already (a content style through PanelStyle.Set,
		//a rules change), so this Tick runs when asked rather than for every label every frame.
		protected override bool TicksEveryFrameOfItsOwn => false;

		internal override void PreLayout( LayoutCascade cascade )
		{
			base.PreLayout( cascade );

			var text = ResolveText();

			_drawnText = text;

			if ( _textBlock is null )
			{
				_textBlock = new TextBlock();
				_textBlock.LookupStyles = HtmlStyleLookup;
				_textBlock.OnTextureChanged = MarkRenderDirty;

				//Whatever was selected before there was a block to hold it.
				_textBlock.SelectionStart = earlySelectionStart;
				_textBlock.SelectionEnd = earlySelectionEnd;
				_textBlock.ShouldDrawSelection = earlyDrawSelection;
				if ( earlySelectionColor is { } selectionColor ) _textBlock.SelectionColor = selectionColor;
			}

			_textBlock.NoWrap = !Multiline;
			_textBlock.Mipped = FindRootPanel() is { IsWorldPanel: true };

			if ( IsRich )
			{
				_textBlock.SetHtml( text );
				_textBlock.NoWrap = false;
			}
			else
			{
				_textBlock.SetText( text );
			}

			int newStateHash = HashCode.Combine( (int)(availableSpace.x * 100), ScaleToScreen, _textBlock.IsTruncated, hoveredNode );

			if ( newStateHash != layoutStateHash )
			{
				layoutStateHash = newStateHash;
				sizeFinalized = false;
			}

			if ( _textBlock.UpdateStyles( ComputedStyle ) )
			{
				LayoutTree.MarkDirty();
				sizeFinalized = false;
			}
		}

		private Styles HtmlStyleLookup( INode node )
		{
			//Seed with the label's own computed styles so inherited properties are present.
			//ComputedStyle is already in screen units, so it should never be rescaled.
			var s = new Styles();
			s.Add( ComputedStyle );

			//Accumulate stylesheet + inline styles in logical units, scale once, then merge
			var local = new Styles();

			var blocks = AllStyleSheets
				.SelectMany( x => x.Nodes )
				.Select( x => x.Test( node ) )
				.Where( x => x is not null )
				.ToList();

			if ( blocks.Count > 0 )
			{
				blocks.Sort( StyleOrderer.Instance );

				foreach ( var entry in blocks )
					local.Add( entry.Block.Styles );
			}

			//Inline styles applied last, highest specificity wins
			if ( node.GetAttribute( "style", null ) is string styles )
			{
				var p = new Parse( styles );
				StyleParser.ParseStyles( ref p, local );
			}

			local.ApplyScale( FindRootPanel().ScaleToScreen );
			s.Add( local );

			return s;
		}

		public override void FinalLayout( Vector2 offset )
		{
			base.FinalLayout( offset );

			if ( !IsVisible ) return;
			if ( ComputedStyle is null ) return;

			_textBlock?.SizeFinalized( Box.RectInner.Width, Box.RectInner.Height );

			if ( !sizeFinalized )
			{
				sizeFinalized = true;
				LayoutTree.MarkDirty();
			}

			_textRect = Box.RectInner;

			if ( ComputedStyle.TextAlign == TextAlign.Center )
			{
				_textRect.Left += (_textRect.Width - _textBlock.BlockSize.x) * 0.5f;
			}
			else if ( ComputedStyle.TextAlign == TextAlign.Right )
			{
				_textRect.Left = _textRect.Right - _textBlock.BlockSize.x;
			}

			if ( ComputedStyle.AlignItems == Align.Center )
			{
				_textRect.Top += (_textRect.Height - _textBlock.BlockSize.y) * 0.5f;
			}
			else if ( ComputedStyle.AlignItems == Align.FlexEnd )
			{
				_textRect.Top = _textRect.Bottom - _textBlock.BlockSize.y;
			}

			_textRect.Size = _textBlock.BlockSize;

			SettleCaretView();
			ScrollParentToCaret();
		}

		//Keeps the view on the caret now that the text has been measured. And keeps it against the left edge when the text no longer
		//fills the field: deleting from the end of a long value would otherwise leave the view where it was and the field looking empty.
		void SettleCaretView()
		{
			if ( _textBlock is null ) return;

			if ( caretNeedsView && !Multiline )
			{
				caretNeedsView = false;
				_textBlock.ScrollToCaret( CaretPosition, ref caretScroll, Box.RectInner.Size );
			}

			//Past the last letter by what the caret standing after it needs, so the end of the text is not also as far as the view may go.
			var slack = _textBlock.BlockSize - Box.RectInner.Size;

			var was = caretScroll;
			caretScroll.x = caretScroll.x.Clamp( 0.0f, MathF.Max( slack.x + TextBlock.CaretMargin, 0.0f ) );
			caretScroll.y = caretScroll.y.Clamp( 0.0f, MathF.Max( slack.y, 0.0f ) );

			//The text is drawn from another offset now; nothing else in layout says so.
			if ( caretScroll != was )
				MarkRenderDirty();
		}

		//The editor walk draws this itself.
		internal override bool DrawnByWalk => true;

		public override void OnDraw()
		{
			//Ensure texture is created if we have text but no texture yet
			if ( _textBlock != null && _textBlock.Texture == null && !string.IsNullOrEmpty( _textBlock.Text ) )
			{
				_textBlock.SizeFinalized( Box.RectInner.Width, Box.RectInner.Height );
			}

			var rect = Box.RectInner;
			rect.Position -= caretScroll;
			_textBlock?.BuildDescriptors( CachedDescriptors, CachedOverrideBlendMode, ComputedStyle, rect, CachedRenderOpacity );

			DrawHostedCaret();
		}

		void DrawHostedCaret()
		{
			if ( !TextCaretDraw.TryGet( this, out var index, out var color ) )
				return;

			var caret = TextCaretDraw.GetRect( this, index );
			var blend = CachedOverrideBlendMode == BlendMode.Normal
				? BlendMode.PremultipliedAlpha
				: CachedOverrideBlendMode;

			CachedDescriptors.AddBox( new BoxDrawDescriptor( caret, color )
			{
				OverrideBlendMode = blend,
			} );

			MarkRenderDirty();
		}

		public int GetLetterAt( Vector2 pos )
		{
			if ( _textBlock == null ) return -1;

			return _textBlock.GetLetterAt( pos );
		}

		public int GetLetterAtScreenPosition( Vector2 pos ) => GetLetterAt( ScreenPositionToTextRectPosition( pos ) );

		/// <summary>Letter index at a position in this label's panel space.</summary>
		public int GetLetterAtPanelPosition( Vector2 panelLocalPos )
		{
			var margin = _textBlock?.TextureMargin ?? Vector2.Zero;
			var textOrigin = new Vector2( _textRect.Left - Box.Rect.Left, _textRect.Top - Box.Rect.Top );
			var x = panelLocalPos.x - textOrigin.x + margin.x;
			var y = panelLocalPos.y - textOrigin.y + margin.y;

			//Where that is in the text rather than in the field: asked without the scroll, a click on
			//the left of a field showing the end of a long value answered with the first letter of it.
			var at = new Vector2( x, y ) + caretScroll;

			if ( _textBlock is not null )
			{
				if ( at.x < -1f )
					return 0;

				if ( at.x >= _textBlock.BlockSize.x )
					return TextLength;
			}

			return GetLetterAt( at );
		}

		/// <summary>Letter index from the root pointer position when the click happened.</summary>
		public int GetLetterAtPointer( Vector2 rootPointer ) =>
			GetLetterAtPanelPosition( ScreenPositionToPanelPosition( rootPointer ) );

		Vector2 ScreenPositionToTextRectPosition( Vector2 pos )
		{
			if ( GlobalMatrix.HasValue )
			{
				pos = GlobalMatrix.Value.Transform( pos );
			}

			var x = pos.x - _textRect.Left;
			var y = pos.y - _textRect.Top;

			return new Vector2( x, y ) + caretScroll;
		}

		public bool HasSelection() => ShouldDrawSelection && SelectionStart != SelectionEnd;

		//Whether the text has a rendered picture; a laid-out visible label without one won't show
		internal bool HasPicture => _textBlock?.Texture is not null;

		/// <summary>When the language changes, if we're token based we need to update to the new phrase.</summary>
		public override void LanguageChanged()
		{
			if ( _textToken == null ) return;
			if ( !Tokenize ) return;

			var token = _textToken;
			_textToken = null; //skip cache
			Text = token;
		}

		INode hoveredNode;

		protected override void OnMouseMove( MousePanelEvent e )
		{
			base.OnMouseMove( e );

			if ( _textBlock is null || !IsRich )
			{
				hoveredNode = default;
				return;
			}

			var hov = _textBlock.GetSpanAt( e.LocalPosition )?.node;
			if ( hov == hoveredNode ) return;

			if ( hoveredNode is not null )
			{
				hoveredNode.SetPseudoClass( PseudoClass.None );
			}

			hoveredNode = hov;

			if ( hoveredNode is not null )
			{
				hoveredNode.SetPseudoClass( PseudoClass.Hover );
			}

			Style.Cursor = (hoveredNode?.Name == "a") ? "pointer" : null;
			_textBlock.Dirty();
			SetNeedsPreLayout();
		}

		/// <summary>Called when a node within rich text (<see cref="IsRich"/>) is clicked, with the clicked node. When set, this replaces the default behaviour - which opens a valid http/https <c>href</c> on an anchor in the user's browser - letting you inspect the node and handle custom anchor schemes or open in-game popups.</summary>
		[Parameter]
		public Action<INode> OnNodeClicked { get; set; }

		protected override void OnClick( MousePanelEvent e )
		{
			base.OnClick( e );

			if ( hoveredNode is null )
				return;

			if ( OnNodeClicked is not null )
			{
				OnNodeClicked.Invoke( hoveredNode );
				return;
			}

			if ( hoveredNode.GetAttribute( "href", null ) is not { } url )
				return;

			bool isValid = Uri.TryCreate( url, UriKind.Absolute, out var parsedUri ) && (parsedUri.Scheme == "http" || parsedUri.Scheme == "https");

			if ( !isValid )
			{
				Log.Warning( $"Blocked URL: {url}" );
				return;
			}

			//Modal popup, are you sure etc?

			System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo()
			{
				FileName = parsedUri.ToString(),
				UseShellExecute = true,
				Verb = "open"
			} );
		}
	}

	namespace Construct
	{
		public static class LabelConstructor
		{
			/// <summary>Create a simple text label with given text and CSS classname.</summary>
			public static Label Label( this PanelCreator self, string text = null, string classname = null )
			{
				var control = self.panel.AddChild<Label>();

				if ( text != null )
					control.Text = text;

				if ( classname != null )
					control.AddClass( classname );

				return control;
			}
		}
	}
}
