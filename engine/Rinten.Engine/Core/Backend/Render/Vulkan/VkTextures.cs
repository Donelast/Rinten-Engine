using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;

namespace Rinten;

//Device textures from .tex_c assets or raw RGBA8, uploaded via the ring in VkTextures.Uploads.cs
internal static unsafe partial class VkTextures
{
	private sealed class GpuTexture
	{
		public VkImage Image;
		public DeviceMemory Memory;
		public ImageView View;


		/// <summary>Every texture carries a material set of its own, pointing all four slots at itself with default numbers - so a caller who only has a picture can still draw with it.</summary>
		public DescriptorSet Set;

		/// <summary>A material's own parameter buffer, when it is a material rather than a plain texture. Plain textures share the device's default.</summary>
		public VkBuffer Params;
		public DeviceMemory ParamsMemory;

		/// <summary>Views and sampler the set was written with, so it can be rewritten when the sampler changes</summary>
		public readonly ImageView[] SetViews = new ImageView[VkDevice.MaterialTextures];
		public Sampler SetSampler;

		/// <summary>A material owns no images; it only names other textures' views.</summary>
		public bool OwnsImage = true;

		/// <summary>A material's: the pictures it was made of, colour, normal, third and emission - see CoreTextureStreaming.</summary>
		public ulong[] Parts;

		/// <summary>Where this picture sits in the indexed table, or zero for one that is not in it - a material, an adopted view, or anything at all on a device without the table.</summary>
		public uint Index;

		/// <summary>What its memory cost, for the profiler. Zero for adopted views and materials, whose images are somebody else's.</summary>
		public ulong Bytes;

		/// <summary>The shape of the picture, for writing new pixels into it in place. Zero for materials and adopted views, which have no picture of their own to write into.</summary>
		public uint Width;
		public uint Height;
		public uint MipCount = 1;

		/// <summary>What its pixels are, so that a picture kept for reuse is only ever handed to somebody who wanted exactly this.</summary>
		public Format Format = Format.Undefined;

		/// <summary>Which slab of memory this picture sits in and where, or -1 for one that owns its allocation outright.</summary>
		public int Block = -1;
		public ulong Offset;
	}

	private static readonly Dictionary<ulong, GpuTexture> textures = new();
	private static ulong nextHandle = 1;

	//-- the slabs small pictures are cut out of ----------------------------

	//Memory slab shared by many optimal-tiling images, as per-image vkAllocateMemory is slow and capped
	private sealed class MemoryBlock
	{
		public DeviceMemory Memory;
		public uint Type;
		public ulong Size;

		/// <summary>What is not in use, by offset, kept sorted and coalesced.</summary>
		public readonly List<(ulong Offset, ulong Size)> Free = new();

		/// <summary>How many pictures are living in it.</summary>
		public int Live;
	}

	private static readonly List<MemoryBlock> blocks = new();

	//How big a slab is, and the largest picture that goes in one. Anything bigger is worth an allocation of
	//its own - a few of those cost nothing, and a sixteen-megabyte texture in a slab wastes what is left of it.
	private const ulong BlockBytes = 32ul * 1024 * 1024;
	private const ulong SuballocateUpTo = 2ul * 1024 * 1024;

	//Puts a range back, joined to whatever it touches.
	private static void GiveBack( MemoryBlock block, ulong offset, ulong size )
	{
		var at = 0;

		while ( at < block.Free.Count && block.Free[at].Offset < offset ) ++at;

		block.Free.Insert( at, ( offset, size ) );

		//Forwards first, so the merge backwards below sees the whole of what is now one range.
		if ( at + 1 < block.Free.Count &&
			block.Free[at].Offset + block.Free[at].Size == block.Free[at + 1].Offset )
		{
			block.Free[at] = ( block.Free[at].Offset, block.Free[at].Size + block.Free[at + 1].Size );
			block.Free.RemoveAt( at + 1 );
		}

		if ( at > 0 &&
			block.Free[at - 1].Offset + block.Free[at - 1].Size == block.Free[at].Offset )
		{
			block.Free[at - 1] = ( block.Free[at - 1].Offset, block.Free[at - 1].Size + block.Free[at].Size );
			block.Free.RemoveAt( at );
		}
	}

	//A picture's memory, out of a slab where it will fit. True with the block, its offset and the memory to bind to. A picture too big for a
	//slab, or one no slab could be had for, comes back with block -1 and an allocation of its own - which is what every picture used to have.
	private static bool TakeMemory( in MemoryRequirements requirements,
		out DeviceMemory memory, out int block, out ulong offset )
	{
		block = -1;
		offset = 0;
		memory = default;

		var type = VkDevice.FindMemoryType( requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit );

		if ( type == uint.MaxValue ) return false;

		if ( requirements.Size <= SuballocateUpTo )
		{
			var alignment = requirements.Alignment > 0 ? requirements.Alignment : 1;

			for ( var i = 0; i < blocks.Count; ++i )
			{
				var candidate = blocks[i];

				if ( candidate.Memory.Handle == 0 ) continue;
				if ( candidate.Type != type ) continue;
				if ( ( requirements.MemoryTypeBits & ( 1u << (int)candidate.Type ) ) == 0 ) continue;

				for ( var r = 0; r < candidate.Free.Count; ++r )
				{
					var start = candidate.Free[r].Offset;
					var end = start + candidate.Free[r].Size;
					var aligned = ( start + alignment - 1 ) / alignment * alignment;

					if ( aligned + requirements.Size > end ) continue;

					//What is left on either side of the piece taken out.
					var before = aligned - start;
					var after = end - ( aligned + requirements.Size );

					candidate.Free.RemoveAt( r );

					if ( after > 0 ) candidate.Free.Insert( r, ( aligned + requirements.Size, after ) );
					if ( before > 0 ) candidate.Free.Insert( r, ( start, before ) );

					candidate.Live++;

					memory = candidate.Memory;
					block = i;
					offset = aligned;

					return true;
				}
			}

			//None of them had room: another slab.
			var made = new MemoryBlock { Type = type, Size = BlockBytes };

			var slab = new MemoryAllocateInfo
			{
				SType = StructureType.MemoryAllocateInfo,
				AllocationSize = made.Size,
				MemoryTypeIndex = type,
			};

			DeviceMemory slabMemory;

			if ( VkDevice.vk.AllocateMemory( VkDevice.device, &slab, null, &slabMemory ) == Result.Success )
			{
				made.Memory = slabMemory;
				made.Free.Add( ( requirements.Size, made.Size - requirements.Size ) );
				made.Live = 1;

				memory = slabMemory;
				block = blocks.Count;
				offset = 0;

				blocks.Add( made );

				return true;
			}

			//No slab to be had - ask for just this one, which may still succeed where thirty-two megabytes did not.
		}

		var allocate = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = type,
		};

		DeviceMemory alone;
		var had = VkDevice.vk.AllocateMemory( VkDevice.device, &allocate, null, &alone ) == Result.Success;

