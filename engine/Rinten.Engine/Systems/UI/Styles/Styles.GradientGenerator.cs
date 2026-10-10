using System;
using System.Collections.Generic;

namespace Rinten.UI
{
	public partial class Styles
	{
		public struct GradientColorOffset
		{
			public Color color;
			public float? offset;

			/// <summary>Offset is a pixel length along the gradient line, not a fraction of it.</summary>
			public bool offsetIsPixels;

			public override int GetHashCode()
			{
				return HashCode.Combine( color, offset, offsetIsPixels );
			}
		}
		public struct GradientGenerator
		{
			public GradientColorOffset from;
			public GradientColorOffset to;
		}

		//An optional stop position after a colour: a percentage or a pixel length. False, with the error said, for anything else.
		private static bool TryReadStopPosition( ref Parse wp, string whole, out float? offset, out bool offsetIsPixels )
		{
			offset = null;
			offsetIsPixels = false;

			if ( !(wp.IsDigit || wp.Is( '-' ) || wp.Is( '.' )) || !wp.TryReadFloat( out var stop ) )
				return true;

			if ( wp.Is( '%' ) )
			{
				wp.Pointer++;
				offset = stop / 100;
				return true;
			}

			if ( wp.Is( "px", 0, true ) )
			{
				wp.Pointer += 2;
				offset = stop;
				offsetIsPixels = true;
				return true;
			}

			//A bare zero is a length too
			if ( stop == 0 && (wp.IsEnd || char.IsWhiteSpace( wp.Current )) )
			{
				offset = 0;
				return true;
			}

			Log.Error( $"Stop positions take a percentage or a pixel length: '{whole}'" );
			return false;
		}

		//The colour stops as written - colour, and a position only where one was given. The positions are
		//resolved where the gradient's length is known: see BakeRamp. False when nothing parsed at all.
		private bool TryParseStops( string token, out System.Collections.Immutable.ImmutableArray<GradientColorOffset> result )
		{
			result = default;

			var stops = System.Collections.Immutable.ImmutableArray.CreateBuilder<GradientColorOffset>();
			var p = new Parse( token );

			while ( !p.IsEnd )
			{
				p = p.SkipWhitespaceAndNewlines();

				var w = p.ReadSentence();
				var wp = new Parse( w );

				var c = Color.Parse( ref wp );
				if ( !c.HasValue )
				{
					Log.Error( $"Cannot read a color from '{w}'" );
					return false;
				}

				wp = wp.SkipWhitespaceAndNewlines();

				if ( !TryReadStopPosition( ref wp, w, out var offset, out var pixels ) )
					return false;

				stops.Add( new GradientColorOffset { color = c.Value, offset = offset, offsetIsPixels = pixels } );

				//"red 10% 30%" is two stops of the same colour
				wp = wp.SkipWhitespaceAndNewlines();
				if ( !wp.IsEnd )
				{
					if ( !TryReadStopPosition( ref wp, w, out var second, out var secondPixels ) || !second.HasValue )
					{
						Log.Error( $"Extra text found after color stop: '{w}'" );
						return false;
					}

					stops.Add( new GradientColorOffset { color = c.Value, offset = second, offsetIsPixels = secondPixels } );
				}

				if ( p.Is( ',' ) ) p.Pointer++;
				else break;
			}

			if ( stops.Count == 0 )
				return false;

			//One colour is a flat gradient of it
			if ( stops.Count == 1 )
				stops.Add( stops[0] with { offset = null, offsetIsPixels = false } );

			result = stops.ToImmutable();
			return true;
		}

