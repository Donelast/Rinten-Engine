namespace Rinten;

public static partial class Gizmo
{
	public sealed partial class GizmoDraw
	{
		//Shapes drawn flat on the screen rather than in the world: the rectangle a box select drags out, the camera speed bar, the
		//orientation gizmo in the corner. Plain triangles in screen pixels, built here and put in the frame's list whole, one shape at a time.
		private static readonly List<Vertex> screenShape = new();

		//A point on the screen, in pixels, with its colour.
		private static Vertex ScreenVertex( Vector2 at, Color32 tint ) => new()
		{
			Position = new Vector3( at.x, at.y, 0 ),
			Color = tint,
			TexCoord0 = new Vector4( 0, 0, 0, 0 ),
		};

		private static void ScreenTriangle( Vector2 a, Vector2 b, Vector2 c, Color32 tint )
		{
			screenShape.Add( ScreenVertex( a, tint ) );
			screenShape.Add( ScreenVertex( b, tint ) );
			screenShape.Add( ScreenVertex( c, tint ) );
		}

		private static void ScreenQuad( Vector2 topLeft, Vector2 topRight, Vector2 bottomLeft, Vector2 bottomRight, Color32 tint )
		{
			ScreenTriangle( topLeft, topRight, bottomLeft, tint );
			ScreenTriangle( bottomLeft, topRight, bottomRight, tint );
		}

		private static void ScreenQuad( Rect rect, Color32 tint )
		{
			ScreenQuad(
				new Vector2( rect.Left, rect.Top ),
				new Vector2( rect.Right, rect.Top ),
				new Vector2( rect.Left, rect.Bottom ),
				new Vector2( rect.Right, rect.Bottom ),
				tint );
		}

		//Hands what was built to the frame and starts the next one.
		private static void FlushScreenShape()
		{
			Target?.AddFlat( System.Runtime.InteropServices.CollectionsMarshal.AsSpan( screenShape ), null, LifeFor( 0.0f ) );

			screenShape.Clear();
		}

		//The rounded corners are not drawn, and the blend is always ordinary: both were a UI shader's, and what draws this is triangles.
		/// <summary>Draw a rect, on the screen</summary>
		public void ScreenRect( Rect rect, Color color, Vector4 borderRadius = default, Color borderColor = default, Vector4 borderSize = default, BlendMode blendMode = BlendMode.Normal )
		{
			if ( Target is null ) return;

			screenShape.Clear();

			if ( color.a > 0 ) ScreenQuad( rect, color );

			if ( borderSize.Length != 0 && borderColor.a > 0 )
			{
				//Left, right, top and bottom as their own bars, each inset by the ones it meets so the corners are covered once.
				var (l, t, r, b) = (borderSize.x, borderSize.y, borderSize.z, borderSize.w);

				if ( l > 0 ) ScreenQuad( new Rect( rect.Left, rect.Top, l, rect.Height ), borderColor );
				if ( r > 0 ) ScreenQuad( new Rect( rect.Right - r, rect.Top, r, rect.Height ), borderColor );
				if ( t > 0 ) ScreenQuad( new Rect( rect.Left + l, rect.Top, rect.Width - l - r, t ), borderColor );
				if ( b > 0 ) ScreenQuad( new Rect( rect.Left + l, rect.Bottom - b, rect.Width - l - r, b ), borderColor );
			}

			FlushScreenShape();
		}

		/// <summary>Draw a line, on the screen, with a thickness in pixels.</summary>
		public void ScreenLine( Vector2 from, Vector2 to, float thickness, Color color )
		{
			if ( Target is null ) return;
			if ( color.a <= 0 || thickness <= 0 ) return;

			var delta = to - from;
			var length = delta.Length;
			if ( length <= 0 ) return;

			//The line as a quad: along it, and half its thickness either side.
			var along = delta / length;
			var across = new Vector2( -along.y, along.x ) * ( thickness * 0.5f );

			screenShape.Clear();

			ScreenQuad( from - across, to - across, from + across, to + across, color );

			FlushScreenShape();
		}

		/// <summary>Draw a circle, on the screen, filled and optionally ringed.</summary>
		public void ScreenCircle( Vector2 center, float radius, Color color, Color borderColor = default, float borderSize = 0.0f )
		{
			if ( Target is null ) return;
			if ( radius <= 0 ) return;

			//Enough sides that the roundness survives being a polygon, and no more: these are a handful of pixels across.
			var sides = System.Math.Clamp( (int)( radius * 1.6f ) + 8, 12, 64 );
			var step = MathF.Tau / sides;

			screenShape.Clear();

			if ( color.a > 0 )
			{
				for ( var at = 0; at < sides; ++at )
				{
					ScreenTriangle( center,
						center + Circle( at * step ) * radius,
						center + Circle( ( at + 1 ) * step ) * radius,
						color );
				}
			}

			if ( borderSize > 0 && borderColor.a > 0 )
			{
				var inner = MathF.Max( 0, radius - borderSize );

				for ( var at = 0; at < sides; ++at )
				{
					var a = Circle( at * step );
					var b = Circle( ( at + 1 ) * step );

					ScreenQuad(
						center + a * inner, center + b * inner,
						center + a * radius, center + b * radius,
						borderColor );
				}
			}

			FlushScreenShape();
		}

		private static Vector2 Circle( float radians ) => new( MathF.Cos( radians ), MathF.Sin( radians ) );

		/// <summary>Draw a picture on the screen, filling a rect of pixels</summary>
		public void ScreenTexture( Texture texture, Rect rect, Color? tint = null, float duration = 0 )
		{
			if ( texture is null || Target is null ) return;

			Color32 color = tint ?? global::Color.White;

			Span<Vertex> quad = stackalloc Vertex[6];

			quad[0] = Corner( rect.Left, rect.Top, 0, 0 );
			quad[1] = Corner( rect.Right, rect.Top, 1, 0 );
			quad[2] = Corner( rect.Left, rect.Bottom, 0, 1 );
			quad[3] = quad[2];
			quad[4] = quad[1];
			quad[5] = Corner( rect.Right, rect.Bottom, 1, 1 );

			Target.AddFlat( quad, texture, LifeFor( duration ) );

			Vertex Corner( float x, float y, float u, float v ) => new()
			{
				Position = new Vector3( x, y, 0 ),
				Color = color,
				TexCoord0 = new Vector4( u, v, 0, 0 ),
			};
		}
	}
}
