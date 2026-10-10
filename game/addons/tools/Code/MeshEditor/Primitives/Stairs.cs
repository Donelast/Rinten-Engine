using System;

namespace Editor.MeshEditor;

/// <summary>Creates a staircase fitted to the drawn bounds.</summary>
[Title( "Stairs" ), Icon( "stairs" )]
public class StairsPrimitive : PrimitiveBuilder
{
	[Title( "Number of steps" ), Range( 2, 64 ), WideMode, Description( "Controls the number of steps in the staircase." )]
	public int NumberOfSteps { get; set; } = 16;

	[Title( "Align to Camera" ), WideMode, Description( "Rotate the staircase so it ascends away from the camera." )]
	public bool AlignToCamera { get; set; } = true;

	[Hide] private Vector3 Center;
	[Hide] private Vector3 Size;
	[Hide] private float StepWidth;
	[Hide] private float StepDepth;
	[Hide] private float StepHeight;

	public override void SetFromBox( BBox box )
	{
		NumberOfSteps = Math.Max( NumberOfSteps, 2 );
		Size = box.Size;
		Center = box.Mins;
		//The steps run along z and widen along x, which are the box's two horizontal extents; height is y.
		StepHeight = Size.y / NumberOfSteps;
		StepDepth = Size.z / NumberOfSteps;
		StepWidth = Size.x;
	}

	public override void Build( PolygonMesh mesh )
	{
		//The run is +Z and the width +X, out of the box's low corner - see below.
		var run = new Vector3( 0.0f, 0.0f, 1.0f );
		var width = new Vector3( 1.0f, 0.0f, 0.0f );
		var origin = Center;
		var stepDepth = StepDepth;
		var stepWidth = StepWidth;

		if ( AlignToCamera )
		{
			var forward = CameraForward;
			var maxs = Center + Size;

			//Looking mostly along x: the flight runs along x instead, and across z.
			if ( MathF.Abs( forward.x ) > MathF.Abs( forward.z ) )
			{
				run = new Vector3( forward.x >= 0.0f ? 1.0f : -1.0f, 0.0f, 0.0f );
				width = new Vector3( 0.0f, 0.0f, 1.0f );
				origin = forward.x >= 0.0f ? Center : Center.WithX( maxs.x );
				stepDepth = Size.x / NumberOfSteps;
				stepWidth = Size.z;
			}
			else if ( forward.z < 0.0f )
			{
				run = new Vector3( 0.0f, 0.0f, -1.0f );
				origin = Center.WithZ( maxs.z );
			}
		}

		//Turning the run onto the other axis mirrors the flight, so its faces would come out inside out without turning them back.
		var flip = Vector3.Cross( run, width ).y < 0.0f;

		void AddFace( params Vector3[] face )
		{
			if ( flip )
				Array.Reverse( face );

			mesh.AddFace( face );
		}

		var vertices = new Vector3[(NumberOfSteps + 1) * 2];

		for ( var stepIndex = 0; stepIndex <= NumberOfSteps; stepIndex++ )
		{
			//Steps run along +Z and span +X from the box's start corner
			float depth = stepDepth * stepIndex;
			var innerPoint = run * depth;
			var outerPoint = width * stepWidth + run * depth;

			vertices[stepIndex * 2] = origin + innerPoint;
			vertices[stepIndex * 2 + 1] = origin + outerPoint;
		}

		for ( var stepIndex = 0; stepIndex < NumberOfSteps; stepIndex++ )
		{
			var lastHeightOffset = Center.y + StepHeight * stepIndex;
			var stepHeightOffset = Center.y + StepHeight * (stepIndex + 1);

			var innerPoint = vertices[stepIndex * 2].WithY( stepHeightOffset );
			var outerPoint = vertices[stepIndex * 2 + 1].WithY( stepHeightOffset );

			var innerPoint2 = vertices[(stepIndex + 1) * 2].WithY( stepHeightOffset );
			var outerPoint2 = vertices[(stepIndex + 1) * 2 + 1].WithY( stepHeightOffset );

			var lastInnerPoint = vertices[(stepIndex) * 2].WithY( lastHeightOffset );
			var lastOuterPoint = vertices[(stepIndex) * 2 + 1].WithY( lastHeightOffset );

			AddFace(
				innerPoint2,
				outerPoint2,
				outerPoint,
				innerPoint
			);

			AddFace(
				innerPoint,
				outerPoint,
				lastOuterPoint,
				lastInnerPoint
			);

			AddFace(
				vertices[(stepIndex + 1) * 2],
				innerPoint2,
				innerPoint,
				vertices[stepIndex * 2]
			);

			AddFace(
				outerPoint,
				outerPoint2,
				vertices[(stepIndex + 1) * 2 + 1],
				vertices[stepIndex * 2 + 1]
			);
		}

		//The back of the flight: the last step's floor edge taken up to the full height, which closes it. Taken up in z, which was
		//the height and is the run now - so both points landed on top of the two they were drawn from and the face had no area at all.
		var backInnerPoint = vertices[^2].WithY( Center.y + Size.y );
		var backOuterPoint = vertices[^1].WithY( Center.y + Size.y );

		AddFace(
			vertices[^2],
			vertices[^1],
			backOuterPoint,
			backInnerPoint
		);
	}

	//Forward direction of the scene view camera, used to orient primitives towards whoever is placing them. Down the default run where there is none.
	internal static Vector3 CameraForward
	{
		get
		{
			var viewport = SceneViewPanel.Current?.LastSelectedViewport;
			return viewport?.State is null
				? new Vector3( 0.0f, 0.0f, 1.0f )
				: viewport.State.CameraRotation.Forward;
		}
	}
}