		memory = alone;
		return had;
	}

	//Gives a picture's memory back, whether it was a slice or the whole thing.
	private static void DropMemory( int block, ulong offset, ulong size, DeviceMemory memory )
	{
		if ( block < 0 )
		{
			if ( memory.Handle != 0 ) VkDevice.vk.FreeMemory( VkDevice.device, memory, null );
			return;
		}

		if ( block >= blocks.Count ) return;

		var slab = blocks[block];

		if ( slab.Memory.Handle == 0 ) return;

		GiveBack( slab, offset, size );

		slab.Live--;

		//An empty slab is kept where it is the only one of its kind - an interface that has just thrown everything away is usually about to
		//make it all again - and given back otherwise. The entry itself stays in the list either way: a picture remembers its slab by index.
		if ( slab.Live > 0 ) return;

		var another = false;

		for ( var i = 0; i < blocks.Count; ++i )
		{
			if ( !ReferenceEquals( blocks[i], slab ) && blocks[i].Type == slab.Type && blocks[i].Memory.Handle != 0 )
			{
				another = true;
				break;
			}
		}

		if ( !another ) return;

		VkDevice.vk.FreeMemory( VkDevice.device, slab.Memory, null );

		slab.Memory = default;
		slab.Free.Clear();
		slab.Size = 0;
	}

	//Hands one slice of a texture slab back. Called by the graveyard once the image that had it is provably finished with.
	internal static void GiveBackTextureSlice( int block, ulong offset, ulong size )
		=> DropMemory( block, offset, size, default );

	//-- pictures kept for the next owner -----------------------------------

	//Freed plain pictures kept by shape so the next texture of that shape reuses them without allocating
	private static readonly Dictionary<ulong, List<GpuTexture>> spares = new();

	private static int spareCount;
	private static long spareBytes;

	//How many pictures may wait, and how much memory they may hold between them. A cap rather than a policy:
	//what is held is what an interface was lately using, and past this it is cheaper to allocate than to hoard.
	private const int SpareCeiling = 2048;
	private const long SpareBytesCeiling = 96L * 1024 * 1024;

	//One number for a shape, so pictures of it can be found together.
	private static ulong ShapeKey( uint width, uint height, Format format )
		=> ( (ulong)width << 40 ) | ( (ulong)height << 16 ) | ( (ulong)format & 0xFFFFul );

	//Whether this picture is plain enough to be somebody else's later.
	private static bool Plain( GpuTexture texture )
		=> texture.OwnsImage
			&& texture.Image.Handle != 0
			&& texture.View.Handle != 0
			&& texture.Set.Handle != 0
			&& texture.MipCount == 1
			&& texture.Format != Format.Undefined
			&& texture.Params.Handle == 0
			&& texture.ParamsMemory.Handle == 0
			&& texture.SetSampler.Handle == 0;

	//A picture of this shape that somebody has finished with, if there is one.
	private static GpuTexture TakeSpare( uint width, uint height, Format format )
	{
		if ( !spares.TryGetValue( ShapeKey( width, height, format ), out var kept ) || kept.Count == 0 )
			return null;

		var taken = kept[^1];
		kept.RemoveAt( kept.Count - 1 );

		spareCount--;
		spareBytes -= (long)taken.Bytes;

		return taken;
	}

	//-- the indexed table --------------------------------------------------

	//Slots handed out in the indexed table, and the ones handed back. Nought is never handed out: a shader indexing the table with a number nobody
	//set reads slot zero, and an empty slot zero draws nothing, which is a great deal easier to recognise than whichever picture happened to be first.
	private static uint nextBindlessSlot = 1;

	//Textures refused a bindless slot; they draw with slot zero, i.e. the wrong picture
	private static uint bindlessDropped;

	private static readonly List<uint> freeBindlessSlots = new();

	//Whether every bindless slot is written; reading an unwritten slot can hang the GPU
	private static bool bindlessFilled;

	//1x1 white picture every empty bindless slot points at, so stale indices never read freed memory
	private static ImageView bindlessBlank;

	//The blank's handle, so the device's teardown destroys it with everything else.
	private static ulong bindlessBlankTexture;

	//Whether the device is being torn down, in which case no slot is worth writing.
	private static bool bindlessGoing;

	//The blank, made on the first ask.
	private static ImageView BindlessBlank()
	{
		if ( bindlessBlank.Handle != 0 ) return bindlessBlank;
		if ( bindlessGoing || !VkDevice.Ready ) return default;

		var texel = stackalloc byte[4];
		texel[0] = 255;
		texel[1] = 255;
		texel[2] = 255;
		texel[3] = 255;

		//Through the ordinary path, which takes a slot of its own for it - and that slot is the
		//one place in the table where the blank is what was asked for rather than what is left.
		bindlessBlankTexture = CreateRgba( texel, 1, 1, 0 );

		if ( bindlessBlankTexture != 0 && ViewOf( bindlessBlankTexture, out var view ) )
			bindlessBlank = view;

		return bindlessBlank;
	}

	//Writes one slot of the table.
	private static void WriteBindless( uint slot, ImageView view, Sampler sampler = default )
	{
		if ( !VkDevice.bindless || view.Handle == 0 ) return;

		var info = new DescriptorImageInfo
		{
			ImageView = view,
			Sampler = sampler.Handle != 0 ? sampler : VkDevice.sampler,
			ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
		};

		var write = new WriteDescriptorSet
		{
			SType = StructureType.WriteDescriptorSet,
			DstSet = VkDevice.bindlessSet,
			DstBinding = 0,
			DstArrayElement = slot,
			DescriptorCount = 1,
			DescriptorType = DescriptorType.CombinedImageSampler,
			PImageInfo = &info,
		};

		VkDevice.vk.UpdateDescriptorSets( VkDevice.device, 1, &write, 0, null );
	}

	private static void FillBindless()
	{
		if ( bindlessFilled || !VkDevice.bindless ) return;

		//Before the blank is made, because making it comes back through here.
		bindlessFilled = true;

		var view = BindlessBlank();

		if ( view.Handle == 0 )
		{
			bindlessFilled = false;
			return;
		}

		var infos = (DescriptorImageInfo*)System.Runtime.InteropServices.NativeMemory.Alloc(
			(nuint)VkDevice.bindlessSlots * (nuint)sizeof( DescriptorImageInfo ) );

		try
		{
			for ( var i = 0u; i < VkDevice.bindlessSlots; ++i )
			{
				infos[i].ImageView = view;
				infos[i].Sampler = VkDevice.sampler;
				infos[i].ImageLayout = ImageLayout.ShaderReadOnlyOptimal;
			}

			var write = new WriteDescriptorSet
			{
				SType = StructureType.WriteDescriptorSet,
				DstSet = VkDevice.bindlessSet,
				DstBinding = 0,
				DstArrayElement = 0,
				DescriptorCount = VkDevice.bindlessSlots,
				DescriptorType = DescriptorType.CombinedImageSampler,
				PImageInfo = infos,
			};

			VkDevice.vk.UpdateDescriptorSets( VkDevice.device, 1, &write, 0, null );
		}
		finally
		{
			System.Runtime.InteropServices.NativeMemory.Free( infos );
		}
	}

	//Puts a picture in the table and answers where, or zero where there is no table or no room left in it.
	private static uint TakeBindlessSlot( ImageView view, Sampler sampler )
	{
		if ( !VkDevice.bindless || view.Handle == 0 ) return 0;

		//Every slot holds something before any of them is handed out.
		FillBindless();

		uint slot;

		if ( freeBindlessSlots.Count > 0 )
		{
			slot = freeBindlessSlots[^1];
			freeBindlessSlots.RemoveAt( freeBindlessSlots.Count - 1 );
		}
		else
		{
			if ( nextBindlessSlot >= VkDevice.bindlessSlots )
			{
				//Said once. Said per refusal, it would be said at the rate textures are made, which is a log nobody can read.
				if ( bindlessDropped == 0 )
				{
					var text = $"the picture table is full at {VkDevice.bindlessSlots} slots - textures past it draw as another one";

					VkDevice.SayWarning( text );
					VkDevice.SetText( text );
				}

				bindlessDropped++;
				return 0;
			}

			slot = nextBindlessSlot++;
		}

		WriteBindless( slot, view, sampler );

		return slot;
	}

	//Returns a bindless slot and points it at the blank; runs once nothing in flight uses the old picture
	private static void DropBindlessSlot( uint slot )
	{
		if ( slot == 0 ) return;

		if ( !bindlessGoing ) WriteBindless( slot, BindlessBlank() );

		freeBindlessSlots.Add( slot );
	}

	//Puts a picture that is not one of ours in the table - a window's image, read by the shading rate pass
	//(see VkFrame.Adaptive) - and answers where, or nought. Given back through VkDevice.RetireBindless.
	internal static uint TakeSlotFor( ImageView view ) => TakeBindlessSlot( view, default );

	//Hands a bindless slot back once nothing in flight can still index it.
	internal static void RecycleBindlessSlot( uint slot ) => DropBindlessSlot( slot );

	//-- material sets ------------------------------------------------------

	private static bool AllocateTextureSet( out DescriptorSet set )
		=> VkDevice.AllocateDescriptorSet( VkDevice.textureSetLayout, out set );

	//Records in the texture what its set was last written with, so that a sampler change can write it again.
	private static void RememberSet( GpuTexture texture, ImageView* views, Sampler sampler )
	{
		for ( var i = 0; i < VkDevice.MaterialTextures; ++i ) texture.SetViews[i] = views[i];

		texture.SetSampler = sampler;
	}

	//Fills in a material set: four image slots and the numbers. The layout is what the pictures are actually in when the set is read. A
	//descriptor that names one layout while the image is in another is not a picture the driver has to answer for - it reads as anything at all.
	private static void WriteMaterialSet( DescriptorSet set, ImageView* views, VkBuffer parameters,
		Sampler sampler = default, ImageLayout layout = ImageLayout.ShaderReadOnlyOptimal )
	{
		var images = stackalloc DescriptorImageInfo[(int)VkDevice.MaterialTextures];
		var writes = stackalloc WriteDescriptorSet[(int)VkDevice.MaterialTextures + 1];

		for ( var i = 0u; i < VkDevice.MaterialTextures; ++i )
		{
			images[i].Sampler = sampler.Handle != 0 ? sampler : VkDevice.sampler;
			images[i].ImageView = views[i];
			images[i].ImageLayout = layout;

			writes[i].SType = StructureType.WriteDescriptorSet;
			writes[i].DstSet = set;
			writes[i].DstBinding = i;
			writes[i].DescriptorCount = 1;
			writes[i].DescriptorType = DescriptorType.CombinedImageSampler;
			writes[i].PImageInfo = &images[i];
		}

		var buffer = new DescriptorBufferInfo
		{
			Buffer = parameters,
			Offset = 0,
			Range = VkDevice.MaterialParamSize,
		};

		writes[VkDevice.MaterialTextures].SType = StructureType.WriteDescriptorSet;
		writes[VkDevice.MaterialTextures].DstSet = set;
		writes[VkDevice.MaterialTextures].DstBinding = VkDevice.MaterialTextures;
		writes[VkDevice.MaterialTextures].DescriptorCount = 1;
		writes[VkDevice.MaterialTextures].DescriptorType = DescriptorType.UniformBuffer;
		writes[VkDevice.MaterialTextures].PBufferInfo = &buffer;

		VkDevice.vk.UpdateDescriptorSets( VkDevice.device, VkDevice.MaterialTextures + 1, writes, 0, null );
	}

	//Writes every material set again, with whatever sampler it was written with last - which is how
	//a new sampler reaches sets that already name the old one. The caller has waited for the GPU.
	internal static bool RewriteMaterialSets()
	{
		var views = stackalloc ImageView[(int)VkDevice.MaterialTextures];

		foreach ( var pair in textures )
		{
			var texture = pair.Value;

			if ( texture.Set.Handle == 0 ) continue;
			if ( texture.SetViews[0].Handle == 0 ) continue;

			for ( var i = 0; i < VkDevice.MaterialTextures; ++i ) views[i] = texture.SetViews[i];

			//The buffer it was written with: a material has its own, everything else shares the device's.
			var parameters = texture.Params.Handle != 0 ? texture.Params : VkDevice.defaultParams;

			WriteMaterialSet( texture.Set, views, parameters, texture.SetSampler );
		}

		return true;
	}

	//-- making and unmaking a picture --------------------------------------

	private static void DestroyOne( GpuTexture texture )
	{
		DropBindlessSlot( texture.Index );

		if ( texture.Set.Handle != 0 ) VkDevice.FreeDescriptorSet( texture.Set );

		if ( texture.Params.Handle != 0 ) VkDevice.vk.DestroyBuffer( VkDevice.device, texture.Params, null );
		if ( texture.ParamsMemory.Handle != 0 ) VkDevice.vk.FreeMemory( VkDevice.device, texture.ParamsMemory, null );

		if ( texture.OwnsImage )
		{
			VkDevice.DestroyView( texture.View );
			if ( texture.Image.Handle != 0 ) VkDevice.vk.DestroyImage( VkDevice.device, texture.Image, null );

			DropMemory( texture.Block, texture.Offset, texture.Bytes, texture.Memory );
		}
	}

	//The deferred version of DestroyOne: everything goes to the graveyard, because frames in flight - and the frame being recorded right now -
	//may still be sampling it. This is what the destroy calls use; the immediate one stays for the paths that have already waited the device.
	private static void RetireOne( GpuTexture texture )
	{
		if ( texture.Index != 0 ) VkDevice.RetireBindless( texture.Index );
		if ( texture.Set.Handle != 0 ) VkDevice.RetireDescriptorSet( texture.Set );

		if ( texture.Params.Handle != 0 || texture.ParamsMemory.Handle != 0 )
			VkDevice.RetireBuffer( texture.Params, texture.ParamsMemory );

		if ( texture.OwnsImage )
		{
			if ( texture.Block >= 0 )
			{
				VkDevice.RetireSlicedImage( texture.Image, texture.View, texture.Block, texture.Offset, texture.Bytes );
			}
			else
			{
				VkDevice.RetireImage( texture.Image, texture.Memory, texture.View );
			}
		}
	}

	//The stored format that is four half floats per texel; see FormatOf
	internal const int HalfFormat = 6;

	//The stored formats a phone's pictures come in - ASTC 4x4 and ETC2 with alpha, sixteen bytes a block each.
	internal const int AstcFormat = 7, Etc2Format = 8;

	private static Format FormatOf( int stored, bool srgb ) => stored switch
	{
		1 => srgb ? Format.BC1RgbaSrgbBlock : Format.BC1RgbaUnormBlock,
		2 => srgb ? Format.BC3SrgbBlock : Format.BC3UnormBlock,
		3 => Format.BC4UnormBlock,
		4 => Format.BC5UnormBlock,
		5 => srgb ? Format.BC7SrgbBlock : Format.BC7UnormBlock,

		//What a build's KTX2 pictures are turned into on a phone - see CoreBasisTranscoder.
		AstcFormat => srgb ? Format.Astc4x4SrgbBlock : Format.Astc4x4UnormBlock,
		Etc2Format => srgb ? Format.Etc2R8G8B8A8SrgbBlock : Format.Etc2R8G8B8A8UnormBlock,

		//Light itself rather than a picture of it: four halves a texel, which is what baked lighting is kept
		//in - see HalfFormat. Every device samples and filters it; it is one of the formats Vulkan requires.
		HalfFormat => Format.R16G16B16A16Sfloat,
		_ => srgb ? Format.R8G8B8A8Srgb : Format.R8G8B8A8Unorm,
	};

	//One mip's shape, and where its bytes sit in the upload blob.
	private struct MipSlice
	{
		public uint Width;
		public uint Height;
		public ulong Offset;
		public ulong ByteCount;
	}

	//Writes new pixels into part of an existing picture; shared by updates and spare reuse
	private static bool RecordWrite( GpuTexture target, byte* pixels, ulong stride,
		int x, int y, int width, int height, ImageLayout from, ulong texelBytes = 4, uint level = 0, ulong blockBytes = 0 )
	{
		var startedNs = TimedNs();

		//A block format goes a row of four-by-four blocks at a time.
		var rowBytes = blockBytes > 0 ? (ulong)( ( width + 3 ) / 4 ) * blockBytes : (ulong)width * texelBytes;
		var rows = blockBytes > 0 ? (ulong)( ( height + 3 ) / 4 ) : (ulong)height;

		var slot = ReserveUpload( rowBytes * rows, out var at );

		if ( slot is null ) return false;

		//Row by row out of the picture the region sits in, so the caller hands over a window onto what it already has rather than packing a copy.
		var destination = slot.StagingMapped + at;

		for ( var row = 0ul; row < rows; ++row )
		{
			System.Buffer.MemoryCopy( pixels + ( row * stride ), destination + ( row * rowBytes ), rowBytes, rowBytes );
		}

		var vk = VkDevice.vk;
		var commands = slot.Commands;

		var toTransfer = new ImageMemoryBarrier2
		{
			SType = StructureType.ImageMemoryBarrier2,
			SrcStageMask = PipelineStageFlags2.FragmentShaderBit,
			SrcAccessMask = AccessFlags2.ShaderSampledReadBit,
			DstStageMask = PipelineStageFlags2.TransferBit,
			DstAccessMask = AccessFlags2.TransferWriteBit,
			OldLayout = from,
			NewLayout = ImageLayout.TransferDstOptimal,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			Image = target.Image,
			SubresourceRange = new ImageSubresourceRange( ImageAspectFlags.ColorBit, level, 1, 0, 1 ),
		};

		var dependency = new DependencyInfo
		{
			SType = StructureType.DependencyInfo,
			ImageMemoryBarrierCount = 1,
			PImageMemoryBarriers = &toTransfer,
		};

		VkDevice.dialect.Barrier( commands, &dependency );

		var copy = new BufferImageCopy
		{
			BufferOffset = at,
			ImageSubresource = new ImageSubresourceLayers( ImageAspectFlags.ColorBit, level, 0, 1 ),
			ImageOffset = new Offset3D( x, y, 0 ),
			ImageExtent = new Extent3D( (uint)width, (uint)height, 1 ),
		};

		vk.CmdCopyBufferToImage( commands, slot.Staging, target.Image, ImageLayout.TransferDstOptimal, 1, &copy );

		var toSampling = toTransfer;
		toSampling.SrcStageMask = PipelineStageFlags2.TransferBit;
		toSampling.SrcAccessMask = AccessFlags2.TransferWriteBit;
		toSampling.DstStageMask = PipelineStageFlags2.FragmentShaderBit;
		toSampling.DstAccessMask = AccessFlags2.ShaderSampledReadBit;
		toSampling.OldLayout = ImageLayout.TransferDstOptimal;
		toSampling.NewLayout = ImageLayout.ShaderReadOnlyOptimal;

		dependency.PImageMemoryBarriers = &toSampling;
		VkDevice.dialect.Barrier( commands, &dependency );

		if ( !batching ) SubmitBatch();

		if ( timingTextures )
		{
			making.Written += TimedNs() - startedNs;
			making.Writes++;

			SayMakingCost();
		}

		return true;
	}

	//The image, its memory, the upload, the view and the descriptor - the whole journey from bytes
	//in memory to something a shader can sample. Zero on failure, with the reason already recorded.
	private static ulong CreateFromPixels( byte* pixels, ulong pixelBytes, MipSlice* mips, int mipCount,
		bool srgb, int stored )
	{
		if ( mipCount <= 0 || pixelBytes == 0 )
		{
			VkDevice.SetText( "a texture needs some pixels" );
			return 0;
		}

		if ( stored is >= 1 and <= 5 && !VkDevice.blockCompression )
		{
			VkDevice.SetText( "this device does not take compressed textures" );
			return 0;
		}

		var startedNs = TimedNs();

		var format = FormatOf( stored, srgb );


		var vk = VkDevice.vk;
		var device = VkDevice.device;

		//A picture of this shape that somebody has finished with, before asking the device for another one. Only the single-level case: a
		//mip chain would have to be written all the way down, and the pictures that churn - every widget of an interface - have one level.
		if ( mipCount == 1 && sparing && stored == 0 )
		{
			var spare = TakeSpare( mips[0].Width, mips[0].Height, format );

			if ( spare is not null )
			{
				if ( RecordWrite( spare, pixels, (ulong)mips[0].Width * 4,
					0, 0, (int)mips[0].Width, (int)mips[0].Height, ImageLayout.ShaderReadOnlyOptimal ) )
				{
					var reused = nextHandle++;
					textures[reused] = spare;

					return reused;
				}

				//It could not be written into after all - which is a staging failure, not anything wrong with
				//the picture. It goes properly rather than back on the pile, and the slow path makes a new one.
				RetireOne( spare );
			}
		}

		var made = new GpuTexture();

		var imageInfo = new ImageCreateInfo
		{
			SType = StructureType.ImageCreateInfo,
			ImageType = ImageType.Type2D,
			Format = format,
			Extent = new Extent3D( mips[0].Width, mips[0].Height, 1 ),
			MipLevels = (uint)mipCount,
			ArrayLayers = 1,
			Samples = SampleCountFlags.Count1Bit,
			Tiling = ImageTiling.Optimal,
			//A source of copies as well as a destination: a texture copied into another, or its levels built out of its first - see VkTextures.CopyTexture.
			Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit,
			InitialLayout = ImageLayout.Undefined,
		};

		VkImage image;

		if ( vk.CreateImage( device, &imageInfo, null, &image ) != Result.Success )
		{
			VkDevice.SetText( "the texture image would not be made" );
			return 0;
		}

		made.Image = image;

		var imageNs = TimedNs();

		MemoryRequirements requirements;
		vk.GetImageMemoryRequirements( device, image, &requirements );

		if ( !TakeMemory( requirements, out var memory, out var block, out var offset ) ||
			vk.BindImageMemory( device, image, memory, offset ) != Result.Success )
		{
			made.Memory = memory;
			made.Block = block;
			made.Offset = offset;
			made.Bytes = requirements.Size;

			VkDevice.SetText( "the texture's memory would not be had" );
			DestroyOne( made );
			return 0;
		}

		made.Memory = memory;
		made.Block = block;
		made.Offset = offset;

		var memoryNs = TimedNs();

		made.Bytes = requirements.Size;
		made.Format = format;
		made.Width = mips[0].Width;
		made.Height = mips[0].Height;
		made.MipCount = (uint)mipCount;

		//By the handle the managed side knows it by, so that a message about an image can be traced back to the asset that made it.
		VkDevice.NameHandle( ObjectType.Image, made.Image.Handle, "texture", nextHandle );
		VkDevice.NameHandle( ObjectType.DeviceMemory, made.Memory.Handle, "texture memory", nextHandle );

		//Transition, copy every mip, transition again - written into the batch of uploads this frame is collecting rather than a
		//submission of its own. Somebody else's copies are already in there and more will follow; they all go to the card together.
		var uploaded = false;

		var slot = ReserveUpload( pixelBytes, out var at );

		if ( slot is not null )
		{
			System.Buffer.MemoryCopy( pixels, slot.StagingMapped + at, pixelBytes, pixelBytes );

			var commands = slot.Commands;

			var toTransfer = new ImageMemoryBarrier2
			{
				SType = StructureType.ImageMemoryBarrier2,
				SrcStageMask = PipelineStageFlags2.TopOfPipeBit,
				DstStageMask = PipelineStageFlags2.TransferBit,
				DstAccessMask = AccessFlags2.TransferWriteBit,
				OldLayout = ImageLayout.Undefined,
				NewLayout = ImageLayout.TransferDstOptimal,
				SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
				DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
				Image = made.Image,
				SubresourceRange = new ImageSubresourceRange( ImageAspectFlags.ColorBit, 0, (uint)mipCount, 0, 1 ),
			};

			var dependency = new DependencyInfo
			{
				SType = StructureType.DependencyInfo,
				ImageMemoryBarrierCount = 1,
				PImageMemoryBarriers = &toTransfer,
			};

			VkDevice.dialect.Barrier( commands, &dependency );

			var copies = stackalloc BufferImageCopy[16];
			var copyBuffer = mipCount <= 16
				? copies
				: (BufferImageCopy*)System.Runtime.InteropServices.NativeMemory.Alloc( (nuint)mipCount * (nuint)sizeof( BufferImageCopy ) );

			try
			{
				for ( var mip = 0; mip < mipCount; ++mip )
				{
					copyBuffer[mip] = new BufferImageCopy
					{
						BufferOffset = at + mips[mip].Offset,
						ImageSubresource = new ImageSubresourceLayers( ImageAspectFlags.ColorBit, (uint)mip, 0, 1 ),

						//Extent in texels even for partial blocks at tiny mips; Vulkan reads the whole block itself
						ImageExtent = new Extent3D( mips[mip].Width, mips[mip].Height, 1 ),
					};
				}

				vk.CmdCopyBufferToImage( commands, slot.Staging, made.Image,
					ImageLayout.TransferDstOptimal, (uint)mipCount, copyBuffer );
			}
			finally
			{
				if ( mipCount > 16 ) System.Runtime.InteropServices.NativeMemory.Free( copyBuffer );
			}

			var toSampling = toTransfer;
			toSampling.SrcStageMask = PipelineStageFlags2.TransferBit;
			toSampling.SrcAccessMask = AccessFlags2.TransferWriteBit;
			toSampling.DstStageMask = PipelineStageFlags2.FragmentShaderBit;
			toSampling.DstAccessMask = AccessFlags2.ShaderSampledReadBit;
			toSampling.OldLayout = ImageLayout.TransferDstOptimal;
			toSampling.NewLayout = ImageLayout.ShaderReadOnlyOptimal;

			dependency.PImageMemoryBarriers = &toSampling;
			VkDevice.dialect.Barrier( commands, &dependency );

			uploaded = true;
		}

		//Nothing to free: the staging buffer is the slot's, and the slot does not hand itself out again until its fence proves this copy finished.
		if ( !uploaded )
		{
			//The batch is still open and still good - what failed was finding room in it, and nothing about this image was written down.
			VkDevice.SetText( "there was no room to copy the texture through" );

			DestroyOne( made );
			return 0;
		}

		if ( !batching ) SubmitBatch();

		var copyNs = TimedNs();

		var viewInfo = new ImageViewCreateInfo
		{
			SType = StructureType.ImageViewCreateInfo,
			Image = made.Image,
			ViewType = ImageViewType.Type2D,
			Format = format,
			SubresourceRange = new ImageSubresourceRange( ImageAspectFlags.ColorBit, 0, (uint)mipCount, 0, 1 ),
		};

		ImageView view;

		if ( vk.CreateImageView( device, &viewInfo, null, &view ) != Result.Success )
		{
			VkDevice.SetText( "the texture's view would not be made" );
			DestroyOne( made );
			return 0;
		}

		made.View = view;

		var viewNs = TimedNs();

		//And into the table every shader can index, so a draw that decides which picture per vertex has a number to decide with.
		made.Index = TakeBindlessSlot( made.View, default );

		if ( !AllocateTextureSet( out var set ) )
		{
			VkDevice.SetText( "out of texture descriptors - something is leaking them" );
			DestroyOne( made );
			return 0;
		}

		made.Set = set;

		//All four slots point at this one picture: a caller with a texture and no material still
		//gets a complete set, and a shader that reads only the colour never notices the rest.
		var views = stackalloc ImageView[(int)VkDevice.MaterialTextures];
		for ( var i = 0; i < VkDevice.MaterialTextures; ++i ) views[i] = made.View;

		WriteMaterialSet( made.Set, views, VkDevice.defaultParams );
		RememberSet( made, views, default );

		if ( timingTextures )
		{
			var doneNs = TimedNs();

			making.Image += imageNs - startedNs;
			making.Memory += memoryNs - imageNs;
			making.Copy += copyNs - memoryNs;
			making.View += viewNs - copyNs;
			making.Descriptors += doneNs - viewNs;

			making.Made++;

			SayMakingCost();
		}

		VkDevice.trackedTextureBytes += (long)made.Bytes;
		VkDevice.trackedTextureCount++;

		var handle = nextHandle++;
		textures[handle] = made;

		return handle;
	}

	//-- what the rest of the renderer asks for -----------------------------

	//A texture's descriptor set by handle, or null.
	internal static DescriptorSet SetOf( ulong handle )
	{
		if ( textures.TryGetValue( handle, out var found ) ) return found.Set;

		//A pyramid answers here as well, so that a dispatch given one to read binds it by the path every other picture takes.
		return pyramids.TryGetValue( handle, out var peak ) ? peak.Set : default;
	}

	//Where a texture sits in the indexed table, or zero where it is not in it - which is the answer on a device that
	//has no such table, and slot zero is left empty so that indexing it draws nothing rather than something arbitrary.
	internal static uint IndexOf( ulong handle )
		=> textures.TryGetValue( handle, out var found ) ? found.Index : 0;

	//The picture behind a texture handle, for a pass that fills in a descriptor set of its own rather than binding the one the texture carries.
	internal static bool ViewOf( ulong handle, out ImageView view )
	{
		view = default;

		if ( !textures.TryGetValue( handle, out var found ) || found.View.Handle == 0 ) return false;

		view = found.View;
		return true;
	}

	//Gives an image somebody else owns a texture handle, so a shader can be pointed at it the same way it is
	//pointed at any other picture. The view has to outlive the handle; releasing the handle does not free it.
	internal static ulong Adopt( ImageView view, Sampler sampler = default )
	{
		if ( view.Handle == 0 ) return 0;

		var made = new GpuTexture { View = view, OwnsImage = false };

		//Adopted pictures need a bindless slot too, or shaders read index zero (black)
		made.Index = TakeBindlessSlot( view, sampler );

		if ( !AllocateTextureSet( out var set ) ) return 0;

		made.Set = set;

		var views = stackalloc ImageView[(int)VkDevice.MaterialTextures];
		for ( var i = 0; i < VkDevice.MaterialTextures; ++i ) views[i] = view;

		WriteMaterialSet( made.Set, views, VkDevice.defaultParams, sampler );
		RememberSet( made, views, sampler );

		var handle = nextHandle++;
		textures[handle] = made;

		return handle;
	}

	//Drops a handle made by Adopt, leaving the image alone. The plain one frees the handle's
	//descriptor set there and then, which is only safe where the device has been waited.
	internal static void Release( ulong handle )
	{
		if ( !textures.TryGetValue( handle, out var found ) ) return;

		DestroyOne( found );
		textures.Remove( handle );
	}

	//The retiring one, which hands it to the graveyard - what a resize wants.
	internal static void Retire( ulong handle )
	{
		if ( !textures.TryGetValue( handle, out var found ) ) return;

		RetireOne( found );
		textures.Remove( handle );
	}

	//Destroys every texture. Must run before the descriptor pool goes.
	internal static void DestroyTextures()
	{
		//Nothing put back into the table from here is worth writing: the blank is one of the pictures going, and the table goes with the device.
		bindlessGoing = true;

		foreach ( var pair in pyramids ) DestroyOne( pair.Value );

		pyramids.Clear();

		DestroyShadowMaps();

		foreach ( var pair in textures ) DestroyOne( pair.Value );

		foreach ( var pair in spares )
		{
			for ( var i = 0; i < pair.Value.Count; ++i ) DestroyOne( pair.Value[i] );
		}

		spares.Clear();
		spareCount = 0;
		spareBytes = 0;

		for ( var i = 0; i < blocks.Count; ++i )
		{
			if ( blocks[i].Memory.Handle != 0 ) VkDevice.vk.FreeMemory( VkDevice.device, blocks[i].Memory, null );
		}

		blocks.Clear();

		textures.Clear();
		VkDevice.trackedTextureBytes = 0;
		VkDevice.trackedTextureCount = 0;

		//The table goes with the device, and so does what was written into it. A second run filling it
		//again is the whole point: the views it held belong to pictures that have just been destroyed.
		nextBindlessSlot = 1;
		bindlessDropped = 0;
		freeBindlessSlots.Clear();
		bindlessFilled = false;
		bindlessBlank = default;
		bindlessBlankTexture = 0;
		bindlessGoing = false;

		DestroyUploadContext();
	}

	//-- what the managed side calls ----------------------------------------

	//A texture's own image and shape, for a copy into it - see VkSwapchain.ViewCopyToTexture.
	//False for one with no picture of its own: a material, an adopted view.
	internal static bool ImageOf( ulong texture, out VkImage image, out uint width, out uint height, out uint levels )
	{
		image = default;
		width = height = levels = 0;

		if ( !textures.TryGetValue( texture, out var found ) || found.Image.Handle == 0 || found.Width == 0 ) return false;

		image = found.Image;
		width = found.Width;
		height = found.Height;
		levels = Math.Max( found.MipCount, 1u );

		return true;
	}

	internal static uint TextureIndex( ulong texture )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready ) return 0;

			return IndexOf( texture );
		}
	}


	//Every level of a compiled texture onto the device: three ints a level - width, height, byte count - and the levels back to back.
	internal static ulong CreateMips( int width, int height, int format, int srgb,
		int mipCount, int* mipInfo, byte* pixelData, int byteCount )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready )
			{
				VkDevice.SetText( "there is no device to put a texture on" );
				return 0;
			}

			if ( width <= 0 || height <= 0 || mipCount <= 0 || mipInfo is null || pixelData is null || byteCount <= 0 )
			{
				VkDevice.SetText( "that texture asset is not loaded" );
				return 0;
			}

			//First level to upload; texture quality skips levels above the max edge, single-level goes as is
			var first = 0;

			if ( VkDevice.textureMaxEdge > 0 )
			{
				for ( var mip = 0; mip < mipCount - 1; ++mip )
				{
					var info = mipInfo + ( mip * 3 );

					if ( info[0] <= 0 || info[1] <= 0 ) break;

					var edge = (uint)( info[0] > info[1] ? info[0] : info[1] );
					if ( edge <= VkDevice.textureMaxEdge ) break;

					first = mip + 1;
				}
			}

			var mips = stackalloc MipSlice[32];
			var taken = 0;

			//The levels lie back to back in the order they are listed, so where one starts is the sum of the ones before it.
			var at = 0ul;

			for ( var mip = 0; mip < first; ++mip ) at += (ulong)mipInfo[( mip * 3 ) + 2];

			var start = at;
			var packed = 0ul;

			for ( var mip = first; mip < mipCount && taken < 32; ++mip )
			{
				var info = mipInfo + ( mip * 3 );

				if ( info[0] <= 0 || info[1] <= 0 || info[2] <= 0 ) break;
				if ( at + (ulong)info[2] > (ulong)byteCount ) break;

				mips[taken].Width = (uint)info[0];
				mips[taken].Height = (uint)info[1];
				mips[taken].Offset = packed;
				mips[taken].ByteCount = (ulong)info[2];

				packed += (ulong)info[2];
				at += (ulong)info[2];
				taken++;
			}

			//The levels that survived the limit lie back to back from `start`, so they are handed over as one run rather than repacked.
			return CreateFromPixels( pixelData + start, packed, mips, taken, srgb != 0, format );
		}
	}

	//A build's KTX2 picture, turned into what this device samples best by rintencore's transcoder:
	//ASTC, ETC2, BC7, or four bytes a pixel where it has none of them - see CoreBasisTranscoder.
	private static bool saidUniversal;

	internal static ulong CreateUniversal( CoreUniversalTexture picture )
	{
		var (format, stored) = VkDevice.astc ? ( CoreBasisTranscoder.Astc4x4Rgba, AstcFormat )
			: VkDevice.etc2 ? ( CoreBasisTranscoder.Etc2Rgba, Etc2Format )
			: VkDevice.blockCompression ? ( CoreBasisTranscoder.Bc7Rgba, 5 )
			: ( CoreBasisTranscoder.Rgba32, 0 );

		var bytes = CoreBasisTranscoder.Transcode( picture, format, out var levels );

		if ( !saidUniversal )
		{
			saidUniversal = true;
			Log.Info( $"the build's KTX2 pictures are transcoded to {( stored == AstcFormat ? "ASTC" : stored == Etc2Format ? "ETC2" : stored == 5 ? "BC7" : "RGBA8" )} as they load - see CoreBasisTranscoder" );
		}

		if ( bytes is null )
		{
			VkDevice.SetText( "a KTX2 picture would not transcode" );
			return 0;
		}

		fixed ( byte* data = bytes )
		fixed ( int* info = levels )
		{
			return CreateMips( picture.Width, picture.Height, stored, picture.IsSrgb ? 1 : 0,
				levels.Length / 3, info, data, bytes.Length );
		}
	}

	internal static ulong CreateRgba( byte* pixels, int width, int height, int srgb )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready )
			{
				VkDevice.SetText( "there is no device to put a texture on" );
				return 0;
			}

			if ( pixels is null || width <= 0 || height <= 0 )
			{
				VkDevice.SetText( "a texture needs some pixels" );
				return 0;
			}

			var byteCount = (ulong)width * (ulong)height * 4;

			var mip = new MipSlice
			{
				Width = (uint)width,
				Height = (uint)height,
				Offset = 0,
				ByteCount = byteCount,
			};

			return CreateFromPixels( pixels, byteCount, &mip, 1, srgb != 0, 0 );
		}
	}

	//One rectangle of new pixels into a texture that is allowed to take them. The whole-texture call is this with the whole texture as the rectangle.
	internal static int UpdateRgbaRegion( ulong texture, byte* pixels, int stride,
		int x, int y, int width, int height, bool whole )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready ) return 0;
			if ( pixels is null || width <= 0 || height <= 0 ) return 0;
			if ( x < 0 || y < 0 || stride < width * 4 ) return 0;

			if ( !textures.TryGetValue( texture, out var target ) ) return 0;

			//Only a plain picture. A material or an adopted view has no image of its own to write into, and a mip chain would go stale
			//above the level written - those get a new texture instead, which is what the caller falls back to when this says no.
			if ( !target.OwnsImage || target.Image.Handle == 0 ) return 0;
			if ( target.MipCount != 1 ) return 0;

			if ( whole )
			{
				//The exact shape, or the caller wanted a different picture and has to make one.
				if ( target.Width != (uint)width || target.Height != (uint)height ) return 0;
			}
			else if ( (uint)( x + width ) > target.Width || (uint)( y + height ) > target.Height )
			{
				return 0;
			}

			return RecordWrite( target, pixels, (ulong)stride, x, y, width, height,
				ImageLayout.ShaderReadOnlyOptimal ) ? 1 : 0;
		}
	}

	internal static int UpdateRgba( ulong texture, byte* pixels, int width, int height )
		=> UpdateRgbaRegion( texture, pixels, width * 4, 0, 0, width, height, true );

	//Bytes a four-by-four block of a block format - nought for a format of pixels.
	private static ulong BlockBytesOf( Format format ) => format switch
	{
		Format.BC1RgbaSrgbBlock or Format.BC1RgbaUnormBlock or Format.BC4UnormBlock => 8,
		Format.BC3SrgbBlock or Format.BC3UnormBlock or Format.BC5UnormBlock or Format.BC7SrgbBlock or Format.BC7UnormBlock => 16,
		Format.Astc4x4SrgbBlock or Format.Astc4x4UnormBlock or Format.Etc2R8G8B8A8SrgbBlock or Format.Etc2R8G8B8A8UnormBlock => 16,
		_ => 0,
	};

	//Writes a rectangle into one level in the texture's format; block formats must be block-aligned
	internal static int WriteLevel( ulong texture, int level, int x, int y, int width, int height, byte* data, int stride )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready || data is null || width <= 0 || height <= 0 || x < 0 || y < 0 || level < 0 ) return 0;
			if ( !textures.TryGetValue( texture, out var target ) ) return 0;
			if ( !target.OwnsImage || target.Image.Handle == 0 || (uint)level >= target.MipCount ) return 0;

			var levelWidth = Math.Max( 1u, target.Width >> level );
			var levelHeight = Math.Max( 1u, target.Height >> level );
			if ( (uint)( x + width ) > levelWidth || (uint)( y + height ) > levelHeight ) return 0;

			var blockBytes = BlockBytesOf( target.Format );
			var texelBytes = target.Format == Format.R16G16B16A16Sfloat ? 8ul : 4ul;

			if ( blockBytes > 0 )
			{
				if ( ( x & 3 ) != 0 || ( y & 3 ) != 0 ) return 0;
				if ( ( width & 3 ) != 0 && (uint)( x + width ) != levelWidth ) return 0;
				if ( ( height & 3 ) != 0 && (uint)( y + height ) != levelHeight ) return 0;
				if ( (ulong)stride < (ulong)( ( width + 3 ) / 4 ) * blockBytes ) return 0;
			}
			else if ( (ulong)stride < (ulong)width * texelBytes )
			{
				return 0;
			}

			return RecordWrite( target, data, (ulong)stride, x, y, width, height,
				ImageLayout.ShaderReadOnlyOptimal, texelBytes, (uint)level, blockBytes ) ? 1 : 0;
		}
	}

	//A texture of nothing - every level of the chain there, all nought - for whoever fills it in pieces with WriteLevel. All of its levels whatever
	//the texture quality says: a piece is written by its place in the finest level, and a texture that had lost that level would put it nowhere.
	internal static ulong CreateBlank( int width, int height, int stored, int srgb, int mipCount )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready )
			{
				VkDevice.SetText( "there is no device to put a texture on" );
				return 0;
			}

			if ( width <= 0 || height <= 0 || mipCount <= 0 || mipCount > 32 ) return 0;

			var blockBytes = BlockBytesOf( FormatOf( stored, srgb != 0 ) );
			var mips = stackalloc MipSlice[32];
			var total = 0ul;

			for ( var mip = 0; mip < mipCount; mip++ )
			{
				var w = (uint)Math.Max( 1, width >> mip );
				var h = (uint)Math.Max( 1, height >> mip );
				var bytes = blockBytes > 0 ? (ulong)( ( w + 3 ) / 4 ) * ( ( h + 3 ) / 4 ) * blockBytes : (ulong)w * h * 4;

				mips[mip] = new MipSlice { Width = w, Height = h, Offset = total, ByteCount = bytes };
				total += bytes;
			}

			var nothing = new byte[total];

			fixed ( byte* pixels = nothing )
				return CreateFromPixels( pixels, total, mips, mipCount, srgb != 0, stored );
		}
	}

	//The buffer of numbers a material was made with, nought for one using the device's shared defaults. The caller holds the render lock.
	internal static VkBuffer MaterialParams( ulong material )
		=> textures.TryGetValue( material, out var found ) && found.Parts is not null ? found.Params : default;

	internal static ulong MaterialCreate( ulong colour, ulong normal, ulong third,
		ulong emission, float* parameters )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready )
			{
				VkDevice.SetText( "there is no device to make a material on" );
				return 0;
			}

			var wanted = stackalloc ulong[(int)VkDevice.MaterialTextures];
			wanted[0] = colour;
			wanted[1] = normal;
			wanted[2] = third;
			wanted[3] = emission;

			//A slot with nothing in it borrows the colour, so every binding is written and a shader
			//reading a map the material does not have gets a picture rather than undefined memory.
			ImageView fallback = default;

			if ( textures.TryGetValue( colour, out var first ) ) fallback = first.View;

			var views = stackalloc ImageView[(int)VkDevice.MaterialTextures];

			for ( var i = 0; i < VkDevice.MaterialTextures; ++i )
			{
				views[i] = textures.TryGetValue( wanted[i], out var found ) ? found.View : fallback;

				if ( views[i].Handle == 0 )
				{
					VkDevice.SetText( "a material needs at least a colour texture" );
					return 0;
				}
			}

			var made = new GpuTexture { OwnsImage = false, Parts = new[] { colour, normal, third, emission } };

			//Its own numbers, or the device's defaults shared with every plain texture.
			var paramsBuffer = VkDevice.defaultParams;

			if ( parameters is not null )
			{
				if ( !VkDevice.MakeParamsBuffer( parameters, out var buffer, out var memory ) )
				{
					VkDevice.SetText( "the material's parameters would not fit anywhere" );
					return 0;
				}

				made.Params = buffer;
				made.ParamsMemory = memory;
				paramsBuffer = buffer;
			}

			if ( !AllocateTextureSet( out var set ) )
			{
				VkDevice.SetText( "out of descriptors - something is leaking materials" );
				DestroyOne( made );
				return 0;
			}

			made.Set = set;

			//The device's own sampler: a material reads its pictures smoothly.
			WriteMaterialSet( made.Set, views, paramsBuffer, default );
			RememberSet( made, views, default );

			var handle = nextHandle++;
			textures[handle] = made;

			return handle;
		}
	}

	//-- the portable frame's sky table --------------------------------------------

	//Sky table view for the portable frame, which binds it since it has no bindless table
	internal static bool PortableSkyTableView( out ImageView view )
	{
		view = VkDevice.skyTableView;

		return view.Handle != 0;
	}

	//The pictures a material was made of, colour, normal, third and emission - nought where it is none.
	internal static void MaterialParts( ulong material, Span<ulong> into )
	{
		into.Clear();

		lock ( VkDevice.renderLock )
		{
			if ( !textures.TryGetValue( material, out var found ) ) return;

			//A plain picture drawn as a material is its own one part.
			if ( found.Parts is null )
			{
				if ( found.OwnsImage && into.Length > 0 ) into[0] = material;
				return;
			}

			for ( var i = 0; i < found.Parts.Length && i < into.Length; i++ ) into[i] = found.Parts[i];
		}
	}

	//How many levels a picture has on the device and what they cost, or false where it is none of ours.
	internal static bool Held( ulong texture, out int levels, out long bytes )
	{
		lock ( VkDevice.renderLock )
		{
			levels = 0;
			bytes = 0;

			if ( !textures.TryGetValue( texture, out var found ) || !found.OwnsImage ) return false;

			levels = (int)found.MipCount;
			bytes = (long)found.Bytes;
			return true;
		}
	}

	//Swaps each target to a source image with different levels; old resources retire via the graveyard
	internal static int Swap( ReadOnlySpan<(ulong Target, ulong Source)> swaps )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready || swaps.IsEmpty ) return 0;

			//What the new pictures were written by, sent ahead of any frame that reads them.
			SubmitBatch();

			var moved = new Dictionary<ulong, ImageView>();
			var done = 0;

			foreach ( var (target, source) in swaps )
			{
				if ( !textures.TryGetValue( target, out var into ) || !into.OwnsImage ) continue;
				if ( !textures.TryGetValue( source, out var from ) || !from.OwnsImage ) continue;
				if ( into.Format != from.Format || ( from.Index == 0 && into.Index != 0 ) ) continue;

				var old = into.View;

				//The old image, slot and set after the frames that read them.
				if ( into.Block >= 0 ) VkDevice.RetireSlicedImage( into.Image, into.View, into.Block, into.Offset, into.Bytes );
				else VkDevice.RetireImage( into.Image, into.Memory, into.View );

				if ( into.Index != 0 ) VkDevice.RetireBindless( into.Index );
				if ( into.Set.Handle != 0 ) VkDevice.RetireDescriptorSet( into.Set );

				VkDevice.trackedTextureBytes -= (long)into.Bytes;
				VkDevice.trackedTextureCount--;

				into.Image = from.Image;
				into.Memory = from.Memory;
				into.View = from.View;
				into.Bytes = from.Bytes;
				into.Width = from.Width;
				into.Height = from.Height;
				into.MipCount = from.MipCount;
				into.Block = from.Block;
				into.Offset = from.Offset;
				into.Index = from.Index;
				into.Set = from.Set;
				for ( var i = 0; i < VkDevice.MaterialTextures; ++i ) into.SetViews[i] = from.SetViews[i];
				into.SetSampler = from.SetSampler;

				textures.Remove( source );

				//A picture moved twice in one batch: whatever was moved onto the view that has just gone is moved on again, so no set is left naming it.
				foreach ( var key in new List<ulong>( moved.Keys ) )
					if ( moved[key].Handle == old.Handle ) moved[key] = into.View;

				moved[old.Handle] = into.View;
				done++;
			}

			if ( moved.Count == 0 ) return 0;

			//Every material made of an old view gets a new set naming the new one; the old set goes when the frames that bound it are done.
			var views = stackalloc ImageView[(int)VkDevice.MaterialTextures];

			foreach ( var pair in textures )
			{
				var texture = pair.Value;
				if ( texture.Set.Handle == 0 || texture.SetViews[0].Handle == 0 ) continue;

				var named = false;

				for ( var i = 0; i < VkDevice.MaterialTextures; ++i )
				{
					views[i] = moved.TryGetValue( texture.SetViews[i].Handle, out var now ) ? now : texture.SetViews[i];
					named |= views[i].Handle != texture.SetViews[i].Handle;
				}

				if ( !named ) continue;

				var parameters = texture.Params.Handle != 0 ? texture.Params : VkDevice.defaultParams;

				if ( AllocateTextureSet( out var set ) )
				{
					WriteMaterialSet( set, views, parameters, texture.SetSampler );
					VkDevice.RetireDescriptorSet( texture.Set );
					texture.Set = set;
				}
				else
				{
					//No set to be had: the old one is written in place, which only a device with nothing in flight
					//allows - the one wait, and only here. Left as it was, it would name an image the graveyard frees.
					VkSubmitter.DeviceWaitIdle();
					WriteMaterialSet( texture.Set, views, parameters, texture.SetSampler );
				}

				RememberSet( texture, views, texture.SetSampler );
			}

			return done;
		}
	}

	internal static void Destroy( ulong texture )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !textures.TryGetValue( texture, out var found ) ) return;

			//Flush pending uploads first, since the graveyard can't see copies in an unsent command buffer
			SubmitBatch();

			//A plain picture is kept rather than freed: the next texture of this shape takes it as it is. Nothing about it changes here, so a frame
			//still in flight goes on sampling exactly what it was sampling - and whoever takes it writes through a barrier that waits for that sampling.
			if ( sparing && Plain( found ) &&
				spareCount < SpareCeiling &&
				spareBytes + (long)found.Bytes <= SpareBytesCeiling )
			{
				var key = ShapeKey( found.Width, found.Height, found.Format );

				if ( !spares.TryGetValue( key, out var kept ) )
				{
					kept = [];
					spares[key] = kept;
				}

				kept.Add( found );

				spareCount++;
				spareBytes += (long)found.Bytes;

				textures.Remove( texture );
				return;
			}

			//Frames in flight may still be sampling it - and so may the frame being recorded right now, which a device-wide wait would
			//not even cover. The graveyard holds it until a fence proves everything that could name it has finished; nobody stalls.
			if ( found.OwnsImage )
			{
				VkDevice.trackedTextureBytes -= (long)found.Bytes;
				VkDevice.trackedTextureCount--;
			}

			RetireOne( found );
			textures.Remove( texture );
		}
	}
}
