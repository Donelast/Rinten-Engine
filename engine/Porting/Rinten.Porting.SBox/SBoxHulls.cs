using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rinten.Porting.SBox;

//Collision shapes from the document's PhysicsShapeList; no list means no collision
internal static class SBoxHulls
{
	//Their node classes, and what each one is here.
	private const string HullFromRender = "PhysicsHullFromRender";
	private const string HullFromFile = "PhysicsHullFile";
	private const string MeshFromRender = "PhysicsMeshFromRender";
	private const string MeshFromFile = "PhysicsMeshFile";
	private const string SphereShape = "PhysicsShapeSphere";
	private const string CapsuleShape = "PhysicsShapeCapsule";
	private const string BoxShape = "PhysicsShapeBox";
	private const string CylinderShape = "PhysicsShapeCylinder";

	/// <summary>The shapes a <c>.vmdl</c> names, as this engine writes them.</summary>
	public static List<ModelDefinition.Hull> Read( string file, ForeignBasis bones,
		Func<string, string> carry = null, Action<string> note = null )
	{
		var hulls = new List<ModelDefinition.Hull>();

		var document = Load( file );
		if ( document is null ) return hulls;

		//One of their units, worth what it is worth on this model: the inch, and the resize the document's modifier
		//list puts on the whole thing. Every length below is a length in the model, so every one of them moves with it.
		var unit = SBoxModelModifiers.Unit( SBoxModelModifiers.Read( document ) );

		//Walk the prefabs too: characters keep their ragdoll shapes in a separate prefab
		foreach ( var node in SBoxModelDoc.Nodes( document, file ) )
		{
			var kind = node.String( "_class" ) ?? "";

			//Hung off a bone, or standing in the model. The first is written in that bone's frame and turns only if the bone did.
			var axes = string.IsNullOrWhiteSpace( node.String( "parent_bone" ) )
				? ForeignBasis.Quake
				: bones;

			if ( kind is HullFromRender or HullFromFile or MeshFromRender && Filtered( node ) )
			{
				note?.Invoke( $"a {kind} picks out some of its meshes and this hulls all of them - " +
					"the shape may be bigger than it was" );
			}

			switch ( kind )
			{
				case HullFromRender:
					hulls.Add( new ModelDefinition.RenderHull
					{
						Name = node.String( "name" ) ?? "",
						Bone = node.String( "parent_bone" ) ?? "",
						Surface = Surface( node ),
						Tags = node.String( "collision_tags" ) ?? "",
						MaxVertices = node.Int( "maxHullVertices" ),

						//Their editor's own word for a hull each: a shelf hulled whole is a solid block, and every prop in a room is authored this way.
						PerElement = string.Equals( node.String( "hull_mode" ) ?? "",
							"HullPerElement", StringComparison.OrdinalIgnoreCase ),
					} );
					break;

				case HullFromFile:
					var named = node.String( "filename" ) ?? "";
					var carried = carry is null ? named : carry( named ) ?? named;

					hulls.Add( new ModelDefinition.RenderHull
					{
						Name = node.String( "name" ) ?? "",
						Bone = node.String( "parent_bone" ) ?? "",
						Surface = Surface( node ),
						Tags = node.String( "collision_tags" ) ?? "",
						MaxVertices = node.Int( "maxHullVertices" ),
						File = carried,
						Scale = ImportScale( node, unit ),
					} );
					break;

				case MeshFromRender:
					hulls.Add( new ModelDefinition.RenderMesh
					{
						Name = node.String( "name" ) ?? "",
						Bone = node.String( "parent_bone" ) ?? "",
						Surface = Surface( node ),
						Tags = node.String( "collision_tags" ) ?? "",
					} );
					break;

				case MeshFromFile:
					note?.Invoke( $"{kind} is not read yet - that shape is missing" );
					break;

				case SphereShape:
					hulls.Add( new ModelDefinition.SphereHull
					{
						Name = node.String( "name" ) ?? "",
						Bone = node.String( "parent_bone" ) ?? "",
						Surface = Surface( node ),
						Tags = node.String( "collision_tags" ) ?? "",
						Center = Inch( node["center"], unit, axes ),
						Radius = Inch( node.Float( "radius" ), unit ),
					} );
					break;

				case CapsuleShape:
					hulls.Add( new ModelDefinition.CapsuleHull
					{
						Name = node.String( "name" ) ?? "",
						Bone = node.String( "parent_bone" ) ?? "",
						Surface = Surface( node ),
						Tags = node.String( "collision_tags" ) ?? "",
						PointA = Inch( node["point0"], unit, axes ),
						PointB = Inch( node["point1"], unit, axes ),
						Radius = Inch( node.Float( "radius" ), unit ),
					} );
					break;

				//Cylinder as a capsule of the same reach; one shorter than wide becomes a sphere
				case CylinderShape:
				{
					var a = Inch( node["point0"], unit, axes );
					var b = Inch( node["point1"], unit, axes );
					var radius = Inch( node.Float( "radius" ), unit );

					var along = b - a;
					var length = along.Length;

					if ( length > radius * 2.0f && length > 0.0f )
					{
						var inset = along / length * radius;
						a += inset;
						b -= inset;
					}
					else
					{
						a = b = ( a + b ) * 0.5f;
					}

					hulls.Add( new ModelDefinition.CapsuleHull
					{
						Name = node.String( "name" ) ?? "",
						Bone = node.String( "parent_bone" ) ?? "",
						Surface = Surface( node ),
						Tags = node.String( "collision_tags" ) ?? "",
						PointA = a,
						PointB = b,
						Radius = radius,
					} );

					note?.Invoke( $"{kind} on {node.String( "parent_bone" ) ?? "the model"} came across as a capsule - " +
						"nothing here is a cylinder, and a capsule of the same length and radius is the closest shape to one" );

					break;
				}

				case BoxShape:
					hulls.Add( new ModelDefinition.BoxHull
					{
						Name = node.String( "name" ) ?? "",
						Bone = node.String( "parent_bone" ) ?? "",
						Surface = Surface( node ),
						Tags = node.String( "collision_tags" ) ?? "",

						//modeldoc30 writes a box as origin and dimensions; the other spellings are what older documents use.
						Center = Inch( node["center"] ?? node["origin"], unit, axes ),
						Extents = InchExtent( Extent( node ), unit, axes ),
						Rotation = AnglesOf( node["rotation"] ?? node["angles"] ),
					} );
					break;
			}
		}

		return hulls;
	}