		//Stop positions as fractions of a gradient length pixels long, fixed up the way CSS says: the first defaults to 0 and the last to 1,
		//a position before an earlier one is moved up to it, and runs without a position share the space between their neighbours evenly.
		internal static float[] ResolveStops( System.Collections.Immutable.ImmutableArray<GradientColorOffset> stops, float length )
		{
			var count = stops.Length;
			var at = new float[count];
			var known = new bool[count];

			for ( int i = 0; i < count; i++ )
			{
				var s = stops[i];
				if ( !s.offset.HasValue ) continue;

				at[i] = s.offsetIsPixels ? s.offset.Value / MathF.Max( length, 0.0001f ) : s.offset.Value;
				known[i] = true;
			}

			if ( !known[0] ) { at[0] = 0; known[0] = true; }
			if ( !known[count - 1] ) { at[count - 1] = 1; known[count - 1] = true; }

			//Never backwards
			var highest = at[0];
			for ( int i = 1; i < count; i++ )
			{
				if ( !known[i] ) continue;
				if ( at[i] < highest ) at[i] = highest;
				highest = at[i];
			}

			//Share out the gaps
			for ( int i = 1; i < count; i++ )
			{
				if ( known[i] ) continue;

				var start = i - 1;
				var end = i;
				while ( !known[end] ) end++;

				for ( int k = start + 1; k < end; k++ )
					at[k] = at[start] + (at[end] - at[start]) * (k - start) / (end - start);

				i = end;
			}

			return at;
		}

		//The gradient's colours along its line, 0 to 1, as sRGB bytes - the table the renderer
		//looks each pixel's position up in. Interpolated like the web: premultiplied, in sRGB.
		internal static byte[] BakeRamp( System.Collections.Immutable.ImmutableArray<GradientColorOffset> stops, float length, int width )
		{
			var data = new byte[width * 4];
			if ( stops.IsDefaultOrEmpty ) return data;

			var at = ResolveStops( stops, length );
			var last = stops.Length - 1;

			for ( int x = 0; x < width; x++ )
			{
				var t = width == 1 ? 0 : (float)x / (width - 1);

				Color32 colour;

				if ( t <= at[0] ) colour = stops[0].color.ToColor32();
				else if ( t >= at[last] ) colour = stops[last].color.ToColor32();
				else
				{
					colour = stops[last].color.ToColor32();

					for ( int s = 0; s < last; s++ )
					{
						if ( t > at[s + 1] ) continue;

						var span = at[s + 1] - at[s];
						var f = span <= 0.0001f ? 1f : Math.Clamp( (t - at[s]) / span, 0f, 1f );

						colour = LerpPremultiplied( stops[s].color.ToColor32(), stops[s + 1].color.ToColor32(), f );
						break;
					}
				}

				data[x * 4 + 0] = colour.r;
				data[x * 4 + 1] = colour.g;
				data[x * 4 + 2] = colour.b;
				data[x * 4 + 3] = colour.a;
			}

			return data;
		}

		//A linear-gradient for the renderer to evaluate per pixel. False when it can't be read. The direction
		//is read by TryReadGradientDirection, so it means what the baked picture's does - corners included.
		private bool TryParseLinearGradientInfo( string token, out GradientInfo info )
		{
			info = default;

			var p = new Parse( token );

			var restore = p;
			var turn = 0f; //"to bottom"

			if ( TryReadGradientDirection( p.ReadSentence(), out var read ) )
			{
				turn = read;
				p.Pointer++; //comma
			}
			else
			{
				p = restore;
			}

			if ( !TryParseStops( p.ReadRemaining(), out var stops ) )
				return false;

			info = new GradientInfo
			{
				Angle = turn,
				GradientType = GradientInfo.GradientTypes.Linear,
				ColorOffsets = stops,
			};

			return true;
		}

