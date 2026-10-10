using Rinten.UI;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The bar of groups along the top of a section of the editor.</summary>
[StyleSheet( "MovieToolBar.cs.scss" )]
public sealed class MovieToolBar : Panel
{
	private readonly Panel _start;
	private readonly Panel _end;
	private readonly List<MovieToolBarGroup> _groups = new();

	public MovieToolBar()
	{
		AddClass( "movie-toolbar" );

		_start = AddChild<Panel>( "start" );
		_end = AddChild<Panel>( "end" );
	}

	/// <summary>A new group at the end of the bar.</summary>
	public MovieToolBarGroup AddGroup( bool permanent = false, bool alignRight = false )
	{
		var group = new MovieToolBarGroup { IsPermanent = permanent, AlignRight = alignRight };

		group.Parent = alignRight ? _end : _start;

		_groups.Add( group );

		return group;
	}

	/// <summary>Drops every group an edit mode put here, leaving the editor's own.</summary>
	public void Reset()
	{
		foreach ( var group in _groups.Where( x => !x.IsPermanent ).ToArray() )
		{
			_groups.Remove( group );

			group.Delete();
		}

		_groups.RemoveAll( x => !x.IsValid() );
	}
}
