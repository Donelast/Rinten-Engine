using Rinten.Rendering;

namespace Rinten;

//Provides functionality for rendering and filtering cubemap textures. Used for environment mapping and image-based lighting in PBR rendering.
internal static class CubemapRendering
{
	static ComputeShader EnvmapFilter => field ??= new( "envmap_filtering_cs" );

	/// <summary>Specifies the quality level for GGX filtering of environment maps.</summary>
	public enum GGXFilterType
	{
		/// <summary>Faster filtering with lower sample count.</summary>
		Fast,

		/// <summary>Higher quality filtering with more samples.</summary>
		Quality
	};

	/// <summary>Renders a cubemap from a scene at the specified transform position and applies filtering.</summary>
	public static void Render( SceneWorld world, Texture cubemapTexture, Transform cubemapTransform, float znear, float zfar, GGXFilterType filterType, ITagSet excludeTags = null )
	{
		if ( Application.IsHeadless )
			throw new Exception( "Tried to call CubemapRendering.Render from a dedicated server" );

		using var camera = new SceneCamera( "CubemapRendering" );
		camera.FieldOfView = 90;
		camera.ZNear = znear;
		camera.ZFar = zfar;
		camera.Position = cubemapTransform.Position;
		camera.Rotation = cubemapTransform.Rotation;
		camera.World = world;
		camera.ExcludeFromTextureStreaming = true;

		if ( excludeTags is not null )
		{
			camera.ExcludeTags.SetFrom( excludeTags );
		}

		//GGX-filter after all 6 faces render, hooked via render stage since cameras lack command lists
		const int numFaces = 6;
		int i = 0;

		camera.OnRenderStageHook += ( stage, camera ) =>
		{
			if ( stage != Stage.AfterPostProcess )
				return;

			i++;
			//Only filter when the last face is rendered
			if ( i < numFaces )
				return;

			//Apply filtering after rendering is done
			Filter( cubemapTexture, filterType ).ExecuteOnRenderThread();
		};


		camera.RenderToCubeTexture( cubemapTexture );
	}

	//Applies filtering to a cubemap texture, generating both downsample and GGX filtering.
	internal static CommandList Filter( Texture cubemapTexture, GGXFilterType filterType )
	{
		if ( cubemapTexture.Depth != 6 ) throw new Exception( "Cubemap texture must have 6 faces" );
		if ( cubemapTexture.Width != cubemapTexture.Height ) throw new Exception( "Cubemap texture must be square" );
		if ( cubemapTexture.Width.IsPowerOfTwo() == false ) throw new Exception( "Cubemap texture must be power of two" );
		if ( cubemapTexture.Mips != 7 ) throw new Exception( $"Cubemap texture must have 7 mip levels (this has {cubemapTexture.Mips})" );

		//Merge into a single command list for callers
		var filter = new CommandList( "Cubemap.Filter" );
		filter.InsertList( FilterDownsample( cubemapTexture ) );
		filter.InsertList( FilterGGX( cubemapTexture, filterType ) );
		return filter;
	}

	//Downsamples a cubemap texture to generate its mip chain.
	internal static CommandList FilterDownsample( Texture cubemapTexture )
	{
		var cmd = new CommandList( "Cubemap.FilterDownsample" );

		//Set static attributes once
		cmd.Attributes.Set( "DownsampleInput", cubemapTexture );
		cmd.Attributes.SetCombo( "D_PASS", 0 );

		for ( int i = 1; i < cubemapTexture.Mips; i++ )
		{
			var size = cubemapTexture.Width >> i;
			cmd.Attributes.Set( "DownsampleOutput", cubemapTexture, i );
			cmd.Attributes.Set( "Size", size );
			cmd.DispatchCompute( EnvmapFilter, size, size, 6 );

			//Barrier for this mip before next iteration using CommandList helper
			cmd.ResourceBarrierTransition( cubemapTexture, ResourceState.UnorderedAccess, i );
		}

		return cmd;
	}

	//Applies GGX filtering to a cubemap texture for image-based lighting. This generates the pre-filtered environment map used in PBR workflows.
	internal static CommandList FilterGGX( Texture cubemapTexture, GGXFilterType filterType )
	{
		var cmd = new CommandList( "Cubemap.FilterGGX" );

		cmd.Attributes.Set( "Source", cubemapTexture );
		for ( int i = 1; i < cubemapTexture.Mips; i++ )
		{
			cmd.Attributes.Set( $"Destination{i}", cubemapTexture, i );
		}
		cmd.Attributes.SetCombo( "D_QUALITY", (int)filterType );
		cmd.Attributes.SetCombo( "D_PASS", 1 );
		cmd.Attributes.Set( "BaseResolution", cubemapTexture.Width );

		for ( int i = 0; i < 2; i++ )
		{
			cmd.DispatchCompute( EnvmapFilter, SumOfSquaresTwo( cubemapTexture.Width / 2 ), 6, 1 );
			cmd.ResourceBarrierTransition( cubemapTexture, ResourceState.UnorderedAccess, cubemapTexture.Mips );
		}

		return cmd;
	}

	//Calculates the sum of squares of powers of two up to n. Formula: 1² + 2² + 4² + 8² +
	//... + (2^k)² where 2^k ≤ n Used for determining compute shader dispatch dimensions.
	internal static int SumOfSquaresTwo( int n )
	{
		int sum = 0;
		for ( int i = 1; i <= n; i *= 2 )
		{
			sum += i * i;
		}
		return sum;
	}
}
