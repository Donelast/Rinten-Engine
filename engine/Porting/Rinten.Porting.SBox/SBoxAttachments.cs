using Rinten;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rinten.Porting.SBox;

//Reads attachments from a model doc's AttachmentList, following Prefab references
internal static class SBoxAttachments
{
	//Their node classes.
	private const string AttachmentNode = "Attachment";
	private const string PrefabNode = "Prefab";

	//How far a prefab may point at another one before this gives up.
	private const int Deepest = 8;

	/// <summary>What a model document says it has, as this engine writes them.</summary>
	public static List<ModelDefinition.Attachment> Read( string file, string sourceAssets, ForeignBasis bones )
	{
		var found = new List<ModelDefinition.Attachment>();
		var named = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var read = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		//An attachment sits at a length inside the model, so it moves with whatever the document's modifier list resizes the model
		//by. Read from the model being ported: the resize belongs to it, not to the prefab an attachment happens to be written in.
		var unit = SBoxModelModifiers.Unit( file );

		Walk( file, sourceAssets, found, named, read, 0, unit, bones );

		//A compiled model has no document in it - what it has is the finished list, which is what an installed game ships and what base content
		//is carried from. Only asked when the document said nothing, so a project with the source beside it is still read from the source.

		return found;
	}

	/// <summary>The bones each attachment is pulled between, and by how much.</summary>
	public static Dictionary<string, List<ModelDefinition.Attachment.Influence>> Influences( string file, string sourceAssets, ForeignBasis bones )
	{
		var found = new Dictionary<string, List<ModelDefinition.Attachment.Influence>>( StringComparer.OrdinalIgnoreCase );

		var unit = SBoxModelModifiers.Unit( file );

		WalkInfluences( file, sourceAssets, found, new HashSet<string>( StringComparer.OrdinalIgnoreCase ), 0, unit, bones );

		//A compiled model has the finished list and no document, and the finished list is already weighted so it adds to one.

		return found;
	}

	//The bones one of their compiled attachments is pulled between.
	private static List<ModelDefinition.Attachment.Influence> Pulled( CoreImportedKeyValues3 value, float unit, ForeignBasis axes )
	{
		var pulled = new List<ModelDefinition.Attachment.Influence>();

		var count = value.Int( "m_nInfluences" );

		if ( count <= 0 ) return pulled;

		var names = value["m_influenceNames"];
		var offsets = value["m_vInfluenceOffsets"];
		var rotations = value["m_vInfluenceRotations"];
		var weights = value["m_influenceWeights"];

		for ( var i = 0; i < count; i++ )
		{
			var bone = At( names, i )?.AsString() ?? "";

			if ( string.IsNullOrWhiteSpace( bone ) ) continue;

			var weight = At( weights, i )?.AsFloat() ?? 1.0f;

			if ( weight == 0.0f ) continue;

			pulled.Add( new ModelDefinition.Attachment.Influence
			{
				Bone = bone,
				Position = Vector( At( offsets, i ), axes ) * unit,
				Rotation = Quaternion( At( rotations, i ), axes ).Angles(),
				Weight = weight,
			} );
		}

		return pulled;
	}

	//The list, or nothing where it says no more than the single bone does. Their compiler writes one influence as two halves of itself, so a list of
	//several entries naming one bone is still a place on that bone - and writing one out for every attachment on every model is a lot of file for nothing.
	private static List<ModelDefinition.Attachment.Influence> Several( List<ModelDefinition.Attachment.Influence> pulled )
	{
		var bones = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var one in pulled ) bones.Add( one.Bone );

