using System;
using System.Diagnostics;
using System.IO;
using System.Text;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Unwrapping unshared-corner meshes stays fast; guards EdgeHash in ModelBuilder.Lightmap.cs
[TestClass]
public class LightmapUnwrapTest
{
	private const int Triangles = 60_000;

	//Leaves in a grid, two triangles each, no two triangles sharing a vertex: every corner is its own v line, so the importer numbers them 3i, 3i+1,
	//3i+2 - the shape the FBX path hands over. OBJ rather than SMD because an SMD brings a skeleton, and a skinned builder is not unwrapped at all.
	private static string SoupObj()
	{
		var text = new StringBuilder( Triangles * 3 * 40 );
		var culture = System.Globalization.CultureInfo.InvariantCulture;
		var across = (int)Math.Ceiling( Math.Sqrt( Triangles / 2.0 ) );

		text.Append( "vn 0 0 1\nvt 0 0\n" );

		void Corner( float x, float y, float z )
			=> text.Append( "v " ).Append( x.ToString( culture ) ).Append( ' ' ).Append( y.ToString( culture ) )
				.Append( ' ' ).Append( z.ToString( culture ) ).Append( '\n' );

		var next = 1;

		for ( var leaf = 0; leaf < Triangles / 2; leaf++ )
		{
			//Each leaf a metre from its neighbours and tilted a little by where it stands, so islands do not all face one way.
			var x = ( leaf % across ) * 1.0f;
			var y = ( leaf / across ) * 1.0f;
			var z = ( ( leaf * 7 ) % 13 ) * 0.05f;

			Corner( x, y, z );
			Corner( x + 0.4f, y, z + 0.1f );
			Corner( x + 0.4f, y + 0.6f, z );
			Corner( x, y, z );
			Corner( x + 0.4f, y + 0.6f, z );
			Corner( x, y + 0.6f, z + 0.1f );

			text.Append( $"f {next}/1/1 {next + 1}/1/1 {next + 2}/1/1\n" );
			text.Append( $"f {next + 3}/1/1 {next + 4}/1/1 {next + 5}/1/1\n" );
			next += 6;
		}

		return text.ToString();
	}

	[TestMethod]
	public void ACornerSoupUnwrapsInLinearTime()
	{
		if ( !CoreNative.Available ) return;

		var obj = Path.Combine( Path.GetTempPath(), $"rinten-unwrap-{Guid.NewGuid():N}.obj" );

		File.WriteAllText( obj, SoupObj() );

		var builder = new CoreAssets.ModelBuilder();

		try
		{
			Assert.AreEqual( 1, CoreObjImport.Import( builder, obj, 1.0f ) );
			Assert.IsTrue( builder.VertexCount >= Triangles * 3, $"the soup should be a corner a triangle, got {builder.VertexCount} vertices" );

			for ( var i = 0; i < builder.VertexCount; i++ )
			{
				var before = builder.VertexAt( i ).TexCoord1;
				Assert.AreEqual( 0.0f, before.x );
				Assert.AreEqual( 0.0f, before.y );
			}

			var clock = Stopwatch.StartNew();

			Assert.AreEqual( 1, builder.GenerateLightmapUv( 0 ), "the soup should have been unwrapped" );

			clock.Stop();

			//The fixed pass does a hundred and fifty thousand triangles in a fifth of a second; with the xor hash sixty thousand is
			//well over a minute. Two seconds is the line between the two on any machine this runs on, not a measurement of either.
			Assert.IsTrue( clock.ElapsedMilliseconds < 2000,
				$"unwrapping {Triangles} triangles took {clock.ElapsedMilliseconds} ms - the edge pass has gone quadratic again" );

			//Every corner on the sheet, and the sheet used: with every triangle its own island, no
			//two triangles may land on the same texels, so the corners cannot all sit in one place.
			var distinct = new System.Collections.Generic.HashSet<(float, float)>();

			for ( var i = 0; i < builder.VertexCount; i++ )
			{
				var uv = builder.VertexAt( i ).TexCoord1;

				Assert.IsTrue( uv.x >= 0.0f && uv.x <= 1.0f && uv.y >= 0.0f && uv.y <= 1.0f,
					$"vertex {i} left the sheet at {uv.x}, {uv.y}" );

				if ( i % 3 == 0 ) distinct.Add( ( uv.x, uv.y ) );
			}

			Console.WriteLine( $"unwrap of {Triangles} triangles: {clock.ElapsedMilliseconds} ms, {distinct.Count} distinct first corners" );
			Assert.IsTrue( distinct.Count > Triangles / 2,
				$"only {distinct.Count} distinct first corners for {Triangles} triangles - islands are stacked" );
		}
		finally
		{
			builder.Dispose();

			if ( File.Exists( obj ) ) File.Delete( obj );
		}
	}
}
