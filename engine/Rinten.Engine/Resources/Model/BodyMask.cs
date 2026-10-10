namespace Rinten;

/// <summary>Body group mask rules, shared by the renderer (ModelParts) and the editor so both agree</summary>
public static class BodyMask
{
	/// <summary>No body group set yet: resolves to the model's default mask</summary>
	public const ulong Unset = ulong.MaxValue;

	/// <summary>What a mask actually means for a model with these groups in it.</summary>
	public static ulong Resolve( ulong mask, ulong defaultMask, IReadOnlyList<ulong> parts )
	{
		if ( mask == 0 ) return 0;

		if ( mask == Unset )
			return defaultMask != 0 ? defaultMask : Unset;

		if ( parts is null ) return mask;

		for ( var i = 0; i < parts.Count; i++ )
		{
			var part = parts[i];
			if ( ( mask & part ) == 0 ) mask |= defaultMask & part;
		}

		return mask;
	}

	//Choosing is clearing the group's bits and setting the choice's. Nothing else in the mask is touched - the other groups keep whatever they were showing.
	/// <summary>The same mask with one group switched to one of its choices.</summary>
	public static ulong WithChoice( ulong mask, ulong part, IReadOnlyList<ulong> choices, int choice )
	{
		mask &= ~part;

		if ( choices is null || choice < 0 || choice >= choices.Count ) return mask;

		return mask | choices[choice];
	}

	/// <summary>Index of the group's choice the mask shows (any bit matches), or zero if none</summary>
	public static int ChoiceOf( ulong mask, IReadOnlyList<ulong> choices )
	{
		if ( choices is null ) return 0;

		for ( var i = 0; i < choices.Count; i++ )
		{
			if ( ( mask & choices[i] ) != 0 ) return i;
		}

		return 0;
	}
}
