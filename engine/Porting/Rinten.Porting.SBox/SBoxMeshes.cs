using Rinten;
using System;
using System.Collections.Generic;
using System.IO;

namespace Rinten.Porting.SBox;

//Art pieces a model document assembles, each with its own scale, offset and rotation (inches)
internal static class SBoxMeshes
{
	private const string MeshNode = "RenderMeshFile";

	//One export in our units; FilterNames null means the whole file, ExcludesByDefault picks the mode
	internal readonly record struct Part( string Name, string File, float Scale, Vector3 Position, Angles Rotation,
		string Parent, bool FilterExcludesByDefault, IReadOnlyList<string> FilterNames );

	/// <summary>Every export the document names, in the order it names them.</summary>
	public static List<Part> Read( string file )
	{
		var found = new List<Part>();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		var document = SBoxModelDoc.Load( file );

		if ( document is null ) return found;

		//What the document does to the assembled model, which every piece of it is subject to: a resize on the
		//model as a whole multiplies each export's own import scale, and moves each export's offset with it.
		var modifier = SBoxModelModifiers.Read( document );

		foreach ( var node in SBoxModelDoc.Nodes( document, file ) )
		{
			if ( (node.String( "_class" ) ?? "") != MeshNode ) continue;

			var named = node.String( "filename" ) ?? "";

			if ( string.IsNullOrWhiteSpace( named ) ) continue;

			//What their document calls this piece, which is what its body groups name: "chest", "legs", "head_01".
			var name = node.String( "name" ) ?? "";

			//Dedupe by name, file and filter, since one file is often split into several filtered parts
			if ( !seen.Add( $"{name}\u0000{named}\u0000{Written( node["import_filter"] )}" ) ) continue;

			var scale = node["import_scale"] is { } given ? given.AsFloat( 1.0f ) : 1.0f;

			if ( scale <= 0.0f ) scale = 1.0f;

			var translation = Triple( node["import_translation"] );
			var rotation = Triple( node["import_rotation"] );

			var (excludeByDefault, filterNames) = Filter( node["import_filter"] );

			found.Add( new Part(
				name,
				named,

				//Their unit is an inch and ours is a metre, and whatever the document asked for on top of
				//that - this export's own import scale, and the resize the modifier list puts on the model.
				scale * SBoxModelModifiers.Unit( modifier ),

				//Their offset is written in their axes; the three angles mean the same thing in both and come over as they are.
				Axes.Vector( new Vector3( translation.x, translation.y, translation.z ) ) * SBoxModelModifiers.Unit( modifier ),
				new Angles( rotation.x, rotation.y, rotation.z ),

				//A piece bolted to a bone is placed from that bone, not from the model - a sight
				//sits where the receiver is, and the receiver is wherever the animation has put it.
				node.String( "parent_bone" ) ?? "",

				excludeByDefault,
				filterNames ) );
		}

		return found;
	}

	//A filter written out flat, so two pieces of one file can be told apart by what each of them keeps.
	private static string Written( CoreImportedKeyValues3 node )
	{
		var (exclude, names) = Filter( node );

		if ( names is not { Count: > 0 } ) return "";

		var sorted = new List<string>( names );
		sorted.Sort( StringComparer.OrdinalIgnoreCase );

		return ( exclude ? "-" : "+" ) + string.Join( ",", sorted );
	}

	//Reads which meshes of a shared export this part draws; without it every mesh comes along
	private static (bool ExcludeByDefault, List<string> Names) Filter( CoreImportedKeyValues3 node )
	{
		if ( node is null ) return (false, null);

		var names = new List<string>();

		foreach ( var item in node.Array( "exception_list" ) )
		{
			var value = item?.AsString();
			if ( !string.IsNullOrWhiteSpace( value ) ) names.Add( value );
		}

		return names.Count > 0 ? (node.Bool( "exclude_by_default" ), names) : (false, null);
	}

	/// <summary>What to read a piece of art at, once it is known which file it is.</summary>
	public static float ScaleFor( in Part part, string path ) => ScaleFor( part.Scale, path );

	public static float ScaleFor( float authored, string path )
	{
		var unit = Path.GetExtension( path )?.ToLowerInvariant() == ".fbx"
			? SBoxFbxUnits.Metres( path )
			: 1.0f;

		return unit > 0.0f ? authored / unit : authored;
	}

	//s&box's axes, Source's, and the turn onto ours.
	private static ForeignBasis Axes => ForeignBasis.Quake;

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
