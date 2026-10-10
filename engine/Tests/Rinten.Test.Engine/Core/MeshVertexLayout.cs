using System;
using System.Runtime.InteropServices;
using Rinten;

namespace EngineTests;

//Meshes with a custom vertex struct (e.g. editable meshes) are built as geometry with materials
[TestClass]
[DoNotParallelize]
public class MeshVertexLayoutTests
{
	//The same shape and layout PolygonMesh builds its vertices with.
	[StructLayout( LayoutKind.Sequential )]
	private struct EditableVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 Texcoord;
		[VertexLayout.TexCoord( 4 )] public Color32 Blend;
		[VertexLayout.TexCoord( 5 )] public Color32 Color;
	}

	//What a shader that works its own vertices out is written with.
	[StructLayout( LayoutKind.Sequential )]
	private struct PackedVertex
	{
		[VertexLayout.TexCoord] public uint Packed;
	}

	private static Mesh Made<T>( T[] vertices, Material material = null ) where T : unmanaged
	{
		var mesh = new Mesh( material );

		mesh.CreateVertexBuffer( vertices.Length, vertices.AsSpan() );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );

		return mesh;
	}

	private static EditableVertex[] Triangle()
	{
		return new[]
		{
			new EditableVertex { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Up },
			new EditableVertex { Position = new Vector3( 1, 0, 0 ), Normal = Vector3.Up },
			new EditableVertex { Position = new Vector3( 1, 1, 0 ), Normal = Vector3.Up },
		};
	}

	//The engine's own vertex describes itself the way the model side has it written down. Both are the same list, and they have to agree: the layout
	//read off the attributes is what a mesh now carries, and a mesh whose layout does not match this one is copied field by field instead of wholesale.
	[TestMethod]
	public void TheEnginesOwnVertexDescribesItself()
	{
		var described = VertexLayout.Attributes( typeof( Vertex ) );

		Assert.IsNotNull( described, "the engine's vertex is labelled throughout" );
		Assert.AreEqual( Vertex.Layout.Length, described.Length );

		for ( var i = 0; i < described.Length; i++ )
		{
			Assert.AreEqual( Vertex.Layout[i].Type, described[i].Type, $"attribute {i}" );
			Assert.AreEqual( Vertex.Layout[i].Format, described[i].Format, $"attribute {i} format" );
			Assert.AreEqual( Vertex.Layout[i].Components, described[i].Components, $"attribute {i} components" );
		}
	}

	//A custom vertex struct's attribute widths add up to its size; one wrong width shifts the rest
	[TestMethod]
	public void AVertexOfItsOwnIsDescribedByItsAttributes()
	{
		var described = VertexLayout.Attributes( typeof( EditableVertex ) );

		Assert.IsNotNull( described );
		Assert.AreEqual( 6, described.Length );

		Assert.AreEqual( VertexAttributeType.Position, described[0].Type );
		Assert.AreEqual( VertexAttributeFormat.Float32, described[0].Format );
		Assert.AreEqual( 3, described[0].Components );

		Assert.AreEqual( VertexAttributeType.TexCoord, described[3].Type );
		Assert.AreEqual( 2, described[3].Components, "a two-float texture coordinate, not a four" );

		//The two colour channels the engine's vertex has no room for, which are dropped on the way in and must not shift what follows them.
		Assert.AreEqual( VertexAttributeType.TexCoord, described[4].Type );
		Assert.AreEqual( VertexAttributeFormat.UInt8, described[4].Format );
		Assert.AreEqual( 4, described[4].SemanticIndex );
		Assert.AreEqual( 5, described[5].SemanticIndex );

		var total = 0;

		foreach ( var attribute in described )
		{
			total += attribute.Format switch
			{
				VertexAttributeFormat.Float32 => 4 * attribute.Components,
				VertexAttributeFormat.UInt8 => attribute.Components,
				_ => 0,
			};
		}

		Assert.AreEqual( Marshal.SizeOf<EditableVertex>(), total, "the attributes have to add up to the struct" );
	}

	//A mesh written with its own vertex goes into the model as geometry, with the material it was given.
	[TestMethod]
	public void AMeshWithItsOwnVertexKeepsItsMaterial()
	{
		ThreadSafe.MarkMainThread();

		var material = Material.FromDefinition( new MaterialDefinition(), "materials/test.mat" );
		Assert.IsNotNull( material, "a material with no file behind it, which is all this needs" );

		var model = new ModelBuilder().AddMesh( Made( Triangle(), material ) ).Create();

		Assert.IsNotNull( model );
		Assert.IsNull( model.pulled, "it says where it is, so it is geometry rather than bytes for a shader" );
		Assert.AreEqual( 1, model.MeshCount );

		Assert.AreEqual( "materials/test.mat", model.core.GroupMaterial( -1, 0 ),
			"and the model names the material, which is what the renderer looks up" );
	}

	//Two materials on one mesh are two meshes in the model, each named. This is what a level's brushwork is:
	//one editable mesh whose faces carry several materials, split into a mesh per material as it is built.
	[TestMethod]
	public void AMeshPerMaterial()
	{
		ThreadSafe.MarkMainThread();

		var one = Material.FromDefinition( new MaterialDefinition(), "materials/one.mat" );
		var two = Material.FromDefinition( new MaterialDefinition(), "materials/two.mat" );

		var model = new ModelBuilder()
			.AddMesh( Made( Triangle(), one ) )
			.AddMesh( Made( Triangle(), two ) )
			.Create();

		Assert.IsNull( model.pulled );
		Assert.AreEqual( 2, model.MeshCount );

		Assert.AreEqual( "materials/one.mat", model.core.GroupMaterial( -1, 0 ) );
		Assert.AreEqual( "materials/two.mat", model.core.GroupMaterial( -1, 1 ) );
	}

	//A vertex that does not say where it is still goes to the shader that knows what it means. The case the byte-carrying path exists for, and the reason
	//the test above is not simply "read everything through its layout": a mesh with no position read that way is however many vertices all at the origin.
	[TestMethod]
	public void AVertexWithNoPositionIsStillHandedToItsShader()
	{
		ThreadSafe.MarkMainThread();

		var packed = new[] { new PackedVertex(), new PackedVertex(), new PackedVertex() };

		var model = new ModelBuilder().AddMesh( Made( packed ) ).Create();

		Assert.IsNotNull( model.pulled, "nothing here says where it is" );
		Assert.AreEqual( 1, model.pulled.Length );
	}
}
