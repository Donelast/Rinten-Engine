using Silk.NET.Vulkan;

namespace Rinten;

//Adaptive shading: coarser rate where the previous frame was smooth (see Rendering.AdaptiveShading)
//Only at the world's native size; not for upscaled or two-eye views
internal static unsafe partial class VkFrame
{
	//The compute pipeline of shading_rate.slang, handed in by CoreMeshRender; nought where there is none.
	internal static ulong AdaptivePipeline;

	//The map for the world's passes where the world is shaded coarser by the picture - made on the first pass that asks - or null where it is not: the switch
	//is off, the device takes no rates, or the picture the world ends up in cannot be read - a window whose images are not sampled, a view of two eyes.
	private static VkFovea.Map AdaptiveMap( VkSwapchain.Pictures p )
	{
		if ( !VkFovea.Adaptive || AdaptivePipeline == 0 || !Rendering.AdaptiveShading.Enabled || p is null ) return null;

		var view = VkSwapchain.currentView;
		if ( view is null ? !Current.Sampled : view.Layers != 1 || view.OutputImage.Handle == 0 ) return null;

		return p.Fovea ??= new VkFovea.Map { Adaptive = true, Strength = 1.0f };
	}

	//Reads the picture about to be shown into the next frame's rates, where this frame's world was drawn with them. True
	//where it did - the image is then in the layout a shader reads it in, and the caller takes it from there to the display.
	private static bool AdaptiveRead( Frame frame )
	{
		var map = Pics?.Fovea;
		if ( map is not { Adaptive: true, DrawnWith: true } || !map.Made ) return false;

		map.DrawnWith = false;

		if ( AdaptivePipeline == 0 || !Current.Sampled ) return false;
		if ( Current.Extent.Width != map.For.Width || Current.Extent.Height != map.For.Height ) return false;

		var index = (int)Current.ImageIndex;
		if ( index < 0 || index >= Current.Views.Length ) return false;

		var slot = SlotOfView( index );
		if ( slot == 0 ) return false;

		Transition( frame.Commands, Current.Images[index],
			ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
			PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.ColorAttachmentWriteBit,
			PipelineStageFlags2.ComputeShaderBit, AccessFlags2.ShaderSampledReadBit );

		//Transitioned whether or not what follows goes through: the caller takes the image on from the layout it was left in.
		LayRates( frame, map, slot, Current.Extent, Current.Format );
		return true;
	}

	//The same for a view of the editor's: its finished picture, already where a shader reads it, read into
	//the rates its world is drawn with next time. Called as the view closes - see VkSwapchain.CloseView.
	internal static void AdaptiveReadView( Frame frame, VkSwapchain.View view, VkSwapchain.Pictures p )
	{
		var map = p?.Fovea;
		if ( view is null || map is not { Adaptive: true, DrawnWith: true } || !map.Made ) return;

		map.DrawnWith = false;

		if ( AdaptivePipeline == 0 || view.Layers != 1 || view.OutputLayout != ImageLayout.ShaderReadOnlyOptimal ) return;
		if ( view.OutputExtent.Width != map.For.Width || view.OutputExtent.Height != map.For.Height ) return;

		if ( view.OutputSlot == 0 ) view.OutputSlot = VkTextures.TakeSlotFor( view.OutputView );
		if ( view.OutputSlot == 0 ) return;

		var zone = ZoneBegin( "shading rates" );
		LayRates( frame, map, view.OutputSlot, view.OutputExtent, view.OutputFormat );
		if ( zone != 0 ) ZoneEnd();
	}

