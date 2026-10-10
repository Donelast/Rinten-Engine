namespace Rinten;

partial class PolygonMesh
{
	//Mesh as FBX geometry: quads kept, per-corner normal/UV/colour as drawn; hidden faces skipped
	internal void WriteFbxGeometry( FbxGeometry into, Rotation rotation, Vector3 scale )
	{
		//A mirroring scale turns every face inside out; its corners go the other way round to face out again.
		var mirrored = scale.x * scale.y * scale.z < 0.0f;
		var squashed = scale.x.AlmostEqual( 0.0f ) || scale.y.AlmostEqual( 0.0f ) || scale.z.AlmostEqual( 0.0f );

		var points = new Dictionary<int, int>();
		var slots = new Dictionary<string, int>();

		foreach ( var hFace in Topology.FaceHandles )
		{
			if ( IsFaceHidden( hFace ) )
				continue;

			var vertices = Topology.GetFaceVertices( hFace );
			if ( vertices.Length < 3 )
				continue;

			if ( !GetFaceVerticesConnectedToFace( hFace, out var corners ) || corners.Length != vertices.Length )
				continue;

			var count = vertices.Length;

			for ( var n = 0; n < count; n++ )
			{
				var i = mirrored ? count - 1 - n : n;
				var hVertex = vertices[i];
				var corner = corners[i];

				if ( !points.TryGetValue( hVertex.Index, out var point ) )
				{
					point = points.Count;
					points.Add( hVertex.Index, point );

					var position = rotation * (Positions[hVertex] * scale);
					into.Positions.Add( position.x );
					into.Positions.Add( position.y );
					into.Positions.Add( position.z );
				}

				//The last corner of a face is written as its complement: that is how FBX says where one polygon ends and the next begins.
				into.PolygonVertices.Add( n == count - 1 ? ~point : point );

				var normal = ComputeFaceVertexNormal( corner );
				if ( !squashed ) normal = new Vector3( normal.x / scale.x, normal.y / scale.y, normal.z / scale.z );
				normal = (rotation * normal).Normal;

				into.Normals.Add( normal.x );
				into.Normals.Add( normal.y );
				into.Normals.Add( normal.z );

				//Ours count down from the top of the picture, FBX's up from the bottom - the importer turns them back the same way.
				var uv = TextureCoord[corner];
				into.Uvs.Add( uv.x );
				into.Uvs.Add( 1.0 - uv.y );

				var color = Colors[corner];
				if ( color.r != 255 || color.g != 255 || color.b != 255 || color.a != 255 ) into.HasColors = true;

				into.Colors.Add( color.r / 255.0 );
				into.Colors.Add( color.g / 255.0 );
				into.Colors.Add( color.b / 255.0 );
				into.Colors.Add( color.a / 255.0 );
			}

			var material = GetMaterial( MaterialIndex[hFace] );
			var name = FbxExport.MaterialName( material );

			if ( !slots.TryGetValue( name, out var slot ) )
			{
				slot = slots.Count;
				slots.Add( name, slot );
				into.Materials.Add( name );
			}

			into.PolygonMaterials.Add( slot );
		}
	}
}
