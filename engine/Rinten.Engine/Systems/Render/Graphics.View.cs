namespace Rinten;

/// <summary>The view being drawn, for the immediate half of this class.</summary>
public static partial class Graphics
{
	private static Transform viewCamera;
	private static float viewFieldOfView;
	private static bool viewActive;
	private static RenderAttributes viewAttributes;
	private static Frustum viewFrustum;

	//The view's own world-to-clip, where the drawer gave one - see ToScreen.
	private static System.Numerics.Matrix4x4? viewToClip;

	//Opens a view. Everything drawn until EndView belongs to it.
	internal static void BeginView( Transform camera, float fieldOfView, Rect viewport, Frustum frustum,
		System.Numerics.Matrix4x4? toClip = null )
	{
		viewCamera = camera;
		viewFieldOfView = fieldOfView;
		viewFrustum = frustum;
		viewToClip = toClip;
		viewActive = true;

		//One set of attributes for the view, emptied rather than replaced: a caller that kept hold of
		//Graphics.Attributes across a frame - which plenty do - would otherwise be writing into last frame's.
		viewAttributes ??= new RenderAttributes();
		viewAttributes.Clear();

		_state.viewport = viewport;
		_state.renderTarget = default;
	}

	//Projects a world point to view pixels (inverse of SceneCamera.GetRay); false if behind or no view
	internal static bool ToScreen( in Vector3 world, out Vector2 screen )
	{
		screen = default;

		var size = _state.viewport.Size;
		if ( !viewActive || size.x <= 0.0f || size.y <= 0.0f || viewFieldOfView <= 0.0f ) return false;

		//The view's own projection where the drawer handed it over - a headset's eye, whose field is off centre
		//and which no field of view describes. The drawer's projections turn Y over, so clip Y already runs down.
		if ( viewToClip is { } toClip )
		{
			var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( world.x, world.y, world.z, 1.0f ), toClip );
			if ( clip.W <= 0.0001f ) return false;

			screen = new Vector2( ( clip.X / clip.W + 1.0f ) * 0.5f * size.x, ( clip.Y / clip.W + 1.0f ) * 0.5f * size.y );
			return true;
		}

		var local = viewCamera.PointToLocal( world );

		//The camera looks down -Z: at or behind it there is nowhere to land.
		if ( local.z >= -0.0001f ) return false;

		var depth = -local.z;
		var halfWidth = MathF.Tan( viewFieldOfView * MathF.PI / 360.0f );
		var halfHeight = halfWidth * size.y / size.x;

		var ndcX = local.x / depth / halfWidth;
		var ndcY = local.y / depth / halfHeight;

		screen = new Vector2( ( ndcX + 1.0f ) * 0.5f * size.x, ( 1.0f - ndcY ) * 0.5f * size.y );

		return true;
	}

	//Closes it again.
	internal static void EndView()
	{
		viewActive = false;
		viewCamera = default;
		viewFieldOfView = 0.0f;
		viewFrustum = default;
		viewToClip = null;

		viewAttributes?.Clear();

		_state.viewport = default;
	}
}
