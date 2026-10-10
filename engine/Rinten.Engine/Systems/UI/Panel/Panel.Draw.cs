using Rinten.Rendering;

namespace Rinten.UI;

public partial class Panel
{
	/// <summary>To be used inside OnDraw to add custom shapes, textures and text to a panel. These draw calls will be batched together with the panel's CSS-styled content for efficient rendering. public override void OnDraw() { Draw.Rect( new Rect( 0, 0, 100, 100 ), Color.Red, cornerRadius: 8 ); Draw.Text( "Hello", new Rect( 10, 10, 80, 20 ), 14, Color.White ); }</summary>
	public static class Draw
	{
		/// <summary>Draws a filled rectangle.</summary>
		public static void Rect( Rect rect, Color color, float cornerRadius = 0 )
		{
			UIDrawBuffer.Current.AddBox( new BoxDrawDescriptor( rect, color )
			{
				BorderRadius = new Vector4( cornerRadius ),
			} );
		}

		/// <summary>Draws a filled rectangle with per-corner radius control.</summary>
		public static void Rect( Rect rect, Color color, Vector4 cornerRadius )
		{
			UIDrawBuffer.Current.AddBox( new BoxDrawDescriptor( rect, color )
			{
				BorderRadius = cornerRadius,
			} );
		}

		/// <summary>Draws a filled circle.</summary>
		public static void Circle( Vector2 center, float radius, Color color )
		{
			var size = new Vector2( radius * 2f );
			var rect = new Rect( center - size * 0.5f, size );
			Rect( rect, color, radius * 2f );
		}

		/// <summary>Draws a straight line between two points.</summary>
		public static void Line( Vector2 from, Vector2 to, float width, Color color, bool rounded = true )
		{
			if ( width <= 0.0f ) return;

			Stroke( Scratch.Clear().MoveTo( from ).LineTo( to ), width, color, rounded ? StrokeLinecap.Round : StrokeLinecap.Butt );
		}

		//One stroke with round ends and round corners, tessellated as a whole: the lengths meet without overlapping, so a translucent line is not darker at its joints.
		/// <summary>Draws a run of connected lines - a curve, by the time it is drawn.</summary>
		public static void Polyline( IReadOnlyList<Vector2> points, float width, Color color )
		{
			Polyline( points, width, color, StrokeLinecap.Round, StrokeLinejoin.Round );
		}

		/// <summary>Draws a run of connected lines with the ends and corners asked for, dashed where <paramref name="dashes"/> gives a pattern of dash and gap lengths in pixels.</summary>
		public static void Polyline( IReadOnlyList<Vector2> points, float width, Color color,
			StrokeLinecap cap, StrokeLinejoin join, bool closed = false, float[] dashes = null, float dashOffset = 0.0f )
		{
			if ( points is null || points.Count < 2 || width <= 0.0f ) return;

			var path = Scratch.Clear().MoveTo( points[0] );
			for ( var i = 1; i < points.Count; i++ ) path.LineTo( points[i] );
			if ( closed ) path.Close();

			Stroke( path, width, color, cap, join, dashes, dashOffset );
		}

		/// <summary>Strokes a vector path - lines, curves and arcs - in the same space as <see cref="Panel.Box"/>: real joins and caps, dashes where a pattern is given, and an antialiased edge a screen pixel wide under any transform.</summary>
		public static void Stroke( VectorPath path, float width, Color color,
			StrokeLinecap cap = StrokeLinecap.Butt, StrokeLinejoin join = StrokeLinejoin.Miter,
			float[] dashes = null, float dashOffset = 0.0f, float miterLimit = 4.0f )
		{
			if ( width <= 0.0f || color.a <= 0.0f ) return;

			UIDrawBuffer.Current.AddPath( path, color, new VectorStrokeStyle
			{
				Width = width,
				Cap = cap,
				Join = join,
				MiterLimit = miterLimit,
				Dashes = dashes,
				DashOffset = dashOffset,
			}, FillRule.NonZero );
		}

