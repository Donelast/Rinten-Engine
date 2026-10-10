using Rinten.UI;
using System;
using System.Collections.Generic;

namespace Rinten;

//The first window of the editor that is ours.
internal sealed class CoreUiShell
{
	/// <summary>Where a tool's own panels go.</summary>
	public Panel Content { get; private set; }

	/// <summary>The strip along the top, with the window's name in it.</summary>
	public Panel TitleBar { get; private set; }

	/// <summary>The strip down the side, where tools are chosen.</summary>
	public Panel Sidebar { get; private set; }

	private readonly Dictionary<Panel, Panel> tools = new();
	private Panel chosen;

	/// <summary>Builds the furniture into a window's root.</summary>
	public static CoreUiShell Build( Panel root, string title )
	{
		var shell = new CoreUiShell();

		EditorTheme.Apply( root );
		root.AddClass( "editor-window" );

		shell.TitleBar = root.AddChild<Panel>();
		shell.TitleBar.AddClass( "title-bar" );

		var name = shell.TitleBar.AddChild<Label>();
		name.Text = title;
		name.Tokenize = false;

		var body = root.AddChild<Panel>();
		body.AddClass( "body" );

		shell.Sidebar = body.AddChild<Panel>();
		shell.Sidebar.AddClass( "sidebar" );

		shell.Content = body.AddChild<Panel>();
		shell.Content.AddClass( "content" );

		return shell;
	}

	//The tool is made when it is added rather than when it is first looked at, and hidden the rest of the time: a console that only listens to the log while somebody is looking at it has nothing to show when they do.
	/// <summary>One entry in the sidebar, and the tool it shows.</summary>
	public Panel AddTool( string text, Func<Panel> build = null )
	{
		var item = Sidebar.AddChild<Panel>();
		item.AddClass( "sidebar-item" );

		var label = item.AddChild<Label>();
		label.Text = text;
		label.Tokenize = false;

		Panel tool = null;

		if ( build is not null )
		{
			tool = build();

			if ( tool is not null )
			{
				Content.AddChild( tool );
				tool.Style.Display = DisplayMode.None;

				tools[item] = tool;
			}
		}

		item.AddEventListener( "onclick", () => Choose( item ) );

		//Whatever came first is what the window opens on.
		if ( chosen is null && tool is not null ) Choose( item );

		return item;
	}

	/// <summary>Shows one tool and hides the rest.</summary>
	public void Choose( Panel item )
	{
		if ( item is null ) return;

		chosen?.SetClass( "active", false );

		chosen = item;
		chosen.SetClass( "active", true );

		foreach ( var (owner, tool) in tools )
		{
			tool.Style.Display = owner == item ? DisplayMode.Flex : DisplayMode.None;
		}
	}
}
