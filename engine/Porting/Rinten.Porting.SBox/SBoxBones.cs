using Rinten;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rinten.Porting.SBox;

//Bones a model document adds (e.g. hold_R on weapons), for models we compile from exported art
internal static class SBoxBones
{
	private const string SkeletonNode = "Skeleton";
	private const string BoneNode = "Bone";
	private const string PrefabNode = "Prefab";

	//How far a prefab may point at another one before this gives up.
	private const int Deepest = 8;

	/// <summary>What a model document adds to the skeleton, as this engine writes it.</summary>
	public static List<ModelDefinition.ExtraBone> Read( string file, string sourceAssets, ForeignBasis bones )
	{
		var found = new List<ModelDefinition.ExtraBone>();
		var named = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var read = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		//The bone offsets a document writes down are lengths in the model, so they move with whatever its modifier list resizes the model by. Read
		//from the model being ported rather than from each prefab it follows: the resize belongs to the model, not to the file a bone came out of.
		Walk( file, sourceAssets, found, named, read, 0, SBoxModelModifiers.Unit( file ), bones );

		return found;
	}

	private static void Walk( string file, string sourceAssets,
		List<ModelDefinition.ExtraBone> found, HashSet<string> named, HashSet<string> read, int depth, float unit,
		ForeignBasis axes )
	{
		if ( depth > Deepest || string.IsNullOrWhiteSpace( file ) ) return;
		if ( !read.Add( Path.GetFullPath( file ) ) ) return;

		var document = SBoxModelDoc.Load( file );
		if ( document is null ) return;

		foreach ( var node in SBoxModelDoc.Nodes( document ) )
		{
			switch ( node.String( "_class" ) ?? "" )
			{
				case SkeletonNode:
					Bones( node["children"], parent: "", found, named, unit, axes );
					break;

				case PrefabNode:
					if ( sourceAssets is null ) continue;

					var target = node.String( "target_file" ) ?? "";
					if ( string.IsNullOrWhiteSpace( target ) ) continue;

					var path = Path.Combine( sourceAssets, target.Replace( '/', Path.DirectorySeparatorChar ) );

					//Only what somebody wrote. The compiled file beside it used to be taken where the document was missing - see PortPaths.IsCompiled.
					if ( !File.Exists( path ) || PortPaths.IsCompiled( path ) ) continue;

					Walk( path, sourceAssets, found, named, read, depth + 1, unit, axes );
					break;
			}
		}
	}

	//The bones under a node, and the bones under those. A bone inside another bone hangs off it - that is how their tree
	//says so, and there is no field naming the parent. One sitting directly under the skeleton hangs off the model itself.
	private static void Bones( CoreImportedKeyValues3 children, string parent,
		List<ModelDefinition.ExtraBone> found, HashSet<string> named, float unit, ForeignBasis axes )
	{
		if ( children is null || !children.IsArray ) return;

		foreach ( var node in children.Items )
		{
			if ( node is null || !node.IsObject ) continue;

			//A folder, or anything else their editor lets people nest bones inside, is walked through rather than treated as one.
			if ( (node.String( "_class" ) ?? "") != BoneNode )
			{
				Bones( node["children"], parent, found, named, unit, axes );
				continue;
			}

			var name = node.String( "name" ) ?? "";

			if ( string.IsNullOrWhiteSpace( name ) || !named.Add( name ) ) continue;

			var origin = Triple( node["origin"] );
			var angles = Triple( node["angles"] );

			//Written against the bone it hangs off, where there is one: those numbers are in that bone's frame and turn only if it did.
			var hung = node.String( "parent_bone" ) ?? parent;
			var written = string.IsNullOrWhiteSpace( hung ) ? ForeignBasis.Quake : axes;

			found.Add( new ModelDefinition.ExtraBone
			{
				Name = name,

				//Written as a nested node rather than by name, except where their document says so outright.
				Parent = hung,

				//Inches there, metres here. The angles are the same three numbers.
				Position = written.Vector( new Vector3( origin.x, origin.y, origin.z ) ) * unit,
				Rotation = new Angles( angles.x, angles.y, angles.z ),
			} );

			Bones( node["children"], name, found, named, unit, axes );
		}
	}

