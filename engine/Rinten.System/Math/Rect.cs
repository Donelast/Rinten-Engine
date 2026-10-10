using Rinten.UI;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Rinten;

#pragma warning disable CS0618 //Type or member is obsolete
/// <summary>Represents a rectangle.</summary>
[StructLayout( LayoutKind.Sequential )]
public struct Rect : System.IEquatable<Rect>
{
	private float left;
	private float top;
	private float right;
	private float bottom;

	/// <summary>Initialize a Rect at given position and with given size.</summary>
	public Rect( in float x, in float y, in float width, in float height )
	{
		left = x;
		top = y;
		right = x + width;
		bottom = y + height;
	}

	/// <summary>Initialize a Rect at given position and with given size.</summary>
	public Rect( in Vector2 point, in Vector2 size = default )
	{
		left = point.x;
		top = point.y;
		right = point.x + size.x;
		bottom = point.y + size.y;
	}

	/// <summary>Width of the rect.</summary>
	[JsonIgnore, Hide]
	public float Width
	{
		readonly get => right - left;
		set => right = left + value;
	}

	/// <summary>Height of the rect.</summary>
	[JsonIgnore, Hide]
	public float Height
	{
		readonly get => bottom - top;
		set => bottom = top + value;
	}

	/// <summary>Position of rect's left edge relative to its parent, can also be interpreted as its position on the X axis.</summary>
	[JsonIgnore, Hide]
	public float Left
	{
		readonly get => left;
		set => left = value;
	}

	/// <summary>Position of rect's top edge relative to its parent, can also be interpreted as its position on the Y axis.</summary>
	[JsonIgnore, Hide]
	public float Top
	{
		readonly get => top;
		set => top = value;
	}

	/// <summary>Position of rect's right edge relative to its parent.</summary>
	[JsonIgnore, Hide]
	public float Right
	{
		readonly get => right;
		set => right = value;
	}

	/// <summary>Position of rect's bottom edge relative to its parent.</summary>
	[JsonIgnore, Hide]
	public float Bottom
	{
		readonly get => bottom;
		set => bottom = value;
	}

	/// <summary>Position of this rect.</summary>
	public Vector2 Position
	{
		readonly get => new( left, top );
		set
		{
			var s = Size;
			left = value.x;
			top = value.y;
			Size = s;
		}
	}

	/// <summary>Center of this rect.</summary>
	[JsonIgnore, Hide]
	public readonly Vector2 Center
	{
		get => Position + Size * 0.5f;
	}

	/// <summary>Size of this rect.</summary>
	public Vector2 Size
	{
		readonly get => new( Width, Height );
		set
		{
			right = left + value.x;
			bottom = top + value.y;
		}
	}

	/// <summary>Returns this rect with position set to 0 on both axes.</summary>
	[JsonIgnore, Hide]
	public readonly Rect WithoutPosition => new( 0, 0, Width, Height );

	public static Rect operator +( Rect r, in Vector2 p )
	{
		r.Position += p;
		return r;
	}

	/// <summary>Return true if the passed rect is partially or fully inside this rect.</summary>
	public readonly bool IsInside( in Rect rect, bool fullyInside = false )
	{
		if ( fullyInside )
		{
			return left < rect.left && right > rect.right && top < rect.top && bottom > rect.bottom;
		}

		if ( rect.right < left ) return false;
		if ( rect.left > right ) return false;
		if ( rect.top > bottom ) return false;
		if ( rect.bottom < top ) return false;

		return true;
	}

	/// <summary>Return true if the passed point is inside this rect.</summary>
	public readonly bool IsInside( in Vector2 pos )
	{
		if ( pos.x < left ) return false;
		if ( pos.x > right ) return false;
		if ( pos.y > bottom ) return false;
		if ( pos.y < top ) return false;

		return true;
	}

	/// <summary>Returns a Rect shrunk in every direction by given values.</summary>
	public readonly Rect Shrink( in float left, in float top, in float right, in float bottom )
	{
		var r = this;
		r.left += left;
		r.top += top;
		r.right -= right;
		r.bottom -= bottom;

		return r;
	}

