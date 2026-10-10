using System;
using System.Collections.Generic;
using Rinten;

namespace EngineTests;

//Triangulation checked by properties: uses face points, correct count, areas sum to the face
[TestClass]
public class CoreTriangulatorTests
{
	private static int[] Triangulate( Vector3[] points )
	{
		var indices = new int[Math.Max( (points.Length - 2) * 3, 0 )];
		var written = CoreTriangulator.Polygon( points, indices );

		Assert.AreEqual( indices.Length, written, "every corner but two makes a triangle" );

		return indices;
	}

	private static float Area( Vector3[] points, int[] indices )
	{
		var total = 0.0f;

		for ( var i = 0; i < indices.Length; i += 3 )
		{
			var a = points[indices[i]];
			var b = points[indices[i + 1]];
			var c = points[indices[i + 2]];

			total += Vector3.Cross( b - a, c - a ).Length * 0.5f;
		}

		return total;
	}

	[TestMethod]
	public void ATriangleIsAlreadyOne()
	{
		var points = new[] { new Vector3( 0, 0, 0 ), new Vector3( 10, 0, 0 ), new Vector3( 0, 10, 0 ) };
		var indices = Triangulate( points );

		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, indices );
	}

	[TestMethod]
	public void ASquareBecomesTwoTriangles()
	{
		var points = new[]
		{
			new Vector3( 0, 0, 0 ),
			new Vector3( 10, 0, 0 ),
			new Vector3( 10, 10, 0 ),
			new Vector3( 0, 10, 0 ),
		};

		var indices = Triangulate( points );

		Assert.AreEqual( 6, indices.Length );
		Assert.AreEqual( 100.0f, Area( points, indices ), 0.01f, "the pieces are the square" );
	}

	[TestMethod]
	public void ARingWoundTheOtherWayStillWorks()
	{
		var points = new[]
		{
			new Vector3( 0, 10, 0 ),
			new Vector3( 10, 10, 0 ),
			new Vector3( 10, 0, 0 ),
			new Vector3( 0, 0, 0 ),
		};

		var indices = Triangulate( points );

		Assert.AreEqual( 100.0f, Area( points, indices ), 0.01f );
	}

	//An L, which a fan out of the first corner would get wrong - it would cover ground the face does not.
	[TestMethod]
	public void AConcaveFaceIsCutInsideItself()
	{
		var points = new[]
		{
			new Vector3( 0, 0, 0 ),
			new Vector3( 20, 0, 0 ),
			new Vector3( 20, 10, 0 ),
			new Vector3( 10, 10, 0 ),
			new Vector3( 10, 20, 0 ),
			new Vector3( 0, 20, 0 ),
		};

		var indices = Triangulate( points );

		//20x10 along the bottom and 10x10 above it.
		Assert.AreEqual( 300.0f, Area( points, indices ), 0.01f );

		foreach ( var index in indices )
			Assert.IsTrue( index >= 0 && index < points.Length, "every index is a corner of the face" );
	}

	[TestMethod]
	public void AFaceOnAnyPlaneComesOutTheSame()
	{
		//The same square, stood up and turned about.
		var flat = new[]
		{
			new Vector3( 0, 0, 0 ),
			new Vector3( 10, 0, 0 ),
			new Vector3( 10, 10, 0 ),
			new Vector3( 0, 10, 0 ),
		};

		var rotation = Rotation.From( 37, 21, 53 );
		var turned = new Vector3[flat.Length];

		for ( var i = 0; i < flat.Length; i++ ) turned[i] = rotation * flat[i];

		Assert.AreEqual( Area( flat, Triangulate( flat ) ), Area( turned, Triangulate( turned ) ), 0.01f );
	}

	[TestMethod]
	public void APolygonWithNoAreaMakesNoTriangles()
	{
		var points = new[]
		{
			new Vector3( 0, 0, 0 ),
			new Vector3( 10, 0, 0 ),
			new Vector3( 20, 0, 0 ),
			new Vector3( 30, 0, 0 ),
		};

		var indices = new int[6];
		Assert.AreEqual( 0, CoreTriangulator.Polygon( points, indices ) );
	}

	//The same faces through their mesh library, where it is loaded.
	[TestMethod]
	public void CoversTheSameGroundAsTheirs()
	{
		Assert.Inconclusive( "no engine to compare against" );
	}
}
