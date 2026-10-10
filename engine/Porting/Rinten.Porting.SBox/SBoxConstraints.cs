using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rinten.Porting.SBox;

//Procedural bone constraints (twist, helpers) read from the model document or its prefab
//They aren't in the compiled model or the clips, so the document is the only source
internal static class SBoxConstraints
{
	private const string PrefabNode = "Prefab";

	//How far a prefab may point at another one before this gives up.
	private const int Deepest = 8;

	//Their class names, and what each becomes here.
	private static readonly Dictionary<string, ModelDefinition.ConstraintKind> Kinds = new( StringComparer.OrdinalIgnoreCase )
	{
		["AnimConstraintTiltTwist"] = ModelDefinition.ConstraintKind.TiltTwist,
		["AnimConstraintParent"] = ModelDefinition.ConstraintKind.Parent,
		["AnimConstraintOrient"] = ModelDefinition.ConstraintKind.Orient,
		["AnimConstraintAim"] = ModelDefinition.ConstraintKind.Aim,
	};

	/// <summary>What a model document says its bones do about each other.</summary>
	public static List<ModelDefinition.Constraint> Read( string file, string sourceAssets, ForeignBasis bones )
	{
		var found = new List<ModelDefinition.Constraint>();
		var read = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		Walk( file, sourceAssets, found, read, 0, bones );

		//Compiled models: look for constraints in a nearby prefabs folder; unknown bones get dropped
		if ( found.Count == 0 ) Beside( file, found, read, bones );

		Spread( found, SBoxAttachments.Influences( file, sourceAssets, bones ) );

		return found;
	}

	//Expands drivers naming an attachment into its weighted bones (e.g. half elbow, half wrist)
	private static void Spread( List<ModelDefinition.Constraint> found,
		Dictionary<string, List<ModelDefinition.Attachment.Influence>> influences )
	{
		if ( influences.Count == 0 ) return;

		foreach ( var constraint in found )
		{
			if ( constraint.Drivers.TrueForAll( x => string.IsNullOrWhiteSpace( x.Attachment ) ) ) continue;

			var spread = new List<ModelDefinition.Constraint.Driver>();

			foreach ( var driver in constraint.Drivers )
			{
				if ( string.IsNullOrWhiteSpace( driver.Attachment )
					|| !influences.TryGetValue( driver.Attachment, out var pulled ) )
				{
					spread.Add( driver );
					continue;
				}

				//Their weights on one attachment already add to one, so the driver's own weight is what the whole place is worth here.
				var whole = 0.0f;

				foreach ( var one in pulled ) whole += one.Weight;

				if ( whole <= 0.0f ) { spread.Add( driver ); continue; }

				foreach ( var one in pulled )
				{
					spread.Add( new ModelDefinition.Constraint.Driver
					{
						Bone = one.Bone,
						Position = one.Position,
						Rotation = one.Rotation,
						Weight = one.Weight / whole * driver.Weight,
					} );
				}
			}

			constraint.Drivers = spread;
		}
	}

	//Searches outward ring by ring for constraint prefabs near a compiled model, stopping at a hit
	private static void Beside( string file, List<ModelDefinition.Constraint> found, HashSet<string> read,
		ForeignBasis axes )
	{
		var directory = Path.GetDirectoryName( file );
		if ( string.IsNullOrWhiteSpace( directory ) ) return;

		var parent = Path.GetDirectoryName( directory );

		//Beside the model, then in its own prefabs folder, then in the prefabs folders of the models it shares a
		//skeleton with - their human citizen reaches sideways into the plain citizen's for exactly that reason.
		foreach ( var ring in Rings( directory, parent ) )
		{
			foreach ( var folder in ring )
			{
				if ( folder is null || !Directory.Exists( folder ) ) continue;

				foreach ( var prefab in Prefabs( folder ) )
					Walk( prefab, null, found, read, 0, axes );
			}

			if ( found.Count > 0 ) return;
		}
	}

	//Where to look, nearest first.
	private static IEnumerable<string[]> Rings( string directory, string parent )
	{
		yield return new[] { directory, Path.Combine( directory, "prefabs" ) };

		if ( string.IsNullOrWhiteSpace( parent ) ) yield break;

		yield return new[] { Path.Combine( parent, "prefabs" ) };

		var sideways = new List<string>();

		try
		{
			foreach ( var beside in Directory.EnumerateDirectories( parent ) )
			{
				if ( string.Equals( beside, directory, StringComparison.OrdinalIgnoreCase ) ) continue;

				var prefabs = Path.Combine( beside, "prefabs" );

				if ( Directory.Exists( prefabs ) ) sideways.Add( prefabs );
			}
		}
		catch ( IOException )
		{
			yield break;
		}

		yield return sideways.ToArray();
	}