	/// <summary>Returns a Rect shrunk in every direction by <see cref="Margin">Margin</see>'s values.</summary>
	public readonly Rect Shrink( in Margin m ) => Shrink( m.Left, m.Top, m.Right, m.Bottom );

	/// <summary>Returns a Rect shrunk in every direction by given values on each axis.</summary>
	public readonly Rect Shrink( in float x, in float y ) => Shrink( x, y, x, y );

	/// <summary>Returns a Rect shrunk in every direction by given amount.</summary>
	public readonly Rect Shrink( in float amt ) => Shrink( amt, amt, amt, amt );

	/// <summary>Returns a Rect grown in every direction by given amounts.</summary>
	public readonly Rect Grow( in float left, in float top, in float right, in float bottom )
	{
		var r = this;
		r.left -= left;
		r.top -= top;
		r.right += right;
		r.bottom += bottom;

		return r;
	}

	/// <summary>Returns a Rect grown in every direction by <see cref="Margin">Margin</see>'s values.</summary>
	public readonly Rect Grow( Margin m ) => Grow( m.Left, m.Top, m.Right, m.Bottom );

	/// <summary>Returns a Rect grown in every direction by given values on each axis.</summary>
	public readonly Rect Grow( in float x, in float y ) => Grow( x, y, x, y );

	/// <summary>Returns a Rect grown in every direction by given amount.</summary>
	public readonly Rect Grow( in float amt ) => Grow( amt, amt, amt, amt );

	/// <summary>Returns a Rect with position and size rounded down.</summary>
	public readonly Rect Floor()
	{
		var r = this;
		r.left = MathF.Floor( r.left );
		r.top = MathF.Floor( r.top );
		r.right = MathF.Floor( r.right );
		r.bottom = MathF.Floor( r.bottom );
		return r;
	}

	/// <summary>Returns a Rect with position and size rounded to closest integer values.</summary>
	public readonly Rect Round()
	{
		var r = this;
		r.left = MathF.Round( r.left );
		r.top = MathF.Round( r.top );
		r.right = MathF.Round( r.right );
		r.bottom = MathF.Round( r.bottom );
		return r;
	}

	/// <summary>Returns a Rect with position and size rounded up.</summary>
	public readonly Rect Ceiling()
	{
		var r = this;
		r.left = MathF.Ceiling( r.left );
		r.top = MathF.Ceiling( r.top );
		r.right = MathF.Ceiling( r.right );
		r.bottom = MathF.Ceiling( r.bottom );
		return r;
	}

	public static Rect operator +( in Rect a, in Rect b )
	{
		return new Rect( a.left + b.left, a.top + b.top, a.Width + b.Width, a.Height + b.Height );
	}

	public static Rect operator *( in Rect a, in float b )
	{
		return new Rect( a.left * b, a.top * b, a.Width * b, a.Height * b );
	}

	public static Rect operator *( in Rect a, in Vector2 b )
	{
		return new Rect( a.left * b.x, a.top * b.y, a.Width * b.x, a.Height * b.y );
	}

	/// <summary>Create a rect between two points. The order of the points doesn't matter.</summary>
	public static Rect FromPoints( in Vector2 a, in Vector2 b )
	{
		var topLeft = Vector2.Min( a, b );

		return new Rect( topLeft.x, topLeft.y, MathF.Abs( a.x - b.x ), MathF.Abs( a.y - b.y ) );
	}

	public override readonly string ToString() => $"{left},{top},{Width},{Height}";

	/// <summary>Returns this rect as a Vector4, where X/Y/Z/W are Left/Top/Right/Bottom respectively.</summary>
	public readonly Vector4 ToVector4()
	{
		return new Vector4( left, top, right, bottom );
	}

