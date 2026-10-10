using Rinten.DataModel;
using System.Collections.Generic;
using System.Linq;

namespace Rinten.Porting.SBox;

//Model lists from the document and its prefabs: masks, IK and look-at chains, body groups, LODs
internal static class SBoxModelLists
{
	/// <summary>The bone masks a graph's layers blend through, by the names it asks for.</summary>
	public static List<ModelDefinition.WeightList> WeightLists( string file )
	{
		var lists = new List<ModelDefinition.WeightList>();

		foreach ( var node in Nodes( file, "WeightList" ) )
		{
			var name = node.String( "name" ) ?? "";
			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			var list = new ModelDefinition.WeightList
			{
				Name = name,
				BaseWeight = node.Float( "default_weight" ),
			};

			foreach ( var entry in Items( node["weights"] ) )
			{
				var bone = entry.String( "bone" ) ?? "";
				if ( string.IsNullOrWhiteSpace( bone ) ) continue;

				list.Bones.Add( new ModelDefinition.WeightList.BoneWeight
				{
					Bone = bone,
					Weight = entry.Float( "weight" ),
				} );
			}

			if ( list.Bones.Count > 0 ) lists.Add( list );
		}

		return Named( lists, x => x.Name );
	}

	/// <summary>IK chains from IKChain (nested joints) or IKChainOld (two ends); 3+ joints give a middle bone</summary>
	public static List<ModelDefinition.IkChain> IkChains( string file )
	{
		var chains = new List<ModelDefinition.IkChain>();

		foreach ( var node in Nodes( file, "IKChain" ) )
		{
			var bones = new List<string>();

			Joints( node, bones );

			if ( bones.Count == 0 ) continue;

			chains.Add( new ModelDefinition.IkChain
			{
				Name = node.String( "name" ) ?? "",
				Bone = bones[^1],
				MiddleBone = bones.Count >= 2 ? bones[^2] : "",
				StartBone = bones.Count >= 3 ? bones[^3] : "",
			} );
		}

		foreach ( var node in Nodes( file, "IKChainOld" ) )
		{
			var tip = node.String( "end_effector_bone" ) ?? "";
			if ( string.IsNullOrWhiteSpace( tip ) ) continue;

			chains.Add( new ModelDefinition.IkChain
			{
				Name = node.String( "name" ) ?? "",
				Bone = tip,

				//Empty is two bones up from the tip, which is a limb - so a chain that says
				//nothing about where it swings from says the ordinary thing rather than nothing.
				StartBone = node.String( "root_bone" ) ?? "",
			} );
		}

		return Named( chains, x => x.Name );
	}

	/// <summary>What a character bends when it looks at something, and how much of the turn each bone takes.</summary>
	public static List<ModelDefinition.LookChain> LookChains( string file )
	{
		var chains = new List<ModelDefinition.LookChain>();

		foreach ( var node in Nodes( file, "LookAtChain" ) )
		{
			var chain = new ModelDefinition.LookChain { Name = node.String( "name" ) ?? "" };

			foreach ( var entry in Items( node["lookat_chain"]?["bones"] ) )
			{
				var bone = entry.String( "name" ) ?? "";
				if ( string.IsNullOrWhiteSpace( bone ) ) continue;

				chain.Bones.Add( new ModelDefinition.LookChain.Link
				{
					Bone = bone,
					Weight = entry.Float( "weight" ),
				} );
			}

			if ( chain.Bones.Count > 0 ) chains.Add( chain );
		}

		return Named( chains, x => x.Name );
	}

	//A choice with no meshes on it is the empty one - a head group's "no hat" - and it counts: the group is a list of alternatives and dropping the empty one would take away the way to wear nothing.
	/// <summary>What the model can wear and what it can take off.</summary>
	public static List<ModelDefinition.BodyGroup> BodyGroups( string file )
	{
		var groups = new List<ModelDefinition.BodyGroup>();

		foreach ( var node in Nodes( file, "BodyGroup" ) )
		{
			var group = new ModelDefinition.BodyGroup { Name = node.String( "name" ) ?? "" };

			foreach ( var choice in Children( node, "BodyGroupChoice" ) )
			{
				var meshes = Strings( choice["meshes"] );

				group.Choices.Add( new ModelDefinition.BodyChoice
				{
					//Theirs has no name on a choice at all - the field is not in the file - so one is made from what the choice shows. Carried across blank,
					//every choice of every group arrives as an empty line in the list somebody picks from, which is a body group that cannot be chosen.
					Name = Called( choice.String( "name" ), meshes, group.Choices.Count ),
					Meshes = meshes,
				} );
			}

			if ( group.Choices.Count > 0 ) groups.Add( group );
		}

		return Named( groups, x => x.Name );
	}

