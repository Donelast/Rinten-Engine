using NativeEngine;
using System.Runtime.InteropServices;

namespace Rinten
{
	/// <summary>Possible primitive types of a <see cref="Mesh"/>.</summary>
	public enum MeshPrimitiveType
	{
		Points,
		Lines,
		LineStrip,
		Triangles,
		TriangleStrip
	}

	/// <summary>A <a href="https://en.wikipedia.org/wiki/Polygon_mesh">mesh</a> is a basic version of a <see cref="Model"/>, containing a set of vertices and indices which make up faces that make up a shape. A set of meshes can be used to create a <see cref="Model"/> via the <see cref="ModelBuilder"/> class.</summary>
	public partial class Mesh : IValid
	{

		internal long instanceId;
		private int renderPrimType = (int)RenderPrimitiveType.RENDER_PRIM_TRIANGLES;

		//The vertices themselves, where this mesh is one of ours rather than a handle into the legacy render device. Null while that is loaded.
		internal CoreMesh core;

		public Mesh() : this( null, MeshPrimitiveType.Triangles )
		{

		}

		public Mesh( Material material, MeshPrimitiveType primType = MeshPrimitiveType.Triangles ) : this( "mesh", material, primType )
		{
		}

		public Mesh( string name, Material material, MeshPrimitiveType primType = MeshPrimitiveType.Triangles )
		{
			if ( string.IsNullOrWhiteSpace( name ) )
				name = "mesh";

			renderPrimType = (int)MeshPrimTypeToRenderPrimType( primType );

			//A render mesh belongs to the legacy renderer, and so does everything hung off it - the vertex and index buffers, the draw
			//calls. Ours keeps the vertices in memory instead, and a model builder makes a model out of them.
			core = new CoreMesh { Name = name, Material = material };
		}

		public bool IsValid => true;

		/// <summary>What the indices make. A model is built out of triangles, so a mesh of anything else is only a list of points to whatever reads it.</summary>
		public MeshPrimitiveType PrimitiveType
		{
			set => renderPrimType = (int)MeshPrimTypeToRenderPrimType( value );
			get => renderPrimType switch
			{
				(int)RenderPrimitiveType.RENDER_PRIM_POINTS => MeshPrimitiveType.Points,
				(int)RenderPrimitiveType.RENDER_PRIM_LINES => MeshPrimitiveType.Lines,
				(int)RenderPrimitiveType.RENDER_PRIM_LINE_STRIP => MeshPrimitiveType.LineStrip,
				(int)RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP => MeshPrimitiveType.TriangleStrip,
				_ => MeshPrimitiveType.Triangles,
			};
		}

		/// <summary>What this mesh is drawn with, where a sub mesh does not say otherwise.</summary>
		public Material Material
		{
			get => core?.Material;
			set { if ( core is not null ) core.Material = value; }
		}

		/// <summary>Add a sub mesh, drawing a range of the index buffer with its own <paramref name="material"/>. Create the vertex and index buffers before adding sub meshes.</summary>
		public Mesh AddSubMesh( Material material, int startIndex, int indexCount, int startVertex = 0, int vertexCount = 0 )
		{
			core?.SubMeshes.Add( new CoreMesh.SubMesh( material, startIndex, indexCount, startVertex, vertexCount ) );
			return this;
		}

		/// <summary>How big the mesh is - what a model built from it is culled and framed by. <see cref="CreateBuffers"/> works it out from the vertices.</summary>
		public BBox Bounds
		{
			get => core?.Bounds ?? default;
			set { if ( core is not null ) core.Bounds = value; }
		}

		/// <summary>Set how many vertices this mesh draws (if there's no index buffer)</summary>
		public void SetVertexRange( int start, int count )
		{
			core.VertexRange = (start, count);
		}

		/// <summary>Set how many indices this mesh draws</summary>
		public void SetIndexRange( int start, int count )
		{
			core.IndexRange = (start, count);
		}

		/// <summary>Create vertex and index buffers.</summary>
		public void CreateBuffers( VertexBuffer vb, bool calculateBounds = true )
		{
			var vertices_span = CollectionsMarshal.AsSpan( vb.Vertex );
			CreateVertexBuffer( vb.Vertex.Count, vertices_span );

			if ( vb.Indexed )
			{
				//This sucks but probably temp
				var indices = new int[vb.Index.Count];
				for ( int i = 0; i < indices.Length; ++i )
				{
					indices[i] = vb.Index[i];
				}

				CreateIndexBuffer( vb.Index.Count, indices );
			}

			if ( calculateBounds )
			{
				var bounds = new BBox();
				foreach ( var v in vb.Vertex )
				{
					bounds = bounds.AddPoint( v.Position );
				}

				Bounds = bounds;
			}
		}