	//Three numbers, whichever way their document wrote them.
	private static (float x, float y, float z) Triple( CoreImportedKeyValues3 node )
	{
		if ( node is null ) return default;

		if ( node.IsArray )
		{
			var items = node.Items;

			return (
				items.Count > 0 ? items[0].AsFloat() : 0.0f,
				items.Count > 1 ? items[1].AsFloat() : 0.0f,
				items.Count > 2 ? items[2].AsFloat() : 0.0f );
		}

		var text = node.AsString();

		if ( string.IsNullOrWhiteSpace( text ) ) return default;

		var parts = text.Split( new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries );

		float At( int i ) => parts.Length > i && float.TryParse( parts[i],
			System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value )
			? value : 0.0f;

		return (At( 0 ), At( 1 ), At( 2 ));
	}
}

//Reading one of their model documents, which is the same however it was written and whatever is being looked for in it.
internal static class SBoxModelDoc
{
	/// <summary>Their document, text or packed.</summary>
	public static CoreImportedKeyValues3 Load( string file )
	{
		try
		{
			//The text their editor writes, and only that. A document read out of the DATA block of the file
			//beside it is the same document after their compiler has been over it - see PortPaths.IsCompiled.
			return CoreImportedKeyValues3.ReadText( File.ReadAllText( file ) );
		}
		catch ( Exception e ) when ( e is IOException or ArgumentException )
		{
			return null;
		}
	}

	/// <summary>Every node in the document and in the documents it is assembled from.</summary>
	public static IEnumerable<CoreImportedKeyValues3> Nodes( CoreImportedKeyValues3 node, string file )
	{
		foreach ( var found in Nodes( node, file, AssetsRootOf( file ), new HashSet<string>( StringComparer.OrdinalIgnoreCase ), 0 ) )
			yield return found;
	}

	private static IEnumerable<CoreImportedKeyValues3> Nodes( CoreImportedKeyValues3 node, string file,
		string root, HashSet<string> seen, int depth )
	{
		//A prefab naming a prefab is ordinary; one naming itself is not, and neither is a chain deeper than a rig ever gets.
		if ( depth > 8 ) yield break;

		foreach ( var found in Nodes( node ) )
		{
			yield return found;

			if ( root is null ) continue;
			if ( (found.String( "_class" ) ?? "") != "Prefab" ) continue;

			var target = found.String( "target_file" );

			if ( string.IsNullOrWhiteSpace( target ) ) continue;

			var path = Path.Combine( root, target.Replace( '/', Path.DirectorySeparatorChar ) );

			if ( !seen.Add( Path.GetFullPath( path ) ) ) continue;
			if ( !File.Exists( path ) ) continue;

			foreach ( var inside in Nodes( Load( path ), path, root, seen, depth + 1 ) )
				yield return inside;
		}
	}

	//The assets folder a file of theirs sits under, which is what its paths are written from. Null where it is not under one.
	private static string AssetsRootOf( string file )
	{
		if ( string.IsNullOrWhiteSpace( file ) ) return null;

		var at = Path.GetDirectoryName( Path.GetFullPath( file ) );

		while ( !string.IsNullOrEmpty( at ) )
		{
			if ( string.Equals( Path.GetFileName( at ), "Assets", StringComparison.OrdinalIgnoreCase ) ) return at;

			var above = Path.GetDirectoryName( at );

			if ( string.Equals( above, at, StringComparison.Ordinal ) ) break;

			at = above;
		}

		return null;
	}

	/// <summary>Every node in one document, however deep - their tree nests things inside folders inside lists, and how deep a node sits says nothing about what it is.</summary>
	public static IEnumerable<CoreImportedKeyValues3> Nodes( CoreImportedKeyValues3 node )
	{
		if ( node is null ) yield break;

		if ( node.IsArray )
		{
			foreach ( var item in node.Items )
			{
				foreach ( var child in Nodes( item ) )
					yield return child;
			}

			yield break;
		}

		if ( !node.IsObject ) yield break;

		if ( node["_class"] is not null ) yield return node;

		foreach ( var key in node.Keys )
		{
			var child = node[key];

			if ( child is null || key == "_class" ) continue;
			if ( !child.IsObject && !child.IsArray ) continue;

			foreach ( var found in Nodes( child ) )
				yield return found;
		}
	}
}