		/// <summary>Fills a vector path, in the same space as <see cref="Panel.Box"/>. Open runs are closed; which parts are inside is the <paramref name="rule"/>'s to say.</summary>
		public static void Fill( VectorPath path, Color color, FillRule rule = FillRule.NonZero )
		{
			if ( color.a <= 0.0f ) return;

			UIDrawBuffer.Current.AddPath( path, color, null, rule );
		}

		//The path Line and Polyline are built in, kept so drawing one allocates nothing.
		[ThreadStatic] private static VectorPath scratch;

		private static VectorPath Scratch => scratch ??= new VectorPath();

		//A length of line as a turned box, for a renderer that cannot take a path.
		internal static BoxDrawDescriptor SegmentBox( Vector2 from, Vector2 to, float width, Color color, bool rounded )
		{
			var along = to - from;
			var length = MathF.Max( along.Length, 0.0001f );
			var centre = (from + to) * 0.5f;
			var rect = new Rect( centre.x - (length * 0.5f), centre.y - (width * 0.5f), length, width );

			return new BoxDrawDescriptor( rect, color )
			{
				BorderRadius = new Vector4( rounded ? width * 0.5f : 0.0f ),
				LocalTransform = Matrix.CreateRotationZ( MathF.Atan2( along.y, along.x ).RadianToDegree(), centre ),
			};
		}

		/// <summary>Draws a texture within the given rectangle.</summary>
		public static void Texture( Texture texture, Rect rect, Color? tint = null )
		{
			UIDrawBuffer.Current.AddBox( new BoxDrawDescriptor( rect, Color.Transparent )
			{
				BackgroundImage = texture,
				BackgroundTint = tint ?? Color.White,
				BackgroundRepeat = BackgroundRepeat.Clamp,
			} );
		}

		/// <summary>Draws a text string within the given rectangle.</summary>
		public static void Text( string text, Rect rect, float size, Color color, TextFlag flags = TextFlag.LeftTop, string font = "Roboto" )
		{
			var buf = UIDrawBuffer.Current;
			var scale = buf.ScaleToScreen;

			var scope = new TextRendering.Scope( text, color, size * scale, font );
			var tb = TextRendering.GetOrCreateTextBlock( scope, flags, rect.Size == default ? new Vector2( 8096 ) : rect.Size );
			tb.MakeReady();
			var texture = tb.Texture;
			if ( texture is null ) return;

			var textRect = rect.Align( texture.Size, flags ).Floor();
			var tint = Color.White;
			tint.a *= buf.Opacity;

			buf.AddBox( new BoxDrawDescriptor( textRect, Color.Transparent )
			{
				BackgroundImage = texture,
				BackgroundRect = new Vector4( 0, 0, textRect.Width, textRect.Height ),
				BackgroundTint = tint,
				OverrideBlendMode = BlendMode.PremultipliedAlpha,
				PremultiplyAlpha = true,
				FilterMode = FilterMode.Bilinear,
			} );
		}

		/// <summary>Draws a box shadow (drop shadow or inset shadow).</summary>
		public static void Shadow( Rect rect, Color color, float blur = 0, float spread = 0, Vector2 offset = default, float cornerRadius = 0, bool inset = false )
		{
			UIDrawBuffer.Current.AddShadow( new ShadowDrawDescriptor( rect, color )
			{
				BorderRadius = new Vector4( cornerRadius ),
				Blur = blur,
				Spread = spread,
				Offset = offset,
				Inset = inset,
			} );
		}

		/// <summary>Draws an outline (stroke) around a rectangle.</summary>
		public static void Outline( Rect rect, Color color, float width, float cornerRadius = 0, float offset = 0 )
		{
			UIDrawBuffer.Current.AddOutline( new OutlineDrawDescriptor( rect, color, width )
			{
				BorderRadius = new Vector4( cornerRadius ),
				Offset = offset,
			} );
		}
	}

