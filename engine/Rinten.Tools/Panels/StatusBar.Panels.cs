using Rinten;

namespace Editor;

/// <summary>The strip along the bottom of a window.</summary>
public partial class StatusBar
{
	private readonly List<Widget> left = new();
	private readonly List<Widget> right = new();

	//How much of the spare room each widget takes. The console's newest line asks for it and fills the bar
	//up to the counts; without it the line sat at its own width and the rest of the bar was an empty spacer.
	private readonly Dictionary<Widget, int> stretches = new();

	private string message;

	//Which message is showing, so the one taken down is the one that ran out.
	private int messageShown;

	private void AddManaged( Widget widget, List<Widget> into, int stretch )
	{
		if ( widget is null || into.Contains( widget ) ) return;

		into.Add( widget );
		stretches[widget] = stretch;

		widget.SetPanelParent( this );

		Rebuild();
	}

	private void RemoveManaged( Widget widget )
	{
		if ( widget is null ) return;

		if ( !left.Remove( widget ) && !right.Remove( widget ) ) return;

		stretches.Remove( widget );

		widget.SetPanelParent( null );

		Rebuild();
	}

	//Says something for a while, then stops saying it.
	private void ShowMessageManaged( string text, float seconds )
	{
		message = text;

		var shown = ++messageShown;

		Update();

		//Taken down when it runs out, by a call made for that - not by asking once a frame, for the life of the editor, whether it had.
		_ = TakeDownAfter( shown, seconds );
	}

	private async Task TakeDownAfter( int shown, float seconds )
	{
		await Task.Delay( System.TimeSpan.FromSeconds( System.MathF.Max( seconds, 0.0f ) ) );

		//Back on the main thread whatever the delay resumed on: a widget is only touched there.
		MainThread.Queue( () =>
		{
			if ( shown != messageShown || !IsValid ) return;

			message = null;
			Update();
		} );
	}

	//Everything in the bar, in a row: the message first, then whatever is on the left, then the room between, then whatever is on the right.
	private void Rebuild()
	{
		Layout ??= Layout.Row();

		Layout.Clear( false );
		Layout.Margin = new Rinten.UI.Margin( 6, 2, 6, 2 );
		Layout.Spacing = 6;

		foreach ( var widget in left ) Layout.Add( widget, stretches.GetValueOrDefault( widget ) );

		Layout.AddStretchCell();

		foreach ( var widget in right ) Layout.Add( widget, stretches.GetValueOrDefault( widget ) );

		MarkNeedsArrange();
	}

	protected override void OnPaint()
	{
		if ( !OnPanels ) { base.OnPaint(); return; }

		//A hairline rather than a border: the bar is told apart from the window by one pixel of white, the way
		//two panels are told apart by one pixel of the layer below. The fill is the stylesheet's (.statusbar).
		Paint.ClearBrush();
		Paint.SetPen( Theme.Edge );
		Paint.DrawLine( LocalRect.TopLeft, LocalRect.TopRight );

		if ( string.IsNullOrEmpty( message ) ) return;

		Paint.SetDefaultFont();
		Paint.SetPen( Theme.InkDim );
		Paint.DrawText( LocalRect.Shrink( 8, 0 ), message, TextFlag.LeftCenter );
	}
}
