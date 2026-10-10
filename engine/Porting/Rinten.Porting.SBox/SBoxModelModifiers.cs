using System;

namespace Rinten.Porting.SBox;

//Model-wide modifiers, mainly ScaleAndMirror's scale, folded into mesh, hull and clip numbers
internal static class SBoxModelModifiers
{
	private const string ScaleAndMirror = "ModelModifier_ScaleAndMirror";

	//What a document asks for on top of its meshes.
	internal readonly record struct Modifier(
		float Scale, bool MirrorX, bool MirrorY, bool MirrorZ, bool SwapLeftRight )
	{
		/// <summary>Nothing asked for: the model at the size its art was authored.</summary>
		public static Modifier None { get; } = new( 1.0f, false, false, false, false );

		/// <summary>Whether the document flips the model about any axis.</summary>
		public bool Mirrors => MirrorX || MirrorY || MirrorZ;

		/// <summary>Whether this leaves the model exactly as its art was.</summary>
		public bool IsIdentity => !Mirrors && !SwapLeftRight && Scale.AlmostEqual( 1.0f );
	}

	public static Modifier Read( string file ) => Read( SBoxModelDoc.Load( file ) );

	//Several of these nodes in one list is legal and their effects compound, so the scales multiply and a flip on any of them flips.
	/// <summary>The modifiers a document asks for, gathered into one.</summary>
	public static Modifier Read( CoreImportedKeyValues3 document )
	{
		if ( document is null ) return Modifier.None;

		var found = Modifier.None;

		foreach ( var node in SBoxModelDoc.Nodes( document ) )
		{
			if ( (node.String( "_class" ) ?? "") != ScaleAndMirror ) continue;

			var scale = node.Float( "scale", 1.0f );

			//A scale of nothing is a model of nothing, and their editor writes a field it was never given as zero. Read as "not asked for".
			if ( scale <= 0.0f ) scale = 1.0f;

			found = new Modifier(
				found.Scale * scale,
				found.MirrorX || node.Bool( "mirror_x" ),
				found.MirrorY || node.Bool( "mirror_y" ),
				found.MirrorZ || node.Bool( "mirror_z" ),
				found.SwapLeftRight || node.Bool( "swap_left_and_right_bones" ) );
		}

		return found;
	}

	/// <summary>What one of their units is worth here for a model with this modifier on it: the inch, and then whatever the document resizes the model by.</summary>
	public static float Unit( in Modifier modifier ) => PortUnits.SBoxUnit * modifier.Scale;

	public static float Unit( string file ) => Unit( Read( file ) );
}
