using System;

namespace Rinten;

//Shared distance policy: pixels-across coverage and its thresholds, mirrored by the GPU cull
internal static class CoreDetail
{
	//One frame's view for detail policies; owned per scene, set once a tick by Scene.LookForDetail
	internal readonly struct Look
	{
		//False without a camera (bakes, servers); then every policy answers as if right in front
		internal bool Looking { get; init; }

		//Where the frame is looked at from: scene camera, editor camera or headset head
		internal Vector3 Eye { get; init; }

		//The camera's own frustum, for the things that only want "is it on the screen at all".
		internal Frustum Where { get; init; }

		//Pixels per metre at one metre: half view height over tan of half the vertical FOV (eye.w on GPU)
		internal float PixelsAtAMetre { get; init; }
	}

	//The view a frame was drawn for - see Scene.LookForDetail, which is the only caller.
	internal static Look Frame( Vector3 eye, in Frustum where, float pixelsAtAMetre )
		=> new()
		{
			Eye = eye,
			Where = where,
			PixelsAtAMetre = MathF.Max( pixelsAtAMetre, 0.0f ),
			Looking = pixelsAtAMetre > 0.0f,
		};

	//Nobody is looking: every policy answers as it does for something at arm's length.
	internal static Look Blind() => default;

	//How many pixels across a sphere of this size at this place is.
	internal static float Coverage( in Look look, Vector3 centre, float radius )
	{
		if ( !look.Looking ) return float.MaxValue;

		var away = Vector3.DistanceBetween( centre, look.Eye );

		//Inside it: as big as the screen, which is what "do not thin this" means to every policy below.
		if ( away <= radius ) return float.MaxValue;

		return 2.0f * radius * look.PixelsAtAMetre / away;
	}

	//The same for a box, measured from its nearest point rather than its middle: a long
	//trail or a wide burst reaches towards the camera and is judged by the part that does.
	internal static float Coverage( in Look look, in BBox bounds )
	{
		if ( !look.Looking ) return float.MaxValue;

		var size = bounds.Size;
		var radius = MathF.Max( MathF.Max( size.x, size.y ), size.z ) * 0.5f;

		var away = bounds.ClosestPoint( look.Eye ).Distance( look.Eye );

		if ( away <= 0.001f ) return float.MaxValue;

		return 2.0f * radius * look.PixelsAtAMetre / away;
	}

	//Whether the box is on the screen at all, grown by what it may reach past its bounds.
	internal static bool Seen( in Look look, in BBox bounds, float grown = 0.0f )
	{
		if ( !look.Looking ) return true;

		return look.Where.IsInside( grown > 0.0f ? bounds.Grow( grown ) : bounds, partially: true );
	}

	//Coverage band: 0 above Whole, plus one per halving; matches the GPU's ClusterSlice quantising
	internal static int Band( float coverage )
	{
		if ( coverage >= Whole ) return 0;
		if ( coverage <= 0.0f ) return Bands - 1;

		var steps = (int)MathF.Log2( Whole / MathF.Max( coverage, 0.01f ) );

		return Math.Clamp( steps + 1, 0, Bands - 1 );
	}

	//How many pixels across a thing has to be to be given everything it asks for.
	internal const float Whole = 256.0f;

	//How many bands there are - past the last, a thing is a few pixels and every policy has bottomed out.
	internal const int Bands = 10;

	//Steps once every N frames, doubling per band; off-screen runs at quarter rate rather than stopping
	internal static int StepRate( in Look look, float coverage, bool seen )
	{
		if ( !look.Looking ) return 1;
		if ( !seen ) return Unseen;

		var band = Band( coverage );

		return band <= 0 ? 1 : Math.Clamp( 1 << Math.Min( band, 5 ), 1, Slowest );
	}

	//The rate of something nobody can see, and the slowest anything is ever stepped.
	internal const int Unseen = 4;
	internal const int Slowest = 16;
}
