using Rinten.UI;

namespace Editor;

partial class Label
{
	private Rinten.UI.Label contentLabel;

	private protected override void OnSurfaceCreated()
	{
		base.OnSurfaceCreated();


		EnsureContentLabel();
		SyncPanelLabel();
	}

	public override void SyncSurfaceContent()
	{

		EnsureContentLabel();
		SyncPanelLabel();
	}

	private void EnsureContentLabel()
	{
		if ( contentLabel is { IsValid: true } ) return;

		contentLabel = new Rinten.UI.Label
		{
			Tokenize = false,
		};

		contentLabel.AddClass( "widget-label-content" );
		Surface.AddChild( contentLabel );

		Surface.AddClass( "widget-label" );
		Surface.SetClass( "hidden", false );
	}

	private void SyncPanelLabel()
	{
		if ( contentLabel is null || !contentLabel.IsValid() || Surface is null ) return;

		//Render as rich text when the text looks like HTML, e.g. compile error links
		contentLabel.IsRich = MightBeRichText( Text );
		contentLabel.OnNodeClicked = OpenExternalLinks ? null : (System.Action<Rinten.Html.INode>)SwallowLink;

		contentLabel.Text = Text;
		contentLabel.Multiline = WordWrap;

		//Pixels, and the same number the label is measured with. It used to be Length.Pixels( SizeInPoints ) - a size in points
		//handed over as a size in pixels - so every label in the editor was drawn at nine pixels inside a box measured for twelve.
		contentLabel.Style.FontSize = Length.Pixels( TextSize );

		var colour = Enabled ? Color : Color.WithAlpha( 0.5f );
		contentLabel.Style.FontColor = colour;

		var pad = labelMargin;
		contentLabel.Style.PaddingLeft = indent + pad;
		contentLabel.Style.PaddingRight = pad;
		contentLabel.Style.PaddingTop = pad;
		contentLabel.Style.PaddingBottom = pad;

		ApplyAlignment( Surface, contentLabel, WordWrap ? Alignment : Alignment | TextFlag.SingleLine );

		Surface.SetClass( "disabled", !Enabled );
	}

	//The words of this label without the markup around them, where there is any: what a reader sees, and so what the label is measured by.
	private string VisibleText => MightBeRichText( Text ) ? WithoutTags( Text ) : Text;

	//Everything outside a <...>.
	private static string WithoutTags( string html )
	{
		var words = new System.Text.StringBuilder( html.Length );
		var inside = false;

		foreach ( var c in html )
		{
			if ( c == '<' ) inside = true;
			else if ( c == '>' ) inside = false;
			else if ( !inside ) words.Append( c );
		}

		return words.ToString();
	}

	//A link the label is not allowed to follow.
	private static void SwallowLink( Rinten.Html.INode node )
	{
	}

	//Whether a string is markup rather than words. A closing tag, which is what markup has and prose does not. Deliberately
	//not "contains a <": half the messages the compiler hands this label name a generic type, and List<int> is not an element.
	private static bool MightBeRichText( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return false;

		var closing = text.IndexOf( "</", System.StringComparison.Ordinal );

		return closing >= 0 && text.IndexOf( '>', closing ) > closing + 1;
	}

	private static void ApplyAlignment( Panel surface, Rinten.UI.Label label, TextFlag flags )
	{
		var horizontal = TextAlign.Left;

		if ( flags.HasFlag( TextFlag.CenterHorizontally ) )
			horizontal = TextAlign.Center;
		else if ( flags.HasFlag( TextFlag.Right ) )
			horizontal = TextAlign.Right;

		label.Style.TextAlign = horizontal;

		if ( flags.HasFlag( TextFlag.Top ) )
			surface.Style.AlignItems = Align.FlexStart;
		else if ( flags.HasFlag( TextFlag.Bottom ) )
			surface.Style.AlignItems = Align.FlexEnd;
		else
			surface.Style.AlignItems = Align.Center;
	}
}
