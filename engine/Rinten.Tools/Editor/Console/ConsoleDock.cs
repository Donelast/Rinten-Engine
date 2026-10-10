namespace Editor;

//Console dock hosting the panels; the log lives in static ConsoleLog since the dock is recreated
[Dock( "Editor", "Console", "text_snippet", DockArea.AutoHideBottom )]
internal class ConsoleDock : PanelDockHost
{
	internal static ConsoleDock Instance { get; private set; }

	private ConsolePanel console;
	private ConsoleStatusBar statusBar;

	public ConsoleDock( Widget parent ) : base( parent )
	{
		Instance = this;

		DeleteOnClose = true;

		WindowTitle = "Console";
		SetWindowIcon( "text_snippet" );
		Name = "Console";
		Size = new Vector2( 500, 500 );
		MinimumSize = new Vector2( 100, 100 );

		console = new ConsolePanel();

		SetContent( console );

		statusBar = new ConsoleStatusBar();

		EditorUtility.AddLogger( ConsoleLog.Add );
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		EditorUtility.RemoveLogger( ConsoleLog.Add );

		statusBar?.Remove();
		statusBar = null;

		console = null;

		if ( Instance == this ) Instance = null;
	}

	//A frame rather than an event, because the numbers move for two different reasons - something was logged, and a build replaced every diagnostic there was - and three integer comparisons is less than either would cost to wire up.
	/// <summary>Reads the counts on the status bar again.</summary>
	[EditorEvent.Frame]
	public void OnFrame() => statusBar?.Update();

	/// <summary>Brings the console up and puts it in front, wherever it is docked.</summary>
	public static void RaiseConsole()
	{
		if ( EditorWindow is null ) return;

		EditorWindow.Blur();
		EditorWindow.Focus( true );
		EditorWindow.DockManager.RaiseDock( "Console" );
	}

	/// <summary>Puts the caret in the field, for whoever pressed the console key.</summary>
	public void FocusInput() => console?.FocusInput();

	/// <summary>Whether the field can be typed into at all - see <see cref="FullScreenManager"/>.</summary>
	public void SetInputEnabled( bool enabled ) => console?.SetInputEnabled( enabled );
}
