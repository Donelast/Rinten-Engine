using Rinten.UI;
using System;
using System.Text;

namespace Editor;

//The visible log lines, as pooled rows reused while scrolling
internal sealed class ConsoleListPanel : PooledRowList<ConsoleRowPanel>, IWantsRawKeys
{
	/// <summary>How tall a line is, in layout units.</summary>
	public const float LineHeight = 17.0f;

	//Which version of the log the rows were last written from.
	private int version = -1;

	//Line-based selection range (start, current); -1 when nothing is picked
	private int pickedFrom = -1;
	private int pickedTo = -1;

	//Whether the pointer is down and picking lines out.
	private bool picking;

	//The line last clicked on, which is what the half beside this one shows.
	private int current = -1;

	/// <summary>A line was clicked, by its place in the log, or minus one where the line that was being looked at is no longer listed.</summary>
	public Action<int> Chosen { get; set; }

	public ConsoleListPanel()
	{
		AddClass( "console-list" );

		AcceptsFocus = true;

		//Follows the newest line, unless somebody has scrolled up to read - in which case where they are reading is where they stay.
		PreferScrollToBottom = true;

		ConsoleLog.Trimmed += OnTrimmed;
		ConsoleLog.Relisted += OnRelisted;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		ConsoleLog.Trimmed -= OnTrimmed;
		ConsoleLog.Relisted -= OnRelisted;
	}

	protected override float RowHeight => LineHeight;

	protected override int CellCount => ConsoleLog.Count;

	protected override ConsoleRowPanel CreateCell() => new()
	{
		Pressed = OnRowPressed,
		Entered = OnRowEntered,
	};

	protected override void BindCell( ConsoleRowPanel cell, int index )
	{
		cell.Index = index;
		cell.Bind( ConsoleLog.Line( index ) );
		cell.SetMarks( IsPicked( index ), index == current );
	}

	protected override void RefreshCell( ConsoleRowPanel cell, int index ) =>
		cell.SetMarks( IsPicked( index ), index == current );

	protected override void UnbindCell( ConsoleRowPanel cell ) => cell.Unbind();

	public override void Tick()
	{
		//What is listed may have moved under the rows - a line arrived, a level was hidden, the
		//top fell off - and every live row is then over a different line than the one it says.
		if ( version != ConsoleLog.Version )
		{
			version = ConsoleLog.Version;

			Invalidate();
		}

		//A release that landed somewhere else - another dock, outside the window - never
		//reaches this list, and a pick that never ends follows the pointer around afterwards.
		if ( picking && !HasActive ) picking = false;

		base.Tick();
	}

	/// <summary>Puts the newest line back in view.</summary>
	public void ScrollToBottom()
	{
		ScrollOffset = new Vector2( ScrollOffset.x, ScrollSize.y );
	}

	/// <summary>Picks out every line there is.</summary>
	public void SelectAll()
	{
		if ( ConsoleLog.Count == 0 ) return;

		pickedFrom = 0;
		pickedTo = ConsoleLog.Count - 1;

		Refresh();
	}

	/// <summary>Puts what is picked out on the clipboard.</summary>
	public void CopyPicked()
	{
		var text = PickedText();

		if ( !string.IsNullOrEmpty( text ) ) EditorUtility.Clipboard.Copy( text );
	}

	//The lines picked out, low to high, or nothing.
	private (int First, int Last) Picked => pickedFrom < 0 || pickedTo < 0
		? (-1, -1)
		: (Math.Min( pickedFrom, pickedTo ), Math.Max( pickedFrom, pickedTo ));

	private bool IsPicked( int index )
	{
		var picked = Picked;

		return picked.First >= 0 && index >= picked.First && index <= picked.Last;
	}

	//The lines listed are different ones now - a level was hidden, the log was emptied - so what was picked out is a run of lines that is no longer there.
	private void OnRelisted()
	{
		pickedFrom = pickedTo = current = -1;
		picking = false;

		Chosen?.Invoke( -1 );
	}

