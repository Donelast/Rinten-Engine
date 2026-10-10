using System.IO;

namespace Rinten;

internal sealed partial class CoreModel
{
	//The compiled file this model already is, handed back as bytes. A model the core built keeps its vertices, indices, meshes and collision in the same
	//layout the file uses. Writing that out is what baking a runtime mesh to an asset is: the game then loads the file and never asks the voxels again.
	internal unsafe byte[] ToBytes()
	{
		if ( !IsValid || !CoreNative.Available ) return null;

		var builder = new CoreAssets.ModelBuilder();

		var scratch = Path.Combine( Path.GetTempPath(), $"rinten-save-{System.Guid.NewGuid():N}.mdl_c" );

		try
		{
			if ( !CopyGeometry( builder ) )
				return null;

			if ( HasCollision )
				builder.CollisionFromMesh( 1, "default", 1000 );

			if ( builder.Save( scratch ) == 0 )
				return null;

			return File.ReadAllBytes( scratch );
		}
		finally
		{
			builder.Dispose();

			if ( File.Exists( scratch ) )
				File.Delete( scratch );
		}
	}

	//Copy meshes, bones and groups into a builder, without collision.
	internal unsafe bool CopyGeometry( CoreAssets.ModelBuilder builder )
	{
		if ( !IsValid || builder is null ) return false;

		var vertexCount = handle.VertexCount;
		var indexCount = handle.IndexCount;
		if ( vertexCount <= 0 || indexCount <= 0 ) return false;

		builder.SetName( Name ?? "" );

		var vertices = new Vertex[vertexCount];
		var indices = new uint[indexCount];

		fixed ( Vertex* v = vertices )
		{
			if ( handle.CopyVertices( v, vertexCount ) <= 0 )
				return false;

			builder.AddVertices( v, vertexCount );
		}

		fixed ( uint* i = indices )
		{
			if ( handle.CopyIndices( i, indexCount ) <= 0 )
				return false;

			builder.AddIndices( i, indexCount );
		}

		//Not the impostor's plane, which is made again from the meshes it stands in for, and is always the last - see ModelBuilder.Impostor.
		var meshes = MeshCount;
		while ( meshes > 0 && handle.StandsIn( meshes - 1 ) ) meshes--;

		for ( var mesh = 0; mesh < meshes; mesh++ )
		{
			var range = MeshRange( mesh );
			builder.AddMesh( MeshName( mesh ), MeshMaterial( mesh ), range.FirstIndex, range.IndexCount, range.FirstVertex, range.VertexCount );
			builder.SetMeshGroup( mesh, MeshGroup( mesh ) );
			builder.SetMeshSurface( mesh, handle.MeshSurface( mesh ) );
		}

		CopyBones( builder );
		CopySkin( builder, vertexCount );
		CopyParts( builder );
		CopyMaterialGroups( builder, meshes );

		var box = stackalloc float[6];
		handle.GetRenderBounds( box );
		builder.SetRenderBounds( box );
		handle.GetPhysicsBounds( box );
		builder.SetPhysicsBounds( box );


		builder.SetDefaultMask( DefaultMask );
		return true;
	}

	//Copies skin weights; without them a rebuilt model's body stays in bind pose while the rig animates
	unsafe void CopySkin( CoreAssets.ModelBuilder builder, int vertexCount )
	{
		var count = handle.SkinCount;

		//Both halves or neither: a run that does not cover every vertex would leave the rest reading somebody else's weights.
		if ( count <= 0 || count != vertexCount ) return;

		var skin = new byte[count * 8];

		fixed ( byte* s = skin )
		{
			if ( handle.CopySkin( s, count ) <= 0 ) return;
		}

		//It arrives four bones then four weights a vertex, and goes back as two runs of its own.
		var bones = new byte[count * 4];
		var weights = new byte[count * 4];

		for ( var vertex = 0; vertex < count; vertex++ )
		{
			for ( var lane = 0; lane < 4; lane++ )
			{
				bones[( vertex * 4 ) + lane] = skin[( vertex * 8 ) + lane];
				weights[( vertex * 4 ) + lane] = skin[( vertex * 8 ) + 4 + lane];
			}
		}

		fixed ( byte* b = bones )
		fixed ( byte* w = weights )
		{
			builder.AddSkin( b, w, count );
		}
	}

	unsafe void CopyBones( CoreAssets.ModelBuilder builder )
	{
		var count = handle.BoneCount;
		for ( var bone = 0; bone < count; bone++ )
		{
			var matrix = stackalloc float[16];
			handle.BoneInverseBind( bone, matrix );
			CoreModelBones.Add( builder, handle.BoneName( bone ),
				handle.BoneParent( bone ), matrix );
		}
	}

	void CopyParts( CoreAssets.ModelBuilder builder )
	{
		for ( var part = 0; part < PartCount; part++ )
		{
			var index = builder.AddPart( PartName( part ) );
			if ( index < 0 ) continue;

			var choices = PartChoiceCount( part );
			for ( var choice = 0; choice < choices; choice++ )
				builder.AddPartChoice( index, PartChoiceName( part, choice ), PartChoiceMask( part, choice ) );
		}
	}

	void CopyMaterialGroups( CoreAssets.ModelBuilder builder, int meshes )
	{
		for ( var group = 0; group < MaterialGroupCount; group++ )
		{
			var index = builder.AddMaterialGroup( MaterialGroupName( group ) );
			if ( index < 0 ) continue;

			for ( var mesh = 0; mesh < meshes; mesh++ )
				builder.SetGroupMaterial( index, mesh, GroupMaterial( group, mesh ) );
		}
	}

	string MeshMaterial( int mesh )
		=> IsValid ? handle.MeshMaterial( mesh ) : "";
}
