using System.Collections.Generic;

namespace Rinten.Porting.SBox;

//Animations embedded in compiled models: ASEQ (names, sequences), AGRP (key), ANIM (poses)
internal static partial class CoreImportedAnimations
{
	//One thing a game can ask a model to play.
	internal sealed class Sequence
	{
		public string Name;

		/// <summary>Whether it runs back into itself rather than stopping.</summary>
		public bool Looping;

		/// <summary>Whether it plays on its own the moment the model is shown.</summary>
		public bool Autoplay;

		/// <summary>Sequence's additive flag; usually unset, so prefer Clip.Delta on the clip it plays</summary>
		public bool Additive;

		/// <summary>Whether it is machinery rather than something to ask for: the tools hide these, and a list that shows them buries what a game wants.</summary>
		public bool Hidden;

		/// <summary>Which clips it plays, as positions in the model's list of clip names. More than one where a sequence blends between them - a walk that leans as it turns is the same walk sampled twice.</summary>
		public int[] Clips = System.Array.Empty<int>();

		/// <summary>What it lays over whatever it plays, as positions in the model's list of names.</summary>
		public int[] Layers = System.Array.Empty<int>();

		/// <summary>How long it takes to fade in and out of, in seconds.</summary>
		public float FadeIn;
		public float FadeOut;
	}

	/// <summary>Every clip the model carries, by name, in the order everything else refers to them by. Null where the model carries none.</summary>
	public static List<string> Names( byte[] bytes ) => Names( Block( bytes ) );

	/// <summary>Every sequence a game can ask for. Null where the model has none.</summary>
	public static List<Sequence> Sequences( byte[] bytes ) => Sequences( Block( bytes ) );

	/// <summary>Both at once, off one read of the file - which is what a caller doing anything with them actually wants, since a sequence names its clips by position in the other list.</summary>
	public static bool Read( byte[] bytes, out List<string> names, out List<Sequence> sequences )
	{
		var block = Block( bytes );

		names = Names( block );
		sequences = Sequences( block );

		return names is { Count: > 0 } || sequences is { Count: > 0 };
	}

	//The sequence block of a compiled model, parsed, or null.
	private static CoreImportedKeyValues3 Block( byte[] bytes )
	{
		var resource = CoreImportedResource.Open( bytes );
		if ( resource is null ) return null;

		return resource.Find( "ASEQ" ) is { } block
			? CoreImportedKeyValues3.Read( resource.Read( block ) )
			: null;
	}

	private static List<string> Names( CoreImportedKeyValues3 block )
	{
		if ( block is null ) return null;

		var names = new List<string>();

		foreach ( var entry in block.Array( "m_localSequenceNameArray" ) )
		{
			var name = entry.AsString();
			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			names.Add( name );
		}

		return names;
	}

	//Named bone masks (weight lists); weights are set at change points and inherited down the tree
	private static List<Mask> Masks( CoreImportedKeyValues3 block )
	{
		if ( block is null ) return null;

		//The mask's own bone list, which is not the model's: a mask indexes the bones the sequence block knows about, and there are fewer of those.
		var bones = new List<string>();

		foreach ( var bone in block.Array( "m_localBoneNameArray" ) )
			bones.Add( bone.AsString() ?? "" );

		var masks = new List<Mask>();

		foreach ( var entry in block.Array( "m_localBoneMaskArray" ) )
		{
			var name = entry.String( "m_sName" );
			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			var indices = entry.Array( "m_nLocalBoneArray" );
			var weights = entry.Array( "m_flBoneWeightArray" );

			var listed = new List<MaskBone>( indices.Count );

			for ( var at = 0; at < indices.Count; at++ )
			{
				var bone = indices[at].AsInt( -1 );
				if ( bone < 0 || bone >= bones.Count ) continue;

				listed.Add( new MaskBone( bones[bone], at < weights.Count ? weights[at].AsFloat() : 1.0f ) );
			}

			masks.Add( new Mask { Name = name, Bones = listed } );
		}

		return masks;
	}

	private static List<Sequence> Sequences( CoreImportedKeyValues3 block )
	{
		if ( block is null ) return null;

		var sequences = new List<Sequence>();

		foreach ( var entry in block.Array( "m_localS1SeqDescArray" ) )
		{
			var name = entry.String( "m_sName" );
			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			var flags = entry["m_flags"];
			var fetch = entry["m_fetch"];
			var transition = entry["m_transition"];

			var sequence = new Sequence
			{
				Name = name,
				Looping = flags?.Bool( "m_bLooping" ) ?? false,
				Autoplay = flags?.Bool( "m_bAutoplay" ) ?? false,
				Hidden = flags?.Bool( "m_bHidden" ) ?? false,

				//Their newer flag and the one it replaced. A file written by either compiler is a file somebody has.
				Additive = ( flags?.Bool( "m_bDelta" ) ?? false ) || ( flags?.Bool( "m_bLegacyDelta" ) ?? false ),

				FadeIn = transition?.Float( "m_flFadeInTime" ) ?? 0.0f,
				FadeOut = transition?.Float( "m_flFadeOutTime" ) ?? 0.0f,
			};

			if ( fetch is not null )
			{
				var clips = new List<int>();

				foreach ( var reference in fetch.Array( "m_localReferenceArray" ) )
				{
					var at = reference.AsInt( -1 );
					if ( at >= 0 ) clips.Add( at );
				}

				sequence.Clips = clips.ToArray();
			}

			var layers = new List<int>();

			foreach ( var layer in entry.Array( "m_autoLayerArray" ) )
			{
				//A layer that takes away rather than adds is how a difference is made, and the clips that need one are already stored as one.
				if ( layer["m_flags"]?.Bool( "m_bSubtract" ) ?? false ) continue;

				var at = layer.Int( "m_nLocalReference", -1 );
				if ( at >= 0 ) layers.Add( at );
			}

			sequence.Layers = layers.ToArray();

			sequences.Add( sequence );
		}

		return sequences;
	}
}