	/// <summary>Expand this Rect to contain the other rect</summary>
	public void Add( Rect r )
	{
		left = MathF.Min( left, r.left );
		right = MathF.Max( right, r.right );
		top = MathF.Min( top, r.top );
		bottom = MathF.Max( bottom, r.bottom );
	}

	/// <summary>Expand this Rect to contain the point</summary>
	public void Add( Vector2 point )
	{
		left = MathF.Min( left, point.x );
		right = MathF.Max( right, point.x );
		top = MathF.Min( top, point.y );
		bottom = MathF.Max( bottom, point.y );
	}

	/// <summary>Returns this rect expanded to include this point</summary>
	public readonly Rect AddPoint( Vector2 pos )
	{
		var r = this;
		r.Add( pos );

		return r;
	}

	/// <summary>Returns the closest point on this rect to another point</summary>
	public readonly Vector2 ClosestPoint( in Vector2 point )
	{
		var x = point.x;
		var y = point.y;

		if ( x < left ) x = left;
		else if ( x > right ) x = right;

		if ( y < top ) y = top;
		else if ( y > bottom ) y = bottom;

		return new Vector2( x, y );
	}

	/// <summary>Position of the bottom left edge of this rect.</summary>
	[JsonIgnore, Hide]
	public readonly Vector2 BottomLeft => new( left, bottom );

	/// <summary>Position of the bottom right edge of this rect.</summary>
	[JsonIgnore, Hide]
	public readonly Vector2 BottomRight => new( right, bottom );

	/// <summary>Position of the top right edge of this rect.</summary>
	[JsonIgnore, Hide]
	public readonly Vector2 TopRight => new( right, top );

	/// <summary>Position of the top left edge of this rect.</summary>
	[JsonIgnore, Hide]
	public readonly Vector2 TopLeft => new( left, top );

	/// <summary>Align the smaller rect inside this rect. Default alignment on each axis is Top, Left.</summary>
	public readonly Rect Align( in Vector2 size, TextFlag align )
	{
		var r = new Rect( Position, size );

		if ( align.Contains( TextFlag.Right ) )
		{
			r.left = right - size.x;
			r.right = right;
		}

		if ( align.Contains( TextFlag.CenterHorizontally ) )
		{
			r.left = left + (Width - size.x) * 0.5f;
			r.right = r.left + size.x;
		}

		if ( align.Contains( TextFlag.Bottom ) )
		{
			r.top = bottom - size.y;
			r.bottom = bottom;
		}

		if ( align.Contains( TextFlag.CenterVertically ) )
		{
			r.top = top + (Height - size.y) * 0.5f;
			r.bottom = r.top + size.y;
		}

		return r;
	}


	/// <summary>Align to a grid</summary>
	public readonly Rect SnapToGrid()
	{
		return new Rect
		{
			left = MathF.Floor( left ),
			top = MathF.Floor( top ),
			right = MathF.Floor( right ),
			bottom = MathF.Floor( bottom ),
		};
	}

	/// <summary>Contain a given rectangle (image) within this rectangle (frame), preserving aspect ratio.</summary>
	public Rect Contain( in Vector2 size, TextFlag align = TextFlag.Center, bool stretch = false )
	{
		//Rescale preserving aspect ratio
		float zoom = Math.Min( Width / size.x, Height / size.y ); //Max for fill
		if ( !stretch ) zoom = Math.Min( zoom, 1 );

		Vector2 newSize = new()
		{
			x = MathF.Ceiling( size.x * zoom ),
			y = MathF.Ceiling( size.y * zoom )
		};

		//Repoisition
		return Align( newSize, align );
	}

	#region equality

	public static bool operator ==( Rect left, Rect right ) => left.Equals( right );
	public static bool operator !=( Rect left, Rect right ) => !(left == right);
	public override bool Equals( object obj ) => obj is Rect o && Equals( o );
	public readonly bool Equals( Rect o ) => (left, right, top, bottom) == (o.left, o.right, o.top, o.bottom);
	public readonly override int GetHashCode() => HashCode.Combine( left, right, top, bottom );

	#endregion
}
