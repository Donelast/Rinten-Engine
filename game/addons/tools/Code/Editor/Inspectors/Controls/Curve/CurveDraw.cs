namespace Editor.Inspectors;

//Samples a curve and strokes it: onto a bitmap the small panels wear as a texture, or as vectors on the chart.
static class CurveDraw
{
	static readonly float[] dotPattern = [1f, 3f];
	static readonly float[] dashPattern = [4f, 4f];

	static Vector2[] exact = [];

	/// <summary>Puts the bitmap on the image, filling the texture it already has where that still fits.</summary>
	public static void Present( Image image, Bitmap bitmap, ref Texture texture )
	{
		if ( texture is not null && texture.Width == bitmap.Width && texture.Height == bitmap.Height )
		{
			texture.Update( bitmap );
			return;
		}

		texture?.Dispose();
		texture = bitmap.ToTexture( mips: false );

		image.Texture = texture;
	}

	//The list as an array of its own length, reusing one buffer between calls.
	static Vector2[] Exact( List<Vector2> points )
	{
		if ( exact.Length != points.Count ) exact = new Vector2[points.Count];

		points.CopyTo( exact );

		return exact;
	}

	public static void Polyline( Bitmap bitmap, List<Vector2> points, Color color, float thickness )
	{
		if ( points.Count < 2 ) return;

		bitmap.SetPen( color, thickness );
		bitmap.DrawLines( Exact( points ) );
	}

	public static void Line( Bitmap bitmap, Vector2 from, Vector2 to, Color color, float thickness )
	{
		bitmap.SetPen( color, thickness );
		bitmap.DrawLine( from, to );
	}

	public static void DottedLine( Bitmap bitmap, Vector2 from, Vector2 to, Color color, float thickness )
	{
		bitmap.SetDashedPen( color, thickness, dotPattern );
		bitmap.DrawLine( from, to );
	}

	public static void DashedRect( Bitmap bitmap, Rect rect, Color color, float thickness )
	{
		bitmap.SetDashedPen( color, thickness, dashPattern );
		bitmap.DrawRect( rect );
	}

	public static void FillRect( Bitmap bitmap, Rect rect, Color color )
	{
		bitmap.SetFill( color );
		bitmap.DrawRect( rect );
	}

	public static void FillCircle( Bitmap bitmap, Vector2 center, float radius, Color color )
	{
		bitmap.SetFill( color );
		bitmap.DrawCircle( center, radius );
	}

	public static void StrokeCircle( Bitmap bitmap, Vector2 center, float radius, Color color, float thickness )
	{
		bitmap.SetPen( color, thickness );
		bitmap.DrawCircle( center, radius );
	}

	public static void DottedCircle( Bitmap bitmap, Vector2 center, float radius, Color color, float thickness )
	{
		bitmap.SetDashedPen( color, thickness, dotPattern );
		bitmap.DrawCircle( center, radius );
	}

	public static void StrokeRect( Bitmap bitmap, Rect rect, Color color, float thickness )
	{
		bitmap.SetPen( color, thickness );
		bitmap.DrawRect( rect );
	}

	/// <summary>The band between two runs of points that share their x positions.</summary>
	public static void Band( Bitmap bitmap, List<Vector2> top, List<Vector2> bottom, Color color, List<Vector2> scratch )
	{
		if ( top.Count < 2 || bottom.Count < 2 ) return;

		scratch.Clear();
		scratch.AddRange( top );

		for ( var i = bottom.Count - 1; i >= 0; i-- )
		{
			scratch.Add( bottom[i] );
		}

		bitmap.SetFill( color );
		bitmap.DrawPolygon( Exact( scratch ) );
	}

	/// <summary>The whole curve across a rectangle, left edge to right edge.</summary>
	public static void SampleCurve( in Curve curve, in Rect rect, List<Vector2> into, float spacing = 2.0f )
	{
		into.Clear();

		var steps = Math.Max( 2, (rect.Width / MathF.Max( spacing, 0.5f )).CeilToInt() );

		for ( var i = 0; i <= steps; i++ )
		{
			var f = (float)i / steps;
			var v = curve.EvaluateDelta( f );

			into.Add( new Vector2( rect.Left + f * rect.Width, rect.Top + rect.Height - (v * rect.Height) ) );
		}
	}

	/// <summary>One line across the band between the two curves of a range.</summary>
	public static void SampleRange( in CurveRange range, in Rect rect, float y, List<Vector2> into, float spacing = 2.0f )
	{
		into.Clear();

		var steps = Math.Max( 2, (rect.Width / MathF.Max( spacing, 0.5f )).CeilToInt() );

		for ( var i = 0; i <= steps; i++ )
		{
			var f = (float)i / steps;
			var v = range.EvaluateDelta( f, y );

			into.Add( new Vector2( rect.Left + f * rect.Width, rect.Top + rect.Height - (v * rect.Height) ) );
		}
	}

