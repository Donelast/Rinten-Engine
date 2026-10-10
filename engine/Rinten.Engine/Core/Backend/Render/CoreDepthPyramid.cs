namespace Rinten;

//The depth pyramid: this viewport's depth folded down level by level, each the farthest of the four below
//it, which is what a cull may test a box against and be certain - see shaders/engine/mesh_cull.slang.
internal static class CoreDepthPyramid
{
	[ConVar( "r_occlusion_grain", ConVarFlags.Protected, Min = 64, Max = 2048,
		Help = "How many patches across the depth the cull pass tests against, a power of two." )]
	internal static int Grain
	{
		get => grain;
		set
		{
			var wanted = System.Math.Clamp( value, 64, 2048 );
			var side = 64;

			while ( side < wanted ) side <<= 1;

			grain = side;
		}
	}

	private static int grain = 1024;

	//Pyramid size in patches; the long side is the grain so texels are square
	internal static int DepthWide => wide;

	internal static int DepthTall => tall;

	private static int wide = 1024;
	private static int tall = 1024;

	//Shapes the pyramid to the depth size; called once a frame before the header is written
	internal static void Fit( int width, int height )
	{
		if ( width < 1 || height < 1 ) return;

		//Half the depth each way up to the grain, at most 8x8 depth texels per level-0 texel
		wide = System.Math.Clamp( ( width + 1 ) / 2, ( width + 7 ) / 8, grain );
		tall = System.Math.Clamp( ( height + 1 ) / 2, ( height + 7 ) / 8, grain );

		wide = System.Math.Max( wide, ( width + 7 ) / 8 );
		tall = System.Math.Max( tall, ( height + 7 ) / 8 );
	}

	//How many levels one dispatch writes - the core's PyramidLevelsAtOnce.
	internal const int LevelsAtOnce = 6;

	//The groups of the pyramid's two launches - a group a 32x32 tile of the first level each writes - packed across in the low half and down
	//in the high. What the cull's finish pass writes as the second build's arguments, so the two agree on the shape without a readback.
	internal static (uint First, uint Second) DispatchGroups()
	{
		static uint Packed( int lod )
		{
			var wide = (uint)System.Math.Max( 1, DepthWide >> lod );
			var tall = (uint)System.Math.Max( 1, DepthTall >> lod );

			return ( ( wide + 31 ) / 32 ) | ( ( ( tall + 31 ) / 32 ) << 16 );
		}

		return ( Packed( 0 ), DepthLevels > LevelsAtOnce ? Packed( LevelsAtOnce ) : 0u );
	}

	internal const float DepthBias = 0.08f;

	//Level count down to one texel; each holds the farthest of the four below it
	internal static int DepthLevels
	{
		get
		{
			var levels = 1;

			for ( int w = DepthWide, h = DepthTall; w > 1 || h > 1;
				w = System.Math.Max( 1, w >> 1 ), h = System.Math.Max( 1, h >> 1 ) )
			{
				levels++;
			}

			return levels;
		}
	}


}
