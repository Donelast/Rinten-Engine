namespace Rinten.Rendering;

/// <summary>When manually rendering a camera this will let you override specific elements of that render. This means you can use most of the camera's properties, but override some without disturbing the camera itself.</summary>
public struct ViewSetup
{
	/// <summary>Overrides the camera's position and rotation</summary>
	public Transform? Transform;

	/// <summary>Overrides the camera's field of view</summary>
	public float? FieldOfView;

	/// <summary>Overrides the camera's znear</summary>
	public float? ZNear;

	/// <summary>Overrides the camera's zfar</summary>
	public float? ZFar;

	/// <summary>Overrides the camera's clear color</summary>
	public Color? ClearColor;

	/// <summary>Overrides the camera's projection matrix</summary>
	public Matrix? ProjectionMatrix;

	/// <summary>Allows overriding gradient fog for this view</summary>
	public GradientFogSetup? GradientFog;

	/// <summary>If set then the regular scene's ambient light will be multiplied by this</summary>
	public Color? AmbientLightTint;

	/// <summary>If set then this will be added to the ambient light color</summary>
	public Color? AmbientLightAdd;

	/// <summary>Clipspace is usually used for rendering posters, or center-offsetting the view. You're basically zooming into a subrect of the clipspace. So imagine you draw a smaller rect inside the first rect of the frustum.. that's what you're gonna render - that rect.</summary>
	public Vector4? ClipSpaceBounds;

	/// <summary>When rendering to a texture, this allows you to flip the view horizontally.</summary>
	public bool? FlipX;

	/// <summary>When rendering to a texture, this allows you to flip the view vertically.</summary>
	public bool? FlipY;

	/// <summary>Whether post processing should be enabled for this view. If null it will use the camera's setting.</summary>
	public bool? EnablePostprocessing;

	/// <summary>If you're rendering a subview this will allow the renderer to find the same view again next frame</summary>
	public int ViewHash;
}

/// <summary>Setup for defining gradient fog in a view</summary>
public struct GradientFogSetup
{
	/// <summary>Whether the fog is enabled.</summary>
	public bool Enabled { get; set; }

	/// <summary>Start distance of the fog.</summary>
	public float StartDistance { get; set; }

	/// <summary>End distance of the fog.</summary>
	public float EndDistance { get; set; }

	/// <summary>The starting height of the gradient fog.</summary>
	public float StartHeight { get; set; }

	/// <summary>The ending height of the gradient fog.</summary>
	public float EndHeight { get; set; }

	/// <summary>The maximum opacity of the gradient fog.</summary>
	public float MaximumOpacity { get; set; }

	/// <summary>The color of the gradient fog.</summary>
	public Color Color { get; set; }

	/// <summary>The exponent controlling the distance-based falloff of the fog.</summary>
	public float DistanceFalloffExponent { get; set; }

	/// <summary>The exponent controlling the vertical falloff of the fog.</summary>
	public float VerticalFalloffExponent { get; set; }

	/// <summary>Whether the fog takes the sky's colour in the direction it is looked through, <see cref="Color"/> tinting it - so what is far away sinks into the sky behind it rather than into a flat wall. See <see cref="CubemapFog"/>.</summary>
	public bool ColorFromSky { get; set; }


	internal void Apply( RenderAttributes attributes )
	{
		if ( !Enabled )
		{
			attributes.SetCombo( "D_ENABLE_GRADIENT_FOG", 0 );
			return;
		}

		attributes.SetCombo( "D_ENABLE_GRADIENT_FOG", 1 );
		attributes.Set( "GradientFogParams", new Vector4( StartDistance, EndDistance, StartHeight, EndHeight ) );
		attributes.Set( "GradientFogParams2", new Vector4( MaximumOpacity, DistanceFalloffExponent, VerticalFalloffExponent, 0.0f ) );
		attributes.Set( "GradientFogParams3", new Vector4( Color, 0.0f ) * Color.a );
	}

	/// <summary>Lerp this GradientFogSetup to a another, allowing transition states.</summary>
	public GradientFogSetup LerpTo( GradientFogSetup desired, float delta, bool clamp = true )
	{
		if ( Enabled != desired.Enabled )
			return desired;

		GradientFogSetup newState = default;

		newState.Enabled = desired.Enabled;
		newState.ColorFromSky = desired.ColorFromSky;

		newState.StartDistance = StartDistance.LerpTo( desired.StartDistance, delta, clamp );
		newState.EndDistance = EndDistance.LerpTo( desired.EndDistance, delta, clamp );
		newState.StartHeight = StartHeight.LerpTo( desired.StartHeight, delta, clamp );
		newState.EndHeight = EndHeight.LerpTo( desired.EndHeight, delta, clamp );
		newState.MaximumOpacity = MaximumOpacity.LerpTo( desired.MaximumOpacity, delta, clamp );
		newState.DistanceFalloffExponent = DistanceFalloffExponent.LerpTo( desired.DistanceFalloffExponent, delta, clamp );
		newState.VerticalFalloffExponent = VerticalFalloffExponent.LerpTo( desired.VerticalFalloffExponent, delta, clamp );
		newState.Color = Color.Lerp( Color, desired.Color, delta );

		return newState;
	}
}


/// <summary>Allows special setup for reflections, such as offsetting the reflection plane</summary>
public struct ReflectionSetup
{
	/// <summary>Allows overriding everything you normally can</summary>
	public ViewSetup ViewSetup;

	/// <summary>Offset the reflection plane's clip plane by this much</summary>
	public float ClipOffset;

	/// <summary>If true we'll render the reflection even if we're behind the plane</summary>
	public bool RenderBehind;

	/// <summary>If we can't render the reflection and this is set, we'll clear the render target to this color</summary>
	public Color? FallbackColor { get; set; }

	//TODO - stencil? clip rect?

}

/// <summary>Allows special setup for refraction, such as offsetting the clip plane</summary>
public struct RefractionSetup
{
	/// <summary>Allows overriding everything you normally can</summary>
	public ViewSetup ViewSetup;

	/// <summary>Offset the reflection plane's clip plane by this much</summary>
	public float ClipOffset;

	/// <summary>If true we'll render the reflection even if we're behind the plane</summary>
	public bool RenderBehind;

	/// <summary>If we can't render the reflection and this is set, we'll clear the render target to this color</summary>
	public Color? FallbackColor { get; set; }

	//TODO - stencil? clip rect?

}

/// <summary>When manually rendering a Renderer this will let you override specific elements of that render. This means you can use most of the Renderer's properties, but override some without disturbing the Renderer itself.</summary>
public struct RendererSetup
{
	/// <summary>Overrides the transform used for rendering</summary>
	public Transform? Transform;

	/// <summary>Overrides the color used for rendering</summary>
	public Color? Color;

	/// <summary>Overrides the material used for rendering</summary>
	public Material Material;

	/// <summary>Draws over whatever stands in front of it: the depth is not tested. What an outline draws the hidden half of something behind a wall with.</summary>
	public bool IgnoreDepth;

	/// <summary>Material's shader reads the mesh's own vertices (pulled geometry); otherwise such models are skipped</summary>
	public bool ReadsOwnVertices;
}
