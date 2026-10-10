namespace Editor.Inspectors;

//The curve on a row of the inspector: a line across the field, with a glow so it reads at that size.
[StyleSheet( "CurveControl.cs.scss" )]
sealed class CurvePreviewPanel : Panel
{
	readonly List<Vector2> scratch = new();
	readonly List<Vector2> scratchB = new();
	readonly List<Vector2> scratchC = new();
	readonly Image image;

	Bitmap bitmap;
	Texture texture;
	Vector2Int painted;

	Curve curve;
	CurveRange range;
	bool isRange;
	bool multiple;
	bool wasHovered;
	bool dirty = true;

	/// <summary>The colour the line is drawn in.</summary>
	public Color HighlightColor { get; set; } = Theme.Yellow;

	public CurvePreviewPanel()
	{

		image = AddChild<Image>( "fill" );
	}

	/// <summary>Whether the field is showing more than one differing value, and so shows none.</summary>
	public bool Multiple
	{
		get => multiple;
		set
		{
			if ( multiple == value ) return;

			multiple = value;
			dirty = true;

			SetClass( "multiple", value );
		}
	}

	public void SetCurve( Curve value )
	{
		if ( !isRange && value.Equals( curve ) ) return;

		curve = value;
		isRange = false;
		dirty = true;
	}

	public void SetRange( CurveRange value )
	{
		if ( isRange && value.Equals( range ) ) return;

		range = value;
		isRange = true;
		dirty = true;
	}

	public override void Tick()
	{
		base.Tick();

		if ( wasHovered != HasHovered )
		{
			wasHovered = HasHovered;
			dirty = true;
		}

		Repaint();
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );

		dirty = true;
	}

	void Repaint()
	{
		var rect = Box.RectInner;

		var width = rect.Width.FloorToInt();
		var height = rect.Height.FloorToInt();

		if ( width < 2 || height < 2 ) return;

		var size = new Vector2Int( width, height );

		if ( !dirty && size == painted ) return;

		dirty = false;

		if ( size != painted || bitmap is null )
		{
			bitmap?.Dispose();
			bitmap = new Bitmap( width, height );
			painted = size;
		}

		var scale = ScaleFromScreen;

		image.Style.Width = Length.Pixels( width * scale );
		image.Style.Height = Length.Pixels( height * scale );

		bitmap.Clear( Color.Transparent );
		bitmap.SetAntialias( true );

		if ( !multiple )
		{
			Paint( new Rect( 0, 0, width, height ).Shrink( 4 ) );
		}

		CurveDraw.Present( image, bitmap, ref texture );
	}

	void Paint( Rect rect )
	{
		if ( rect.Width <= 1 || rect.Height <= 1 ) return;

		var color = HighlightColor.WithAlpha( HasHovered ? 1 : 0.5f );

		if ( isRange )
		{
			CurveDraw.SampleRange( range, rect, 0, scratch );
			CurveDraw.SampleRange( range, rect, 1, scratchB );
			CurveDraw.Band( bitmap, scratch, scratchB, color, scratchC );

			return;
		}

		CurveDraw.SampleCurve( curve, rect, scratch, 3.0f );

		//A hint of a glow under the line, sized to the row so it cannot wash it out.
		var glow = MathF.Max( rect.Height * 0.4f, 4f );

		CurveDraw.Polyline( bitmap, scratch, color.WithAlphaMultiplied( 0.06f ), glow );
		CurveDraw.Polyline( bitmap, scratch, color.WithAlphaMultiplied( 0.085f ), glow * 0.5f );
		CurveDraw.Polyline( bitmap, scratch, color, 2 );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		bitmap?.Dispose();
		bitmap = null;

		texture?.Dispose();
		texture = null;
	}
}
