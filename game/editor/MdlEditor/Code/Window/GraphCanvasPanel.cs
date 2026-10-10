using Rinten.UI;
using System;
using System.Collections.Generic;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Graph document: breadcrumb strip on top, the graph below; the window only calls SetTrail
[StyleSheet( "GraphCanvasPanel.cs.scss" )]
internal sealed class GraphCanvasPanel : Panel
{
	//One step of the trail, as the window describes it.
	internal readonly record struct Crumb( string Title, string Icon, Color? Tint, Action OnClick );

	private readonly Panel _strip;
	private readonly Panel _crumbs;
	private readonly Workspace _workspace;
	private readonly List<NavButtonPanel> _navButtons = new();

	//The graph, with the strip of a second graph over its foot.
	internal Workspace Workspace => _workspace;

	//Where the main graph or a node page is shown.
	internal Panel PageHost => _workspace.PrimaryHost;

	//The main graph view, which is the page whenever a page is not.
	internal View GraphView { get; }

	//What the search popup hangs off - it opens at the foot of the strip.
	internal Panel Breadcrumb => _strip;

	internal GraphCanvasPanel( Window window )
	{
		AddClass( "graph-canvas" );

		_strip = AddChild<Panel>( "breadcrumb" );

		AddNav( "arrow_back", "Back", window.GoBack, () => window.CanGoBack );
		AddNav( "arrow_forward", "Forward", window.GoForward, () => window.CanGoForward );
		AddNav( "arrow_upward", "Up one level", window.GoUp, () => window.CanGoUp );
		AddNav( "home", "Graph root", window.GoHome, () => window.CanGoUp );

		_strip.AddChild<Panel>( "divider" );

		_crumbs = _strip.AddChild<Panel>( "crumbs" );

		//The trail takes what is going, so these end up against the right edge.
		var actions = _strip.AddChild<PanelToolbar>();
		actions.AddIcon( "search", "Find in Graph", window.OpenSearch );
		actions.AddIcon( "fit_screen", "Fit View", window.FitCurrentView );

		_workspace = AddChild( new Workspace( window ) );

		//A child of the page host like any page, and the one that is up whenever a page is not.
		GraphView = _workspace.PrimaryHost.AddChild( new View( window ) );
	}

	private void AddNav( string icon, string tooltip, Action onClick, Func<bool> enabled )
		=> _navButtons.Add( _strip.AddChild( new NavButtonPanel( icon, tooltip, onClick, enabled ) ) );

	//Asks the arrows again whether there is anywhere to go. They ask for themselves every tick
	//as well. This is for the frame a jump happens in, which is the one somebody is looking at.
	internal void RefreshNav()
	{
		foreach ( var button in _navButtons )
		{
			if ( button.IsValid() ) button.Refresh();
		}
	}

	//Puts a fresh trail in the strip, chevrons and all.
	internal void SetTrail( IReadOnlyList<Crumb> steps )
	{
		//Cleared and refilled in the same breath, so the old steps have to go now rather than at the end of the frame.
		_crumbs.DeleteChildren( true );

		for ( var i = 0; i < steps.Count; i++ )
		{
			if ( i > 0 )
				_crumbs.AddChild<UILabel>( "chevron" ).Text = "›";

			var step = steps[i];
			_crumbs.AddChild( new CrumbButtonPanel( step.Title, step.Icon, step.Tint, step.OnClick ) );
		}
	}
}
