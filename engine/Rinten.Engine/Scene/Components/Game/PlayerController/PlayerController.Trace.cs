namespace Rinten;

public sealed partial class PlayerController : Component
{
	/// <summary>AABB of the body: half the radius wide on X/Z, from the feet up Y to CurrentHeight</summary>
	public BBox BodyBox( float scale = 1.0f, float heightScale = 1.0f ) => new BBox( new Vector3( -BodyRadius * 0.5f * scale, 0, -BodyRadius * 0.5f * scale ), new Vector3( BodyRadius * 0.5f * scale, CurrentHeight * heightScale, BodyRadius * 0.5f * scale ) );

	/// <summary>Trace the aabb body from one position to another and return the result</summary>
	public SceneTraceResult TraceBody( Vector3 from, Vector3 to, float scale = 1.0f, float heightScale = 1.0f )
	{
		return Scene.Trace.Box( BodyBox( scale, heightScale ), from, to )
								.IgnoreGameObjectHierarchy( GameObject )
								.WithCollisionRules( Tags )
								.Run();
	}
}
