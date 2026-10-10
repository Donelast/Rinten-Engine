using NativeEngine;
using System.Threading;

namespace Rinten;

public sealed partial class RenderTarget
{
	static Lock _lock = new();
	static List<RenderTarget> All = new();

	/// <summary>Temporary target from a pool keyed by shape; dispose to return it, unused ones freed after 8 frames</summary>
	public static RenderTarget GetTemporary( int width, int height, ImageFormat colorFormat = ImageFormat.Default, ImageFormat depthFormat = ImageFormat.Default, MultisampleAmount msaa = MultisampleAmount.MultisampleNone, int numMips = 1, string targetName = "" )
	{
		const int maxSize = 1024 * 16;

		if ( width <= 0 ) throw new ArgumentException( $"width should be higher than 0 (was {width}x{height})" );
		if ( width > maxSize ) throw new ArgumentException( $"width should be lower than ({maxSize})" );
		if ( height <= 0 ) throw new ArgumentException( $"height should be higher than 0 (was {width}x{height})" );
		if ( height > maxSize ) throw new ArgumentException( $"height should be higher than {maxSize}" );
		if ( numMips <= 0 ) throw new ArgumentException( $"numMips should be higher than 0 (was {width}x{height}x{numMips})" );
		if ( numMips > 1 && msaa != MultisampleAmount.MultisampleNone ) throw new ArgumentException( $"Texture cannot have both msaa and mips at same time" );

		numMips = Math.Min( numMips, CalculateMaxMipCount( width, height ) );

		if ( colorFormat == ImageFormat.Default )
			colorFormat = Graphics.IdealColorFormat;

		var withDepth = depthFormat is not ( ImageFormat.Default or ImageFormat.None );
		var hash = HashCode.Combine( width, height, colorFormat, withDepth ? depthFormat : ImageFormat.None, numMips );

		lock ( _lock )
		{
			foreach ( var kept in All )
			{
				if ( kept.Loaned || kept.CreationHash != hash ) continue;

				kept.Loaned = true;
				kept.FramesSinceUsed = 0;

				return kept;
			}
		}

		var name = string.IsNullOrWhiteSpace( targetName ) ? "rendertarget" : targetName;

		var color = Texture.CreateRenderTarget()
			.WithFormat( colorFormat )
			.WithSize( width, height )
			.WithMips( numMips )
			.Create( name );

		var depth = withDepth
			? Texture.CreateRenderTarget().WithFormat( depthFormat ).WithSize( width, height ).Create( name + " depth" )
			: null;

		var made = new RenderTarget
		{
			ColorTarget = color,
			DepthTarget = depth,
			Width = width,
			Height = height,
			CreationHash = hash,
			Loaned = true,
		};

		lock ( _lock )
		{
			All.Add( made );
		}

		return made;
	}

	internal static int CalculateMaxMipCount( int width, int height )
	{
		return (int)Math.Log2( Math.Max( width, height ) ) + 1; //matt: this is correct, do not fucking tocuh it
	}

	/// <summary>Get a temporary render target. You should dispose the returned handle when you're done to return the textures to the pool.</summary>
	public static RenderTarget GetTemporary( int sizeFactor, ImageFormat colorFormat = ImageFormat.Default, ImageFormat depthFormat = ImageFormat.Default, MultisampleAmount msaa = MultisampleAmount.MultisampleNone, int numMips = 1, string targetName = "" )
	{
		if ( sizeFactor <= 0 )
			throw new ArgumentException( "cannot be lower than 1", nameof( sizeFactor ) );

		var ss = Graphics.Viewport.Size;

		if ( ss.x < 8 || ss.y < 8 )
		{
			Log.Warning( $"Really small viewport {ss}, forcing to 8x8" );
			ss = 8;
		}

		return GetTemporary( (int)(ss.x / sizeFactor), (int)(ss.y / sizeFactor), colorFormat, depthFormat, msaa, numMips, targetName );
	}

	internal void Return( RenderTarget source )
	{
		source.Loaned = false;
	}

	//Called at the end of the frame. At this point none of the render targets that
	//were loaned out should be being used, so we can put them all back in the pool.
	internal static void EndOfFrame()
	{
		lock ( _lock )
		{
			for ( int i = All.Count - 1; i >= 0; i-- )
			{
				All[i].FramesSinceUsed++;
				All[i].Loaned = false;

				if ( All[i].FramesSinceUsed < 8 ) continue;

				All[i].Destroy();
				All.RemoveAt( i );
			}
		}
	}

	//Flush all the render targets out. Useful to do when screen size changes.
	internal static void Flush()
	{
		lock ( _lock )
		{
			foreach ( var entry in All )
			{
				//Defer deletion
				entry.Loaned = false;
			}
		}
	}

	//Destroy all cached render targets immediately. Called during shutdown to release native texture handles before the resource system tears down.
	internal static void Shutdown()
	{
		EndOfFrame();

		lock ( _lock )
		{
			for ( int i = All.Count - 1; i >= 0; i-- )
			{
				All[i].Destroy();
			}

			All.Clear();
		}
	}
}
