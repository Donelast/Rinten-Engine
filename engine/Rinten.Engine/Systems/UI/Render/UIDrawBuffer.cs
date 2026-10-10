using Rinten.Rendering;

namespace Rinten.UI;

//Collects Panel.Draw calls in editor windows, whose renderer builds vertices instead of descriptors
internal interface IUiDrawCollector
{
	void Box( in BoxDrawDescriptor desc );
	void Shadow( in ShadowDrawDescriptor desc );
	void Outline( in OutlineDrawDescriptor desc );

	//A vector path filled, or stroked where a stroke is given, in the space of Panel.Box.
	void Path( VectorPath path, Color color, in VectorStrokeStyle? stroke, FillRule rule );
}

//Thread-local buffer for collecting UI draw descriptors during panel.OnDraw(). Descriptors are routed directly to the active RenderLayer.
internal class UIDrawBuffer
{
	[ThreadStatic] static UIDrawBuffer _current;

	internal static UIDrawBuffer Current => _current ??= new();

	/// <summary>The target layer for draw calls. Set by the renderer before OnDraw().</summary>
	public RenderLayer ActiveLayer;

	/// <summary>Takes the draw calls instead of <see cref="ActiveLayer"/> while it is set.</summary>
	public IUiDrawCollector Collector;

	/// <summary>The scale factor from logical to physical pixels for the current panel. Set before OnDraw().</summary>
	public float ScaleToScreen = 1f;

	/// <summary>Accumulated CSS opacity for the current panel. Set before OnDraw().</summary>
	public float Opacity = 1f;

	/// <summary>Active blend mode override for the current panel. Set before OnDraw().</summary>
	public BlendMode OverrideBlendMode = BlendMode.Normal;

	public void AddBox( in BoxDrawDescriptor desc )
	{
		if ( Collector is not null )
		{
			Collector.Box( desc );
			return;
		}

		ActiveLayer.AddBox( desc );
	}

	public void AddShadow( in ShadowDrawDescriptor desc )
	{
		if ( Collector is not null )
		{
			Collector.Shadow( desc );
			return;
		}

		ActiveLayer.AddShadow( desc );
	}

	public void AddOutline( in OutlineDrawDescriptor desc )
	{
		if ( Collector is not null )
		{
			Collector.Outline( desc );
			return;
		}

		ActiveLayer.AddOutline( desc );
	}

	/// <summary>A vector path, filled or stroked. Only the walk's collector can tessellate one; drawn into a layer of the old renderer instead, a stroke falls back to a box a length and a fill to nothing.</summary>
	public void AddPath( VectorPath path, Color color, in VectorStrokeStyle? stroke, FillRule rule )
	{
		if ( path is null || path.IsEmpty ) return;

		if ( Collector is not null )
		{
			Collector.Path( path, color, stroke, rule );
			return;
		}

		if ( stroke is not { } line || ActiveLayer is null ) return;

		var contours = new List<VectorContour>();
		VectorTessellator.Flatten( path, VectorTransform.Identity, 0.25f, contours );

		foreach ( var contour in contours )
		{
			var n = contour.Points.Count;
			var segments = contour.Closed ? n : n - 1;

			for ( var i = 0; i < segments; i++ )
				ActiveLayer.AddBox( Panel.Draw.SegmentBox( contour.Points[i], contour.Points[( i + 1 ) % n], line.Width, color, line.Cap == StrokeLinecap.Round ) );
		}
	}

	public void AddBackdrop( in BackdropDrawDescriptor desc )
	{
		if ( Collector is not null ) return;

		ActiveLayer.Backdrops.Add( desc );
	}
}