		//A radial-gradient for the renderer to evaluate per pixel. False when it can't be read.
		private bool TryParseRadialGradientInfo( string token, out GradientInfo info )
		{
			info = default;

			var p = new Parse( token );

			//CSS defaults: an ellipse reaching the farthest corner, centred.
			var sizeMode = GradientInfo.RadialSizeMode.FarthestCorner;
			var circle = false;
			var center = new Length[] { Length.Percent( 50 ).Value, Length.Percent( 50 ).Value };

			//"[ <shape> || <size> ] [ at <position> ]" sits before the first comma, if at all.
			var restore = p;

			if ( TryParseRadialPrelude( p.ReadSentence(), ref sizeMode, ref circle, center ) )
				p.Pointer++; //comma
			else
				p = restore;

			if ( !TryParseStops( p.ReadRemaining(), out var stops ) )
				return false;

			info = new GradientInfo
			{
				GradientType = GradientInfo.GradientTypes.Radial,
				SizeMode = sizeMode,
				Circle = circle,
				OffsetX = center[0],
				OffsetY = center[1],
				ColorOffsets = stops,
			};

			return true;
		}

		//A conic-gradient for the renderer to evaluate per pixel. False when it can't be read.
		private bool TryParseConicGradientInfo( string token, out GradientInfo info )
		{
			info = default;

			var p = new Parse( token );

			var angle = 0f;
			var center = new Length[] { Length.Percent( 50 ).Value, Length.Percent( 50 ).Value };

			//"[ from <angle> ] [ at <position> ]" sits before the first comma, if at all.
			var restore = p;

			if ( TryParseConicPrelude( p.ReadSentence(), ref angle, center ) )
				p.Pointer++; //comma
			else
				p = restore;

			if ( !TryParseStops( p.ReadRemaining(), out var stops ) )
				return false;

			info = new GradientInfo
			{
				Angle = angle,
				GradientType = GradientInfo.GradientTypes.Conic,
				OffsetX = center[0],
				OffsetY = center[1],
				ColorOffsets = stops,
			};

			return true;
		}

		//Shape and size keywords in any order, then an optional "at <position>".
		private static bool TryParseRadialPrelude( string text, ref GradientInfo.RadialSizeMode sizeMode, ref bool circle, Length[] center )
		{
			if ( string.IsNullOrWhiteSpace( text ) )
				return false;

			var p = new Parse( text );
			var recognised = false;

			while ( true )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) break;

				if ( p.Is( "at", 0, true ) )
				{
					p.Pointer += 2;

					if ( !TryParsePosition( ref p, center ) )
						return false;

					recognised = true;
					continue;
				}

				var word = p.ReadWord( null, true );
				if ( string.IsNullOrEmpty( word ) )
					return false;

				switch ( word.ToLowerInvariant() )
				{
					case "circle": circle = true; break;
					case "ellipse": circle = false; break;
					case "closest-side": sizeMode = GradientInfo.RadialSizeMode.ClosestSide; break;
					case "closest-corner": sizeMode = GradientInfo.RadialSizeMode.ClosestCorner; break;
					case "farthest-side": sizeMode = GradientInfo.RadialSizeMode.FarthestSide; break;
					case "farthest-corner": sizeMode = GradientInfo.RadialSizeMode.FarthestCorner; break;

					//A colour, or anything we don't know - this wasn't a prelude.
					default: return false;
				}

				recognised = true;
			}

