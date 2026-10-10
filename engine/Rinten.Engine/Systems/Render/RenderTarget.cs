using NativeEngine;

namespace Rinten;

/// <summary>Essentially wraps a couple of textures that we're going to render to. The color texture and the depth texture.</summary>
public sealed partial class RenderTarget : IDisposable
{
	//Is this currently loaned out (Active)
	internal bool Loaned { get; set; }

	//The amount of time since this texture was last used
	internal int FramesSinceUsed { get; set; }

	//The hash of the parameters used to create this
	internal int CreationHash { get; set; }

	/// <summary>Width of the render target</summary>
	public int Width { get; internal set; }

	/// <summary>Height of the render target</summary>
	public int Height { get; internal set; }

	/// <summary>The target colour texture</summary>
	public Texture ColorTarget { get; internal set; }

	/// <summary>The target depth texture</summary>
	public Texture DepthTarget { get; internal set; }

	//Private - Only way to get a valid render target should be with RenderTarget.From
	private RenderTarget()
	{

	}

	/// <summary>Stop using this texture, return it to the pool</summary>
	public void Dispose()
	{
		if ( !Loaned ) return;

		Return( this );
	}

	//Destroy this buffer. It shouldn't be used anymore after this.
	internal void Destroy()
	{
		//Log.Info( $"Freeing RT {this}" );

		ColorTarget?.Dispose();
		ColorTarget = null;

		DepthTarget?.Dispose();
		DepthTarget = null;
	}

	public override string ToString()
	{
		return $"RenderTarget_{Width}x{Height}";
	}

	//The name of the picture a command list draws into while this is its target - see
	//CorePasses.WantTexture - made once and kept, so a frame asking for it again makes no string.
	internal string PictureName => pictureName ??= $"render target {System.Threading.Interlocked.Increment( ref pictureNames )}";

	private string pictureName;
	private static int pictureNames;

	//A target whose colour is a picture the frame already has rather than one of its own - see
	//Graphics.GrabFrameTexture, which points it at the copy it took. Nothing until it has been pointed.
	internal static RenderTarget Borrowing()
	{
		return new RenderTarget
		{
			ColorTarget = new Texture( CoreTexture.Borrowing( 0, 1, 1 ) ),
			Width = 1,
			Height = 1,
		};
	}

	//Points a borrowing target at a picture on the device.
	internal void Point( ulong texture, int width, int height )
	{
		ColorTarget?.core?.Borrow( texture, Math.Max( width, 1 ), Math.Max( height, 1 ) );

		Width = Math.Max( width, 1 );
		Height = Math.Max( height, 1 );
	}

	/// <summary>Create a render target from these textures</summary>
	public static RenderTarget From( Texture color, Texture depth = null )
	{
		if ( color is not null && !color.IsRenderTarget )
		{
			throw new ArgumentException( "Texture was not created as a render target (Texture.CreateRenderTarget)", nameof( color ) );
		}

		if ( depth is not null && !depth.IsRenderTarget )
		{
			throw new ArgumentException( "Texture was not created as a render target (Texture.CreateRenderTarget)", nameof( depth ) );
		}

		return new RenderTarget
		{
			ColorTarget = color,
			DepthTarget = depth,
			Width = color.Width,
			Height = color.Height,
		};
	}

}
