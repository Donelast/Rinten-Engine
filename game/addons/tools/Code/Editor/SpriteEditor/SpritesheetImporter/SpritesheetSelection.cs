namespace Editor.SpriteEditor;

//Which cells of the sheet are picked, in the order they were picked - which is the order the frames come out in.
internal sealed class SpritesheetSelection
{
	private readonly Dictionary<Vector2Int, int> _order = new();

	/// <summary>Something was picked or unpicked.</summary>
	public event Action Changed;

	public int Count => _order.Count;

	public bool Contains( Vector2Int cell ) => _order.ContainsKey( cell );

	/// <summary>Where a cell comes in the order, or -1 for one that is not picked.</summary>
	public int IndexOf( Vector2Int cell ) => _order.GetValueOrDefault( cell, -1 );

	/// <summary>The picked cells, first picked first.</summary>
	public IEnumerable<Vector2Int> InOrder => _order.OrderBy( pair => pair.Value ).Select( pair => pair.Key );

	public void Select( Vector2Int cell )
	{
		if ( _order.ContainsKey( cell ) ) return;

		_order[cell] = _order.Count;
		Changed?.Invoke();
	}

	public void Deselect( Vector2Int cell )
	{
		if ( !_order.Remove( cell, out var removed ) ) return;

		//Everything picked after it moves up one, so the order stays gapless.
		foreach ( var key in _order.Where( pair => pair.Value > removed ).Select( pair => pair.Key ).ToList() )
			_order[key]--;

		Changed?.Invoke();
	}

	public void Clear()
	{
		if ( _order.Count == 0 ) return;

		_order.Clear();
		Changed?.Invoke();
	}

	/// <summary>Every cell of a grid, row by row.</summary>
	public void SelectAll( int columns, int rows )
	{
		_order.Clear();

		for ( var row = 0; row < rows; row++ )
			for ( var column = 0; column < columns; column++ )
				_order[new Vector2Int( column, row )] = _order.Count;

		Changed?.Invoke();
	}
}
