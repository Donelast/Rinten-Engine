using System;

namespace Editor;

//Status bar widgets: per-level counts that double as filters, plus the newest line
internal sealed class ConsoleStatusBar
{
	//Copied from addons Theme.cs
	private static readonly Color MessageColour = Color.Parse( "#cccdcd" ) ?? default;
	private static readonly Color WarningColour = Color.Parse( "#E6DB74" ) ?? default;
	private static readonly Color ErrorColour = Color.Parse( "#FB5A5A" ) ?? default;

	private readonly StatusBarCount[] buttons = new StatusBarCount[3];

	//What each button says now, so an unchanged number is not written again.
	private readonly int[] said = { -1, -1, -1 };

	private StatusBarLog latest;

	public ConsoleStatusBar()
	{
		Add( ConsoleLevel.Message, "comment", MessageColour );
		Add( ConsoleLevel.Warning, "warning", WarningColour );
		Add( ConsoleLevel.Error, "error", ErrorColour );

		ConsoleLog.Logged += OnLogged;

		Update();
	}

	/// <summary>Takes everything of the console's off the status bar again.</summary>
	public void Remove()
	{
		ConsoleLog.Logged -= OnLogged;

		if ( EditorWindow is null ) return;

		foreach ( var button in buttons )
		{
			if ( button is null ) continue;

			EditorWindow.StatusBar.RemoveWidget( button );
			button.Destroy();
		}

		Array.Clear( buttons );

		if ( latest is null ) return;

		EditorWindow.StatusBar.RemoveWidget( latest );
		latest.Destroy();
		latest = null;
	}

	/// <summary>Reads the counts again - cheap, and the only thing that catches a build that fixed them all.</summary>
	public void Update()
	{
		for ( var level = 0; level < buttons.Length; ++level )
		{
			var count = ConsoleLog.CountOf( (ConsoleLevel)level );

			if ( said[level] == count ) continue;

			said[level] = count;

			if ( buttons[level].IsValid() ) buttons[level].Text = Format( count );
		}
	}

	private void Add( ConsoleLevel level, string icon, Color colour )
	{
		var button = new StatusBarCount( icon, colour );

		button.Clicked = () =>
		{
			ConsoleLog.SetHidden( level, !ConsoleLog.IsHidden( level ) );

			button.Off = ConsoleLog.IsHidden( level );

			ConsoleDock.RaiseConsole();
		};

		button.Off = ConsoleLog.IsHidden( level );

		buttons[(int)level] = button;

		EditorWindow.StatusBar.AddWidgetRight( button, 0 );
	}

	private void OnLogged( LogEvent e )
	{
		if ( EditorWindow is null ) return;

		if ( latest is null )
		{
			latest = new StatusBarLog( null, e );

			EditorWindow.StatusBar.AddWidgetLeft( latest, 2 );
		}

		latest.SetLog( e );
	}

	//A count, kept short enough to sit on a status bar.
	private static string Format( int count, int most = 999 ) =>
		$"{count.Clamp( 0, most ):n0}{(count > most ? "+" : "")}";
}

//One level's count on the status bar: its icon and the number, painted straight onto the
//bar. A widget of its own rather than a Button, so it wears none of the button's plate.
internal sealed class StatusBarCount : Widget
{
	public Action Clicked;

	private readonly string icon;
	private readonly Color colour;

	private string text = "0";
	private bool off;

	public string Text
	{
		get => text;
		set
		{
			if ( text == value ) return;

			text = value;

			AdjustSize();
			Update();
		}
	}

	/// <summary>A level that is not being listed is drawn faint: the count saying it is off.</summary>
	public bool Off
	{
		get => off;
		set
		{
			if ( off == value ) return;

			off = value;

			Update();
		}
	}

	internal StatusBarCount( string icon, Color colour ) : base( null )
	{
		this.icon = icon;
		this.colour = colour;

		Cursor = CursorShape.Finger;

		AdjustSize();
	}

	protected override Vector2 SizeHint()
	{
		var measured = EditorText.Measure( text );

		return new Vector2( measured.x + 50.0f, MathF.Max( measured.y + 10.0f, Theme.ControlHeight ) );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		Clicked?.Invoke();

		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrush( Theme.VeilHover );
			Paint.DrawRect( LocalRect, Theme.ControlRadius );
		}

		var rect = LocalRect.Shrink( 4, 4 );

		Paint.SetPen( off ? colour.WithAlpha( 0.5f ) : colour );
		Paint.ClearBrush();

		var iconRect = rect;
		iconRect.Width = 16;

		Paint.DrawIcon( iconRect, icon, 16, TextFlag.Center );

		Paint.SetDefaultFont();
		Paint.DrawText( rect.Shrink( iconRect.Right + 2, 0, 0, 0 ), text, TextFlag.Center );
	}
}

//The newest line, on the status bar, for whoever is not looking at the console.
internal sealed class StatusBarLog : Widget
{
	private static readonly Color ControlText = Color.Parse( "#ccc" ) ?? default;

	public string Text
	{
		get => text;
		set
		{
			if ( text == value ) return;

			text = value;

			AdjustSize();
			Update();
		}
	}

	private string text = string.Empty;
	private Color colour;
	private string Icon;

	private bool isEmpty = true;
	private RealTimeSince timeSinceLastLog;

	internal StatusBarLog( Widget parent, LogEvent e ) : base( parent )
	{
		Cursor = CursorShape.Finger;

		SetLog( e );
	}

	internal void SetLog( LogEvent e )
	{
		var msg = e.Message;

		//Get rid of all newlines/tabs
		msg = msg.Replace( "\n", string.Empty );
		msg = msg.Replace( "\r", string.Empty );
		msg = msg.Replace( "\t", string.Empty );

		Text = msg;

		colour = e.Level switch
		{
			LogLevel.Warn => Color.Orange,
			LogLevel.Error => Color.Parse( "#D53535" ) ?? default,
			_ => ControlText
		};

		Icon = e.Level switch
		{
			LogLevel.Warn => "warning",
			LogLevel.Error => "error",
			_ => "info"
		};

		isEmpty = false;

		//Only update the panel every 10ms
		if ( timeSinceLastLog > 0.1f )
		{
			Update();
		}

		timeSinceLastLog = 0;
	}

	[EditorEvent.Frame]
	private void OnFrame()
	{
		//Empties the log so it doesn't render
		if ( timeSinceLastLog > 10f && !isEmpty )
		{
			isEmpty = true;
			Update();
		}
	}

	//Nothing across: the bar stretches it to whatever is left beside the counts,
	//and a long line is cut off at the end rather than pushing them off the bar.
	protected override Vector2 SizeHint() => new( 0, MathF.Max( EditorText.Measure( Text ).y + 10.0f, Theme.ControlHeight ) );

	protected override void OnPaint()
	{
		//Don't bother rendering anything if we're empty
		if ( isEmpty ) return;

		var hovered = IsUnderMouse;
		var color = colour.Lighten( hovered ? 0.2f : 0 );

		Paint.Antialiasing = true;

		Paint.SetPen( color );
		Paint.SetDefaultFont( 10, 500 );

		var rect = LocalRect.Grow( -8, 0 );

		if ( !string.IsNullOrEmpty( Icon ) )
		{
			Paint.DrawIcon( rect, Icon, Size.y, TextFlag.LeftCenter );
			rect = rect.Grow( -Size.y, 0 );
		}

		Paint.DrawText( rect.Grow( -8, 0 ), Text, TextFlag.LeftCenter );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		ConsoleDock.RaiseConsole();
	}
}
