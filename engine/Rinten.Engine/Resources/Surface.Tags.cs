using NativeEngine;

namespace Rinten;

public partial class Surface
{
	//Internal accessor for the surface's tags (stored as StringToken)
	private HashSet<uint> TagList = new();

	/// <summary>A list of tags as one string.</summary>
	[Category( "Tags" )]
	[TagString]
	public string Tags { get; set; }

	/// <summary>Do we have a tag?</summary>
	public bool HasTag( string tag )
	{
		return TagList.Contains( StringToken.FindOrCreate( tag ) );
	}

	/// <summary>Do we have all the tags on this hitbox?</summary>
	public bool HasAllTags( params string[] tags )
	{
		foreach ( var tag in tags )
		{
			if ( !HasTag( tag ) )
				return false;
		}

		return true;
	}

	/// <summary>Do we have all the tags on this hitbox?</summary>
	public bool HasAnyTags( params string[] tags )
	{
		foreach ( var tag in tags )
		{
			if ( HasTag( tag ) )
				return true;
		}

		return false;
	}
}
