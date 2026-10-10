using System;

namespace Rinten;

//Camera projection math (reverse-Z, near = 1, far = 0) done here instead of in native code
internal struct CoreCameraFrustum
{
	private Vector3 position;
	private Rotation rotation;

	private float near;
	private float far;
	private float fieldOfView;
	private float aspect;

	private float orthoWidth;
	private float orthoHeight;
	private bool ortho;

	private Vector2 size;

	private Matrix view;
	private Matrix projection;
	private bool built;

	/// <summary>A perspective camera, as the scene camera describes itself.</summary>
	public void InitCamera( Vector3 origin, Angles angles, float zNear, float zFar, float fov, float viewAspect )
	{
		position = origin;
		rotation = angles.ToRotation();

		near = MathF.Max( zNear, 0.01f );
		far = MathF.Max( zFar, near + 0.01f );
		fieldOfView = fov.Clamp( 1.0f, 179.0f );
		aspect = viewAspect <= 0.0f ? 1.0f : viewAspect;

		ortho = false;
		built = false;
	}

	/// <summary>The same, without perspective.</summary>
	public void InitOrthoCamera( Vector3 origin, Angles angles, float zNear, float zFar, float width, float height )
	{
		position = origin;
		rotation = angles.ToRotation();

		near = zNear;
		far = MathF.Max( zFar, zNear + 0.01f );

		orthoWidth = MathF.Max( width, 0.01f );
		orthoHeight = MathF.Max( height, 0.01f );

		ortho = true;
		built = false;
	}

	/// <summary>How many pixels across the picture is, for the size below.</summary>
	public void SetCameraWidthHeight( float width, float height )
	{
		size = new Vector2( width, height );
	}

	private void Build()
	{
		if ( built ) return;

		built = true;

		//The camera's own axes, as the rows of the basis that takes the world into its
		//view. Nothing here names an axis, so nothing here had to move when the world's did.
		var forward = rotation.Forward;
		var left = rotation.Left;
		var up = rotation.Up;

		view = (Matrix)new System.Numerics.Matrix4x4(
			-left.x, up.x, forward.x, 0.0f,
			-left.y, up.y, forward.y, 0.0f,
			-left.z, up.z, forward.z, 0.0f,
			Vector3.Dot( left, position ), -Vector3.Dot( up, position ), -Vector3.Dot( forward, position ), 1.0f );

		if ( ortho )
		{
			var halfWidth = orthoWidth * 0.5f;
			var halfHeight = orthoHeight * 0.5f;

			projection = (Matrix)new System.Numerics.Matrix4x4(
				1.0f / halfWidth, 0, 0, 0,
				0, 1.0f / halfHeight, 0, 0,
				0, 0, -1.0f / (far - near), 0,
				0, 0, far / (far - near), 1.0f );

			return;
		}

		//Reverse-Z; the field of view is horizontal, so it scales x and y follows by aspect
		var scaleX = 1.0f / MathF.Tan( fieldOfView.DegreeToRadian() * 0.5f );
		var scaleY = scaleX * aspect;

		//In front of the camera is positive z: w is that distance, depth runs 1 at near to 0 at far
		projection = (Matrix)new System.Numerics.Matrix4x4(
			scaleX, 0, 0, 0,
			0, scaleY, 0, 0,
			0, 0, -near / (far - near), 1.0f,
			0, 0, (far * near) / (far - near), 0 );
	}

	/// <summary>The projection on its own.</summary>
	public Matrix GetProj()
	{
		Build();

		return projection;
	}

	/// <summary>The view, for whoever wants the two apart.</summary>
	public Matrix GetView()
	{
		Build();

		return view;
	}

	/// <summary>Where a point of the world lands on the screen, from minus one at one edge to one at the other.</summary>
	public void WorldToView( Vector3 world, out Vector2 view )
	{
		Build();

		var clip = System.Numerics.Vector4.Transform(
			new System.Numerics.Vector4( world.x, world.y, world.z, 1.0f ),
			System.Numerics.Matrix4x4.Multiply( this.view, projection ) );

		//Behind the camera there is no answer, and pretending there is one puts things on the screen that are not in front of it.
		var w = MathF.Abs( clip.W ) < 1e-6f ? 1e-6f : clip.W;

		view = new Vector2( clip.X / w, clip.Y / w );

		if ( clip.W < 0.0f ) view = -view;
	}

	/// <summary>How much of the screen a ball of this size at this distance covers, from zero to one.</summary>
	public float ComputeScreenSize( Vector3 origin, float radius )
	{
		Build();

		if ( ortho )
		{
			return orthoHeight <= 0.0f ? 0.0f : (radius * 2.0f) / orthoHeight;
		}

		var distance = Vector3.DistanceBetween( origin, position );

		if ( distance <= 0.001f ) return 1.0f;

		var half = MathF.Tan( fieldOfView.DegreeToRadian() * 0.5f ) * distance;

		return half <= 0.0f ? 0.0f : radius / half;
	}
}