	//Body group choice name: its first mesh, "off" when empty, else its index
	private static string Called( string named, IReadOnlyList<string> meshes, int index )
	{
		if ( !string.IsNullOrWhiteSpace( named ) ) return named;

		if ( meshes is null || meshes.Count == 0 ) return "none";

		var first = meshes[0];

		return string.IsNullOrWhiteSpace( first ) ? $"choice {index + 1}" : first;
	}

	/// <summary>LOD-only meshes to skip: named by later LOD groups but not by the nearest one</summary>
	public static List<string> CoarseMeshes( string file )
	{
		var levels = Nodes( file, "LODGroup" )
			.Select( node => Strings( node["meshes"] ) )
			.Where( meshes => meshes.Count > 0 )
			.ToList();

		if ( levels.Count < 2 ) return new List<string>();

		var nearest = new HashSet<string>( levels[0], System.StringComparer.OrdinalIgnoreCase );

		return levels
			.Skip( 1 )
			.SelectMany( meshes => meshes )
			.Where( mesh => !string.IsNullOrWhiteSpace( mesh ) && !nearest.Contains( mesh ) )
			.Distinct( System.StringComparer.OrdinalIgnoreCase )
			.ToList();
	}

	//Every node of one class in the model and in the documents it is assembled from.
	private static IEnumerable<CoreImportedKeyValues3> Nodes( string file, string kind )
	{
		var document = SBoxModelDoc.Load( file );

		if ( document is null ) return Enumerable.Empty<CoreImportedKeyValues3>();

		return SBoxModelDoc.Nodes( document, file )
			.Where( node => (node.String( "_class" ) ?? "") == kind );
	}

	//The children of one node that are of a class, in order.
	private static IEnumerable<CoreImportedKeyValues3> Children( CoreImportedKeyValues3 node, string kind )
	{
		foreach ( var child in Items( node?["children"] ) )
		{
			if ( (child.String( "_class" ) ?? "") == kind ) yield return child;
		}
	}

	//The bones of an ik chain, root first - one IKChainJoint inside the next.
	private static void Joints( CoreImportedKeyValues3 node, List<string> bones )
	{
		foreach ( var joint in Children( node, "IKChainJoint" ) )
		{
			var bone = joint.String( "bone" ) ?? joint.String( "name" ) ?? "";

			if ( !string.IsNullOrWhiteSpace( bone ) ) bones.Add( bone );

			Joints( joint, bones );
		}
	}

	private static IEnumerable<CoreImportedKeyValues3> Items( CoreImportedKeyValues3 node )
	{
		if ( node is not { IsArray: true } ) yield break;

		foreach ( var item in node.Items )
		{
			if ( item is not null ) yield return item;
		}
	}

	private static List<string> Strings( CoreImportedKeyValues3 node )
	{
		var found = new List<string>();

		if ( node is not { IsArray: true } ) return found;

		foreach ( var item in node.Items )
		{
			var text = item?.AsString( null );

			if ( !string.IsNullOrWhiteSpace( text ) ) found.Add( text );
		}

		return found;
	}

	//One of each name. A model assembled from prefabs names the same list twice whenever one of them is included by two of its
	//parts, and a definition holding two chains called left_arm_IK is a definition whose second one nothing will ever ask for.
	private static List<T> Named<T>( List<T> found, System.Func<T, string> name )
	{
		var seen = new HashSet<string>( System.StringComparer.OrdinalIgnoreCase );
		var kept = new List<T>();

		foreach ( var one in found )
		{
			var called = name( one ) ?? "";

			if ( !string.IsNullOrWhiteSpace( called ) && !seen.Add( called ) ) continue;

			kept.Add( one );
		}

		return kept;
	}
}
