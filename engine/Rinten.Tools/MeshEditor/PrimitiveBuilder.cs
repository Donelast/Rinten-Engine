namespace Editor.MeshEditor;

/// <summary>Build primitives out of polygons.</summary>
public abstract class PrimitiveBuilder
{
	/// <summary>A list of vertices and faces.</summary>
	public sealed class PolygonMesh
	{
		/// <summary>A list of indices indexing into the <see cref="Vertices"/> list.</summary>
		public sealed class Face
		{
			private readonly int[] _indices;
			public IReadOnlyList<int> Indices => _indices;
			public string Material { get; set; }

			internal Face( IEnumerable<int> indices )
			{
				_indices = indices.ToArray();
			}
		}

		public List<Vector3> Vertices { get; private init; } = new();
		public List<Face> Faces { get; private init; } = new();

		/// <summary>Adds a new vertex to the end of the <see cref="Vertices"/> list.</summary>
		public int AddVertex( Vector3 position )
		{
			var index = Vertices.FindIndex( x => x.Distance( position ).AlmostEqual( 0.0f ) );
			if ( index >= 0 )
				return index;

			Vertices.Add( position );
			return Vertices.Count - 1;
		}

		/// <summary>Adds a new face to the end of the <see cref="Faces"/> list.</summary>
		public Face AddFace( params int[] indices )
		{
			if ( indices.Length < 3 )
				return null;

			Faces.Add( new Face( indices ) );
			return Faces[^1];
		}

		/// <summary>Adds a new face to the end of the <see cref="Faces"/> list and it's vertices to the end of the <see cref="Vertices"/> list.</summary>
		public Face AddFace( params Vector3[] positions )
		{
			if ( positions.Length < 3 )
				return null;

			Faces.Add( new Face( positions.Select( AddVertex ) ) );
			return Faces[^1];
		}

		/// <summary>Appends another mesh without welding its vertices into this one.</summary>
		public void AddMesh( PolygonMesh mesh )
		{
			var firstVertex = Vertices.Count;
			Vertices.AddRange( mesh.Vertices );

			foreach ( var face in mesh.Faces )
			{
				Faces.Add( new Face( face.Indices.Select( index => firstVertex + index ) )
				{
					Material = face.Material
				} );
			}
		}
	}

	/// <summary>Create the primitive in the mesh.</summary>
	public abstract void Build( PolygonMesh mesh );

	/// <summary>Setup properties from box.</summary>
	public abstract void SetFromBox( BBox box );

	/// <summary>If this primitive is 2D the bounds box will be limited to have no depth.</summary>
	[Hide]
	public virtual bool Is2D { get => false; }

	/// <summary>The material to use for this whole primitive. Loaded on demand so builders can be created without the render system.</summary>
	[Hide]
	public Material Material
	{
		get => field ??= Material.Load( "materials/dev/grid.mat" );
		set;
	}
}
