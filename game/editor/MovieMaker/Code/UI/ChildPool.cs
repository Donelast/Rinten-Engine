using Rinten.UI;
using System.Collections.Generic;

namespace Editor.MovieMaker;

#nullable enable

//How many labels a block wants depends on the zoom, and the zoom changes every frame while the wheel is turning. Making and destroying panels for that costs more than the panels do, so they are parked instead and taken up again.
/// <summary>A run of child panels that is rebuilt often and kept between rebuilds.</summary>
public sealed class ChildPool<TPanel>
	where TPanel : Panel, new()
{
	private readonly Panel _host;
	private readonly string? _class;
	private readonly List<TPanel> _items = new();

	private int _used;

	public ChildPool( Panel host, string? className = null )
	{
		_host = host;
		_class = className;
	}

	public IReadOnlyList<TPanel> Items => _items;

	/// <summary>Starts a rebuild. Everything not taken before <see cref="End"/> is parked.</summary>
	public void Begin() => _used = 0;

	/// <summary>The next panel in the run, made if the pool has run out.</summary>
	public TPanel Next()
	{
		if ( _used == _items.Count )
		{
			var created = new TPanel { Parent = _host };

			if ( _class is not null ) created.AddClass( _class );

			_items.Add( created );
		}

		var panel = _items[_used++];

		panel.SetClass( "hidden", false );

		return panel;
	}

	/// <summary>Parks whatever was not taken this time round.</summary>
	public void End()
	{
		for ( var i = _used; i < _items.Count; i++ )
		{
			_items[i].SetClass( "hidden", true );
		}
	}
}