		private static RenderPrimitiveType MeshPrimTypeToRenderPrimType( MeshPrimitiveType primType )
		{
			RenderPrimitiveType renderPrimType;
			switch ( primType )
			{
				case MeshPrimitiveType.Points:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_POINTS;
					break;
				case MeshPrimitiveType.Lines:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_LINES;
					break;
				case MeshPrimitiveType.LineStrip:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_LINE_STRIP;
					break;
				case MeshPrimitiveType.Triangles:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_TRIANGLES;
					break;
				case MeshPrimitiveType.TriangleStrip:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP;
					break;
				default:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_TRIANGLES;
					break;
			}

			return renderPrimType;
		}

		/// <summary>Triangulate a polygon made up of points, returns triangle indices into the list of vertices.</summary>
		public static unsafe Span<int> TriangulatePolygon( Span<Vector3> vertices )
		{
			if ( vertices.Length < 3 )
				return default;

			var vertexCount = vertices.Length;
			var indexCount = (vertexCount - 2) * 3;
			var indices = new int[indexCount];

			//Cutting a ring of points into triangles is arithmetic rather than engine, and ours does it where theirs is not in the process.
			indexCount = CoreTriangulator.Polygon( vertices, indices );
			return indices.AsSpan( 0, indexCount );
		}

		//The parts of the segment from a to b that lie inside a flat polygon, as pairs of points in
		//order along it. The polygon may be concave; the segment is taken in the polygon's plane.
		internal static void ClipPolygon( Span<Vector3> vertices, Vector3 a, Vector3 b, out Vector3[] outVertices )
		{
			outVertices = Array.Empty<Vector3>();

			var count = vertices.Length;
			if ( count < 3 ) return;

			//The polygon's plane, by Newell's method - which holds for a concave polygon as well as a convex one.
			var normal = Vector3.Zero;

			for ( var i = 0; i < count; ++i )
			{
				var p = vertices[i];
				var q = vertices[( i + 1 ) % count];

				normal.x += ( p.y - q.y ) * ( p.z + q.z );
				normal.y += ( p.z - q.z ) * ( p.x + q.x );
				normal.z += ( p.x - q.x ) * ( p.y + q.y );
			}

			if ( normal.LengthSquared < 1e-12f ) return;
			normal = normal.Normal;

			//Two axes in it, and everything flattened onto them. Crossed with whichever of Up and Forward is not along it - Up is y.
			var right = MathF.Abs( normal.y ) < 0.9f ? Vector3.Cross( normal, Vector3.Up ).Normal : Vector3.Cross( normal, Vector3.Forward ).Normal;
			var up = Vector3.Cross( normal, right );

			Vector2 Flat( Vector3 v ) => new( Vector3.Dot( v, right ), Vector3.Dot( v, up ) );

			var polygon = new Vector2[count];
			for ( var i = 0; i < count; ++i ) polygon[i] = Flat( vertices[i] );

			var start = Flat( a );
			var along = Flat( b ) - start;

			//Every place the segment crosses an edge, as a fraction of the way along it.
			var cuts = new List<float> { 0.0f, 1.0f };

			for ( var i = 0; i < count; ++i )
			{
				var p = polygon[i];
				var edge = polygon[( i + 1 ) % count] - p;

				var denominator = ( along.x * edge.y ) - ( along.y * edge.x );
				if ( MathF.Abs( denominator ) < 1e-12f ) continue;

				var offset = p - start;
				var t = ( ( offset.x * edge.y ) - ( offset.y * edge.x ) ) / denominator;
				var s = ( ( offset.x * along.y ) - ( offset.y * along.x ) ) / denominator;

				if ( t > 0.0f && t < 1.0f && s >= 0.0f && s <= 1.0f ) cuts.Add( t );
			}

			cuts.Sort();

			var inside = new List<Vector3>();

			for ( var i = 0; i + 1 < cuts.Count; ++i )
			{
				var from = cuts[i];
				var to = cuts[i + 1];

				if ( to - from < 1e-6f ) continue;

				//A piece between two crossings is all inside or all outside, so its middle says which.
				if ( !Contains( polygon, start + ( along * ( ( from + to ) * 0.5f ) ) ) ) continue;

				inside.Add( Vector3.Lerp( a, b, from ) );
				inside.Add( Vector3.Lerp( a, b, to ) );
			}

			outVertices = inside.ToArray();
		}

		//Even-odd: a point is inside where a ray from it crosses the outline an odd number of times.
		private static bool Contains( Vector2[] polygon, Vector2 point )
		{
			var inside = false;

			for ( int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++ )
			{
				var p = polygon[i];
				var q = polygon[j];

				if ( ( p.y > point.y ) != ( q.y > point.y ) &&
					point.x < ( ( q.x - p.x ) * ( point.y - p.y ) / ( q.y - p.y ) ) + p.x )
				{
					inside = !inside;
				}
			}

			return inside;
		}
	}
}
