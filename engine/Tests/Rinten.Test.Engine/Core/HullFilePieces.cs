using System.IO;
using System.Linq;

namespace EngineTests;

//A hull file with several pieces yields one hull each; not parallel, native handles aren't locked
[TestClass]
[DoNotParallelize]
public class HullFilePiecesTest
{
	//A box as an OBJ object. Vertex numbers run across the whole file, so first is where this one starts.
	private static string Box( string name, float x, int first = 1 )
	{
		var text = new System.Text.StringBuilder();
		text.AppendLine( $"o {name}" );

		for ( var i = 0; i < 8; i++ )
		{
			text.AppendLine( string.Format( System.Globalization.CultureInfo.InvariantCulture,
				"v {0} {1} {2}",
				x + ( ( i & 1 ) == 0 ? -0.5f : 0.5f ),
				( i & 2 ) == 0 ? -0.5f : 0.5f,
				( i & 4 ) == 0 ? -0.5f : 0.5f ) );
		}

		//Every face of the box, wound however - a hull cares about corners only.
		int[][] faces =
		[
			[1, 2, 4, 3], [5, 6, 8, 7], [1, 2, 6, 5],
			[3, 4, 8, 7], [1, 3, 7, 5], [2, 4, 8, 6],
		];

		foreach ( var face in faces )
		{
			text.AppendLine( $"f {face[0] + first - 1} {face[1] + first - 1} " +
				$"{face[2] + first - 1} {face[3] + first - 1}" );
		}

		return text.ToString();
	}

	//How far a piece reaches along its widest axis.
	private static float Span( Vector3[] points )
	{
		var bounds = BBox.FromPoints( points );
		var size = bounds.Size;

		return System.MathF.Max( size.x, System.MathF.Max( size.y, size.z ) );
	}

	private static string WriteObj( string body )
	{
		var path = Path.Combine( Path.GetTempPath(), $"hullpieces-{Path.GetRandomFileName()}.obj" );
		File.WriteAllText( path, body );
		return path;
	}

	[TestMethod]
	public void EachMeshInTheFileIsItsOwnHull()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		//Two boxes, eight apart: one convex shape around both would be a slab filling the gap between them, which is exactly what this stops.
		var path = WriteObj( Box( "left", -4.0f ) + Box( "right", 4.0f, first: 9 ) );

		try
		{
			var pieces = CoreMeshImport.Pieces( path );

			Assert.AreEqual( 2, pieces.Count, "the file has two meshes in it" );

			foreach ( var piece in pieces )
			{
				Assert.AreEqual( 8, piece.Points.Length,
					$"{piece.Name} is a box, and a box turns on eight corners" );

				//The gap is never crossed: a piece is its own box, not both of them.
				Assert.AreEqual( 1.0f, Span( piece.Points ), 0.01f,
					$"{piece.Name} reaches across more than the box it was drawn as" );
			}
		}
		finally
		{
			File.Delete( path );
		}
	}

	[TestMethod]
	public void ACornerSharedBetweenFacesIsCountedOnce()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var path = WriteObj( Box( "box", 0.0f ) );

		try
		{
			var pieces = CoreMeshImport.Pieces( path );

			Assert.AreEqual( 1, pieces.Count );

			//The importer splits a corner per face that meets there. Left as they arrive, a box would look
			//like a twenty-four point shape and be cut down as though it were too much for the solver.
			Assert.AreEqual( 8, pieces[0].Points.Length,
				"a box is eight corners however many vertices the file spends on it" );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[TestMethod]
	public void ScaleIsAppliedOnTheWayIn()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var path = WriteObj( Box( "box", 0.0f ) );

		try
		{
			var pieces = CoreMeshImport.Pieces( path, 2.0f );

			Assert.AreEqual( 1, pieces.Count );

			foreach ( var point in pieces[0].Points )
				Assert.AreEqual( 1.0f, System.MathF.Abs( point.x ), 0.001f, "half of one, doubled" );
		}
		finally
		{
			File.Delete( path );
		}
	}
}