	//Lines fell off the top, so everything counted from the top moved.
	private void OnTrimmed( int gone )
	{
		pickedFrom = Moved( pickedFrom, gone );
		pickedTo = Moved( pickedTo, gone );

		var was = current;

		current = Moved( current, gone );

		//The line being read fell off the top: there is nothing to show beside the log any more, rather than a different line where that one was.
		if ( was >= 0 && current < 0 ) Chosen?.Invoke( -1 );
	}

	private static int Moved( int index, int gone ) => index < 0 ? -1 : Math.Max( -1, index - gone );

	private void OnRowPressed( ConsoleRowPanel row )
	{
		Focus();

		//The source label is its own click, not the start of a pick: it names a file and a line the way
		//a hyperlink names a page, and dragging a selection over it is not what anyone wants from it.
		if ( row.IsOverSource && row.Index >= 0 && row.Index < ConsoleLog.Count )
		{
			var line = ConsoleLog.Line( row.Index );

			if ( line.SourceFile is { } file )
			{
				CodeEditor.OpenFile( file, line.SourceLine );
				return;
			}
		}

		//Where the pick starts. Held rather than finished: which lines it covers is not known until the pointer stops moving or comes up.
		pickedFrom = pickedTo = current = row.Index;
		picking = row.Index >= 0;

		Refresh();

		Chosen?.Invoke( current );
	}

	private void OnRowEntered( ConsoleRowPanel row )
	{
		if ( !picking || row.Index < 0 || row.Index == pickedTo ) return;

		pickedTo = row.Index;

		Refresh();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		picking = false;
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		OpenMenu();
	}

	//Right-click menu, duplicating Ctrl+A/Ctrl+C for when the panel lacks focus
	private void OpenMenu()
	{
		var menu = new Menu();

		menu.AddOption( "Select All", "select_all", SelectAll, "Ctrl+A" );
		menu.AddOption( "Copy", "content_copy", CopyPicked, "Ctrl+C" ).Enabled = Picked.First >= 0;
		menu.AddSeparator();
		menu.AddOption( "Copy Everything", "content_paste", () =>
		{
			SelectAll();
			CopyPicked();
		} );

		menu.OpenAtCursor( this );
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( !e.Pressed || !e.HasCtrl )
		{
			base.OnButtonTyped( e );
			return;
		}

		switch ( e.Button )
		{
			case "a":
				SelectAll();
				e.StopPropagation = true;
				return;

			case "c":
				CopyPicked();
				e.StopPropagation = true;
				return;
		}

		base.OnButtonTyped( e );
	}

	/// <summary>What ctrl+C takes, where it arrives as the panel system's own copy.</summary>
	public override string GetClipboardValue( bool cut ) => PickedText();

	//The lines picked out, as text somebody can paste somewhere.
	private string PickedText()
	{
		var picked = Picked;

		if ( picked.First < 0 ) return string.Empty;

		var text = new StringBuilder();

		for ( var at = picked.First; at <= picked.Last && at < ConsoleLog.Count; ++at )
		{
			var e = ConsoleLog.Line( at ).Event;

			//The time, who said it, and what was said - the logger is worth having here even though the line on screen no longer wears it.
			text.Append( e.Time.ToString( "HH:mm:ss" ) );
			text.Append( ' ' );

			if ( !string.IsNullOrEmpty( e.Logger ) && e.Logger != ConsoleLog.CommandLogger && e.Logger != ConsoleLog.DefaultLogger )
			{
				text.Append( e.Logger );
				text.Append( ' ' );
			}

			text.AppendLine( e.Message );

			//Copy stacks only for warnings and errors; info stacks are just frame-loop noise
			if ( e.Level >= LogLevel.Warn && !string.IsNullOrEmpty( e.Stack ) ) text.AppendLine( e.Stack );
		}

		return text.ToString();
	}
}
