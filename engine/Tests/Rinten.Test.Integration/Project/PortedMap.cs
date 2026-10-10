using System;
using System.IO;
using System.Text.Json.Nodes;
using Rinten.Porting;
using Rinten.Porting.SBox;
using TestFixtures;

namespace ProjectTests;

//The brushwork a .vmap map is carved out of, arriving as an editable mesh. Here rather than a tier down because a polygon mesh reaches for the
//material system the moment it is constructed, and that needs a booted engine. The reading of the map itself is checked in Rinten.Test.Engine.
[TestClass]
public class PortedMapMeshTest
{
	private static string TempDirectory()
	{
		var path = Path.Combine( Path.GetTempPath(), $"rinten-map-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( path );
		return path;
	}

	//The ported brush mesh blob must contain real geometry, not just an empty mesh component
	[TestMethod]
	public void BringsTheBrushworkItself()
	{
		var source = TempDirectory();
		var destination = TempDirectory();

		try
		{
			var map = Path.Combine( source, "maps", "test.vmap" );
			Directory.CreateDirectory( Path.GetDirectoryName( map ) );
			File.WriteAllBytes( map, HammerMap.Build() );

			var report = new PortReport();

			Assert.IsTrue( SBoxMaps.Port( map, Path.Combine( "maps", "test.vmap" ), source, destination, report ),
				$"the map should have been read - {string.Join( "; ", report.Skipped )}" );

			var prefab = JsonNode.Parse( File.ReadAllText( Path.Combine( destination, "maps", "test.prefab" ) ) ) as JsonObject;
			var objects = ( prefab["RootObject"] as JsonObject )["Children"] as JsonArray;

			Assert.AreEqual( 2, objects.Count, "an object for the brush and one for the prop" );

			var brush = objects[0] as JsonObject;
			var at = Json.FromNode<Vector3>( brush["Position"] );

			Assert.AreEqual( -3.2512f, at.z, 0.0001f, "the brush should be where it was, in metres" );
			Assert.AreEqual( 1.6256f, at.y, 0.0001f );

			var component = ( brush["Components"] as JsonArray )[0] as JsonObject;
			Assert.AreEqual( "Rinten.MeshComponent", component["__type"].GetValue<string>() );
			Assert.AreEqual( "Mesh", component["Collision"].GetValue<string>() );
			Assert.AreEqual( 40.0f, component["SmoothingAngle"].GetValue<float>(), 0.001f );

			var mesh = Json.FromNode<PolygonMesh>( component["Mesh"] );

			Assert.IsNotNull( mesh, "the mesh should read back out of the prefab" );
			Assert.AreEqual( 4, mesh.VertexHandles.Count(), "the quad's four corners" );
			Assert.AreEqual( 1, mesh.FaceHandles.Count(), "and the one face across them" );

			//64 inches on a side, which is 1.6256 of ours. Vertices left in inches are a level
			//thirty-nine times too big, which reads as one that opens fine and plays wrong.
			var bounds = mesh.CalculateBounds();
			Assert.AreEqual( 1.6256f, bounds.Size.x, 0.0001f, "the brush should be metres across, not inches" );
			Assert.AreEqual( 1.6256f, bounds.Size.z, 0.0001f );
		}
		finally
		{
			Directory.Delete( source, true );
			Directory.Delete( destination, true );
		}
	}
}
