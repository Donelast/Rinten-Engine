using System;

namespace Editor;

public partial class DockManager : Widget
{
	public DockManager( Widget parent = null )
	{
		MakeSurface( parent );

		Window = parent as DockWindow;
	}

	/// <summary>The window that created this, if any; stored because embedded workspaces reparent the manager</summary>
	public DockWindow Window { get; }

	//Every dock this manager made.
	private readonly List<DockWidget> managed = new();


	/// <summary>Find an existing dock widget by name. Returns null if not found.</summary>
	public DockWidget FindDockWidget( string name )
	{
		return managed.FirstOrDefault( x => x.Title == name );
	}

	/// <summary>Find the dock widget containing the given widget. Returns null if it isn't docked.</summary>
	public DockWidget FindDockWidget( Widget widget )
	{
		for ( var w = widget; w.IsValid(); w = w.Parent )
		{
			if ( w is DockWidget dock )
				return dock;
		}

		return null;
	}

	/// <summary>Creates a native dock widget container for the given widget.</summary>
	public DockWidget CreateDockWidget( string name, string icon, Widget widget )
	{
		var made = new DockWidget( this, name, icon, widget );

		managed.Add( made );

		return made;
	}

	/// <summary>Adds a <see cref="DockWidget"/> into the given area.</summary>
	public void AddDock( DockWidget widget, DockArea area )
	{
		ArgumentNullException.ThrowIfNull( widget );

		PlaceManaged( widget, area );
	}

	/// <summary>Adds a <see cref="DockWidget"/> as a floating window.</summary>
	public void AddDockFloating( DockWidget widget )
	{
		ArgumentNullException.ThrowIfNull( widget );

		//Nothing floats yet: a dock that asked to be its own window is put in the middle instead, where it can at least be worked in.
		PlaceManaged( widget, DockArea.Center );
	}

	/// <summary>Adds a <see cref="DockWidget"/> into an area relative to an existing dock, splitting its space. <see cref="DockArea.Center"/> docks it as a tab. Pass null to dock relative to the whole window.</summary>
	public void AddDock( DockWidget widget, DockArea area, DockWidget relativeTo )
	{
		ArgumentNullException.ThrowIfNull( widget );

		//Center makes a tab in relativeTo's group; other sides split that dock, not the window edge
		if ( relativeTo is not null )
		{
			if ( area == DockArea.Center )
			{
				PlaceManaged( widget, relativeTo );
				return;
			}

			if ( SideOf( relativeTo ) is { } group )
			{
				SplitManaged( widget, group, area );
				return;
			}
		}

		PlaceManaged( widget, area );
	}

	/// <summary>Adds a <see cref="DockWidget"/> as a tab alongside an existing dock.</summary>
	public void AddDock( DockWidget widget, DockWidget tabWith )
	{
		ArgumentNullException.ThrowIfNull( tabWith );

		AddDock( widget, DockArea.Center, tabWith );
	}

	/// <summary>Creates, registers and docks a widget; replaces content if the name exists (e.g. after hotload)</summary>
	public DockWidget AddDock( string name, string icon, Widget widget, DockArea area = DockArea.Left, DockWidget relativeTo = null )
	{
		ArgumentNullException.ThrowIfNull( widget );

		if ( !docks.TryGetValue( name, out var info ) )
		{
			info = new DockInfo { Title = name, CreateAction = () => widget };
			docks[name] = info;
		}

		info.Icon = icon;
		info.Area = area;

		if ( FindDockWidget( name ) is { } existing )
		{
			var previous = existing.Widget;
			if ( previous == widget ) return existing;

			existing.Widget = widget;
			previous?.Destroy();
			return existing;
		}

		var dockWidget = CreateDockWidget( name, icon, widget );

		if ( relativeTo is not null )
			AddDock( dockWidget, area, relativeTo );
		else
			AddDock( dockWidget, area );

		return dockWidget;
	}

	/// <summary>Sets a widget as the central (always present, non-closable) area. Must be called before any other dock is added. Returns the dock hosting it, which can be used as a target for relative docking.</summary>
	public DockWidget SetCentralWidget( Widget widget )
	{
		ArgumentNullException.ThrowIfNull( widget );

		var middle = CreateDockWidget( "CentralWidget", string.Empty, widget );

		middle.NoTab = true;

		PlaceManaged( middle, DockArea.Center );

		return middle;
	}

