using Rinten.UI;

namespace Editor.SpriteEditor;

//The panel is made by the subclass and can be made again - on hotload the panel types are new, and the window asks every dock to rebuild rather than building the docks themselves over.
/// <summary>A dock of the sprite editor: the one widget a dock has to be, with a panel filling it.</summary>
public abstract class SpriteDock : PanelDockHost
{
	//No parent: the dock manager is what puts a dock somewhere, and a parent handed over here is replaced the moment the dock is opened.
	protected SpriteDock( string title, string icon ) : base( null )
	{
		Name = title;
		WindowTitle = title;
		SetWindowIcon( icon );
	}

	/// <summary>Puts a fresh panel in the dock, in place of whatever was in it.</summary>
	public void Rebuild() => SetContent( CreatePanel() );

	//The dock's content, made anew.
	protected abstract Panel CreatePanel();
}
