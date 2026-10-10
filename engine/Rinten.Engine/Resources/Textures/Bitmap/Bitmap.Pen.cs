using SkiaSharp;

namespace Rinten;

public partial class Bitmap
{
	private SKPaint _pen;

	SKPaint GetPen()
	{
		if ( _pen is not null ) return _pen;

		//defaults
		_pen = new SKPaint();
		_pen.IsAntialias = true;
		return _pen;
	}

	/// <summary>Sets the pen for drawing with a solid color and stroke style.</summary>
	public void SetAntialias( bool on )
	{
		var pen = GetPen();
		pen.IsAntialias = on;
	}

	/// <summary>Sets the pen to use a specific blend mode.</summary>
	public void SetBlendMode( BlendMode blendMode )
	{
		var pen = GetPen();
		//pen.BlendMode = blendMode;
		//TODO
	}

	/// <summary>Sets the pen for drawing with a solid color and stroke style.</summary>
	public void SetPen( Color color, float width )
	{
		var pen = GetPen();
		pen.Color = color.ToSk();
		pen.ColorF = color.ToSkF();
		pen.StrokeWidth = width;
		pen.Style = SKPaintStyle.Stroke;
		pen.PathEffect = null;
		pen.Shader = default;
	}

	/// <summary>Sets the pen for drawing dashed or dotted lines.</summary>
	public void SetDashedPen( Color color, float width, float[] dashPattern )
	{
		var pen = GetPen();
		pen.Color = color.ToSk();
		pen.ColorF = color.ToSkF();
		pen.StrokeWidth = width;
		pen.Style = SKPaintStyle.Stroke;
		pen.PathEffect = SKPathEffect.CreateDash( dashPattern, 0 );
		pen.Shader = default;
	}

	/// <summary>Sets the pen for drawing filled shapes with a solid color.</summary>
	public void SetFill( Color color )
	{
		var pen = GetPen();
		pen.Color = color.ToSk();
		pen.ColorF = color.ToSkF();
		pen.Style = SKPaintStyle.Fill;
		pen.StrokeWidth = default;
		pen.Shader = default;
	}

	static readonly float[] gradientPoints = Enumerable.Range( 0, 64 ).Select( x => x / 64.0f ).ToArray();

	/// <summary>Sets the pen for drawing with a linear gradient.</summary>
	public void SetLinearGradient( Vector2 start, Vector2 end, Gradient gradient )
	{
		var pen = GetPen();
		pen.StrokeWidth = default;

		using var colorSpace = SKColorSpace.CreateSrgbLinear();

		pen.Shader = SKShader.CreateLinearGradient(
			new SKPoint( start.x, start.y ),
			new SKPoint( end.x, end.y ),
			gradientPoints.Select( x => gradient.Evaluate( x ).ToSkF() ).ToArray(),
			colorSpace,
			gradientPoints,
			SKShaderTileMode.Clamp
		);
		pen.Style = SKPaintStyle.Fill; //Gradients typically fill shapes
	}

	/// <summary>Sets the pen for drawing with a radial gradient.</summary>
	public void SetRadialGradient( Vector2 center, float radius, Gradient gradient )
	{
		var pen = GetPen();
		pen.StrokeWidth = default;

		using var colorSpace = SKColorSpace.CreateSrgbLinear();

		pen.Shader = SKShader.CreateRadialGradient(
			new SKPoint( center.x, center.y ),
			radius,
			gradientPoints.Select( x => gradient.Evaluate( x ).ToSkF() ).ToArray(),
			colorSpace,
			gradientPoints,
			SKShaderTileMode.Clamp
		);
		pen.IsAntialias = true;
		pen.Style = SKPaintStyle.Fill; //Gradients typically fill shapes
	}

}
