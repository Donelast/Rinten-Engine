using System.Text.Json.Serialization;

namespace Rinten;

partial class ModelDefinition
{
	/// <summary>One shape a model is collided with.</summary>
	[JsonPolymorphic( TypeDiscriminatorPropertyName = "__type", IgnoreUnrecognizedTypeDiscriminators = true )]
	[JsonDerivedType( typeof( RenderHull ), nameof( RenderHull ) )]
	[JsonDerivedType( typeof( RenderMesh ), nameof( RenderMesh ) )]
	[JsonDerivedType( typeof( SphereHull ), nameof( SphereHull ) )]
	[JsonDerivedType( typeof( CapsuleHull ), nameof( CapsuleHull ) )]
	[JsonDerivedType( typeof( BoxHull ), nameof( BoxHull ) )]
	public abstract class Hull
	{
		[Property]
		public string Name { get; set; } = "";

		[Property, Hide]
		public bool Enabled { get; set; } = true;

		/// <summary>Bone this body hangs off. Empty is one rigid piece.</summary>
		[Property, ModelDocBone, ShowIf( nameof( ShowBone ), true )]
		public string Bone { get; set; } = "";

		[Property, ResourceType( "surface" ), Title( "Surface Override" )]
		public string Surface { get; set; } = "";

		//Kept on the definition rather than applied: a compiled model has no room for a shape's tags yet, so this is what was authored and what a rebuild writes back, not what the physics world is told.
		/// <summary>What this shape is, for whatever asks: solid, ladder, trigger. Space separated.</summary>
		[Property, Title( "Collision Tags" )]
		public string Tags { get; set; } = "";

		//A name rather than a place in a tree: a node keeps its folder when the list around it is reordered, and a folder nobody is in stops existing on its own.
		/// <summary>What folder the outliner sorts this into, or nothing for the top.</summary>
		[Property, Hide]
		public string Folder { get; set; } = "";

		[Property, Hide, JsonIgnore]
		public virtual bool ShowBone => true;

		[Hide, JsonIgnore]
		public abstract string Title { get; }

		[Hide, JsonIgnore]
		public virtual string Icon => "hexagon";

		[Hide, JsonIgnore]
		public virtual bool CanPlace => false;

		[Hide, JsonIgnore]
		public virtual Vector3 Origin => default;

		[Hide, JsonIgnore]
		public virtual Angles Turn => default;

		public virtual void Place( Vector3 origin, Angles turn ) { }
	}

	/// <summary>A convex hull, around what the model draws or around a mesh file of its own.</summary>
	public sealed class RenderHull : Hull
	{
		/// <summary>A mesh file to hull instead of the render mesh; empty hulls what the model draws.</summary>
		[Property, FilePath( "fbx,gltf,glb,obj,smd" ), Description( "A mesh to hull instead of what the model draws. Each mesh in it is a hull of its own." )]
		public string File { get; set; } = "";

		/// <summary>Applied to the file on the way in, for an export in the wrong units.</summary>
		[Property, Title( "Import Scale" ), ShowIf( nameof( HasFile ), true )]
		public float Scale { get; set; } = 1.0f;

		/// <summary>How many corners each hull may have; 44 is where Box3D stops being able to build one.</summary>
		[Property, Range( 0, 44 ), Description( "Corners to keep. 0 is the most the solver can build: 44. Under four is four." )]
		public int MaxVertices { get; set; } = 0;

		//What a shelf, a table or a bed needs: hulled whole, a convex shape fills every gap between the parts, so a bookcase collides as a solid block and nothing can be put on a shelf. A file is always taken mesh by mesh, so this is only asked where there is none.
		/// <summary>One hull per mesh in the model, rather than one around all of them at once.</summary>
		[Property, Description( "One hull per mesh rather than one around the whole model." ), ShowIf( nameof( HasFile ), false )]
		public bool PerElement { get; set; }

		[Property, Hide, JsonIgnore]
		public bool HasFile => !string.IsNullOrWhiteSpace( File );

		[Hide, JsonIgnore]
		public override string Title => HasFile ? System.IO.Path.GetFileName( File ) : "Hull";
	}

