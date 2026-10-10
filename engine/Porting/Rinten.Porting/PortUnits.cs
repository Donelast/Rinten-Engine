namespace Rinten.Porting;

/// <summary>What a number means, so that a port knows what to do to it.</summary>
public enum PortDimension
{
	/// <summary>Not a measurement. Counts, fractions, angles, colours, flags.</summary>
	None,

	/// <summary>A distance. Positions, radii, extents, offsets, clip planes.</summary>
	Length,

	/// <summary>A distance over time. Speeds, velocities.</summary>
	Speed,

	/// <summary>A distance over time squared. Gravity, accelerations.</summary>
	Acceleration,

	/// <summary>A mass times an acceleration - what a joint breaks at, what a motor pushes with. Moves the same way an acceleration does, because the mass in it does not move: a kilogram is a kilogram in either engine.</summary>
	Force,

	//Kept apart from Inertia despite the identical factor. The two are told apart by a person reading the table, not by arithmetic, and a torque labelled as a moment of inertia is a line nobody can check.
	/// <summary>A force times the arm it acts on, so it moves twice - the same as <see cref="Inertia"/> and for the same reason.</summary>
	Torque,

	/// <summary>A length squared.</summary>
	Area,

	/// <summary>A length cubed.</summary>
	Volume,

	/// <summary>A mass over a volume, so it moves the opposite way to one.</summary>
	Density,

	/// <summary>A mass times a length squared - what a body resists a spin with.</summary>
	Inertia,

	//Reported rather than converted. A number nobody can name is worth more in a list the porter hands back than silently multiplied by something.
	/// <summary>A length, but one this port could not prove the meaning of.</summary>
	Unknown,
}

/// <summary>Metres per source unit and its effect per PortDimension; axis changes live in ForeignBasis</summary>
public readonly struct PortUnits
{
	/// <summary>How many of our units one of theirs is.</summary>
	public float UnitsPerSourceUnit { get; }

	/// <summary>What the source engine calls its unit, for reports.</summary>
	public string SourceUnitName { get; }

	/// <summary>What this engine calls its own, which is a metre.</summary>
	public string TargetUnitName => "metre";

	/// <summary>Literal conversion factor written into ported code, so it doesn't depend on this assembly</summary>
	public string ConversionExpression { get; }

	public PortUnits( float unitsPerSourceUnit, string sourceUnitName, string conversionExpression = "" )
	{
		UnitsPerSourceUnit = unitsPerSourceUnit;
		SourceUnitName = sourceUnitName;
		ConversionExpression = conversionExpression;
	}

	/// <summary>Whether any number would come out of a conversion changed.</summary>
	public bool IsIdentity => UnitsPerSourceUnit == 1.0f;

	/// <summary>s&amp;box, which is s&amp;box, which measures in inches.</summary>
	public static PortUnits SBox { get; } = new( SBoxUnit, "inch", "0.0254f" );

	//Here rather than in the engine on purpose. The engine counts in metres and nothing in it converts; an inch is a fact about the thing being ported, and it stops at this assembly. Exact: the inch has been 25.4 millimetres by definition since 1959.
	/// <summary>What one of their units is worth, which is an inch.</summary>
	public const float SBoxUnit = 0.0254f;

	/// <summary>Unity, which measures in metres already. Its axes are another matter.</summary>
	public static PortUnits Unity { get; } = new( 1.0f, "metre" );

	//PortDimension.Unknown comes back as one, on purpose: the caller is expected to have reported it rather than converted it, and a factor of one at least leaves the original number legible in the output instead of scaling it by a guess.
	/// <summary>What to multiply a number of this dimension by.</summary>
	public double Factor( PortDimension dimension )
	{
		double s = UnitsPerSourceUnit;

		return dimension switch
		{
			PortDimension.Length => s,
			PortDimension.Speed => s,
			PortDimension.Acceleration => s,
			PortDimension.Force => s,
			PortDimension.Torque => s * s,
			PortDimension.Area => s * s,
			PortDimension.Volume => s * s * s,
			PortDimension.Density => 1.0 / ( s * s * s ),
			PortDimension.Inertia => s * s,
			_ => 1.0,
		};
	}

	/// <summary>Converts one number of a known dimension.</summary>
	public double Convert( double value, PortDimension dimension )
		=> value * Factor( dimension );

	/// <summary>Multiplier text for a dimension (area: 0.0254f * 0.0254f); empty when nothing would change</summary>
	public string Expression( PortDimension dimension )
	{
		if ( IsIdentity || string.IsNullOrEmpty( ConversionExpression ) ) return "";

		var s = ConversionExpression;

		return dimension switch
		{
			PortDimension.Length or
			PortDimension.Speed or
			PortDimension.Acceleration or
			PortDimension.Force => s,

			PortDimension.Torque or
			PortDimension.Area or
			PortDimension.Inertia => $"{s} * {s}",

			PortDimension.Volume => $"{s} * {s} * {s}",
			PortDimension.Density => $"1f / ( {s} * {s} * {s} )",

			_ => "",
		};
	}
}
