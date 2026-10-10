namespace Rinten;

//Both of a headset's eyes in one pass - see SecondEye.
internal sealed partial class CoreSceneDrawer
{
	//Second eye when both eyes draw in one pass into a two-layer view; culling and lights are shared
	internal XrEyeView? SecondEye { get; set; }

	//How the eyes this drawer draws are foveated this frame - see CoreFovea. Off for everything that is not a headset's eye.
	internal CoreFovea.Setting Fovea { get; set; }

	//Whether this drawer draws both eyes at once.
	private bool BothEyes => Eye is not null && SecondEye is not null;

	//The field the frame is drawn in: the one eye's, both at once, or none - the camera's.
	private XrEyeView? Field => Eye is { } first && SecondEye is { } second ? XrEyeView.Around( first, second ) : Eye;

	//Each eye's view-projection the frame before, for what reads where a pixel was.
	private readonly System.Numerics.Matrix4x4[] previousEyes = new System.Numerics.Matrix4x4[2];
	private bool hasPreviousEyes;

	//The second eye's place and field, as the block is given them.
	private System.Numerics.Matrix4x4 secondViewProjection;
	private System.Numerics.Matrix4x4 secondRay;
	private System.Numerics.Matrix4x4 secondPrevious;
	private Vector3 secondPosition;

	//The second eye's block: this frame's, with the second eye's place and field in it.
	private float[] secondBlock;

	//The first eye's place and field into the block, over the field both are culled by, and the second's kept for SetupSecondEye.
	private void WriteEyes( float[] frame, float near, float far )
	{
		for ( var i = 0; i < 2; i++ )
		{
			var eye = i == 0 ? Eye.Value : SecondEye.Value;

			var forward = eye.Rotation.Forward;
			var up = eye.Rotation.Up;

			var view = System.Numerics.Matrix4x4.CreateLookAt(
				new System.Numerics.Vector3( eye.Position.x, eye.Position.y, eye.Position.z ),
				new System.Numerics.Vector3( eye.Position.x + forward.x, eye.Position.y + forward.y, eye.Position.z + forward.z ),
				new System.Numerics.Vector3( up.x, up.y, up.z ) );

			var projection = eye.Projection( near, far );
			var viewProjection = view * projection;

			//Directions only, for the sky - see CoreSceneRenderer.RayToWorldAt.
			var still = System.Numerics.Matrix4x4.CreateLookAt(
				System.Numerics.Vector3.Zero,
				new System.Numerics.Vector3( forward.x, forward.y, forward.z ),
				new System.Numerics.Vector3( up.x, up.y, up.z ) );

			System.Numerics.Matrix4x4.Invert( still * projection, out var ray );

			var previous = hasPreviousEyes ? previousEyes[i] : viewProjection;
			previousEyes[i] = viewProjection;

			if ( i == 0 )
			{
				CoreSceneRenderer.SetEye( frame, viewProjection, eye.Position );
				CoreSceneRenderer.SetRayToWorld( frame, ray );
				CoreSceneRenderer.SetPrevious( frame, previous );
				continue;
			}

			secondViewProjection = viewProjection;
			secondRay = ray;
			secondPrevious = previous;
			secondPosition = eye.Position;
		}

		hasPreviousEyes = true;
	}

	//The second eye's block up beside the first: the frame as it goes up, with the second eye's place and field in it.
	private void SetupSecondEye( float[] frame )
	{
		secondBlock ??= new float[CoreSceneRenderer.FrameFloats];

		System.Array.Copy( frame, secondBlock, System.Math.Min( frame.Length, secondBlock.Length ) );

		CoreSceneRenderer.SetEye( secondBlock, secondViewProjection, secondPosition );
		CoreSceneRenderer.SetRayToWorld( secondBlock, secondRay );
		CoreSceneRenderer.SetPrevious( secondBlock, secondPrevious );

		CoreRenderDevice.SetupSecondEye( secondBlock );
	}
}