	//-- the same, stroked as vectors by the panel that draws them ------------------------------

	/// <summary>Where vector drawing lands: the corner the points are measured from, in the space of the panel's box. What a chart draws this way is tessellated with real joins, caps and dashes and clipped by the panel it is drawn in, rather than painted into a picture first.</summary>
	public readonly record struct Canvas( Vector2 Origin );

	static readonly List<Vector2> placed = new();

	static List<Vector2> Placed( in Canvas canvas, List<Vector2> points )
	{
		placed.Clear();
		foreach ( var p in points ) placed.Add( canvas.Origin + p );
		return placed;
	}

	//One path, emptied and filled for each shape, so drawing the chart allocates nothing.
	static readonly Rinten.UI.VectorPath path = new();

	static Rinten.UI.VectorPath RectPath( in Canvas canvas, Rect rect ) =>
		path.Clear().AddRectangle( canvas.Origin.x + rect.Left, canvas.Origin.y + rect.Top, rect.Width, rect.Height );

	static Rinten.UI.VectorPath CirclePath( in Canvas canvas, Vector2 center, float radius ) =>
		path.Clear().AddEllipse( canvas.Origin + center, radius, radius );

	public static void Polyline( in Canvas canvas, List<Vector2> points, Color color, float thickness )
	{
		if ( points.Count < 2 ) return;

		Rinten.UI.Panel.Draw.Polyline( Placed( canvas, points ), thickness, color, Rinten.UI.StrokeLinecap.Butt, Rinten.UI.StrokeLinejoin.Miter );
	}

	public static void Line( in Canvas canvas, Vector2 from, Vector2 to, Color color, float thickness )
	{
		Rinten.UI.Panel.Draw.Line( canvas.Origin + from, canvas.Origin + to, thickness, color, rounded: false );
	}

	public static void DottedLine( in Canvas canvas, Vector2 from, Vector2 to, Color color, float thickness )
	{
		Rinten.UI.Panel.Draw.Stroke( path.Clear().MoveTo( canvas.Origin + from ).LineTo( canvas.Origin + to ), thickness, color, dashes: dotPattern );
	}

	public static void DashedRect( in Canvas canvas, Rect rect, Color color, float thickness )
	{
		Rinten.UI.Panel.Draw.Stroke( RectPath( canvas, rect ), thickness, color, dashes: dashPattern );
	}

	public static void FillRect( in Canvas canvas, Rect rect, Color color )
	{
		Rinten.UI.Panel.Draw.Fill( RectPath( canvas, rect ), color );
	}

	public static void FillCircle( in Canvas canvas, Vector2 center, float radius, Color color )
	{
		Rinten.UI.Panel.Draw.Fill( CirclePath( canvas, center, radius ), color );
	}

	public static void StrokeCircle( in Canvas canvas, Vector2 center, float radius, Color color, float thickness )
	{
		Rinten.UI.Panel.Draw.Stroke( CirclePath( canvas, center, radius ), thickness, color );
	}

	public static void DottedCircle( in Canvas canvas, Vector2 center, float radius, Color color, float thickness )
	{
		Rinten.UI.Panel.Draw.Stroke( CirclePath( canvas, center, radius ), thickness, color, dashes: dotPattern );
	}

	public static void StrokeRect( in Canvas canvas, Rect rect, Color color, float thickness )
	{
		Rinten.UI.Panel.Draw.Stroke( RectPath( canvas, rect ), thickness, color );
	}

	/// <summary>The band between two runs of points that share their x positions.</summary>
	public static void Band( in Canvas canvas, List<Vector2> top, List<Vector2> bottom, Color color, List<Vector2> scratch )
	{
		if ( top.Count < 2 || bottom.Count < 2 ) return;

		path.Clear().MoveTo( canvas.Origin + top[0] );
		for ( var i = 1; i < top.Count; i++ ) path.LineTo( canvas.Origin + top[i] );
		for ( var i = bottom.Count - 1; i >= 0; i-- ) path.LineTo( canvas.Origin + bottom[i] );

		Rinten.UI.Panel.Draw.Fill( path.Close(), color );
	}

	/// <summary>A grid step that lands on a round number - 1, 2, 5, 10 - rather than wherever the range fell.</summary>
	public static float NiceIncrement( float range, int ticks )
	{
		if ( ticks < 1 ) ticks = 1;
		if ( range <= 0 || !float.IsFinite( range ) ) return 1.0f;

		var roughStep = range / ticks;
		var exponent = MathF.Floor( MathF.Log10( roughStep ) );
		var fraction = roughStep / MathF.Pow( 10, exponent );

		var niceFraction =
			fraction < 1.5f ? 1f :
			fraction < 3f ? 2f :
			fraction < 7f ? 5f : 10f;

		return niceFraction * MathF.Pow( 10, exponent );
	}
}
