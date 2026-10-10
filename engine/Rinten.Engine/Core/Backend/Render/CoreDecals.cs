using System;
using System.Collections.Generic;

namespace Rinten;

//Decal table read by every surface shader before lighting (see ApplyDecals); capped at MostDecals
internal static class CoreDecals
{
	//How many decals one frame may carry.
	internal const int MostDecals = 256;

	//Floats per decal: eight float4s laid out as DecalAt in frame.slang reads them
	internal const int DecalFloats = 32;

	//The decals of the last frame, packed - see Gather.
	internal static readonly float[] Block = new float[MostDecals * DecalFloats];

	//How many of them the last frame gathered.
	internal static int Count { get; private set; }

	private static readonly List<(DecalSceneObject Decal, float Away)> scratch = new();

	//Packs the world's decals that intersect the view's six planes; called before the lights upload
	internal static void Gather( SceneWorld world, Vector3 camera, ReadOnlySpan<System.Numerics.Vector4> planes )
	{
		Count = 0;

		if ( world is null ) return;

		//Not in the portable frame: kDecalsOn is nought in frame.slang, so no surface there asks a decal anything, and
		//CoreLights sends none up. Walking the world's decals and culling them would be a list nobody reads - see RenderTier.
		if ( CoreRenderDevice.PortableFrame ) return;

		var wanted = scratch;
		wanted.Clear();

		lock ( world.InternalSceneObjects )
		{
			foreach ( var decal in world.InternalDecals )
			{
				if ( !decal.RenderingEnabled ) continue;
				if ( decal.ColorTexture is null && decal.NormalTexture is null && decal.RMOTexture is null
					&& decal.EmissionTexture is null ) continue;

				var tx = decal.Transform;

				//Half the box's diagonal, which is as far as it reaches whichever way it turns - the same number the shader tests first.
				var reach = tx.Scale.Length * 0.5f;

				if ( !Seen( tx.Position, reach, planes ) ) continue;

				var away = tx.Position.Distance( camera ) - reach;

				wanted.Add( (decal, away) );
			}
		}

		if ( wanted.Count == 0 ) return;

		//By distance where the frame has more than fit, and otherwise by the order the component asked for
		//- the higher sort order is meant to land on top, and the last decal applied is the one on top.
		if ( wanted.Count > MostDecals )
		{
			wanted.Sort( static ( a, b ) => a.Away.CompareTo( b.Away ) );
			wanted.RemoveRange( MostDecals, wanted.Count - MostDecals );
		}

		wanted.Sort( static ( a, b ) => a.Decal.SortOrder.CompareTo( b.Decal.SortOrder ) );

		for ( var i = 0; i < wanted.Count; i++ )
		{
			Pack( wanted[i].Decal, Block.AsSpan( i * DecalFloats, DecalFloats ) );
		}

		Count = wanted.Count;
	}

	private static void Pack( DecalSceneObject decal, Span<float> into )
	{
		var tx = decal.Transform;

		//The axes without their scale, and the scale kept beside them as its reciprocal: the shader
		//wants a point in the box's own units, which is a dot with each axis and a multiply.
		var right = tx.Rotation.Right;
		var up = tx.Rotation.Up;
		var back = tx.Rotation.Backward;

		into[0] = right.x; into[1] = right.y; into[2] = right.z; into[3] = Over( tx.Scale.x );
		into[4] = up.x; into[5] = up.y; into[6] = up.z; into[7] = Over( tx.Scale.y );
		into[8] = back.x; into[9] = back.y; into[10] = back.z; into[11] = Over( tx.Scale.z );

		into[12] = tx.Position.x; into[13] = tx.Position.y; into[14] = tx.Position.z;
		into[15] = Math.Clamp( decal.AttenuationAngle, 0.0f, 1.0f );

		var tint = decal.Color;
		into[16] = tint.r; into[17] = tint.g; into[18] = tint.b; into[19] = tint.a;

		into[20] = Math.Clamp( decal.ColorMix, 0.0f, 1.0f );
		into[21] = MathF.Max( 0.0f, decal.EmissionEnergy );
		into[22] = decal.ParallaxStrength;
		into[23] = Math.Clamp( decal.CoverageAmount, 0.0f, 1.0f );

		var srgb = 0u;

		into[24] = Slot( decal.ColorTexture, picture: true, 0, ref srgb );
		into[25] = Slot( decal.NormalTexture, picture: false, 1, ref srgb );
		into[26] = Slot( decal.RMOTexture, picture: false, 2, ref srgb );
		into[27] = Slot( decal.HeightTexture, picture: false, 3, ref srgb );

		into[28] = Slot( decal.EmissionTexture, picture: true, 4, ref srgb );
		into[29] = BitConverter.UInt32BitsToSingle( srgb );
		into[30] = MathF.Max( 0.0f, decal.CoverageRange );

		//Half the box's diagonal: a pixel further from the centre than this is outside whichever way the box turns,
		//and is what most pixels are to most decals - the one test the shader makes before reading the rest.
		into[31] = tx.Scale.Length * 0.5f;
	}

	//Whether a sphere is inside or across the view, rather than wholly past one of its planes.
	private static bool Seen( Vector3 centre, float radius, ReadOnlySpan<System.Numerics.Vector4> planes )
	{
		foreach ( var plane in planes )
		{
			var distance = ( plane.X * centre.x ) + ( plane.Y * centre.y ) + ( plane.Z * centre.z ) + plane.W;

			if ( distance < -radius ) return false;
		}

		return true;
	}

	//One over a size - and, for a box with no size that way, a number so large that
	//any distance at all lands outside it, which is what a box with no size is.
	private static float Over( float size )
		=> MathF.Abs( size ) > 1e-6f ? 1.0f / size : 1e30f;

	//A decal map's texture table index as float bits, plus the bit if it is sampled as sRGB but is data
	private static float Slot( Texture texture, bool picture, int bit, ref uint srgb )
	{
		if ( texture is null ) return 0.0f;

		var handle = 0UL;

		if ( !string.IsNullOrEmpty( texture.ResourcePath ) )
			handle = CoreResources.Texture( texture.ResourcePath, srgb: picture );

		if ( handle == 0 )
		{
			var core = texture.core;

			handle = core?.Device() ?? 0;
			if ( handle == 0 ) return 0.0f;

			if ( !picture && core.DecodesSrgb ) srgb |= 1u << bit;
		}

		return BitConverter.UInt32BitsToSingle( CoreRender.TextureIndex( handle ) );
	}
}