		return bones.Count > 1 ? pulled : new List<ModelDefinition.Attachment.Influence>();
	}

	private static CoreImportedKeyValues3 First( CoreImportedKeyValues3 node )
		=> node is { IsArray: true, Items.Count: > 0 } ? node.Items[0] : null;

	private static Vector3 Vector( CoreImportedKeyValues3 node, ForeignBasis axes )
	{
		var (x, y, z) = Triple( node );

		return axes.Vector( new Vector3( x, y, z ) );
	}

	private static Rotation Quaternion( CoreImportedKeyValues3 node, ForeignBasis axes )
	{
		if ( node is not { IsArray: true } || node.Items.Count < 4 ) return Rotation.Identity;

		var made = new Rotation(
			node.Items[0].AsFloat(), node.Items[1].AsFloat(),
			node.Items[2].AsFloat(), node.Items[3].AsFloat() );

		//A quaternion of nothing is what an attachment with no rotation written into it reads as, and normalising that gives something arbitrary.
		var length = ( made.x * made.x ) + ( made.y * made.y ) + ( made.z * made.z ) + ( made.w * made.w );

		return length <= 0.0001f ? Rotation.Identity : axes.Quat( made.Normal );
	}

	private static void Walk( string file, string sourceAssets,
		List<ModelDefinition.Attachment> found, HashSet<string> named, HashSet<string> read, int depth, float unit, ForeignBasis axes )
	{
		if ( depth > Deepest || string.IsNullOrWhiteSpace( file ) ) return;
		if ( !read.Add( Path.GetFullPath( file ) ) ) return;

		var document = Load( file );
		if ( document is null ) return;

		foreach ( var node in Nodes( document ) )
		{
			switch ( node.String( "_class" ) ?? "" )
			{
				case AttachmentNode:
					var attachment = Attachment( node, unit, axes );

					//The same name twice is the model overriding what a prefab gave it, and the model is read first.
					if ( attachment is null || !named.Add( attachment.Name ) ) continue;

					attachment.Influences = Several( Written( node, unit, axes ) );

					found.Add( attachment );
					break;

				case PrefabNode:
					if ( sourceAssets is null ) continue;

					var target = node.String( "target_file" ) ?? "";
					if ( string.IsNullOrWhiteSpace( target ) ) continue;

					var path = Path.Combine( sourceAssets, target.Replace( '/', Path.DirectorySeparatorChar ) );

					//Only what somebody wrote. An install has the compiled copy beside the name their document
					//uses, and that is the one thing a port does not read - see PortPaths.IsCompiled.
					if ( !File.Exists( path ) || PortPaths.IsCompiled( path ) ) continue;

					Walk( path, sourceAssets, found, named, read, depth + 1, unit, axes );
					break;
			}
		}
	}

	//One of their attachments, in metres and in our own shape.
	private static ModelDefinition.Attachment Attachment( CoreImportedKeyValues3 node, float unit, ForeignBasis axes )
	{
		var name = node.String( "name" ) ?? "";

		if ( string.IsNullOrWhiteSpace( name ) ) return null;

		var origin = Triple( node["relative_origin"] );
		var angles = Triple( node["relative_angles"] );

		return new ModelDefinition.Attachment
		{
			Name = name,
			Parent = node.String( "parent_bone" ) ?? "",
			FacesOneWay = node.Bool( "ignore_rotation", false ),

			//Units and axes go through the basis; angles carry over between right-handed bases
			Position = axes.Vector( new Vector3( origin.x, origin.y, origin.z ) ) * unit,
			Rotation = axes.Angles( new Angles( angles.x, angles.y, angles.z ) ),
		};
	}

	//The bones one of their written attachments is pulled between.
	private static List<ModelDefinition.Attachment.Influence> Written( CoreImportedKeyValues3 node, float unit, ForeignBasis axes )
	{
		var pulled = new List<ModelDefinition.Attachment.Influence>();

		foreach ( var child in node.Array( "children" ) )
		{
			if ( ( child.String( "_class" ) ?? "" ) != "AttachmentInfluence" ) continue;

			var bone = child.String( "parent_bone" ) ?? "";

			if ( string.IsNullOrWhiteSpace( bone ) ) continue;

			var weight = child.Float( "weight", 1.0f );

			if ( weight == 0.0f ) continue;

			var origin = Triple( child["relative_origin"] );
			var angles = Triple( child["relative_angles"] );

			pulled.Add( new ModelDefinition.Attachment.Influence
			{
				Bone = bone,
				Position = axes.Vector( new Vector3( origin.x, origin.y, origin.z ) ) * unit,
				Rotation = axes.Angles( new Angles( angles.x, angles.y, angles.z ) ),
				Weight = weight,
			} );
		}

		return pulled;
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

		//Written as text - "0 0 8" - which is how the older documents have it.
		var text = node.AsString();

		if ( string.IsNullOrWhiteSpace( text ) ) return default;

		var parts = text.Split( new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries );

		float At( int i ) => parts.Length > i && float.TryParse( parts[i],
			System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value )
			? value : 0.0f;

		return (At( 0 ), At( 1 ), At( 2 ));
	}

	//Every node in the document, however deep - their tree nests attachments inside folders inside lists.
	private static IEnumerable<CoreImportedKeyValues3> Nodes( CoreImportedKeyValues3 node )
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

	private static void WalkInfluences( string file, string sourceAssets,
		Dictionary<string, List<ModelDefinition.Attachment.Influence>> found, HashSet<string> read, int depth, float unit, ForeignBasis axes )
	{
		if ( depth > Deepest || string.IsNullOrWhiteSpace( file ) ) return;
		if ( !File.Exists( file ) ) return;
		if ( !read.Add( Path.GetFullPath( file ) ) ) return;

		var document = Load( file );
		if ( document is null ) return;

		foreach ( var node in Nodes( document ) )
		{
			switch ( node.String( "_class" ) ?? "" )
			{
				case AttachmentNode:
					var name = node.String( "name" ) ?? "";

					if ( string.IsNullOrWhiteSpace( name ) || found.ContainsKey( name ) ) continue;

					//The same reading as everywhere else. Written out a second time it drifted: the copy here kept its angles unturned long after the other one stopped.
					var pulled = Written( node, unit, axes );

					if ( pulled.Count > 0 ) found[name] = pulled;
					break;

				case PrefabNode:
					if ( sourceAssets is null ) continue;

					var target = node.String( "target_file" ) ?? "";
					if ( string.IsNullOrWhiteSpace( target ) ) continue;

					var path = Path.Combine( sourceAssets, target.Replace( '/', Path.DirectorySeparatorChar ) );

					//Only what somebody wrote - see PortPaths.IsCompiled.
					if ( !File.Exists( path ) || PortPaths.IsCompiled( path ) ) continue;

					WalkInfluences( path, sourceAssets, found, read, depth + 1, unit, axes );
					break;
			}
		}
	}

	private static CoreImportedKeyValues3 At( CoreImportedKeyValues3 node, int index )
		=> node is { IsArray: true } && index < node.Items.Count ? node.Items[index] : null;

	//Their document, read however it was written.
	private static CoreImportedKeyValues3 Load( string file )
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
}
