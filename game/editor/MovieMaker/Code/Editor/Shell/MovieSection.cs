using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

//Subclasses carry [StyleSheet] themselves - the engine does not inherit it.
/// <summary>One column of the editor: a toolbar, and whatever the section is for under it.</summary>
public abstract class MovieSection : Panel
{
	public MovieEditor Editor { get; }
	public Session Session { get; }

	public MovieToolBar ToolBar { get; }

	//Everything below the toolbar.
	protected Panel Body { get; }

	protected MovieSection( MovieEditor editor, Session session )
	{
		Editor = editor;
		Session = session;

		AddClass( "movie-section" );

		ToolBar = AddChild<MovieToolBar>();
		Body = AddChild<Panel>( "body" );
	}

	//One set of editing keys for two lists, so the last section touched is the one they mean.
	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		Editor.ActiveSection = this;
	}
}
