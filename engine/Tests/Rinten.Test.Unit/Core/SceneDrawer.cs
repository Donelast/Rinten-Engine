using System;
using Num = System.Numerics;

namespace EngineTests.Core;

//Camera projection for the scene drawer: perspective foreshortens, ortho does not.
[TestClass]
public class SceneDrawerTests
{
	[TestMethod]
	public void OrthographicProjectionKeepsDepthFromChangingSize()
	{
		var projection = CoreSceneDrawer.ProjectionMatrix( true, 90, 10, 1, 0.1f, 100 );

		var near = Num.Vector4.Transform( new Num.Vector4( 2, 0, -5, 1 ), projection );
		var far = Num.Vector4.Transform( new Num.Vector4( 2, 0, -40, 1 ), projection );

		Assert.AreEqual( near.X / near.W, far.X / far.W, 1e-4f,
			"an orthographic camera should not shrink things that are farther away" );
	}

	[TestMethod]
	public void PerspectiveProjectionShrinksDistantPoints()
	{
		var projection = CoreSceneDrawer.ProjectionMatrix( false, 90, 10, 1, 0.1f, 100 );

		var near = Num.Vector4.Transform( new Num.Vector4( 2, 0, -5, 1 ), projection );
		var far = Num.Vector4.Transform( new Num.Vector4( 2, 0, -40, 1 ), projection );

		Assert.IsTrue( MathF.Abs( far.X / far.W ) < MathF.Abs( near.X / near.W ),
			"a perspective camera should shrink things that are farther away" );
	}

	[TestMethod]
	public void OrthographicHeightFillsTheView()
	{
		var height = 20.0f;
		var projection = CoreSceneDrawer.ProjectionMatrix( true, 90, height, 1, 0.1f, 100 );

		var top = Num.Vector4.Transform( new Num.Vector4( 0, height * 0.5f, -10, 1 ), projection );
		var ndcY = top.Y / top.W;

		Assert.AreEqual( 1.0f, MathF.Abs( ndcY ), 0.05f,
			"the ortho height should reach the top of the clip volume" );
	}
}
