namespace Editor.SpriteEditor;

//Spritesheet cell positions on the importer plane; rows go down, so y is negated
internal readonly struct SheetGeometry
{
	private readonly int _columns;
	private readonly int _rows;
	private readonly float _startX;
	private readonly float _startY;
	private readonly float _cellWidth;
	private readonly float _cellHeight;
	private readonly float _stepX;
	private readonly float _stepY;

	/// <summary>Whether there is a sheet to measure at all.</summary>
	public bool IsValid => _columns > 0 && _rows > 0 && _cellWidth > 0 && _cellHeight > 0;

	/// <summary>The sheet's cells, laid onto a plane that fills the view along its longer side.</summary>
	public SheetGeometry( SpritesheetImportSettings settings, Vector2 textureSize, float viewSize )
	{
		this = default;

		if ( textureSize.x <= 0 || textureSize.y <= 0 ) return;

		var ratio = textureSize.x / textureSize.y;
		var planeWidth = ratio >= 1f ? viewSize : viewSize * ratio;
		var planeHeight = ratio >= 1f ? viewSize / ratio : viewSize;

		_columns = Math.Max( 1, settings.HorizontalFrames );
		_rows = Math.Max( 1, settings.VerticalFrames );

		var availableWidth = textureSize.x - settings.PaddingLeft - settings.PaddingRight;
		var availableHeight = textureSize.y - settings.PaddingTop - settings.PaddingBottom;
		var pixelWidth = Math.Max( 1f, (availableWidth - (_columns - 1) * settings.HorizontalSeparation) / _columns );
		var pixelHeight = Math.Max( 1f, (availableHeight - (_rows - 1) * settings.VerticalSeparation) / _rows );

		var scaleX = planeWidth / textureSize.x;
		var scaleY = planeHeight / textureSize.y;

		_cellWidth = pixelWidth * scaleX;
		_cellHeight = pixelHeight * scaleY;
		_stepX = _cellWidth + settings.HorizontalSeparation * scaleX;
		_stepY = _cellHeight + settings.VerticalSeparation * scaleY;
		_startX = settings.PaddingLeft * scaleX - planeWidth * 0.5f;
		_startY = settings.PaddingTop * scaleY - planeHeight * 0.5f;
	}

	public int Columns => _columns;
	public int Rows => _rows;

	/// <summary>The shorter side of a cell, which is what an inset is measured against.</summary>
	public float CellExtent => Math.Min( _cellWidth, _cellHeight );

	/// <summary>A cell as a rectangle on the plane, y pointing down the picture.</summary>
	public Rect Cell( int column, int row )
		=> new( _startX + column * _stepX, _startY + row * _stepY, _cellWidth, _cellHeight );

	/// <summary>The cell under a point on the plane, or null for a point in the gaps between cells or off the sheet.</summary>
	public Vector2Int? CellAt( Vector2 planePoint )
	{
		if ( !IsValid || _stepX <= 0 || _stepY <= 0 ) return null;

		//The plane's y points up; the picture's points down.
		var relativeColumn = (planePoint.x - _startX) / _stepX;
		var relativeRow = (-planePoint.y - _startY) / _stepY;

		var column = (int)MathF.Floor( relativeColumn );
		var row = (int)MathF.Floor( relativeRow );

		if ( relativeColumn - column > _cellWidth / _stepX ) return null;
		if ( relativeRow - row > _cellHeight / _stepY ) return null;

		if ( column < 0 || column >= _columns || row < 0 || row >= _rows ) return null;

		return new Vector2Int( column, row );
	}
}