			return recognised;
		}

		//An optional "from <angle>", then an optional "at <position>".
		private static bool TryParseConicPrelude( string text, ref float angle, Length[] center )
		{
			if ( string.IsNullOrWhiteSpace( text ) )
				return false;

			var p = new Parse( text );
			var recognised = false;

			while ( true )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) break;

				if ( p.Is( "from", 0, true ) )
				{
					p.Pointer += 4;
					p = p.SkipWhitespaceAndNewlines();

					//Straight CSS degrees - a conic's "from" turns where the sweep starts, it isn't a direction, so it doesn't get the linear mirroring.
					if ( !p.TryReadFloat( out var number ) )
						return false;

					var unit = "deg";
					if ( p.IsLetter ) unit = p.ReadUntilWhitespaceOrNewlineOrEnd( "," );

					angle = StyleHelpers.RotationDegrees( number, unit );

					recognised = true;
					continue;
				}

				if ( p.Is( "at", 0, true ) )
				{
					p.Pointer += 2;

					if ( !TryParsePosition( ref p, center ) )
						return false;

					recognised = true;
					continue;
				}

				return false;
			}

			return recognised;
		}

		//A CSS position - one or two components of keywords or lengths. A single component leaves the other centred, and keywords may come in either order.
		private static bool TryParsePosition( ref Parse p, Length[] center )
		{
			var gotX = false;
			var gotY = false;
			var loose = new List<Length>();

			while ( true )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) break;

				if ( p.Is( "left", 0, true ) ) { center[0] = Length.Percent( 0 ).Value; gotX = true; p.Pointer += 4; continue; }
				if ( p.Is( "right", 0, true ) ) { center[0] = Length.Percent( 100 ).Value; gotX = true; p.Pointer += 5; continue; }
				if ( p.Is( "top", 0, true ) ) { center[1] = Length.Percent( 0 ).Value; gotY = true; p.Pointer += 3; continue; }
				if ( p.Is( "bottom", 0, true ) ) { center[1] = Length.Percent( 100 ).Value; gotY = true; p.Pointer += 6; continue; }
				if ( p.Is( "center", 0, true ) ) { loose.Add( Length.Percent( 50 ).Value ); p.Pointer += 6; continue; }

				if ( p.TryReadLength( out var length ) )
				{
					loose.Add( length );
					continue;
				}

				return false;
			}

			//Whatever wasn't pinned by a keyword fills the axes left over, in order.
			foreach ( var length in loose )
			{
				if ( !gotX ) { center[0] = length; gotX = true; continue; }
				if ( !gotY ) { center[1] = length; gotY = true; continue; }

				return false;
			}

			return gotX || gotY;
		}

		private List<GradientGenerator> ParseGradient( string token )
		{
			var gradientGenerators = new List<GradientGenerator>();

			Color? lastColor = null;
			float? lastOffset = null;
			bool lastOffsetIsPixels = false;

			var p = new Parse( token );

			//Parse the color values
			while ( !p.IsEnd )
			{
				p = p.SkipWhitespaceAndNewlines();

				//Read up to a comma or end of the text within the brackets
				var w = p.ReadSentence();
				var wp = new Parse( w );

				//First parse the color
				var c = Color.Parse( ref wp );
				if ( !c.HasValue )
				{
					Log.Error( $"Cannot read a color from '{w}'" );
					break;
				}

				wp = wp.SkipWhitespaceAndNewlines();

				//Then optionally parse the stop position
				float? offset = null;
				bool offsetIsPixels = false;

				if ( !TryReadStopPosition( ref wp, w, out offset, out offsetIsPixels ) )
					break;

				wp = wp.SkipWhitespaceAndNewlines();

				if ( !wp.IsEnd )
				{
					Log.Error( $"Extra text found after color stop: '{w}'" );
					break;
				}

				if ( c.HasValue )
				{
					if ( lastColor.HasValue )
					{
						var gradient = new GradientGenerator();
						gradient.from.color = lastColor.Value;
						gradient.from.offset = lastOffset;
						gradient.from.offsetIsPixels = lastOffsetIsPixels;
						gradient.to.color = c.Value;
						gradient.to.offset = offset;
						gradient.to.offsetIsPixels = offsetIsPixels;

						gradientGenerators.Add( gradient );
					}

					lastColor = c;
					lastOffset = offset;
					lastOffsetIsPixels = offsetIsPixels;
				}

				if ( p.Is( ',' ) )
				{
					p.Pointer++;
				}
				else
				{
					break;
				}
			}

			if ( gradientGenerators.Count == 0 )
			{
				var solidColor = lastColor ?? Color.Black;
				var item = new GradientGenerator();
				item.from = new GradientColorOffset()
				{
					color = solidColor,
					offset = 0
				};
				item.to = new GradientColorOffset()
				{
					color = solidColor,
					offset = 1
				};

				gradientGenerators.Add( item );

				return gradientGenerators;
			}

			//Set the distance properties that were not initialized
			float perSliceDistance = 1.0f / (float)gradientGenerators.Count;

			for ( int i = 0; i < gradientGenerators.Count; i++ )
			{
				var gradientGenerator = gradientGenerators[i];

				if ( !gradientGenerator.from.offset.HasValue )
					gradientGenerator.from.offset = (float)i * perSliceDistance;
				if ( !gradientGenerator.to.offset.HasValue )
					gradientGenerator.to.offset = (float)(i + 1) * perSliceDistance;

				gradientGenerators[i] = gradientGenerator;
			}

			//fill in the gap if we weren't given a final stop point
			var lastGenerator = gradientGenerators[^1];
			if ( lastGenerator.to.offset.Value < 1 )
			{
				gradientGenerators.Add( new GradientGenerator
				{
					from = lastGenerator.to,
					to = new GradientColorOffset
					{
						color = lastGenerator.to.color,
						offset = 1,
					},
				} );
			}

			return gradientGenerators;
		}

		private int CalcOptimalGradientWidth()
		{
			var width = BackgroundSizeX?.GetPixels( 1f ) ?? Width?.GetPixels( 1f ) ?? 0f;
			var height = BackgroundSizeY?.GetPixels( 1f ) ?? Height?.GetPixels( 1f ) ?? 0f;

			var calcWidth = MathF.Max( width, height );
			var gradientWidth = Math.Clamp( (int)calcWidth, 256, 2048 );

			return gradientWidth;
		}

		private static Color32 LerpPremultiplied( Color32 from, Color32 to, float t )
		{
			//Interpolate the way the web does: premultiplied components and alpha both lerped linearly, in sRGB space, then
			//un-premultiplied by that same alpha. Premultiplying keeps transparent stops from bleeding their hue into the ramp.
			var colA = from.ToColor();
			var colB = to.ToColor();

			float a = colA.a + t * (colB.a - colA.a);

			float r = colA.r * colA.a + t * (colB.r * colB.a - colA.r * colA.a);
			float g = colA.g * colA.a + t * (colB.g * colB.a - colA.g * colA.a);
			float b = colA.b * colA.a + t * (colB.b * colB.a - colA.b * colA.a);

			if ( a > 0.0001f ) //avoid division by zero
			{
				r /= a;
				g /= a;
				b /= a;
			}

			return new Color( r, g, b, a ).ToColor32();
		}

		//A stop's position as a fraction of the gradient. The baked path only has the texture to
		//measure a pixel position against, where the renderer's ramp has the real gradient line.
		private static float StopFraction( GradientColorOffset stop, int gradientWidth )
		{
			var offset = stop.offset ?? 0f;

			if ( stop.offsetIsPixels )
				return Math.Clamp( offset / Math.Max( gradientWidth, 1 ), 0f, 1f );

			return offset;
		}

		private byte[] GenerateGradient( string token, int gradientWidth )
		{
			var gradientGenerators = ParseGradient( token );

			byte[] gradientData = new byte[gradientWidth * 4];

			//Before the first stop is the first stop's colour, not nothing
			if ( gradientGenerators.Count > 0 )
			{
				var first = gradientGenerators[0].from.color.ToColor32();
				int firstPixel = Math.Clamp( (int)(StopFraction( gradientGenerators[0].from, gradientWidth ) * gradientWidth), 0, gradientWidth );

				for ( int i = 0; i < firstPixel; i++ )
				{
					gradientData[(i * 4) + 0] = first.r;
					gradientData[(i * 4) + 1] = first.g;
					gradientData[(i * 4) + 2] = first.b;
					gradientData[(i * 4) + 3] = first.a;
				}
			}

			//Actually generate gradient data now
			foreach ( var gradient in gradientGenerators )
			{
				var fromColor = gradient.from.color.ToColor32();
				var toColor = gradient.to.color.ToColor32();

				int fromPixel = Math.Clamp( (int)(StopFraction( gradient.from, gradientWidth ) * gradientWidth), 0, gradientWidth );
				int toPixel = Math.Clamp( (int)(StopFraction( gradient.to, gradientWidth ) * gradientWidth), 0, gradientWidth );

				for ( int i = fromPixel; i < toPixel; i++ )
				{
					float j = (float)(i - fromPixel) / (float)(toPixel - fromPixel);

					var color = LerpPremultiplied( fromColor, toColor, j );

					gradientData[(i * 4) + 0] = color.r;
					gradientData[(i * 4) + 1] = color.g;
					gradientData[(i * 4) + 2] = color.b;
					gradientData[(i * 4) + 3] = color.a;
				}
			}

			return gradientData;
		}

		Texture GenerateConicGradientTexture( string token )
		{
			var p = new Parse( token );
			Vector2 centerOffset;

			//Temporary, this can be changed by client too
			centerOffset = new Vector2( 0.5f, 0.5f );

			//Step over "from <angle> at <position>" - see GenerateRadialGradientTexture
			{
				var restore = p;
				var from = 0f;
				var center = new Length[] { Length.Percent( 50 ).Value, Length.Percent( 50 ).Value };

				if ( TryParseConicPrelude( p.ReadSentence(), ref from, center ) ) p.Pointer++;
				else p = restore;
			}

			var gradientWidth = CalcOptimalGradientWidth();
			byte[] gradientLUT = GenerateGradient( p.ReadRemaining(), gradientWidth );

			gradientWidth = gradientLUT.Length / 4;

			byte[] gradientData = new byte[gradientWidth * gradientWidth * 4];

			//Wrap the 1D linear gradient we have calculated into a cone
			for ( int x = 0; x < gradientWidth; x++ )
			{
				for ( int y = 0; y < gradientWidth; y++ )
				{
					Vector2 pos = new Vector2( (float)x / gradientWidth, (float)y / gradientWidth );
					var distance = (Math.Atan2( pos.y - centerOffset.y, pos.x - centerOffset.y ) + Math.PI) / (Math.PI * 2.0f);

					int s = Math.Clamp( gradientWidth - (int)(distance * gradientWidth), 0, gradientWidth - 1 );
					int outS = ((x * gradientWidth) + y) * 4;

					gradientData[outS + 0] = gradientLUT[(s * 4) + 0];
					gradientData[outS + 1] = gradientLUT[(s * 4) + 1];
					gradientData[outS + 2] = gradientLUT[(s * 4) + 2];
					gradientData[outS + 3] = gradientLUT[(s * 4) + 3];
				}
			}

			var gradientTexture = Texture.Create( gradientWidth, gradientWidth )
			.WithName( "conic-gradient" )
			.WithData( gradientData )
			.Finish();

			return gradientTexture;
		}

		Texture GenerateRadialGradientTexture( string token )
		{
			var p = new Parse( token );
			Vector2 centerOffset;

			//https://developer.mozilla.org/en-US/docs/Web/CSS/radial-gradient() The shape, size and position before the first comma are the renderer's to use
			//(see TryParseRadialGradientInfo); this picture only has to step over them, or "circle" is read as a colour and the whole gradient goes with it.
			{
				var restore = p;
				var sizeMode = GradientInfo.RadialSizeMode.FarthestCorner;
				var circle = false;
				var center = new Length[] { Length.Percent( 50 ).Value, Length.Percent( 50 ).Value };

				if ( TryParseRadialPrelude( p.ReadSentence(), ref sizeMode, ref circle, center ) ) p.Pointer++;
				else p = restore;
			}

			//Temporary, this can be changed by client too
			centerOffset = new Vector2( 0.5f, 0.5f );

			var gradientWidth = CalcOptimalGradientWidth();
			byte[] gradientLUT = GenerateGradient( p.ReadRemaining(), gradientWidth );

			gradientWidth = gradientLUT.Length / 4;

			byte[] gradientData = new byte[gradientWidth * gradientWidth * 4];

			//Wrap the 1D linear gradient we have calculated into a radial
			for ( int x = 0; x < gradientWidth; x++ )
			{
				for ( int y = 0; y < gradientWidth; y++ )
				{
					Vector2 pos = new Vector2( (float)x / gradientWidth, (float)y / gradientWidth );
					var distance = Vector2.Distance( pos, centerOffset );

					int s = Math.Clamp( (int)(distance * gradientWidth), 0, gradientWidth - 1 );
					int outS = ((x * gradientWidth) + y) * 4;

					gradientData[outS + 0] = gradientLUT[(s * 4) + 0];
					gradientData[outS + 1] = gradientLUT[(s * 4) + 1];
					gradientData[outS + 2] = gradientLUT[(s * 4) + 2];
					gradientData[outS + 3] = gradientLUT[(s * 4) + 3];
				}
			}

			var gradientTexture = Texture.Create( gradientWidth, gradientWidth )
			.WithName( "radial-gradient" )
			.WithData( gradientData )
			.Finish();

			return gradientTexture;
		}

		//Linear gradient direction as strip rotation: CSS angle + 180; to-keywords, corners are diagonals
		private bool TryReadGradientDirection( string value, out float turn )
		{
			turn = 0.0f;

			var text = value?.Trim();

			if ( string.IsNullOrEmpty( text ) ) return false;

			//A number: the shared reader already hands one back as a turn, and it is the one place that knows how the two conventions line up.
			if ( !text.StartsWith( "to ", System.StringComparison.OrdinalIgnoreCase ) )
			{
				if ( !TryParseAngle( text, out var read ) ) return false;

				turn = Wrapped( read );

				return true;
			}

			var words = text[3..].ToLowerInvariant()
				.Split( ' ', System.StringSplitOptions.RemoveEmptyEntries );

			var up = System.Array.IndexOf( words, "top" ) >= 0;
			var down = System.Array.IndexOf( words, "bottom" ) >= 0;
			var left = System.Array.IndexOf( words, "left" ) >= 0;
			var right = System.Array.IndexOf( words, "right" ) >= 0;

			//A side, or two sides that are not opposites, and nothing else in there: "to nowhere" is
			//not a direction, and reading it as one would point a gradient somewhere without saying so.
			if ( words.Length == 0 || words.Length > 2 ) return false;
			if ( up && down ) return false;
			if ( left && right ) return false;
			if ( !up && !down && !left && !right ) return false;

			var x = right ? 1 : left ? -1 : 0;
			var y = up ? 1 : down ? -1 : 0;

			//Where it runs to, the way CSS counts: up is nought and it goes round clockwise.
			var towards = x switch
			{
				0 => y > 0 ? 0.0f : 180.0f,
				1 => y == 0 ? 90.0f : y > 0 ? 45.0f : 135.0f,
				_ => y == 0 ? 270.0f : y > 0 ? 315.0f : 225.0f,
			};

			turn = Wrapped( 180.0f - towards );

			return true;
		}

		private static float Wrapped( float degrees ) => ((degrees % 360.0f) + 360.0f) % 360.0f;

		Texture GenerateLinearGradientTexture( string token, out float angle )
		{
			angle = -1;

			var p = new Parse( token );

			var restoreP = p;
			var direction = p.ReadSentence();

			if ( TryReadGradientDirection( direction, out var turn ) )
			{
				angle = turn;
				p.Pointer++; //comma
			}
			else
			{
				p = restoreP;
			}

			var gradientWidth = CalcOptimalGradientWidth();
			byte[] gradientData = GenerateGradient( p.ReadRemaining(), gradientWidth );

			var gradientTexture = Texture.Create( 1, gradientData.Length / 4 )
			.WithName( "linear-gradient" )
			.WithData( gradientData )
			.Finish();

			return gradientTexture;
		}
	}
}
