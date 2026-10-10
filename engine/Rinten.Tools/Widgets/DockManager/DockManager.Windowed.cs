using System;

namespace Editor;

/// <summary>Panels opened into a window of their own rather than straight into a layout.</summary>
public partial class DockManager
{
	//How big a panel's own window opens, before anybody moves it.
	private static readonly Vector2 WindowSize = new( 760, 520 );

	private readonly Dictionary<string, PanelWindow> windowed = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Whether this panel is open anywhere: docked in this layout, or in a window of its own.</summary>
	public bool IsPanelOpen( string name ) => Windowed( name ) is not null || IsDockOpen( name );

	/// <summary>Open or put away a registered panel. Opening gives it a window of its own unless it is already docked in this layout, in which case it is raised where it already is.</summary>
	public void SetPanelOpen( string name, bool open )
	{
		if ( open ) OpenPanelWindow( name );
		else ClosePanel( name );
	}

	/// <summary>Open a registered panel in a window of its own. Null where there is no such panel, or where it was already docked in this layout and has been raised there instead.</summary>
	public DockWindow OpenPanelWindow( string name )
	{
		//Already has a window: bring that one forward rather than make a second.
		if ( Windowed( name ) is { } already )
		{
			already.Show();
			already.Focus();

			return already;
		}

		//Already docked and open: just bring it to front. Registered docks exist closed, so check open too
		if ( FindDockWidget( name ) is { } existing && IsDockOpen( name ) )
		{
			existing.SetAsCurrentTab();

			return null;
		}

		if ( !docks.TryGetValue( name, out var info ) )
			return null;

		if ( info.CreateAction?.Invoke() is not { } widget )
			return null;

		var window = new PanelWindow
		{
			Owner = this,
			Panel = name,
			WindowTitle = info.Title,
			DeleteOnClose = true,
			Size = WindowSize,
		};

		//Its own manager, holding the one panel. A window with a dock manager in it is also how a panel gets its
		//tab strip and its close button, so it looks like itself rather than like a bare window with a widget in.
		window.DockManager.AddDock( info.Title, info.Icon, widget, DockArea.Center );

		window.Show();

		windowed[name] = window;

		return window;
	}

	/// <summary>Put a panel away, wherever it is: its own window is closed, and one docked in this layout is hidden.</summary>
	public void ClosePanel( string name )
	{
		if ( Windowed( name ) is { } window )
		{
			//The panel's to refuse, wherever it is - see DockWidget.CanClose.
			if ( window.DockManager.FindDockWidget( name ) is { } inside && !inside.MayPutAway() )
				return;

			windowed.Remove( name );
			window.Close();

			return;
		}

		if ( FindDockWidget( name ) is { } docked && !docked.IsClosed && !docked.MayPutAway() )
			return;

		SetDockState( name, false );
	}

	//The window this panel was opened into, if it has one and it is still open. A window that has gone is forgotten on the way past.
	private PanelWindow Windowed( string name )
	{
		if ( !windowed.TryGetValue( name, out var window ) )
			return null;

		if ( window.IsValid() )
			return window;

		windowed.Remove( name );

		return null;
	}

	//A window holding one of a manager's panels, which tells that manager when it is closed so the View menu stops ticking it.
	internal sealed class PanelWindow : DockWindow
	{
		internal DockManager Owner { get; init; }
		internal string Panel { get; init; }

		//The cross on the window asks the panel inside it first - see DockWidget.CanClose.
		protected override bool OnClose()
		{
			if ( Panel is not null && DockManager.FindDockWidget( Panel ) is { } dock && !dock.MayPutAway() )
				return false;

			return base.OnClose();
		}

		protected override void OnClosed()
		{
			if ( Owner is not null && Panel is not null )
				Owner.windowed.Remove( Panel );

			base.OnClosed();
		}
	}
}
