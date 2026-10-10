using System.Collections.Generic;
using System.Linq;

namespace SceneTests.Components;

[TestClass]
public class PolygonMeshSerializationTest
{
	//Old format with Position, Rotation, and world-space texture parameters.
	const string LegacyMeshJson = """
	{
		"Topology": "H4sIAAAAAAAACl2QCQ7CMAwEp4Qj3NBy/f+laMRGsohUVbFn1+t04ANs+Z0VsM69AwfgBFyAKZ9MPXdgk3qLnsI+Um/h/me9i2ZXfFvYOazafTJN0XRgKZmPyWtfXvYVL73PZRd52WdYPa7xtC8veys7u6t17/7t6Tn0ZtFjvIXZR895zjKjPWf4NiOrGt+ivpXZ3XnozaOndVn9vvVkkTjEAQAA",
		"Position": "4.695496,-71.68547,64",
		"Rotation": "0,0,0,1",
		"Positions": [
			"-26.3793,-152.6447,64",
			"26.3793,-152.6447,64",
			"26.3793,152.6447,64",
			"-26.3793,152.6447,64",
			"-26.3793,152.6447,-64",
			"26.3793,152.6447,-64",
			"26.3793,-152.6447,-64",
			"-26.3793,-152.6447,-64"
		],
		"Blends": ["0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0"],
		"Colors": ["0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0","0,0,0,0"],
		"TextureCoord": [
			"0.2427719,1.752579",
			"-0.1694047,-1",
			"0.2427719,-0.632494",
			"-1.752579,-1",
			"-0.1694047,-0.632494",
			"-0.2427719,-1",
			"-0.1694047,1.752579",
			"-0.632494,-1",
			"0.2427719,-0.632494",
			"0.1694047,0",
			"0.2427719,1.752579",
			"0.632494,0",
			"-0.1694047,1.752579",
			"0.2427719,0",
			"-0.1694047,-0.632494",
			"1.752579,0",
			"1.752579,-1",
			"-0.1694047,0",
			"-0.632494,0",
			"0.1694047,-1",
			"-1.752579,0",
			"0.2427719,-1",
			"0.632494,-1",
			"-0.2427719,0"
		],
		"TextureUAxis": ["1,0,0","1,0,-0","0,-1,0","0,1,0","-1,0,0","1,-0,0"],
		"TextureVAxis": ["0,-1,0","0,-1,0","0,0,-1","-0,0,-1","0,0,-1","-0,0,-1"],
		"TextureScale": ["0.25,0.25","0.25,0.25","0.25,0.25","0.25,0.25","0.25,0.25","0.25,0.25"],
		"TextureOffset": ["0,0","0,0","0,0","0,0","0,0","0,0"],
		"MaterialIndex": [-1,-1,-1,-1,-1,-1],
		"EdgeFlags": [0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]
	}
	""";