	//A length: the same number of metres wherever it is written.
	private static float Inch( float value, float unit ) => value * unit;

	//A place, turned onto the axes it is written against and measured in metres. How big a shape is
	//does not turn - only which of our axes each of its extents lands on, which is what Size answers.
	private static Vector3 Inch( Vector3 value, float unit, ForeignBasis axes ) =>
		axes.Vector( value ) * unit;

	private static Vector3 InchExtent( Vector3 value, float unit, ForeignBasis axes ) =>
		axes.Size( value ) * unit;

	private static Vector3 Inch( CoreImportedKeyValues3 node, float unit, ForeignBasis axes )
	{
		var (x, y, z) = Triple( node );
		return axes.Vector( new Vector3( x, y, z ) ) * unit;
	}

	private static Angles AnglesOf( CoreImportedKeyValues3 node )
	{
		var (x, y, z) = Triple( node );
		return new Angles( x, y, z );
	}

	private static Vector3 Extent( CoreImportedKeyValues3 node )
	{
		if ( node["half_extents"] is { } half ) return Vec( half );
		if ( node["extents"] is { } extents ) return Vec( extents );
		if ( node["extent"] is { } extent ) return Vec( extent );

		//Both are the whole box rather than half of it. dimensions is what modeldoc30 writes, and a box read without it came out a unit cube.
		if ( node["dimensions"] is { } dimensions ) return Vec( dimensions ) * 0.5f;
		if ( node["size"] is { } size ) return Vec( size ) * 0.5f;

		return new Vector3( 1, 1, 1 );
	}

	private static Vector3 Vec( CoreImportedKeyValues3 node )
	{
		var (x, y, z) = Triple( node );
		return new Vector3( x, y, z );
	}

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

		return (node.Float( "x" ), node.Float( "y" ), node.Float( "z" ));
	}

	//Import scale for a hull file: their scale times inches-to-metres
	private static float ImportScale( CoreImportedKeyValues3 node, float unit )
	{
		var scale = node.Float( "import_scale", 1.0f );
		if ( scale <= 0.0f ) scale = 1.0f;

		return scale * unit;
	}

	//Shape built from only some meshes; unsupported, so it hulls the whole file (reported)
	private static bool Filtered( CoreImportedKeyValues3 node )
	{
		var filter = node["import_filter"];
		if ( filter is null ) return false;

		return filter.Bool( "exclude_by_default" ) || filter.Array( "exception_list" ) is { Count: > 0 };
	}

	//What a shape says it feels like, where it says anything.
	private static string Surface( CoreImportedKeyValues3 node )
	{
		var surface = node.String( "surface_prop" ) ?? "";

		//Their default is a word, ours is a resource path that may not have been ported. An empty one
		//takes the model's own, which is the same answer without naming a file that might not be there.
		return string.Equals( surface, "default", StringComparison.OrdinalIgnoreCase ) ? "" : surface;
	}

	//Every node in the document, however deep. Their tree nests shapes inside lists inside folders, and how deep a node sits says nothing about what it is.
	private static IEnumerable<CoreImportedKeyValues3> Nodes( CoreImportedKeyValues3 node )
	{
		if ( node is null ) yield break;

		if ( node.IsArray )
		{
			foreach ( var item in node.Items )
			{
				foreach ( var found in Nodes( item ) )
					yield return found;
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