	//Static only. A triangle soup has no inside, so nothing can work out how heavy it is or push it about - a movable prop wants hulls.
	/// <summary>The model's own triangles, collided with exactly.</summary>
	public sealed class RenderMesh : Hull
	{
		[Hide, JsonIgnore]
		public override string Title => "Render Mesh";
		[Hide, JsonIgnore]
		public override string Icon => "grid_on";

		[Property, Hide, JsonIgnore]
		public override bool ShowBone => false;
	}

	/// <summary>A sphere in the bone's space, the usual head or fist.</summary>
	public sealed class SphereHull : Hull
	{
		[Property]
		public Vector3 Center { get; set; }

		[Property, Range( 0.01f, 8.0f )]
		public float Radius { get; set; } = 0.15f;

		[Hide, JsonIgnore]
		public override string Title => "Sphere";
		[Hide, JsonIgnore]
		public override string Icon => "circle";
		[Hide, JsonIgnore]
		public override bool CanPlace => true;
		[Hide, JsonIgnore]
		public override Vector3 Origin => Center;

		public override void Place( Vector3 origin, Angles turn ) => Center = origin;
	}

	/// <summary>A capsule between two points, the usual limb.</summary>
	public sealed class CapsuleHull : Hull
	{
		[Property, Title( "End A" )]
		public Vector3 PointA { get; set; } = new( 0, 0, -0.12f );

		[Property, Title( "End B" )]
		public Vector3 PointB { get; set; } = new( 0, 0, 0.12f );

		[Property, Range( 0.01f, 8.0f )]
		public float Radius { get; set; } = 0.08f;

		[Hide, JsonIgnore]
		public override string Title => "Capsule";
		[Hide, JsonIgnore]
		public override string Icon => "medication";
		[Hide, JsonIgnore]
		public override bool CanPlace => true;
		[Hide, JsonIgnore]
		public override Vector3 Origin => ( PointA + PointB ) * 0.5f;
		[Hide, JsonIgnore]
		public override Angles Turn
		{
			get
			{
				var along = PointB - PointA;
				if ( along.IsNearlyZero( 0.0001f ) ) return default;
				return Rotation.FromToRotation( Vector3.Up, along.Normal ).Angles();
			}
		}

		public override void Place( Vector3 origin, Angles turn )
		{
			var half = ( PointB - PointA ) * 0.5f;
			if ( half.IsNearlyZero( 0.0001f ) ) half = new Vector3( 0, 0, 0.12f );
			var rot = turn.ToRotation();
			var along = rot * new Vector3( 0, 0, half.Length );
			PointA = origin - along;
			PointB = origin + along;
		}
	}

	/// <summary>A box. Compiled as a hull of its eight corners.</summary>
	public sealed class BoxHull : Hull
	{
		[Property]
		public Vector3 Center { get; set; }

		[Property, Title( "Half Extents" )]
		public Vector3 Extents { get; set; } = new( 0.1f, 0.1f, 0.1f );

		[Property]
		public Angles Rotation { get; set; }

		[Hide, JsonIgnore]
		public override string Title => "Box";
		[Hide, JsonIgnore]
		public override string Icon => "crop_square";
		[Hide, JsonIgnore]
		public override bool CanPlace => true;
		[Hide, JsonIgnore]
		public override Vector3 Origin => Center;
		[Hide, JsonIgnore]
		public override Angles Turn => Rotation;

		public override void Place( Vector3 origin, Angles turn )
		{
			Center = origin;
			Rotation = turn;
		}

		public Vector3[] Corners()
		{
			var e = Extents.Abs();
			var rot = Rotation.ToRotation();
			var c = Center;
			return
			[
				c + rot * new Vector3( -e.x, -e.y, -e.z ),
				c + rot * new Vector3( e.x, -e.y, -e.z ),
				c + rot * new Vector3( -e.x, e.y, -e.z ),
				c + rot * new Vector3( e.x, e.y, -e.z ),
				c + rot * new Vector3( -e.x, -e.y, e.z ),
				c + rot * new Vector3( e.x, -e.y, e.z ),
				c + rot * new Vector3( -e.x, e.y, e.z ),
				c + rot * new Vector3( e.x, e.y, e.z ),
			];
		}
	}
}
