using Silk.NET.Vulkan;
using VkImage = Silk.NET.Vulkan.Image;

namespace Rinten;

//Shadow maps: layered depth per drawer (sun pages, movers, lamp faces), read compared or as depth
internal static unsafe partial class VkTextures
{
	internal sealed class GpuShadowMap
	{
		public VkImage Image;
		public DeviceMemory Memory;
		public int Block = -1;
		public ulong Offset;
		public ulong Bytes;

		/// <summary>Every layer at once, which is what the pixels read.</summary>
		public ImageView Whole;

		/// <summary>And one apiece, which is what a bake draws into.</summary>
		public ImageView[] Layers = [];

		public uint Side;

		/// <summary>Each layer's layout. Read whole, so every layer rests readable; only the one being drawn or copied leaves it, and comes back. Moving the whole image for every bake decompressed every layer's depth for the sake of one square of one of them - most of what a lamp's face cost.</summary>
		public ImageLayout[] Layouts = [];

		/// <summary>Whether the whole image has been made readable once, before anything was drawn into it.</summary>
		public bool Ready;

		/// <summary>Where the whole of it sits in the picture table with the sampler that compares, and with the one that reads depth.</summary>
		public uint Index;
		public uint DepthIndex;
	}

	private static readonly Dictionary<ulong, GpuShadowMap> shadowMaps = new();

	//What every shadow map holds between them, for the memory report.
	internal static long ShadowMapBytes()
	{
		var bytes = 0L;
		foreach ( var pair in shadowMaps ) bytes += (long)pair.Value.Bytes;
		return bytes;
	}

	internal static GpuShadowMap ShadowMapOf( ulong handle )
		=> shadowMaps.TryGetValue( handle, out var found ) ? found : null;

	//Where a shadow map sits in the picture table, or nought - compared, or read as depth.
	internal static uint ShadowMapIndex( ulong handle, bool depth = false )
		=> shadowMaps.TryGetValue( handle, out var found ) ? depth ? found.DepthIndex : found.Index : 0u;

	private static void DestroyOne( GpuShadowMap map )
	{
		if ( map.Index != 0 ) VkDevice.RetireBindless( map.Index );
		if ( map.DepthIndex != 0 ) VkDevice.RetireBindless( map.DepthIndex );

		for ( var i = 0; i < map.Layers.Length; ++i )
		{
			if ( map.Layers[i].Handle != 0 ) VkDevice.RetireImage( default, default, map.Layers[i] );
		}

		//Back the way it came - a slice of a shared block or an allocation of its own - exactly as the depth pyramid's goes. See there.
		if ( map.Block >= 0 )
		{
			VkDevice.RetireSlicedImage( map.Image, map.Whole, map.Block, map.Offset, map.Bytes );
		}
		else if ( map.Whole.Handle != 0 || map.Image.Handle != 0 )
		{
			VkDevice.RetireImage( map.Image, map.Memory, map.Whole );
		}

		map.Index = 0;
		map.DepthIndex = 0;
		map.Layers = [];
		map.Image = default;
		map.Memory = default;
		map.Whole = default;
		map.Block = -1;
	}

	internal static ulong ShadowMapCreate( int side, int layers, bool readDepth )
	{
		lock ( VkDevice.renderLock )
		{
			if ( VkDevice.device.Handle == 0 || side < 1 || layers < 1 ||
				VkDevice.shadowSampler.Handle == 0 || ( readDepth && VkDevice.shadowDepthSampler.Handle == 0 ) ) return 0;

			var made = new GpuShadowMap { Side = (uint)side };

			var vk = VkDevice.vk;
			var device = VkDevice.device;

			//In the format the world's own depth is, so the pipelines that draw the world's pre-pass draw a bake as they are.
			var imageInfo = new ImageCreateInfo
			{
				SType = StructureType.ImageCreateInfo,
				ImageType = ImageType.Type2D,
				Format = VkDevice.DepthFormat,
				Extent = new Extent3D( made.Side, made.Side, 1 ),
				MipLevels = 1,
				ArrayLayers = (uint)layers,
				Samples = SampleCountFlags.Count1Bit,
				Tiling = ImageTiling.Optimal,
				Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
				InitialLayout = ImageLayout.Undefined,
			};

			VkImage image;

			if ( vk.CreateImage( device, &imageInfo, null, &image ) != Result.Success )
			{
				VkDevice.SetText( "a shadow map would not be made" );
				return 0;
			}

			made.Image = image;

			MemoryRequirements requirements;
			vk.GetImageMemoryRequirements( device, image, &requirements );

			if ( !TakeMemory( requirements, out var memory, out var block, out var offset ) ||
				vk.BindImageMemory( device, image, memory, offset ) != Result.Success )
			{
				vk.DestroyImage( device, image, null );
				VkDevice.SetText( "a shadow map's memory would not be had" );
				return 0;
			}

			made.Memory = memory;
			made.Block = block;
			made.Offset = offset;
			made.Bytes = requirements.Size;

			var viewInfo = new ImageViewCreateInfo
			{
				SType = StructureType.ImageViewCreateInfo,
				Image = image,
				ViewType = ImageViewType.Type2DArray,
				Format = VkDevice.DepthFormat,
				SubresourceRange = new ImageSubresourceRange( ImageAspectFlags.DepthBit, 0, 1, 0, (uint)layers ),
			};

			ImageView whole;

			if ( vk.CreateImageView( device, &viewInfo, null, &whole ) != Result.Success )
			{
				DestroyOne( made );
				VkDevice.SetText( "a shadow map's view would not be made" );
				return 0;
			}

			made.Whole = whole;
			made.Layers = new ImageView[layers];
			made.Layouts = new ImageLayout[layers];

			viewInfo.ViewType = ImageViewType.Type2D;

			for ( var layer = 0u; layer < (uint)layers; ++layer )
			{
				viewInfo.SubresourceRange = new ImageSubresourceRange( ImageAspectFlags.DepthBit, 0, 1, layer, 1 );

				ImageView one;

				if ( vk.CreateImageView( device, &viewInfo, null, &one ) != Result.Success )
				{
					DestroyOne( made );
					VkDevice.SetText( "a layer of a shadow map would not be viewed" );
					return 0;
				}

				made.Layers[layer] = one;
			}

			//Into the table with the sampler that compares, which is how much of a pixel is lit - and where
			//asked, again with the one that reads depth, which is how far in front of it what shades it stands.
			made.Index = TakeBindlessSlot( whole, VkDevice.shadowSampler );
			made.DepthIndex = readDepth && made.Index != 0 ? TakeBindlessSlot( whole, VkDevice.shadowDepthSampler ) : 0;

			//Where there is a table. The portable frame has none, and binds its one map where the shaders look for it instead - see VkDevice.portableSunMap.
			if ( VkDevice.bindless && ( made.Index == 0 || ( readDepth && made.DepthIndex == 0 ) ) )
			{
				DestroyOne( made );
				VkDevice.SetText( "no room in the picture table for a shadow map" );
				return 0;
			}

			var handle = nextHandle++;

			VkDevice.NameHandle( ObjectType.Image, made.Image.Handle, "shadow map", handle );

			shadowMaps[handle] = made;

			return handle;
		}
	}

	internal static void ShadowMapDestroy( ulong handle )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !shadowMaps.TryGetValue( handle, out var found ) ) return;

			DestroyOne( found );
			shadowMaps.Remove( handle );
		}
	}

	//Every shadow map, at the device's end - see DestroyTextures.
	private static void DestroyShadowMaps()
	{
		foreach ( var pair in shadowMaps ) DestroyOne( pair.Value );

		shadowMaps.Clear();
	}
}
