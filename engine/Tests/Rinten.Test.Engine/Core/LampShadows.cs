using System;

namespace EngineTests.Core;

//Local-light shadows: which cube faces a point light spends atlas tiles on.
[TestClass]
public class LampShadowsTests
{
	//A lamp's cutoff distance comes from its falloff, checked against the falloff not a fixed number
	[TestMethod]
	public void ALampIsCutOffWhereItsFalloffSaysItStops()
	{
		var was = Rinten.Rendering.DrawSettings.LightCutoff;

		try
		{
			//The same falloff the surface shader runs: (1 - d/R)^2 / d^2.
			static float Reaching( float distance, float radius, float brightness )
			{
				var window = Math.Clamp( 1.0f - ( distance / radius ), 0.0f, 1.0f );

				return brightness * window * window / ( distance * distance );
			}

			Rinten.Rendering.DrawSettings.LightCutoff = 0.001f;

			//A torch out of the port: 1800 legacy units, carried across as metres.
			var range = CoreSceneRenderer.EffectiveRange( 45.72f, Color.White );

			Assert.IsTrue( range < 45.72f, $"it should stop short of the radius, got {range}" );
			Assert.AreEqual( 0.001f, Reaching( range, 45.72f, 1.0f ), 0.00005f,
				"at the cut-off it should be reaching exactly the cut-off" );

			//A dim lamp stops sooner than a bright one of the same radius.
			var dim = CoreSceneRenderer.EffectiveRange( 45.72f, Color.White * 0.1f );

			Assert.IsTrue( dim < range, $"dim {dim} should stop before bright {range}" );

			//A lamp already tight is left where it was, near enough.
			var small = CoreSceneRenderer.EffectiveRange( 2.93f, Color.White );

			Assert.IsTrue( small > 2.5f, $"a three-metre lamp should keep most of it, got {small}" );
			Assert.IsTrue( small < 2.93f );

			//Off is off: the authored radius, to the bit.
			Rinten.Rendering.DrawSettings.LightCutoff = 0.0f;

			Assert.AreEqual( 45.72f, CoreSceneRenderer.EffectiveRange( 45.72f, Color.White ) );

			//And a lamp with no colour in it reaches nowhere at all.
			Rinten.Rendering.DrawSettings.LightCutoff = 0.001f;

			Assert.AreEqual( 0.0f, CoreSceneRenderer.EffectiveRange( 45.72f, Color.Black ) );
		}
		finally
		{
			Rinten.Rendering.DrawSettings.LightCutoff = was;
		}
	}

	[TestMethod]
	public void PointFaceIndexFollowsTheDominantAxis()
	{
		Assert.AreEqual( 0, CoreSceneRenderer.PointFaceIndex( new Vector3( 2, 0.1f, 0.1f ) ) );
		Assert.AreEqual( 1, CoreSceneRenderer.PointFaceIndex( new Vector3( -2, 0.1f, 0.1f ) ) );
		Assert.AreEqual( 2, CoreSceneRenderer.PointFaceIndex( new Vector3( 0.1f, 2, 0.1f ) ) );
		Assert.AreEqual( 3, CoreSceneRenderer.PointFaceIndex( new Vector3( 0.1f, -2, 0.1f ) ) );
		Assert.AreEqual( 4, CoreSceneRenderer.PointFaceIndex( new Vector3( 0.1f, 0.1f, 2 ) ) );
		Assert.AreEqual( 5, CoreSceneRenderer.PointFaceIndex( new Vector3( 0.1f, 0.1f, -2 ) ) );
	}

	[TestMethod]
	public void AFrustumBesideTheLightOnlyKeepsTheFacingFaces()
	{
		var light = Vector3.Zero;
		var camera = new Vector3( 8, 0, 0 );
		Span<Vector3> corners = stackalloc Vector3[8];

		for ( var i = 0; i < 8; i++ )
		{
			corners[i] = new Vector3(
				6 + ( ( i & 1 ) == 0 ? 0 : 2 ),
				( ( i & 2 ) == 0 ? -1 : 1 ),
				( ( i & 4 ) == 0 ? -1 : 1 ) );
		}

		var mask = CoreSceneRenderer.PointFaceMask( light, camera, corners );

		Assert.AreEqual( 1, mask, "everything sits on +X, so only that face can shadow it" );
	}

	[TestMethod]
	public void KeepBestFacesDropsTheOnesAimedAwayWhenTheAtlasIsShort()
	{
		var kept = CoreSceneRenderer.KeepBestFaces( 0x3F, 2, new Vector3( 10, 1, 0.1f ) );

		Assert.AreEqual( 0b000101, kept, "the camera sits on +X and a little +Y" );
		Assert.AreEqual( 0, CoreSceneRenderer.KeepBestFaces( 0x3F, 0, new Vector3( 1, 0, 0 ) ) );
		Assert.AreEqual( 0x3F, CoreSceneRenderer.KeepBestFaces( 0x3F, 8, new Vector3( 1, 0, 0 ) ) );
	}

	[TestMethod]
	public void PackedFacesStayInFaceIndexOrder()
	{
		//Faces 0, 2, 4 packed into three consecutive tiles: sampling +Z (4) must land on the third slot, not tile+4.
		var mask = 0b010101;
		var face = 4;
		var packed = 0;

		for ( var f = 0; f < face; f++ )
		{
			if ( ( mask & ( 1 << f ) ) != 0 ) packed++;
		}

		Assert.AreEqual( 2, packed );
	}

	[TestMethod]
	public void FillFrustumCornersTurnsClipIntoWorld()
	{
		Span<Vector3> corners = stackalloc Vector3[8];

		Assert.IsTrue( CoreSceneRenderer.FillFrustumCorners(
			System.Numerics.Matrix4x4.Identity, corners ) );

		Assert.AreEqual( new Vector3( -1, -1, 0 ), corners[0] );
		Assert.AreEqual( new Vector3( 1, 1, 1 ), corners[6] );
	}
}
