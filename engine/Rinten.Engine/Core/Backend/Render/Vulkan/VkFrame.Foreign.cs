using Silk.NET.Vulkan;

namespace Rinten;

//Lends the frame's command buffer to an outside library (NGX for DLSS); bound state is forgotten after
internal static unsafe partial class VkFrame
{
	//An image as a library outside the engine takes one.
	internal struct ForeignImage
	{
		public ulong View;
		public ulong Image;
		public Format Format;
		public uint Width;
		public uint Height;
		public bool Depth;
	}

	//One of the frame's own pictures - the world's colour, its depth, the other picture a chain of effects alternates
	//with - by the texture a pass reads it through. The single level a pass draws, not the chain under it.
	internal static bool ForeignTexture( ulong texture, out ForeignImage image )
	{
		image = default;

		lock ( VkDevice.renderLock )
		{
			if ( texture == 0 ) return false;

			var p = Pics;

			if ( texture == p.SceneHandle && p.SceneImage.Handle != 0 )
			{
				image = new ForeignImage { View = p.SceneTop.Handle, Image = p.SceneImage.Handle, Format = VkDevice.WorldFormat,
					Width = p.Extent.Width, Height = p.Extent.Height };
				return true;
			}

			//The other picture a chain of effects alternates with, where the chain left the frame in it - made in the world's format, at the world's size.
			if ( texture == p.PostHandle && p.PostImage.Handle != 0 && p.PostTop.Handle != 0 )
			{
				image = new ForeignImage { View = p.PostTop.Handle, Image = p.PostImage.Handle, Format = VkDevice.WorldFormat,
					Width = p.Extent.Width, Height = p.Extent.Height };
				return true;
			}

			if ( texture == p.DepthHandle && p.DepthImage.Handle != 0 )
			{
				image = new ForeignImage { View = p.DepthView.Handle, Image = p.DepthImage.Handle, Format = VkDevice.DepthFormat,
					Width = p.Extent.Width, Height = p.Extent.Height, Depth = true };
				return true;
			}

			return false;
		}
	}

	//A picture of the frame's passes - see VkPictures - its top level.
	internal static bool ForeignPicture( ulong picture, out ForeignImage image )
	{
		image = default;

		lock ( VkDevice.renderLock )
		{
			if ( VkPictures.Get( picture ) is not { } found ) return false;

			image = new ForeignImage { View = found.Level[0].Handle, Image = found.Image.Handle, Format = found.Format,
				Width = found.Extent.Width, Height = found.Extent.Height };

			return true;
		}
	}

	//The command buffer, with no pass open and written - a picture's top level - in GENERAL for
	//the library to store into. Nought outside a frame. Every call is followed by ForeignEnd.
	internal static nint ForeignBegin( ulong written )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !Current.Recording ) return 0;

			var frame = CurrentFrame;

			EndPictureIfOpen( frame );
			CloseOpenPasses( frame );

			if ( VkPictures.Get( written ) is { } picture )
			{
				ReadyLevels( frame.Commands, picture );
				LevelTo( frame.Commands, picture, 0, ImageLayout.General );
			}

			return frame.Commands.Handle;
		}
	}

	//The command buffer back: what the library wrote readable, and nothing the frame thought was bound still thought so.
	internal static void ForeignEnd( ulong written )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !Current.Recording ) return;

			var frame = CurrentFrame;

			if ( VkPictures.Get( written ) is { } picture )
				LevelTo( frame.Commands, picture, 0, ImageLayout.ShaderReadOnlyOptimal );

			frame.LastPipeline = default;
			frame.LastTexture = 0;
			frame.LastBoneOffset = 0;
			frame.LastMorphOffset = 0;
			frame.FrameSetBound = false;
			frame.LastViewport = default;
			frame.ViewportSet = false;
			frame.LastVertexBuffer = default;
			frame.LastSkinBuffer = default;
			frame.LastIndexBuffer = default;
		}
	}
}