	/// <summary>Sets the relative proportions of every visible area in the splitter containing <paramref name="dock"/>. Values are ordered left-to-right or top-to-bottom.</summary>
	public unsafe bool SetSplitterProportions( DockWidget dock, params ReadOnlySpan<float> proportions )
	{
		if ( dock is null || proportions.Length < 2 )
			return false;

		//The proportions run across the splitter in order, so the share of a group is the
		//end of the list it is on: the first half comes first and the second half last.
		var leaf = SideOf( dock );

		if ( leaf is null ) return false;

		var split = ParentOf( leaf );

		if ( split is null ) return false;

		var total = 0.0f;

		foreach ( var proportion in proportions ) total += proportion;

		if ( total <= 0.0f ) return false;

		var ours = split.First == leaf ? proportions[0] : total - proportions[^1];

		split.Share = Math.Clamp( ours / total, 0.05f, 0.95f );

		MarkNeedsArrange();
		return true;
	}

	/// <summary>Whether the named dock is currently open (visible).</summary>
	public bool IsDockOpen( string name )
	{
		var dock = FindDockWidget( name );
		return dock is not null && !dock.IsClosed;
	}

	/// <summary>Whether the dock containing the given widget is currently open.</summary>
	public bool IsDockOpen( Widget widget )
	{
		var dock = FindDockWidget( widget );
		return dock is not null && !dock.IsClosed;
	}

	/// <summary>Remove a dock widget from the manager.</summary>
	public void RemoveDock( DockWidget widget )
	{
		if ( widget is null ) return;

		managed.Remove( widget );

		widget.SetPanelParent( null );

		MarkNeedsArrange();
		Update();
	}

	/// <summary>Raise a dock to the front of its tab group.</summary>
	public bool RaiseDock( string name )
	{
		var dock = FindDockWidget( name );
		if ( dock is null ) return false;

		dock.SetAsCurrentTab();
		return true;
	}

	/// <summary>Raise the dock containing the given widget to the front of its tab group.</summary>
	public bool RaiseDock( Widget widget )
	{
		var dock = FindDockWidget( widget );
		if ( dock is null ) return false;

		dock.SetAsCurrentTab();
		return true;
	}

	//The docks the layout on screen asked for that nobody has registered - since the layout was last restored or reset.
	private readonly HashSet<string> missing = new( StringComparer.Ordinal );

	/// <summary>Layout lacks docks it named (project failed to compile), so it must not be saved over</summary>
	public bool IsMissingDocks => missing.Count > 0;

	/// <summary>The names in <see cref="IsMissingDocks"/>.</summary>
	public IReadOnlyCollection<string> MissingDocks => missing;

	//A whole layout is about to be put up: what the last one lacked is its own business.
	internal void ForgetMissing() => missing.Clear();

	/// <summary>Toggle a registered dock's visibility by name. If the dock doesn't exist yet, it will be created from its registered <see cref="DockInfo"/>.</summary>
	public void SetDockState( string name, bool visible )
	{
		var existing = FindDockWidget( name );
		if ( existing is not null )
		{
			//A layout restore rebuilds the tree from what was open. Docks that were registered at startup but never saved are left off
			//it, and opening one from the View menu only toggled visibility - it stayed at 0,0 with whatever size it was born with.
			if ( visible && SideOf( existing ) is null )
			{
				var area = docks.TryGetValue( name, out var info ) ? info.Area : DockArea.Center;
				PlaceManaged( existing, area == DockArea.Hidden ? DockArea.Center : area );
			}

			existing.ToggleView( visible );

			if ( visible )
				existing.SetAsCurrentTab();

			return;
		}

		//Nothing to hide if it doesn't exist
		if ( !visible )
			return;

		//Create from registered dock type. One nobody registered is written down: the layout asking for it is then not the one to save - see IsMissingDocks.
		if ( !docks.TryGetValue( name, out var create ) )
		{
			missing.Add( name );
			return;
		}

		var widget = create.CreateAction();
		if ( widget is null )
		{
			missing.Add( name );
			return;
		}

		AddDock( create.Title, create.Icon, widget, create.Area );

		FindDockWidget( name )?.SetAsCurrentTab();
	}

	/// <summary>Open a registered dock and move it into the given area, creating it if needed.</summary>
	public DockWidget OpenDock( string name, DockArea area, DockWidget relativeTo = null )
	{
		SetDockState( name, true );

		var dock = FindDockWidget( name );
		if ( dock is null ) return null;

		AddDock( dock, area, relativeTo );
		return dock;
	}

	/// <summary>Clear all registered dock types.</summary>
	public void Clear()
	{
		docks.Clear();
	}
}
