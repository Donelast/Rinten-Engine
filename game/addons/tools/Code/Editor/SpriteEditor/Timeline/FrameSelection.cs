namespace Editor.SpriteEditor;

/// <summary>Which frames of the timeline are picked, by index, and where a shift-click ranges from.</summary>
public sealed class FrameSelection
{
	private readonly HashSet<int> _indices = new();

	/// <summary>The frame the last plain click landed on, which a shift-click ranges from. -1 for none.</summary>
	public int Anchor { get; private set; } = -1;

	public IReadOnlyCollection<int> Indices => _indices;

	public int Count => _indices.Count;

	public bool Contains( int index ) => _indices.Contains( index );

	public void Clear()
	{
		_indices.Clear();
		Anchor = -1;
	}

	/// <summary>Exactly these, and the anchor on the first of them.</summary>
	public void Set( IEnumerable<int> indices )
	{
		_indices.Clear();

		foreach ( var index in indices )
			_indices.Add( index );

		Anchor = _indices.Count > 0 ? _indices.Min() : -1;
	}

	/// <summary>A click on a frame: alone it picks that one, with shift it ranges from the anchor, with control it adds to what is picked.</summary>
	public void Click( int index, bool shift, bool control )
	{
		if ( shift && Anchor >= 0 )
		{
			_indices.Clear();

			var from = Math.Min( Anchor, index );
			var to = Math.Max( Anchor, index );

			for ( var i = from; i <= to; i++ )
				_indices.Add( i );

			return;
		}

		if ( !control ) _indices.Clear();

		_indices.Add( index );
		Anchor = index;
	}

	/// <summary>Drops whatever no longer exists after frames were removed.</summary>
	public void ClampTo( int count )
	{
		_indices.RemoveWhere( i => i >= count );

		if ( Anchor >= count ) Anchor = -1;
	}
}
