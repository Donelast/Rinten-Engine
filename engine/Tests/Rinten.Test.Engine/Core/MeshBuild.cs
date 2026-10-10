using System;
using Rinten;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Meshes keep their own vertices: reading them back in any layout, and building models without a file
[TestClass]
public class CoreMeshTests
{
	private static CoreMesh Made( Vertex[] vertices, int[] indices = null )
	{
		var mesh = new CoreMesh
		{
			Name = "test",
			VertexCount = vertices.Length,
			VertexStride = System.Runtime.InteropServices.Marshal.SizeOf<Vertex>(),
			Vertices = new byte[vertices.Length * System.Runtime.InteropServices.Marshal.SizeOf<Vertex>()],
			Indices = indices,
		};

		System.Runtime.InteropServices.MemoryMarshal.AsBytes( vertices.AsSpan() ).CopyTo( mesh.Vertices );
		return mesh;
	}

	private static Vertex[] Triangle()
	{
		return new[]
		{
			new Vertex( new Vector3( 0, 0, 0 ), Vector3.Up, Vector3.Forward, new Vector4( 0, 0, 0, 0 ) ),
			new Vertex( new Vector3( 10, 0, 0 ), Vector3.Up, Vector3.Forward, new Vector4( 1, 0, 0, 0 ) ),
			new Vertex( new Vector3( 0, 10, 0 ), Vector3.Up, Vector3.Forward, new Vector4( 0, 1, 0, 0 ) ),
		};
	}

	[TestMethod]
	public void TheEnginesOwnVerticesCrossAsACopy()
	{
		var source = Triangle();
		var mesh = Made( source );

		Assert.IsTrue( mesh.IsEngineVertex, "the engine's vertex is the one the core reads" );

		var read = mesh.AsEngineVertices();

		Assert.AreEqual( source.Length, read.Length );

		for ( var i = 0; i < source.Length; i++ )
		{
			Assert.AreEqual( source[i].Position, read[i].Position, $"position {i}" );
			Assert.AreEqual( source[i].Normal, read[i].Normal, $"normal {i}" );
			Assert.AreEqual( source[i].TexCoord0, read[i].TexCoord0, $"uv {i}" );
		}
	}

	//A vertex of somebody else's, read through the layout it declared.
	private struct Small
	{
		public Vector3 Position;
		public Color32 Colour;
	}

	[TestMethod]
	public void AVertexOfAnotherShapeIsReadThroughItsLayout()
	{
		var source = new[]
		{
			new Small { Position = new Vector3( 1, 2, 3 ), Colour = new Color32( 10, 20, 30, 40 ) },
			new Small { Position = new Vector3( 4, 5, 6 ), Colour = new Color32( 50, 60, 70, 80 ) },
		};

		var stride = System.Runtime.InteropServices.Marshal.SizeOf<Small>();

		var mesh = new CoreMesh
		{
			VertexCount = source.Length,
			VertexStride = stride,
			Vertices = new byte[source.Length * stride],
			Layout = new[]
			{
				new VertexAttribute( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
				new VertexAttribute( VertexAttributeType.Color, VertexAttributeFormat.UInt8, 4 ),
			},
		};

		System.Runtime.InteropServices.MemoryMarshal.AsBytes( source.AsSpan() ).CopyTo( mesh.Vertices );

		Assert.IsFalse( mesh.IsEngineVertex, "not the engine's own vertex" );

		var read = mesh.AsEngineVertices();

		Assert.AreEqual( new Vector3( 1, 2, 3 ), read[0].Position );
		Assert.AreEqual( new Vector3( 4, 5, 6 ), read[1].Position );

		Assert.AreEqual( 10, read[0].Colour().r );
		Assert.AreEqual( 80, read[1].Colour().a );
	}

	//A model out of the core's builder, with no file anywhere.
	[TestMethod]
	public unsafe void ABuilderMakesAModelInMemory()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "no core to build with" );
			return;
		}

		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder, "the core would not make a builder" );

		try
		{
			builder.SetName( "test" );

			var vertices = Triangle();
			var indices = new uint[] { 0, 1, 2 };

			fixed ( Vertex* v = vertices )
				Assert.AreEqual( 0, builder.AddVertices( v, vertices.Length ) );

			fixed ( uint* i = indices )
				Assert.AreEqual( 0, builder.AddIndices( i, indices.Length ) );

			builder.AddMesh( "test", "", 0, indices.Length, 0, vertices.Length );

			var bounds = stackalloc float[6] { 0, 0, 0, 10, 10, 0 };
			builder.SetRenderBounds( bounds );

			using var model = CoreModel.FromBuilder( builder, "test.mdl" );

			Assert.IsNotNull( model, "the builder made nothing" );
			Assert.IsTrue( model.IsValid );
			Assert.AreEqual( 1, model.MeshCount );

			var range = model.MeshRange( 0 );

			Assert.AreEqual( 3, range.IndexCount );
			Assert.AreEqual( 3, range.VertexCount );
		}
		finally
		{
			builder.Dispose();
		}
	}
}

internal static class VertexColourExtension
{
	//The colour of a vertex, named so the test reads.
	internal static Color32 Colour( this Vertex vertex ) => vertex.Color;
}