	[TestMethod]
	public void ReadLegacyFormatWithWorldSpaceFields()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );

		Assert.IsNotNull( mesh );
		Assert.AreEqual( new Vector3( 4.695496f, -71.68547f, 64f ), mesh.Transform.Position );
		Assert.AreEqual( 8, mesh.VertexHandles.Count() );
	}

	[TestMethod]
	public void WriteDoesNotContainWorldSpaceFields()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( mesh );

		Assert.IsFalse( json.Contains( "\"Position\"" ), "Should not write world-space Position" );
		Assert.IsFalse( json.Contains( "\"Rotation\"" ), "Should not write world-space Rotation" );
		Assert.IsFalse( json.Contains( "\"TextureUAxis\"" ), "Should not write derived TextureUAxis" );
		Assert.IsFalse( json.Contains( "\"TextureVAxis\"" ), "Should not write derived TextureVAxis" );
		Assert.IsFalse( json.Contains( "\"TextureScale\"" ), "Should not write derived TextureScale" );
		Assert.IsFalse( json.Contains( "\"TextureOffset\"" ), "Should not write derived TextureOffset" );
	}

	[TestMethod]
	public void WritesBinaryData()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( mesh );

		Assert.IsTrue( json.Contains( "\"Data\"" ) );

		foreach ( var stream in new[] { "Topology", "Positions", "Blends", "Colors", "TextureCoord", "MaterialIndex", "EdgeFlags" } )
			Assert.IsFalse( json.Contains( $"\"{stream}\"" ), $"{stream} should be packed into the binary blob, not written as a JSON array" );
	}

	[TestMethod]
	public void RoundTripPreservesUvs()
	{
		var original = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var restored = Json.Deserialize<PolygonMesh>( Json.Serialize( original ) );

		var transform = new Transform( new Vector3( 32, -16, 8 ), Rotation.FromYaw( 30 ) );
		original.Transform = transform;
		restored.Transform = transform;

		var originalFaces = original.FaceHandles.ToArray();
		var restoredFaces = restored.FaceHandles.ToArray();

		Assert.AreEqual( original.VertexHandles.Count(), restored.VertexHandles.Count() );
		Assert.AreEqual( originalFaces.Length, restoredFaces.Length );

		for ( int i = 0; i < originalFaces.Length; i++ )
		{
			original.GetFaceTextureParameters( originalFaces[i], out var u1, out var v1, out var s1 );
			restored.GetFaceTextureParameters( restoredFaces[i], out var u2, out var v2, out var s2 );

			Assert.AreEqual( u1, u2, $"Face {i} TextureUAxis changed through the blob" );
			Assert.AreEqual( v1, v2, $"Face {i} TextureVAxis changed through the blob" );
			Assert.AreEqual( s1, s2, $"Face {i} TextureScale changed through the blob" );
		}
	}

	[TestMethod]
	public void BlobContextWritesReference()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );

		var inlineJson = Json.Serialize( mesh );

		using var blobs = BlobDataSerializer.Capture();
		var referencedJson = Json.Serialize( mesh );

		Assert.IsTrue( referencedJson.Contains( "$blob" ), "An active context should reference the blob by guid" );
		Assert.IsTrue( referencedJson.Length < inlineJson.Length, "Referencing the sidecar should be smaller than embedding base64 inline" );

		var restored = Json.Deserialize<PolygonMesh>( referencedJson );

		Assert.AreEqual( mesh.VertexHandles.Count(), restored.VertexHandles.Count() );
		Assert.AreEqual( mesh.FaceHandles.Count(), restored.FaceHandles.Count() );
	}

	[TestMethod]
	public void RoundTripPreservesData()
	{
		var original = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( original );
		var restored = Json.Deserialize<PolygonMesh>( json );

		Assert.AreEqual( original.VertexHandles.Count(), restored.VertexHandles.Count() );
		Assert.AreEqual( original.FaceHandles.Count(), restored.FaceHandles.Count() );
	}

	//A face wearing a material this installation cannot load keeps it. Every ported level names legacy materials that are
	//not mounted here, and the mesh has to come back as the mapper left it rather than as whatever is still resolvable.
	[TestMethod]
	public void UnresolvableMaterialKeepsItsSlot()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var faces = mesh.FaceHandles.ToArray();

		Assert.IsTrue( faces.Length >= 3, "The fixture should have faces to paint" );

		//Neither of these exists, which is the whole point: nothing mounts them, so Material.Load comes back null for both.
		const string first = "materials/dev/reflectivity_30.vmat";
		const string second = "materials/dev/reflectivity_50.vmat";

		Assert.IsNull( Material.Load( first ), "The fixture depends on this name not resolving" );

		mesh.SetFaceMaterial( faces[0], first );
		mesh.SetFaceMaterial( faces[1], second );
		mesh.SetFaceMaterial( faces[2], first );

		var json = Json.Serialize( mesh );
		var restored = Json.Deserialize<PolygonMesh>( json );
		var restoredFaces = restored.FaceHandles.ToArray();

		Assert.AreEqual( first, restored.GetFaceMaterialName( restoredFaces[0] ) );
		Assert.AreEqual( second, restored.GetFaceMaterialName( restoredFaces[1] ) );
		Assert.AreEqual( first, restored.GetFaceMaterialName( restoredFaces[2] ),
			"The same name twice is one slot, not two" );

		Assert.AreNotEqual( restored.GetFaceMaterialIndex( restoredFaces[0] ),
			restored.GetFaceMaterialIndex( restoredFaces[1] ),
			"Two different names must not collapse onto one slot" );
	}

	//The mesh survives a save at all. Sizing the remap by the material table while indexing it by a face's id threw out of JsonWrite, and
	//a component whose Mesh property throws is written as no mesh - so saving a level of unmounted materials emptied every brush in it.
	[TestMethod]
	public void SaveSurvivesMaterialsThatDidNotLoad()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var faces = mesh.FaceHandles.ToArray();

		//One name, on one face of several: used ids and table entries disagree, which is what sent the old cleanup off the end of its array.
		mesh.SetFaceMaterial( faces[0], "materials/dev/reflectivity_30.vmat" );

		var json = Json.Serialize( mesh );

		Assert.IsTrue( json.Contains( "materials/dev/reflectivity_30.vmat" ),
			"The name should be in the file even with nothing behind it" );

		var restored = Json.Deserialize<PolygonMesh>( json );

		Assert.AreEqual( mesh.FaceHandles.Count(), restored.FaceHandles.Count() );
		Assert.AreEqual( json, Json.Serialize( restored ), "Second save should not drift" );
	}

	//The shape dom.prefab is in: a Materials list naming something that does not load, and faces indexing it by position. Reading that used
	//to leave the list empty while the faces still said 0, and the next save walked off the end of a remap array sized by the empty list.
	[TestMethod]
	public void FaceIndexingAMaterialThatDidNotLoadSurvivesResave()
	{
		const string name = "materials/dev/reflectivity_30.vmat";

		Assert.IsNull( Material.Load( name ), "The fixture depends on this name not resolving" );

		var json = LegacyMeshJson
			.Replace( "\"MaterialIndex\": [-1,-1,-1,-1,-1,-1]",
				$"\"Materials\": [\"{name}\"],\n\t\t\"MaterialIndex\": [0,0,0,-1,-1,-1]" );

		var mesh = Json.Deserialize<PolygonMesh>( json );
		var faces = mesh.FaceHandles.ToArray();

		Assert.AreEqual( name, mesh.GetFaceMaterialName( faces[0] ),
			"A slot whose material did not load is still the slot the face names" );
		Assert.AreEqual( 0, mesh.GetFaceMaterialIndex( faces[0] ) );
		Assert.AreEqual( -1, mesh.GetFaceMaterialIndex( faces[3] ) );

		//This is the call that threw IndexOutOfRangeException, taking the whole Mesh property out of the component with it.
		var written = Json.Serialize( mesh );

		Assert.IsTrue( written.Contains( name ), "The name has to still be in the file" );

		var restored = Json.Deserialize<PolygonMesh>( written );

		Assert.AreEqual( name, restored.GetFaceMaterialName( restored.FaceHandles.ToArray()[0] ) );
		Assert.AreEqual( mesh.FaceHandles.Count(), restored.FaceHandles.Count() );
	}

	[TestMethod]
	public void RoundTripIsDeterministic()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json1 = Json.Serialize( mesh );
		var json2 = Json.Serialize( mesh );

		Assert.AreEqual( json1, json2 );
	}

	[TestMethod]
	public void ResaveDoesNotDrift()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );

		var json1 = Json.Serialize( mesh );
		var mesh2 = Json.Deserialize<PolygonMesh>( json1 );
		var json2 = Json.Serialize( mesh2 );

		Assert.AreEqual( json1, json2, "Second serialize should be identical — no drift from world-space recomputation" );
	}

	[TestMethod]
	public void TextureParametersRestoredAfterTransformSet()
	{
		var original = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( original );
		var worldTransform = new Transform( new Vector3( 100, 200, 300 ), Rotation.FromYaw( 45 ) );

		//Deserialize twice from new format (no world-space fields)
		var mesh1 = Json.Deserialize<PolygonMesh>( json );
		var mesh2 = Json.Deserialize<PolygonMesh>( json );

		//This is what MeshComponent.TransformChanged does: Mesh.Transform = WorldTransform
		mesh1.Transform = worldTransform;
		mesh2.Transform = worldTransform;

		//Both should produce identical texture parameters from the same UVs + transform
		var faces1 = mesh1.FaceHandles.ToArray();
		var faces2 = mesh2.FaceHandles.ToArray();
		Assert.AreEqual( faces1.Length, faces2.Length );

		for ( int i = 0; i < faces1.Length; i++ )
		{
			mesh1.GetFaceTextureParameters( faces1[i], out var u1, out var v1, out var s1 );
			mesh2.GetFaceTextureParameters( faces2[i], out var u2, out var v2, out var s2 );

			Assert.AreEqual( u1, u2, $"Face {i} TextureUAxis mismatch" );
			Assert.AreEqual( v1, v2, $"Face {i} TextureVAxis mismatch" );
			Assert.AreEqual( s1, s2, $"Face {i} TextureScale mismatch" );
		}

		//Verify something was actually computed (not all zeros)
		mesh1.GetFaceTextureParameters( faces1[0], out var axisU, out _, out var scale );
		Assert.AreNotEqual( Vector4.Zero, axisU, "TextureUAxis should not be zero" );
		Assert.AreNotEqual( Vector2.Zero, scale, "TextureScale should not be zero" );
	}

	[TestMethod]
	public void TransformChangeDoesNotDirtyMeshJson()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json1 = Json.Serialize( mesh );

		//Simulate MeshComponent moving: Mesh.Transform = WorldTransform
		var restored = Json.Deserialize<PolygonMesh>( json1 );
		restored.Transform = new Transform( new Vector3( 500, -300, 100 ), Rotation.FromYaw( 90 ) );

		//Re-serialize — should be unchanged since world-space fields are no longer written
		var json2 = Json.Serialize( restored );
		Assert.AreEqual( json1, json2, "Mesh JSON should not change when the transform changes" );
	}

	//TextureCoord is the persisted source; derived texture params recompute from UVs and transform
	[TestMethod]
	public void TextureParametersStableAcrossDifferentTransforms()
	{
		var mesh = Json.Deserialize<PolygonMesh>( LegacyMeshJson );
		var json = Json.Serialize( mesh );

		var transform1 = new Transform( new Vector3( 100, 0, 0 ), Rotation.Identity );
		var transform2 = new Transform( new Vector3( 50000, 50000, 50000 ), Rotation.FromYaw( 180 ) );

		//Serialize under transform1, restore under transform2
		var mesh1 = Json.Deserialize<PolygonMesh>( json );
		mesh1.Transform = transform1;
		var roundTripped = Json.Deserialize<PolygonMesh>( Json.Serialize( mesh1 ) );
		roundTripped.Transform = transform2;

		//The persisted data (including TextureCoord) is transform independent
		Assert.AreEqual( Json.Serialize( mesh1 ), Json.Serialize( roundTripped ),
			"The serialized mesh must not change under a different transform" );

		//Returning to transform1 recomputes the exact same parameters from the UVs - no drift from having passed through transform2
		roundTripped.Transform = transform1;

		var faces1 = mesh1.FaceHandles.ToArray();
		var faces2 = roundTripped.FaceHandles.ToArray();
		Assert.AreEqual( faces1.Length, faces2.Length );

		for ( int i = 0; i < faces1.Length; i++ )
		{
			mesh1.GetFaceTextureParameters( faces1[i], out var u1, out var v1, out var s1 );
			roundTripped.GetFaceTextureParameters( faces2[i], out var u2, out var v2, out var s2 );

			Assert.AreEqual( u1, u2, $"Face {i} TextureUAxis drifted after round-trip" );
			Assert.AreEqual( v1, v2, $"Face {i} TextureVAxis drifted after round-trip" );
			Assert.AreEqual( s1, s2, $"Face {i} TextureScale drifted after round-trip" );
		}
	}
}
