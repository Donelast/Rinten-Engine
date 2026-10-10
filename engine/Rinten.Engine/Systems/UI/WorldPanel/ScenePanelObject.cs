namespace Rinten;

//A panel's place in a scene world: transform, bounds, tags. You are probably looking for WorldPanel
internal sealed class ScenePanelObject : SceneCustomObject
{
	/// <summary>Metres per panel pixel. Eight hundred pixels to the metre: it was a twentieth of a unit where this came from and the unit was an inch, so a thousand-pixel panel stood a little over a metre wide - and stands the same here.</summary>
	public const float ScreenToWorldScale = 0.00125f;

	public ScenePanelObject( SceneWorld world ) : base( world )
	{
	}

	internal static Matrix BuildPanelToObjectMatrix()
	{
		//A panel is built x right, y down, z into the screen. Half a turn about x turns its y up and its z the
		//other way - no mirror, a rotation - so the words read the right way round to a viewer the panel faces.
		Matrix mat = Matrix.CreateRotation( Rotation.FromPitch( 180 ) );
		mat *= Matrix.CreateScale( ScreenToWorldScale );
		return mat;
	}

	internal static Matrix BuildPanelToWorldMatrix( Transform transform )
	{
		return BuildPanelToObjectMatrix() * Matrix.FromTransform( transform );
	}

	public override void RenderSceneObject()
	{
	}
}
