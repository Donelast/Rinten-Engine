namespace Editor.Inspectors;

/// <summary>Everyday sounds at the distance in metres they carry.</summary>
public static class AudioDistancePresets
{
	/// <summary>The closest a falloff is worth setting, in metres.</summary>
	public const float Nearest = 0.01f;

	/// <summary>The furthest distance any of these names covers, in metres.</summary>
	public const float Loudest = 1270f;

	//Whole metres, and to the ten once past a hundred: nobody sets a falloff to 215.9, and the name beside it is what is being chosen anyway.
	public static readonly (float Distance, string Name)[] All =
	{
		( 13, "leaf rustle" ),
		( 19, "whispering" ),
		( 25, "library" ),
		( 38, "fridge" ),
		( 51, "avg home" ),
		( 89, "clothes dryer" ),
		( 130, "dishwasher" ),
		( 180, "vacuum cleaner" ),
		( 220, "traffic" ),
		( 250, "restaurant" ),
		( 280, "alarm clock" ),
		( 320, "child scream" ),
		( 330, "motorcycle" ),
		( 360, "subway train" ),
		( 380, "helicopter" ),
		( 440, "sandblasting" ),
		( 510, "jet plane" ),
		( 640, "air raid siren" ),
		( 760, "gunshot" ),
		( 830, "fireworks" ),
		( 890, "jet engine" ),
		( 1020, "shotgun" ),
		( 1080, "357 revolver" ),
		( 1140, "grenade" ),
		( Loudest, "rocket launch" )
	};

	/// <summary>The first named loudness this distance still reaches.</summary>
	public static string Describe( float value )
	{
		foreach ( var preset in All )
		{
			if ( value <= preset.Distance ) return preset.Name;
		}

		return "really loud";
	}

	/// <summary>How a preset reads in a menu.</summary>
	public static string Label( (float Distance, string Name) preset ) =>
		$"{preset.Distance:0} m - {preset.Name}";
}
