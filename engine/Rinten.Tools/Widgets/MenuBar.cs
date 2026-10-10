using System;

namespace Editor;

public partial class MenuBar : Widget
{

	public MenuBar( Widget parent ) : base()
	{
		MakeSurface( parent );
	}

	public Option AddOption( string path, string icon = null, Action action = null, string shortcut = null )
	{
		var menu = GetPathTo( path );
		if ( menu is not { Count: > 0 } ) return null;

		var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		return menu.Last().AddOption( parts.Last(), icon, action, shortcut );
	}

	public void AddOption( string path, Option option )
	{
		if ( option == null ) return;

		var menu = GetPathTo( path );
		if ( menu is not { Count: > 0 } ) return;

		var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		option.Text = parts.Last();
		menu.Last().AddOption( option );
	}

	public void RemovePath( string path )
	{
		var menus = GetPathTo( path );
		if ( menus is not { Count: > 0 } ) return;

		var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );

		menus.Last().RemoveOption( parts.Last() );
		menus.Reverse();

		foreach ( var menu in menus )
		{
			if ( menu.HasOptions ) break;
			if ( menu.HasMenus ) break;

			menu.Destroy();
		}
	}

	public List<Menu> GetPathTo( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;

		var list = new List<Menu>();
		var parts = Menu.GetSplitPath( path );
		if ( parts.Length == 1 ) return null;

		var menu = FindOrCreateMenu( parts[0].Name );
		if ( menu == null ) return null;

		menu.Icon = parts[0].Icon;

		list.Add( menu );

		if ( parts.Length <= 2 ) return list;

		menu.GetPathTo( parts[1..], list );
		return list;
	}

	internal List<Menu> Menus = new();

	public Menu FindOrCreateMenu( string name )
	{
		Menus.RemoveAll( x => !x.IsValid );

		var m = Menus.FirstOrDefault( x => string.Equals( x.Title, name, StringComparison.OrdinalIgnoreCase ) );
		if ( m != null ) return m;

		return AddMenu( name );
	}

	public Option AddSeparator()
	{
		return new Option( this );
	}

	public Menu AddMenu( string name )
	{
		var m = new Menu( name, this );
		m.DeleteOnClose = false;

		Menus.Add( m );

		Update();
		Repaint();

		return m;
	}

	public Menu AddMenu( string icon, string name )
	{
		var m = new Menu( name, this );
		m.DeleteOnClose = false;
		m.Icon = icon;

		Menus.Add( m );
		Update();
		Repaint();
		return m;
	}

	public void Clear()
	{
		Menus.Clear();
		Update();
	}
}
