using System;
using System.Collections.Generic;
using System.IO;

namespace Rinten.Porting.SBox;

//Axes of bone-relative numbers: .smd (their format) is turned per bone, .fbx needs no per-bone turn
internal static class SBoxSkeleton
{
	//Their own formats: art this engine reads in their axes, and turns.
	private static readonly string[] Theirs = { ".smd", ".dmx", ".vta", ".qc" };

	//Interchange art, which records the axes it was drawn in and is read into ours by both engines.
	private static readonly string[] Interchange = { ".fbx", ".gltf", ".glb", ".obj", ".dae" };

	/// <summary>The axes a bone-relative number in this document has to be turned by.</summary>
	public static ForeignBasis BoneAxes( IEnumerable<string> art )
	{
		var known = false;

		foreach ( var file in art ?? Array.Empty<string>() )
		{
			if ( string.IsNullOrWhiteSpace( file ) ) continue;

			var extension = Path.GetExtension( file );

			foreach ( var one in Theirs )
			{
				if ( string.Equals( extension, one, StringComparison.OrdinalIgnoreCase ) )
					return ForeignBasis.Quake;
			}

			foreach ( var one in Interchange )
			{
				if ( string.Equals( extension, one, StringComparison.OrdinalIgnoreCase ) )
					known = true;
			}
		}

		return known ? ForeignBasis.Same : ForeignBasis.Quake;
	}
}
