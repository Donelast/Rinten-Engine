namespace Editor;

//Current tool's settings and footer, docked beside the scene in the mapping workspace
internal sealed class MappingToolPanel : Widget, IShortcutPassthrough
{
	private readonly Widget _content;
	private readonly Widget _footer;

	public MappingToolPanel( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		//The tool mode picker that headed this is on the bar over the scene view now - see ToolModePickerPanel - where it is the same in and out of mapping.

		var scroller = Layout.Add( new ScrollArea( this ), 1 );
		scroller.HorizontalScrollbarMode = ScrollbarMode.Off;

		_content = new Widget( null ) { Name = "ToolSidebar" };
		_content.Layout = Layout.Column();
		_content.HorizontalSizeMode = SizeMode.Flexible;
		scroller.Canvas = _content;

		_footer = Layout.Add( new Widget( this ) { Name = "ToolFooter" } );
		_footer.Layout = Layout.Column();
	}

	//A dock is glass on its own - see DockWidget.OnPaint - and a panel of buttons reads better against a plate than
	//against the window showing through. Black, so the grey buttons on it are the grey thing and the panel is not.
	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.BaseAlt );
		Paint.DrawRect( LocalRect );
	}

	EditorTool _activeTool;
	int _selectionHash;

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !MappingWorkspaceTools.Active )
		{
			//Built again on the way back in: what this holds was built for the selection of the last visit, and its panels carry the tool's keys.
			_activeTool = null;
			return;
		}

		var view = SceneViewPanel.Current;
		var tool = view?.Tools?.CurrentSubTool ?? view?.Tools?.CurrentTool;
		var selectionHash = tool?.Selection?.GetHashCode() ?? 0;

		if ( _activeTool != tool || (selectionHash != _selectionHash && (tool?.RebuildSidebarOnSelectionChange ?? false)) )
		{
			_activeTool = tool;
			_selectionHash = selectionHash;
			Rebuild( view );
		}
	}

	void Rebuild( SceneViewPanel view )
	{
		using var x = SuspendUpdates.For( this );

		_content.Layout.Clear( true );
		_footer.Layout.Clear( true );

		var rootTool = view?.Tools?.CurrentTool;
		var subTool = view?.Tools?.CurrentSubTool;

		if ( (subTool?.CreateToolSidebarPanel() ?? rootTool?.CreateToolSidebarPanel()) is { } toolPanel )
		{
			//The docked panel scrolls itself, since the outer scroll area sizes it to the window
			toolPanel.AddClass( "docked" );

			_content.Layout.Add( new PanelWidget( null, toolPanel ) );
		}
		else if ( (subTool?.CreateToolSidebar() ?? rootTool?.CreateToolSidebar()) is { } toolWidget )
			_content.Layout.Add( toolWidget );

		if ( (subTool?.CreateToolFooterPanel() ?? rootTool?.CreateToolFooterPanel()) is { } footerPanel )
			_footer.Layout.Add( new PanelWidget( null, footerPanel ) );
		else if ( (subTool?.CreateToolFooter() ?? rootTool?.CreateToolFooter()) is { } footerWidget )
			_footer.Layout.Add( footerWidget );

		if ( rootTool?.CreateShortcutsPanel() is { } rootShortcuts ) _footer.Layout.Add( new PanelWidget( null, rootShortcuts ) );
		if ( subTool?.CreateShortcutsPanel() is { } subShortcuts ) _footer.Layout.Add( new PanelWidget( null, subShortcuts ) );
	}
}