	//Their model prefabs in a folder, as their author wrote them. The compiled spelling used
	//to be globbed alongside. A port reads what was authored - see PortPaths.IsCompiled.
	private static IEnumerable<string> Prefabs( string folder )
	{
		string[] files;

		try { files = Directory.GetFiles( folder, "*.vmdl_prefab" ); }
		catch ( IOException ) { yield break; }

		foreach ( var file in files ) yield return file;
	}

	private static void Walk( string file, string sourceAssets,
		List<ModelDefinition.Constraint> found, HashSet<string> read, int depth, ForeignBasis axes )
	{
		if ( depth > Deepest || string.IsNullOrWhiteSpace( file ) ) return;
		if ( !File.Exists( file ) ) return;
		if ( !read.Add( Path.GetFullPath( file ) ) ) return;

		var document = SBoxModelDoc.Load( file );
		if ( document is null ) return;

		foreach ( var node in SBoxModelDoc.Nodes( document ) )
		{
			var kind = node.String( "_class" ) ?? "";

			if ( kind == PrefabNode )
			{
				if ( sourceAssets is null ) continue;

				var target = node.String( "target_file" ) ?? "";
				if ( string.IsNullOrWhiteSpace( target ) ) continue;

				var path = Path.Combine( sourceAssets, target.Replace( '/', Path.DirectorySeparatorChar ) );

				//Only what somebody wrote - see PortPaths.IsCompiled.
				if ( !File.Exists( path ) || PortPaths.IsCompiled( path ) ) continue;

				Walk( path, sourceAssets, found, read, depth + 1, axes );
				continue;
			}

			if ( !Kinds.TryGetValue( kind, out var made ) ) continue;

			if ( Constraint( node, made, axes ) is { } constraint ) found.Add( constraint );
		}
	}

	//Converts one constraint; the driven bone comes from its field, the child only as a fallback
	private static ModelDefinition.Constraint Constraint( CoreImportedKeyValues3 node,
		ModelDefinition.ConstraintKind kind, ForeignBasis axes )
	{
		var drivers = new List<ModelDefinition.Constraint.Driver>();

		var slave = "";
		var weight = 1.0f;

		foreach ( var child in node.Array( "children" ) )
		{
			switch ( child.String( "_class" ) ?? "" )
			{
				case "AnimConstraintBoneInput":
					drivers.Add( new ModelDefinition.Constraint.Driver
					{
						Bone = child.String( "parent_bone" ) ?? "",
						Weight = child.Float( "weight", 1.0f ),
					} );
					break;

				case "AnimConstraintAttachmentInput":
					drivers.Add( new ModelDefinition.Constraint.Driver
					{
						Attachment = child.String( "parent_attachment" ) ?? "",
						Weight = child.Float( "weight", 1.0f ),
					} );
					break;

				case "AnimConstraintSlave":
					slave = child.String( "parent_bone" ) ?? "";
					weight = child.Float( "weight", 1.0f );
					break;
			}
		}

		var named = node.String( "constrained_bone" ) ?? "";

		if ( !string.IsNullOrWhiteSpace( named ) ) slave = named;
		if ( string.IsNullOrWhiteSpace( slave ) || drivers.Count == 0 ) return null;

		//A parent constraint carries its own weight on the constraint rather than on the bone it drives, and
		//theirs run past one: the elbow helper is pushed a quarter of the way past where its driver sits.
		if ( kind == ModelDefinition.ConstraintKind.Parent ) weight = node.Float( "weight", weight );

		return new ModelDefinition.Constraint
		{
			Kind = kind,
			Bone = slave,
			Weight = weight,
			Drivers = drivers,
			DriverAxis = Axis( node.Int( "input_axis" ), axes ),
			BoneAxis = Axis( node.Int( "slave_axis" ), axes ),
		};
	}

	//Maps their axis index to ours through the basis; sign flips can't be expressed here
	private static ModelDefinition.ConstraintAxis Axis( int theirs, ForeignBasis axes )
	{
		var landed = axes.Vector( theirs switch
		{
			0 => new Vector3( 1, 0, 0 ),
			1 => new Vector3( 0, 1, 0 ),
			_ => new Vector3( 0, 0, 1 ),
		} );

		var x = MathF.Abs( landed.x );
		var y = MathF.Abs( landed.y );
		var z = MathF.Abs( landed.z );

		if ( x >= y && x >= z ) return ModelDefinition.ConstraintAxis.X;

		return y >= z ? ModelDefinition.ConstraintAxis.Y : ModelDefinition.ConstraintAxis.Z;
	}
}
