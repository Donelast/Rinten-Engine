using Rinten.Rendering;

namespace Rinten;

/// <summary>Applies color grading to the camera</summary>
[Title( "Color Grading" )]
[Category( "Post Processing" )]
[Icon( "center_focus_strong" )]
public sealed class ColorGrading : BasePostProcess<ColorGrading>
{
	public enum GradingType
	{
		None,
		TemperatureControl,
		LUT
	};

	public enum ColorSpaceEnum
	{
		None,
		RGB,
		HSV
	}

	[Group( "Grading" )]
	[Property] public GradingType GradingMethod { get; set; } = GradingType.None;

	[Group( "Grading" )]
	[Range( 1000, 40000 )]
	[ShowIf( nameof( GradingMethod ), GradingType.TemperatureControl )]
	[Property] public float ColorTempK { get; set; } = 6500.0f;

	[Group( "Grading" )]
	[Range( 0, 1 )]
	[ShowIf( nameof( GradingMethod ), GradingType.TemperatureControl )]
	[Property] public float BlendFactor { get; set; } = 1.0f;

	[Group( "Grading" )]
	[ShowIf( nameof( GradingMethod ), GradingType.LUT )]
	[Property] public Texture LookupTexture { get; set; } = Texture.White;

	//Per channel

	[Group( "Per Channel" )]
	[Property]
	public ColorSpaceEnum ColorSpace { get; set; } = ColorSpaceEnum.None;

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.RGB )]
	public Curve RedCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.RGB )]
	public Curve GreenCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.RGB )]
	public Curve BlueCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.HSV )]
	public Curve HueCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.HSV )]
	public Curve SaturationCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.HSV )]
	public Curve ValueCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	//The engine's own - see colorgrading.shader. What this named before was a shader that is not in this engine, so the effect drew nothing.
	private static Material Shader => field ??= Material.FromShader( "shaders/postprocess/colorgrading.shader" );

	public override void Render()
	{
		var blendFactor = GetWeighted( x => x.BlendFactor );
		var colorTempK = GetWeighted( x => x.ColorTempK );

		if ( blendFactor <= 0f )
			return;

		if ( GradingMethod == GradingType.None && ColorSpace == ColorSpaceEnum.None )
			return;

		Attributes.Set( "BlendFactor", blendFactor );
		Attributes.Set( "WhiteBalance", Kelvin( colorTempK ) );

		//Numbers rather than combos: this engine bakes one pipeline a shader, and what a combo chose is a branch the shader takes on a number.
		Attributes.Set( "GradingMethod", (int)GradingMethod );
		Attributes.Set( "ColorSpace", (int)ColorSpace );

		if ( GradingMethod == GradingType.LUT )
		{
			var lookup = LookupTexture.IsValid() ? LookupTexture : Texture.White;
			Attributes.Set( "LookupTexture", lookup );
		}

		if ( ColorSpace == ColorSpaceEnum.RGB )
		{
			ProcessCurve( RedCurve, "R", Attributes );
			ProcessCurve( GreenCurve, "G", Attributes );
			ProcessCurve( BlueCurve, "B", Attributes );
		}
		if ( ColorSpace == ColorSpaceEnum.HSV )
		{
			//Under the red, green and blue names, which is where the shader reads hue, saturation and value from - sent as H, S and V they reached nothing.
			ProcessCurve( HueCurve, "R", Attributes );
			ProcessCurve( SaturationCurve, "G", Attributes );
			ProcessCurve( ValueCurve, "B", Attributes );
		}

		//Before the tonemap, with the rest of the colour work - the stage after it draws on the window and cannot read what the tonemap made, so a grade
		//hung there was given the raw picture and threw the curve away. A point effect in the light the world was shaded in - see colorgrading.shader.
		Apply( Shader, Stage.BeforePostProcess, 7500, "ColorGrading" );
	}

	//Black-body colour at a temperature relative to daylight (6500 is neutral), Tanner Helland's fit
	private static Vector3 Kelvin( float kelvin )
	{
		var t = Math.Clamp( kelvin, 1000.0f, 40000.0f ) / 100.0f;

		var r = t <= 66.0f ? 1.0f : Saturate( 1.292936f * MathF.Pow( t - 60.0f, -0.1332047592f ) );

		var g = t <= 66.0f
			? Saturate( ( 0.3900815788f * MathF.Log( t ) ) - 0.6318414438f )
			: Saturate( 1.129890861f * MathF.Pow( t - 60.0f, -0.0755148492f ) );

		var b = t >= 66.0f
			? 1.0f
			: t <= 19.0f
				? 0.0f
				: Saturate( ( 0.5432067891f * MathF.Log( t - 10.0f ) ) - 1.1962540891f );

		//Against what the same fit gives for 6500, so that daylight is one.
		return new Vector3( r / 1.0f, g / 0.9760f, b / 0.9925f );

		static float Saturate( float x ) => Math.Clamp( x, 0.0f, 1.0f );
	}

	void ProcessCurve( Curve curve, string ChannelLetter, RenderAttributes attributes )
	{
		int TotalFrames = curve.Length;

		//Cap the maximum points for this to 4
		if ( TotalFrames > 4 )
		{
			TotalFrames = 4;
		}

		Span<float> xArray = stackalloc float[4];
		attributes.Set( "CurveFrames" + ChannelLetter, TotalFrames );
		for ( int i = 0; i < TotalFrames; ++i )
		{
			var frame = curve.Frames[i];
			xArray[i] = frame.Time;
			Vector4 localFrame = new Vector4( frame.Time, frame.Value, frame.In, frame.Out );
			attributes.Set( "CurveFrame" + ChannelLetter + i, localFrame );
		}

		//Store the reciprocals of the 'x' deltas  (time in Curve.cs)  between successive
		//point pairs. These are then passed to the pixel shader to avoid divisions.
		Vector4 divisorVector = new Vector4( 0 );
		if ( TotalFrames >= 2 )
		{
			divisorVector.x = 1.0f / Math.Max( xArray[1] - xArray[0], 0.00001f );
			if ( TotalFrames >= 3 )
			{
				divisorVector.y = 1.0f / Math.Max( xArray[2] - xArray[1], 0.00001f );
				if ( TotalFrames >= 4 )
				{
					divisorVector.z = 1.0f / Math.Max( xArray[3] - xArray[2], 0.00001f );
				}
			}
		}
		attributes.Set( "CurveDivisors" + ChannelLetter, divisorVector );
	}
}
