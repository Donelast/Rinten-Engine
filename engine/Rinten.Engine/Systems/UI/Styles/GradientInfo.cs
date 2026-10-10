using System.Collections.Immutable;

namespace Rinten.UI;

[SkipHotload]
internal struct GradientInfo
{
	/// <summary>Linear: the direction in degrees, 0 being "to bottom" and 90 "to right" (CSS mirrored top to bottom - see Styles.GetAngleInDegrees). Conic: the start angle in CSS degrees, clockwise from "up". The text gradient keeps its own convention.</summary>
	public float Angle;

	/// <summary>Centre of a radial or conic gradient. Percentages are a fraction of the box.</summary>
	public Length OffsetX;
	public Length OffsetY;
	public RadialSizeMode SizeMode;
	public GradientTypes GradientType;

	/// <summary>Radial only - a circle rather than an ellipse.</summary>
	public bool Circle;

	public ImmutableArray<Styles.GradientColorOffset> ColorOffsets;

	public void CopyFrom( GradientInfo other )
	{
		ColorOffsets = ImmutableArray<Styles.GradientColorOffset>.Empty;

		if ( !other.ColorOffsets.IsDefaultOrEmpty )
		{
			ColorOffsets = ColorOffsets.AddRange( other.ColorOffsets );
		}

		Angle = other.Angle;
		Circle = other.Circle;
		SizeMode = other.SizeMode;
		OffsetX = other.OffsetX;
		OffsetY = other.OffsetY;
		GradientType = other.GradientType;
	}

	public void AddFrom( GradientInfo other )
	{
		if ( other.ColorOffsets.IsDefaultOrEmpty )
			return;

		CopyFrom( other );
	}

	public override int GetHashCode()
	{
		if ( ColorOffsets.IsDefaultOrEmpty )
			return 0;

		return HashCode.Combine( HashCode.Combine( Angle, SizeMode, OffsetX, OffsetY, GradientType, ColorOffsets ), Circle );
	}

	public enum RadialSizeMode
	{
		FarthestSide = 0,
		FarthestCorner = 1,
		ClosestSide = 2,
		ClosestCorner = 3,
		Circle = 4
	}

	public enum GradientTypes
	{
		Linear = 0,
		Radial = 1,
		Conic = 2
	}

}