	//Draws a texture with this panel's CSS box styling, for Image, ScenePanel, SvgPanel and similar
	protected void DrawBackgroundTexture( Texture texture, Length defaultSize )
	{
		var style = ComputedStyle;
		if ( style == null ) return;

		if ( texture == Texture.Invalid )
			texture = null;

		var opacity = CachedRenderOpacity;
		var rect = Box.Rect;
		var size = (rect.Width + rect.Height) * 0.5f;

		var color = style.BackgroundColor.Value;
		color.a *= opacity;

		var desc = new BoxDrawDescriptor( rect, color )
		{
			BorderRadius = new Vector4(
				style.BorderBottomRightRadius.Value.GetPixels( size ),
				style.BorderTopRightRadius.Value.GetPixels( size ),
				style.BorderBottomLeftRadius.Value.GetPixels( size ),
				style.BorderTopLeftRadius.Value.GetPixels( size )
			),
			BorderSize = new Vector4(
				style.BorderLeftWidth.Value.GetPixels( size ),
				style.BorderTopWidth.Value.GetPixels( size ),
				style.BorderRightWidth.Value.GetPixels( size ),
				style.BorderBottomWidth.Value.GetPixels( size )
			),
			BorderColorL = style.BorderLeftColor.Value.WithAlphaMultiplied( opacity ),
			BorderColorT = style.BorderTopColor.Value.WithAlphaMultiplied( opacity ),
			BorderColorR = style.BorderRightColor.Value.WithAlphaMultiplied( opacity ),
			BorderColorB = style.BorderBottomColor.Value.WithAlphaMultiplied( opacity ),
			BackgroundTint = style.BackgroundTint.Value.WithAlphaMultiplied( opacity ),
			BackgroundRepeat = style.BackgroundRepeat ?? BackgroundRepeat.Repeat,
			BackgroundAngle = style.BackgroundAngle.Value.GetPixels( 360.0f ).DegreeToRadian(),
			OverrideBlendMode = CachedOverrideBlendMode,
			FilterMode = (style.ImageRendering ?? ImageRendering.Anisotropic) switch
			{
				ImageRendering.Point => FilterMode.Point,
				ImageRendering.Bilinear => FilterMode.Bilinear,
				ImageRendering.Trilinear => FilterMode.Trilinear,
				_ => FilterMode.Anisotropic
			},
		};

		if ( style.BorderImageSource != null )
		{
			desc.BorderImageTexture = style.BorderImageSource;
			desc.BorderImageSlice = new Vector4(
				style.BorderImageWidthLeft.Value.GetPixels( size ),
				style.BorderImageWidthTop.Value.GetPixels( size ),
				style.BorderImageWidthRight.Value.GetPixels( size ),
				style.BorderImageWidthBottom.Value.GetPixels( size )
			);
			desc.BorderImageRepeat = style.BorderImageRepeat ?? BorderImageRepeat.Stretch;
			desc.BorderImageFill = style.BorderImageFill ?? BorderImageFill.Unfilled;
			desc.BorderImageTint = style.BorderImageTint.Value.WithAlphaMultiplied( opacity );
		}

		if ( texture != null )
		{
			texture.MarkUsed();

			desc.BackgroundImage = texture;
			desc.BackgroundRect = ImageRect.Calculate( new ImageRect.Input
			{
				ScaleToScreen = ScaleToScreen,
				Image = texture,
				PanelRect = rect,
				DefaultSize = defaultSize,
				ImagePositionX = style.BackgroundPositionX,
				ImagePositionY = style.BackgroundPositionY,
				ImageSizeX = style.BackgroundSizeX,
				ImageSizeY = style.BackgroundSizeY,
			} ).Rect;
		}

		CachedDescriptors.AddBox( desc );
	}
}
