using Rinten.UI;

namespace Editor;

//Whether it is folded is remembered per title, so a mapper who never wants to see the shortcuts list again only has to say so once.
/// <summary>A named block of a tool's settings, which can be folded away.</summary>
public sealed class SidebarGroupPanel : Panel
{
	/// <summary>Where the rows of this group go.</summary>
	public Panel Content { get; }

	private readonly string title;
	private readonly bool collapsible;

	public SidebarGroupPanel( string title, bool collapsible )
	{
		this.title = title;
		this.collapsible = collapsible;

		AddClass( "sidebar-group" );
		SetClass( "collapsible", collapsible );

		var header = AddChild<Panel>( "header" );

		if ( collapsible )
		{
			var chevron = header.AddChild<IconPanel>( "chevron" );
			chevron.Text = "expand_more";

			header.AddEventListener( "onclick", _ => Collapsed = !Collapsed );
		}

		var label = header.AddChild<Rinten.UI.Label>( "title" );
		label.Text = title;

		Content = AddChild<Panel>( "content" );

		if ( collapsible ) Collapsed = EditorCookie.Get( $"SidebarGroup.{title}", false );
	}

	/// <summary>Whether the rows are hidden. Only a collapsible group is ever folded.</summary>
	public bool Collapsed
	{
		get => HasClass( "collapsed" );
		set
		{
			if ( !collapsible ) return;

			SetClass( "collapsed", value );

			EditorCookie.Set( $"SidebarGroup.{title}", value );
		}
	}

	/// <summary>A row of things standing side by side in this group.</summary>
	public Panel AddRow() => Content.AddChild<Panel>( "row gap-2" );
}