	//The picture in table slot slot, in a layout a shader reads, read into map: the words
	//cleared, the analysis run, the words copied into the map the world's passes take.
	private static void LayRates( Frame frame, VkFovea.Map map, uint slot, Extent2D extent, Format format )
	{
		VkPipelines.GetBoth( AdaptivePipeline, out var bound, out var layout );
		if ( bound.Handle == 0 || layout.Handle == 0 ) return;

		var rates = VkBuffers.Get( map.Rates );
		if ( rates.Handle == 0 ) return;

		var handle = map.Rates;
		var set = TransientObjectSet( &handle, 1, "the shading rate pass" );
		if ( set.Handle == 0 ) return;

		var vk = VkDevice.vk;
		var commands = frame.Commands;

		//Every square is ORed into its word, so the words start at nought - once the copy the frame before made out of them is done with them.
		var copied = new MemoryBarrier2
		{
			SType = StructureType.MemoryBarrier2,
			SrcStageMask = PipelineStageFlags2.AllTransferBit,
			SrcAccessMask = AccessFlags2.TransferReadBit,
			DstStageMask = PipelineStageFlags2.AllTransferBit,
			DstAccessMask = AccessFlags2.TransferWriteBit,
		};

		var before = new DependencyInfo { SType = StructureType.DependencyInfo, MemoryBarrierCount = 1, PMemoryBarriers = &copied };
		VkDevice.dialect.Barrier( commands, &before );

		vk.CmdFillBuffer( commands, rates, 0, Vk.WholeSize, 0u );

		var cleared = new MemoryBarrier2
		{
			SType = StructureType.MemoryBarrier2,
			SrcStageMask = PipelineStageFlags2.AllTransferBit,
			SrcAccessMask = AccessFlags2.TransferWriteBit,
			DstStageMask = PipelineStageFlags2.ComputeShaderBit,
			DstAccessMask = AccessFlags2.ShaderStorageReadBit | AccessFlags2.ShaderStorageWriteBit,
		};

		var dependency = new DependencyInfo { SType = StructureType.DependencyInfo, MemoryBarrierCount = 1, PMemoryBarriers = &cleared };
		VkDevice.dialect.Barrier( commands, &dependency );

		vk.CmdBindPipeline( commands, PipelineBindPoint.Compute, bound );
		vk.CmdBindDescriptorSets( commands, PipelineBindPoint.Compute, layout, 0, 1, &set, 0, null );

		var table = VkDevice.bindlessSet;
		vk.CmdBindDescriptorSets( commands, PipelineBindPoint.Compute, layout, 1, 1, &table, 0, null );

		var push = stackalloc uint[9];
		push[0] = slot;
		push[1] = extent.Width;
		push[2] = extent.Height;
		push[3] = map.Size.Width;
		push[4] = map.Size.Height;
		push[5] = VkFovea.Texel.Width;
		push[6] = map.RowBytes;
		push[7] = IsSrgb( format ) ? 1u : 0u;
		push[8] = BitConverter.SingleToUInt32Bits( Rendering.AdaptiveShading.Step / 255.0f );

		PushConstants( frame, layout, ShaderStageFlags.ComputeBit, push, 9 * sizeof( uint ) );

		//Sixteen pixels square a group - see shading_rate.slang.
		var per = 16u / VkFovea.Texel.Width;
		vk.CmdDispatch( commands, ( map.Size.Width + per - 1 ) / per, ( map.Size.Height + per - 1 ) / per, 1 );

		var written = new MemoryBarrier2
		{
			SType = StructureType.MemoryBarrier2,
			SrcStageMask = PipelineStageFlags2.ComputeShaderBit,
			SrcAccessMask = AccessFlags2.ShaderStorageWriteBit,
			DstStageMask = PipelineStageFlags2.AllTransferBit,
			DstAccessMask = AccessFlags2.TransferReadBit,
		};

		dependency.PMemoryBarriers = &written;
		VkDevice.dialect.Barrier( commands, &dependency );

		VkFovea.CopyIn( frame, map );

		frame.StatDispatches++;
	}

	//A window image's place in the picture table, put there the first time it is read.
	private static uint SlotOfView( int index )
	{
		if ( Current.ViewSlots.Length != Current.Views.Length )
		{
			foreach ( var old in Current.ViewSlots ) if ( old != 0 ) VkDevice.RetireBindless( old );
			Current.ViewSlots = new uint[Current.Views.Length];
		}

		if ( Current.ViewSlots[index] == 0 ) Current.ViewSlots[index] = VkTextures.TakeSlotFor( Current.Views[index] );

		return Current.ViewSlots[index];
	}

	private static bool IsSrgb( Format format ) => format is Format.B8G8R8A8Srgb or Format.R8G8B8A8Srgb or Format.A8B8G8R8SrgbPack32;
}
