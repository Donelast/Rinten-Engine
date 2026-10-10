using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;

namespace Rinten;

//A viewport that is not a window: the same pictures and the same passes, no surface and nothing to present to. What a preview or a thumbnail is.
internal static unsafe partial class VkSwapchain
{
	internal sealed class View
	{
		public readonly Pictures Pics = new();

		/// <summary>How many pictures deep it is: one, or both of a headset's eyes drawn in one pass, a layer each - see VkDevice.multiview.</summary>
		public uint Layers => Pics.Layers;

		/// <summary>External images (e.g. headset swapchain) the picture is copied into at close, one per layer</summary>
		public readonly Image[] CopyTargets = new Image[MostLayers];
		public Extent2D CopyExtent;

		/// <summary>A texture of the engine's own this frame's picture is copied into as it closes - a camera drawing into a render texture, see CoreCameraTargets. Every level of it, and left ready to be sampled. One frame's worth.</summary>
		public ulong CopyTexture;

		/// <summary>How much smaller than the view its world is drawn. One is the same size. Below it, the pictures the world goes through are smaller than the output and the compose pass carries what they hold up to it.</summary>
		public float RenderScale = 1.0f;

		/// <summary>Two command frames used in turn, so a view drawn every frame never waits on its own fence</summary>
		public readonly VkFrame.Frame[] Frames = [ new(), new() ];

		/// <summary>Which of the two the next draw uses.</summary>
		public int FrameAt;

		/// <summary>The one this view is drawing with now.</summary>
		public VkFrame.Frame Frame => Frames[FrameAt];

		/// <summary>Size of the finished picture (the view size); differs from Pics.Extent below render scale 1</summary>
		public Extent2D OutputExtent;

		/// <summary>Final 8-bit output, like a swapchain image for a window; sampled and copyable for readback</summary>
		public VkImage OutputImage;
		public DeviceMemory OutputMemory;
		public ImageView OutputView;
		public ulong OutputHandle;

		/// <summary>Copy of the output mid-frame, for panels drawn later that blur or tint what is behind them</summary>
		public VkImage BackdropImage;
		public DeviceMemory BackdropMemory;
		public ImageView BackdropView;
		public ulong BackdropHandle;

		/// <summary>Whether the backdrop holds this frame's copy yet.</summary>
		public bool BackdropTaken;

		/// <summary>How many levels the backdrop has - see BackdropLevelsFor.</summary>
		public uint BackdropLevels;

		/// <summary>Its place in the picture table, once the shading rate pass has read it - see VkFrame.Adaptive.</summary>
		public uint OutputSlot;
		public ImageLayout OutputLayout;

		/// <summary>Which way round its channels are. The same format a window's image has, because the pass that draws them is the same pass - and on this platform that is usually blue first.</summary>
		public Format OutputFormat = Format.Undefined;

		/// <summary>The same pixels in host memory, for whoever wants them as an image rather than as a texture - a thumbnail on its way to a file, a preview on its way into a widget.</summary>
		public VkBuffer ReadBuffer;
		public DeviceMemory ReadMemory;
		public byte* ReadMapped;
		public bool ReadPending;

		/// <summary>The last finished drawing of its own, measured.</summary>
		public readonly VkFrame.Profile Profile = new();
	}

	private static readonly Dictionary<ulong, View> views = new();

	//How many layers a view may have: a headset's two eyes.
	internal const int MostLayers = 2;

	internal static View FindView( ulong handle )
		=> views.TryGetValue( handle, out var found ) ? found : null;

	//The finished picture of a view, and the host memory it can be read into.
	private static bool CreateViewOutput( View view, Extent2D extent )
	{
		var vk = VkDevice.vk;
		var device = VkDevice.device;

		view.OutputFormat = ScreenFormat();
		view.OutputExtent = extent;

		if ( !MakePicture( view.OutputFormat, extent, 1,
			ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit,
			out var image, out var memory, out var imageView, view.Layers ) )
		{
			view.OutputImage = image;
			view.OutputMemory = memory;
			view.OutputView = imageView;
			return false;
		}

		view.OutputImage = image;
		view.OutputMemory = memory;
		view.OutputView = imageView;

		//A texture is one picture; a view of both eyes is copied out, not sampled.
		if ( view.Layers == 1 ) view.OutputHandle = VkTextures.Adopt( imageView, VkDevice.clampSampler );

		//And its backdrop, the same shape. A view that cannot have one still draws; a panel that wants it draws without it.
		var backdropLevels = BackdropLevelsFor( extent );

		if ( view.Layers == 1 && MakePicture( view.OutputFormat, extent, backdropLevels,
			ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit,
			out var backdrop, out var backdropMemory, out var backdropView ) )
		{
			view.BackdropLevels = backdropLevels;
			view.BackdropImage = backdrop;
			view.BackdropMemory = backdropMemory;
			view.BackdropView = backdropView;
			view.BackdropHandle = VkTextures.Adopt( backdropView );
		}

		var bufferInfo = new BufferCreateInfo
		{
			SType = StructureType.BufferCreateInfo,
			Size = (ulong)extent.Width * extent.Height * 4 * view.Layers,
			Usage = BufferUsageFlags.TransferDstBit,
			SharingMode = SharingMode.Exclusive,
		};

		VkBuffer buffer;

		//A view that cannot be read is still a view that draws.
		if ( vk.CreateBuffer( device, &bufferInfo, null, &buffer ) != Result.Success ) return true;

		view.ReadBuffer = buffer;

		MemoryRequirements requirements;
		vk.GetBufferMemoryRequirements( device, buffer, &requirements );

		//Must be host-cached: coherent-only memory is write-combined and reading it back is very slow
		var typeIndex = VkDevice.FindMemoryType( requirements.MemoryTypeBits,
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit |
			MemoryPropertyFlags.HostCachedBit );

		if ( typeIndex == uint.MaxValue )
		{
			typeIndex = VkDevice.FindMemoryType( requirements.MemoryTypeBits,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit );
		}

		var allocate = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = typeIndex,
		};

		DeviceMemory readMemory = default;

		if ( typeIndex != uint.MaxValue &&
			vk.AllocateMemory( device, &allocate, null, &readMemory ) == Result.Success )
		{
			view.ReadMemory = readMemory;

			if ( vk.BindBufferMemory( device, buffer, readMemory, 0 ) == Result.Success )
			{
				void* mapped = null;
				vk.MapMemory( device, readMemory, 0, bufferInfo.Size, 0, &mapped );
				view.ReadMapped = (byte*)mapped;
			}
		}

		return true;
	}

	private static void DestroyViewOutput( View view )
	{
		var vk = VkDevice.vk;
		var device = VkDevice.device;

		if ( view.OutputHandle != 0 ) VkTextures.Release( view.OutputHandle );
		if ( view.OutputSlot != 0 ) VkDevice.RetireBindless( view.OutputSlot );
		view.OutputSlot = 0;
		VkDevice.DestroyView( view.OutputView );
		if ( view.OutputImage.Handle != 0 ) vk.DestroyImage( device, view.OutputImage, null );
		VkDevice.UncountTarget( view.OutputMemory );
		if ( view.OutputMemory.Handle != 0 ) vk.FreeMemory( device, view.OutputMemory, null );

		if ( view.BackdropHandle != 0 ) VkTextures.Release( view.BackdropHandle );
		VkDevice.DestroyView( view.BackdropView );
		if ( view.BackdropImage.Handle != 0 ) vk.DestroyImage( device, view.BackdropImage, null );
		VkDevice.UncountTarget( view.BackdropMemory );
		if ( view.BackdropMemory.Handle != 0 ) vk.FreeMemory( device, view.BackdropMemory, null );

		view.BackdropHandle = 0;
		view.BackdropLevels = 0;
		view.BackdropView = default;
		view.BackdropImage = default;
		view.BackdropMemory = default;
		view.BackdropTaken = false;

		if ( view.ReadBuffer.Handle != 0 ) vk.DestroyBuffer( device, view.ReadBuffer, null );
		if ( view.ReadMemory.Handle != 0 ) vk.FreeMemory( device, view.ReadMemory, null );

		view.OutputHandle = 0;
		view.OutputFormat = Format.Undefined;
		view.OutputView = default;
		view.OutputImage = default;
		view.OutputMemory = default;
		view.OutputLayout = ImageLayout.Undefined;
		view.ReadBuffer = default;
		view.ReadMemory = default;
		view.ReadMapped = null;
		view.ReadPending = false;
	}

	private static void DestroyEveryView()
	{
		foreach ( var pair in views )
		{
			foreach ( var one in pair.Value.Frames ) VkFrame.UnmakeFrame( one );
			DestroyViewOutput( pair.Value );
			DestroyPictures( pair.Value.Pics );
		}

		views.Clear();
		currentPictures = null;
		currentView = null;
		activeFrame = null;
	}

	//layers above one is a view of a headset's eyes drawn in one pass: the portable frame only, whose view is a depth and
	//a finished picture and nothing between - the full frame's pictures, and everything that reads them, are one eye's.
	internal static ulong ViewCreate( int width, int height, int layers = 1 )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready )
			{
				VkDevice.SetText( "there is no device to make a view on" );
				return 0;
			}

			if ( width <= 0 || height <= 0 || layers < 1 || layers > MostLayers ) return 0;

			if ( layers > 1 && ( !VkDevice.multiview || VkDevice.Tier != RenderTier.Portable ) )
			{
				VkDevice.SetText( "a view of both eyes at once needs multiview and the portable frame" );
				return 0;
			}

			if ( (uint)width > VkDevice.maxImageSize || (uint)height > VkDevice.maxImageSize )
			{
				VkDevice.SetText( "that view is bigger than a picture may be" );
				return 0;
			}

			var made = new View();
			made.Pics.Layers = (uint)layers;

			var extent = new Extent2D( (uint)width, (uint)height );

			if ( !CreatePictures( made.Pics, extent, 1.0f ) || !CreateViewOutput( made, extent ) ||
				!VkFrame.MakeFrame( made.Frames[0], false, 0 ) ||
				!VkFrame.MakeFrame( made.Frames[1], false, 1 ) )
			{
				foreach ( var one in made.Frames ) VkFrame.UnmakeFrame( one );

				DestroyViewOutput( made );
				DestroyPictures( made.Pics );
				return 0;
			}

			var handle = nextHandle++;
			views[handle] = made;

			return handle;
		}
	}

	internal static void ViewDestroy( ulong handle )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null ) return;

			//A frame sent off without waiting is settled first, so the fence and the ticket are closed out properly rather than torn down mid-flight.
			CatchUpView( view, all: true );

			//Whatever is still running may be drawing into it, or reading it.
			if ( VkDevice.device.Handle != 0 )
			{
				VkSubmitter.DeviceWaitIdle();
				VkDevice.AllTicketsDone();
			}

			if ( ReferenceEquals( currentPictures, view.Pics ) ) currentPictures = null;
			if ( ReferenceEquals( currentView, view ) ) currentView = null;

			foreach ( var one in view.Frames ) VkFrame.UnmakeFrame( one );
			DestroyViewOutput( view );
			DestroyPictures( view.Pics );
			views.Remove( handle );
		}
	}

	//Makes a view's pictures again at the size they already are. For a change to how big the effects pictures should be, which is the same
	//work as a resize without the resize. The view's output - the picture somebody else samples - does not change size and is left alone.
	private static bool RemakeViewPictures( View view )
	{
		var extent = view.Pics.Extent;

		if ( extent.Width == 0 || extent.Height == 0 ) return false;

		CatchUpView( view, all: true );

		if ( ReferenceEquals( currentPictures, view.Pics ) ) currentPictures = null;

		//Deferred: a frame drawn against them may still be running, and the graveyard knows when it is not.
		DestroyPictures( view.Pics, true );

		if ( CreatePictures( view.Pics, extent, view.RenderScale ) ) return true;

		DestroyPictures( view.Pics );
		view.Pics.Extent = default;

		return false;
	}

	internal static int ViewScale( ulong handle, float scale )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null ) return 0;

			//A quarter is as far down as this goes, for the same reason a window will not go further: past
			//that the world is fewer pixels than the interface drawn over it by a factor no upscaler hides.
			if ( scale > 1.0f ) scale = 1.0f;
			if ( scale < 0.25f ) scale = 0.25f;

			//The shape of the world has to reach the output's depth buffer, and a device that cannot blit its depth format has no way to carry it there.
			if ( scale < 1.0f && !VkDevice.depthBlit )
			{
				VkDevice.SetText( "this device cannot blit depth, so the world cannot be drawn smaller than the view" );
				return 0;
			}

			//Asking for what it already is costs nothing, which is what lets the caller ask every frame rather than remembering.
			var difference = view.RenderScale > scale ? view.RenderScale - scale : scale - view.RenderScale;

			if ( difference < 0.001f ) return 1;

			view.RenderScale = scale;

			return RemakeViewPictures( view ) ? 1 : 0;
		}
	}

	internal static int ViewResize( ulong handle, int width, int height )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null || width <= 0 || height <= 0 ) return 0;

			var extent = new Extent2D( (uint)width, (uint)height );

			//Refused before anything is torn down. A size no picture may have used to be found out halfway through: the working pictures were already
			//destroyed and the new ones could not be made, which left the view drawable-looking and empty - and the first draw into it took the driver with it.
			if ( extent.Width > VkDevice.maxImageSize || extent.Height > VkDevice.maxImageSize )
			{
				VkDevice.SetText( "that view is bigger than a picture may be" );
				return 0;
			}

			if ( view.Pics.Extent.Width == extent.Width && view.Pics.Extent.Height == extent.Height ) return 1;

			//Settle any frame still in flight before its pictures go away under it.
			CatchUpView( view, all: true );

			if ( VkDevice.device.Handle != 0 )
			{
				VkSubmitter.DeviceWaitIdle();
				VkDevice.AllTicketsDone();
			}

			if ( ReferenceEquals( currentPictures, view.Pics ) ) currentPictures = null;
			if ( ReferenceEquals( currentView, view ) ) currentView = null;

			DestroyViewOutput( view );
			DestroyPictures( view.Pics );

			if ( CreatePictures( view.Pics, extent, view.RenderScale ) && CreateViewOutput( view, extent ) ) return 1;

			//Half-made is not a state anything may draw into. Everything goes, and the extent with
			//it, so that nothing later mistakes this for a view of the size it was asked for.
			DestroyViewOutput( view );
			DestroyPictures( view.Pics );

			view.Pics.Extent = default;

			return 0;
		}
	}

	internal static int ViewBegin( ulong handle, float r, float g, float b, float a )
	{
		lock ( VkDevice.renderLock )
		{
			//Inside a frame, and only one view at a time. A view is drawn as part of the window's frame rather than as a frame of its own - so
			//it costs no command buffer, no fence and no present, and what it draws is finished by the time the window's own pass composites it.
			if ( !Current.Recording || currentPictures is not null ) return 0;
			if ( Current.TargetPass || Current.MainPass ) return 0;

			var view = FindView( handle );
			if ( view is null ) return 0;

			//A view whose pictures are not there is a view a resize could not remake. Drawing into one is drawing into
			//nothing, which the driver does not survive - so it is refused here rather than found out at the first draw.
			if ( view.Pics.SceneImage.Handle == 0 ||
				view.Pics.Extent.Width == 0 || view.Pics.Extent.Height == 0 )
			{
				VkDevice.SetText( "that view has no pictures to draw into" );
				return 0;
			}

			currentPictures = view.Pics;
			currentView = view;

			VkFrame.ResetPictures( view.Pics );

			//The view is cleared to its own colour rather than the window's - a preview of a model and the
			//scene behind it have no reason to share a background. The window's is put back when the view ends.
			for ( var i = 0; i < 4; ++i ) windowClear[i] = Current.Clear[i];

			Current.Clear[0] = r;
			Current.Clear[1] = g;
			Current.Clear[2] = b;
			Current.Clear[3] = a;

			//The screen's pass belongs to whichever viewport is open, and this one has not drawn its own yet.
			Current.MainPass = false;
			Current.MainBegun = false;
			view.BackdropTaken = false;

			return 1;
		}
	}

	//Ends the view's pass and leaves what it drew where everything after it expects to find it: the world's
	//picture readable, the finished picture readable, and a copy of the finished picture on its way to host memory.
	private static void CloseView( VkFrame.Frame frame )
	{
		//Opened if nothing opened it, so that a view nobody drew into comes out as its clear colour
		//rather than as whatever was in the memory - the same thing the window's frame does at its end.
		VkFrame.EnsureMainPass();

		VkDevice.dialect.EndRendering( frame.Commands );
		Current.MainPass = false;

		VkFrame.MoveTo( frame.Commands, currentPictures.SceneImage, ref currentPictures.SceneLayout,
			ImageLayout.ShaderReadOnlyOptimal );

		if ( currentView is not null )
		{
			var view = currentView;

			//Into host memory as well, for whoever wants the pixels rather than a texture. Always, rather than on
			//request: the copy has to be recorded while the frame is open, and by the time anybody asks it is not.
			if ( view.ReadBuffer.Handle != 0 && view.OutputImage.Handle != 0 )
			{
				VkFrame.MoveTo( frame.Commands, view.OutputImage, ref view.OutputLayout,
					ImageLayout.TransferSrcOptimal );

				//One read buffer per view, and a view drawn inside a window's frame is drawn again in the next one before the first has necessarily finished copying.
				var previous = new BufferMemoryBarrier2
				{
					SType = StructureType.BufferMemoryBarrier2,
					SrcStageMask = PipelineStageFlags2.AllTransferBit,
					SrcAccessMask = AccessFlags2.TransferWriteBit,
					DstStageMask = PipelineStageFlags2.AllTransferBit,
					DstAccessMask = AccessFlags2.TransferWriteBit,
					SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
					DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
					Buffer = view.ReadBuffer,
					Size = Vk.WholeSize,
				};

				var dependency = new DependencyInfo
				{
					SType = StructureType.DependencyInfo,
					BufferMemoryBarrierCount = 1,
					PBufferMemoryBarriers = &previous,
				};

				VkDevice.dialect.Barrier( frame.Commands, &dependency );

				//Every layer, one after another in the buffer.
				var copy = new BufferImageCopy
				{
					ImageSubresource = new ImageSubresourceLayers( ImageAspectFlags.ColorBit, 0, 0, view.Layers ),
					ImageExtent = new Extent3D( view.OutputExtent.Width, view.OutputExtent.Height, 1 ),
				};

				VkDevice.vk.CmdCopyImageToBuffer( frame.Commands, view.OutputImage,
					ImageLayout.TransferSrcOptimal, view.ReadBuffer, 1, &copy );

				view.ReadPending = true;
			}

			for ( var layer = 0u; layer < view.Layers; layer++ )
			{
				if ( view.CopyTargets[layer].Handle == 0 || view.OutputImage.Handle == 0 ) continue;

				CopyOut( frame, view, layer );
				view.CopyTargets[layer] = default;
			}

			if ( view.CopyTexture != 0 && view.OutputImage.Handle != 0 )
			{
				CopyOutTexture( frame, view );
				view.CopyTexture = 0;
			}

			//And readable, for whoever is about to composite it.
			VkFrame.MoveTo( frame.Commands, view.OutputImage, ref view.OutputLayout,
				ImageLayout.ShaderReadOnlyOptimal );

			//And read, where its world was shaded coarser off its last picture - into the rates the next one is drawn with. See VkFrame.Adaptive.
			VkFrame.AdaptiveReadView( frame, view, currentPictures );
		}

		currentPictures = null;
		currentView = null;

		//The screen's pass has not begun - the view's use of it is over and nothing else has started one.
		Current.MainPass = false;
		Current.MainBegun = false;
	}

	//One layer of the view's picture into the image it was told to copy it into - a blit, so the two may
	//differ in size; both are eight bits a channel with the curve, so nothing is converted on the way.
	private static void CopyOut( VkFrame.Frame frame, View view, uint layer )
	{
		var target = view.CopyTargets[layer];

		VkFrame.MoveTo( frame.Commands, view.OutputImage, ref view.OutputLayout, ImageLayout.TransferSrcOptimal );

		//Everything in it is written, so what it held on the way in is not kept.
		VkFrame.Transition( frame.Commands, target, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
			PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.None,
			PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit );

		var blit = new ImageBlit
		{
			SrcSubresource = new ImageSubresourceLayers( ImageAspectFlags.ColorBit, 0, layer, 1 ),
			DstSubresource = new ImageSubresourceLayers( ImageAspectFlags.ColorBit, 0, 0, 1 ),
		};

		blit.SrcOffsets[1] = new Offset3D( (int)view.OutputExtent.Width, (int)view.OutputExtent.Height, 1 );
		blit.DstOffsets[1] = new Offset3D( (int)view.CopyExtent.Width, (int)view.CopyExtent.Height, 1 );

		VkDevice.vk.CmdBlitImage( frame.Commands, view.OutputImage, ImageLayout.TransferSrcOptimal,
			target, ImageLayout.TransferDstOptimal, 1, &blit, Filter.Linear );

		VkFrame.Transition( frame.Commands, target, ImageLayout.TransferDstOptimal, ImageLayout.ColorAttachmentOptimal,
			PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit,
			PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.ColorAttachmentWriteBit );
	}

	//Blits the view's picture into every level of an engine texture, converting format as needed
	private static void CopyOutTexture( VkFrame.Frame frame, View view )
	{
		if ( !VkTextures.ImageOf( view.CopyTexture, out var image, out var width, out var height, out var levels ) ) return;

		VkFrame.MoveTo( frame.Commands, view.OutputImage, ref view.OutputLayout, ImageLayout.TransferSrcOptimal );

		//Every level is written whole, so what it held on the way in is not kept - and whatever last sampled it, a frame before, is behind this on the queue.
		VkFrame.Transition( frame.Commands, image, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
			PipelineStageFlags2.AllCommandsBit, AccessFlags2.None,
			PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit,
			levelCount: levels );

		for ( var level = 0u; level < levels; ++level )
		{
			var blit = new ImageBlit
			{
				SrcSubresource = new ImageSubresourceLayers( ImageAspectFlags.ColorBit, 0, 0, 1 ),
				DstSubresource = new ImageSubresourceLayers( ImageAspectFlags.ColorBit, level, 0, 1 ),
			};

			blit.SrcOffsets[1] = new Offset3D( (int)view.OutputExtent.Width, (int)view.OutputExtent.Height, 1 );
			blit.DstOffsets[1] = new Offset3D( (int)Math.Max( width >> (int)level, 1u ), (int)Math.Max( height >> (int)level, 1u ), 1 );

			VkDevice.vk.CmdBlitImage( frame.Commands, view.OutputImage, ImageLayout.TransferSrcOptimal,
				image, ImageLayout.TransferDstOptimal, 1, &blit, Filter.Linear );
		}

		VkFrame.Transition( frame.Commands, image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
			PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit,
			PipelineStageFlags2.AllCommandsBit, AccessFlags2.ShaderSampledReadBit,
			levelCount: levels );
	}

	//Says which texture this view's next frame is copied into as it closes - see View.CopyTexture.
	internal static int ViewCopyToTexture( ulong handle, ulong texture )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null || texture == 0 ) return 0;

			view.CopyTexture = texture;
			return 1;
		}
	}

	//Says which image a layer of this view's next frame is copied into as it closes - see View.CopyTargets.
	internal static int ViewCopyTo( ulong handle, ulong image, int width, int height, int layer = 0 )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null || image == 0 || width <= 0 || height <= 0 || layer < 0 || layer >= view.Layers ) return 0;

			view.CopyTargets[layer] = new Image( image );
			view.CopyExtent = new Extent2D( (uint)width, (uint)height );
			return 1;
		}
	}

	//How this view's world is foveated from its next frame on: how strongly, and where
	//each layer's middle is - see VkFovea. Nothing where the device does not foveate.
	internal static int ViewFovea( ulong handle, float strength, bool tracked, float u0, float v0, float u1, float v1 )
	{
		lock ( VkDevice.renderLock )
		{
			if ( VkFovea.Kind == VkFovea.Kinds.None ) return 0;

			var view = FindView( handle );
			if ( view is null ) return 0;

			VkFovea.Set( view.Pics, strength, tracked, u0, v0, u1, v1 );
			return 1;
		}
	}

	internal static void ViewEnd()
	{
		lock ( VkDevice.renderLock )
		{
			if ( currentPictures is null ) return;

			CloseView( CurrentFrame );

			//The window's clear colour, which the view borrowed the slot of.
			for ( var i = 0; i < 4; ++i ) Current.Clear[i] = windowClear[i];
		}
	}

	//Waits for the view's in-flight frame (or both with all) before it records, resizes or is destroyed
	private static void CatchUpView( View view, bool all = false )
	{
		if ( all )
		{
			foreach ( var one in view.Frames ) CatchUpFrame( view, one );

			return;
		}

		CatchUpFrame( view, view.Frame );
	}

	private static void CatchUpFrame( View view, VkFrame.Frame frame )
	{
		if ( !frame.Submitted ) return;

		//A wait that returned because the device is gone has proved nothing. Left marked
		//as submitted, so nothing records over a command buffer the GPU may still hold.
		var fence = frame.Finished;
		var waited = VkDevice.vk.WaitForFences( VkDevice.device, 1, &fence, true, ulong.MaxValue );

		if ( waited != Result.Success )
		{
			VkDevice.MarkLostIf( waited );
			return;
		}

		VkDevice.vk.ResetFences( VkDevice.device, 1, &fence );
		frame.Submitted = false;

		VkDevice.TicketDone( frame.Ticket );
		VkFrame.ResolveProfile( frame, view.Profile );

		view.ReadPending = false;
	}

	internal static int ViewFrameBegin( ulong handle, float r, float g, float b, float a )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null || view.Frame.Commands.Handle == 0 ) return 0;

			//Outside anybody's frame, and one at a time all the same. The placeholder - the swapchain that stands in for "no window" - is what
			//a view on its own keeps its pass state in, so that every draw between here and the end behaves exactly as it does inside a window.
			if ( !ReferenceEquals( active, placeholder ) || placeholder.Recording ) return 0;
			if ( currentPictures is not null ) return 0;

			//Covers waiting, picture rebuild and recording start; RINTEN_SWAPCHAIN_TIMING reports slow ones
			var startedNs = timingRebuilds ? NowNs() : 0;

			//Turn about first, then wait for the one being taken: that is the frame from two draws ago, which came back long since. Waiting
			//for both would be waiting for the one just submitted, which is the stall the second frame exists to remove - see View.Frames.
			view.FrameAt = view.FrameAt == 0 ? 1 : 0;

			CatchUpView( view );

			var waitedNs = timingRebuilds ? NowNs() : 0;

			var remadeNs = timingRebuilds ? NowNs() : 0;

			var frame = view.Frame;
			activeFrame = frame;

			//Named before recording so the frame set points at this view's pictures, not the window's - a preview
			//draws before the window's first frame begin, and sampling a still-UNDEFINED image is a GPUVM fault.
			currentPictures = view.Pics;
			currentView = view;

			if ( !VkFrame.BeginRecording( frame ) )
			{
				currentPictures = null;
				currentView = null;
				activeFrame = null;
				VkDevice.SetText( "no room in the view for the camera and lights" );
				return 0;
			}

			VkFrame.ResetPictures( view.Pics );
			VkFrame.WriteFrameSet( frame );

			if ( timingRebuilds )
			{
				var doneNs = NowNs();

				//Only the expensive ones. A viewport begins its frame sixty times a second and saying so each time would bury the one that mattered.
				if ( doneNs - startedNs > 2000000 )
				{
					VkDevice.Say( $"view {view.Pics.Extent.Width}x{view.Pics.Extent.Height} began in " +
						$"{( doneNs - startedNs ) / 1.0e6:F1} ms: waiting for its last frame {( waitedNs - startedNs ) / 1.0e6:F1}, " +
						$"pictures {( remadeNs - waitedNs ) / 1.0e6:F1}, recording {( doneNs - remadeNs ) / 1.0e6:F1}" );
				}
			}

			placeholder.Clear[0] = r;
			placeholder.Clear[1] = g;
			placeholder.Clear[2] = b;
			placeholder.Clear[3] = a;

			placeholder.MainPass = false;
			placeholder.MainBegun = false;
			placeholder.BackdropTaken = false;
			view.BackdropTaken = false;
			placeholder.TargetPass = false;
			placeholder.Target = VkDevice.TargetScreen;
			placeholder.Recording = true;

			return 1;
		}
	}

	//Closing a view's frame up to and including the submit - shared between the end that waits and the end that does not.
	private static int SubmitViewFrame( View view )
	{
		var frame = view.Frame;

		CloseView( frame );

		//The buffers somebody asked to have back - see VkReadbacks.
		VkReadbacks.RecordInto( frame );

		placeholder.Recording = false;
		activeFrame = null;

		//The view's own pair of timestamps, and any zone left open.
		VkFrame.CloseOpenZones( frame );

		if ( frame.Queries.Handle != 0 )
			VkDevice.dialect.Timestamp( frame.Commands, PipelineStageFlags2.AllCommandsBit, frame.Queries, 1 );

		if ( VkDevice.vk.EndCommandBuffer( frame.Commands ) != Result.Success )
		{
			VkDevice.SetText( "the view's frame could not be recorded" );
			VkDevice.RecordingAbandoned();
			return 0;
		}

		frame.CpuEnd = NowNs();

		var commands = new CommandBufferSubmitInfo
		{
			SType = StructureType.CommandBufferSubmitInfo,
			CommandBuffer = frame.Commands,
		};

		//No semaphores either side: nothing was acquired and nothing is presented.
		var submit = new SubmitInfo2
		{
			SType = StructureType.SubmitInfo2,
			CommandBufferInfoCount = 1,
			PCommandBufferInfos = &commands,
		};

		//Every picture uploaded while this frame was being recorded goes to the card first, in one submission.
		//The queue is one, so copies queued here have run by the time the draws sample what they filled.
		VkTextures.FlushTextureUploads();

		var submitted = VkDevice.dialect.Submit( VkDevice.graphicsQueue, &submit, frame.Finished );

		if ( submitted != Result.Success )
		{
			VkDevice.MarkLostIf( submitted );
			VkDevice.SetText( submitted == Result.ErrorDeviceLost
				? "the device was lost submitting a view"
				: "the view's frame could not be submitted" );

			VkDevice.RecordingAbandoned();
			return 0;
		}

		frame.Ticket = VkDevice.RecordingSubmitted();
		frame.Submitted = true;
		frame.ProfilePending = true;

		return 1;
	}

	internal static int ViewFrameEnd( ulong handle )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null || !ReferenceEquals( currentView, view ) || !ReferenceEquals( activeFrame, view.Frame ) ) return 0;

			if ( SubmitViewFrame( view ) == 0 ) return 0;

			//Waited for rather than pipelined. Whoever asked for a view on its own - a thumbnail, a preview being
			//saved - wants the picture, not a promise of one, and there is no next frame here to hide the wait behind.
			CatchUpView( view, all: true );

			return 1;
		}
	}

	internal static int ViewFrameEndAsync( ulong handle )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null || !ReferenceEquals( currentView, view ) || !ReferenceEquals( activeFrame, view.Frame ) ) return 0;

			//Not waited for: queue order makes it finish before the window reads it; fence waited next begin
			return SubmitViewFrame( view );
		}
	}

	internal static int ViewFrameReady( ulong handle )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );

			//No such view is not "busy": say ready and let the ordinary path fail on it, rather than inventing a reason to skip a draw for ever.
			if ( view is null ) return 1;
			if ( !view.Frame.Submitted ) return 1;
			if ( VkDevice.Lost ) return 1;

			return VkDevice.vk.GetFenceStatus( VkDevice.device, view.Frame.Finished ) == Result.Success ? 1 : 0;
		}
	}

	internal static ulong ViewTexture( ulong handle )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			return view?.OutputHandle ?? 0;
		}
	}

	internal static int ViewRead( ulong handle, byte* outPixels, int capacity, int layer = 0 )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null || outPixels is null || view.ReadMapped is null || layer < 0 || layer >= view.Layers ) return 0;

			var bytes = (int)view.OutputExtent.Width * (int)view.OutputExtent.Height * 4;

			if ( capacity < bytes ) return 0;

			//A frame ended without waiting has to be waited for now - the pixels being asked for are the ones it is still copying.
			CatchUpView( view, all: true );

			//The copy was recorded in a frame that may still be running. Waiting is blunt and right for what this is
			//for - a thumbnail is made once, and a preview that wants no stall reads the frame before this one instead.
			if ( view.ReadPending )
			{
				if ( VkDevice.device.Handle != 0 ) VkSubmitter.DeviceWaitIdle();
				view.ReadPending = false;
			}

			System.Buffer.MemoryCopy( view.ReadMapped + (long)bytes * layer, outPixels, bytes, bytes );

			//Red first, whatever the image had. Everything on this side counts pixels as RGBA - a bitmap,
			//a file, a widget - and which way round a swapchain wants them is the swapchain's business.
			if ( view.OutputFormat == Format.B8G8R8A8Srgb || view.OutputFormat == Format.B8G8R8A8Unorm )
			{
				for ( var at = 0; at < bytes; at += 4 )
				{
					var blue = outPixels[at];

					outPixels[at] = outPixels[at + 2];
					outPixels[at + 2] = blue;
				}
			}

			return bytes;
		}
	}

	internal static int ViewReadLayered( ulong handle, byte* outPixels, int capacity )
	{
		lock ( VkDevice.renderLock )
		{
			var view = FindView( handle );
			if ( view is null || outPixels is null || view.ReadMapped is null ) return 0;

			var bytes = (int)view.OutputExtent.Width * (int)view.OutputExtent.Height * 4;

			if ( capacity < bytes ) return 0;

			CatchUpView( view, all: true );

			if ( view.ReadPending )
			{
				if ( VkDevice.device.Handle != 0 ) VkSubmitter.DeviceWaitIdle();
				view.ReadPending = false;
			}

			//Blue first is what a layered window wants, and what the image already is on this platform - so
			//the usual read's swizzle is the thing being skipped here rather than an extra one being done.
			var swap = !( view.OutputFormat == Format.B8G8R8A8Srgb || view.OutputFormat == Format.B8G8R8A8Unorm );

			var from = view.ReadMapped;

			for ( var at = 0; at < bytes; at += 4 )
			{
				uint alpha = from[at + 3];

				//Wholly there and wholly absent are the two cases nearly every pixel of an interface falls into, and neither needs the arithmetic.
				if ( alpha == 255 )
				{
					outPixels[at + 0] = swap ? from[at + 2] : from[at + 0];
					outPixels[at + 1] = from[at + 1];
					outPixels[at + 2] = swap ? from[at + 0] : from[at + 2];
					outPixels[at + 3] = 255;
					continue;
				}

				if ( alpha == 0 )
				{
					outPixels[at + 0] = 0;
					outPixels[at + 1] = 0;
					outPixels[at + 2] = 0;
					outPixels[at + 3] = 0;
					continue;
				}

				uint blue = swap ? from[at + 2] : from[at + 0];
				uint green = from[at + 1];
				uint red = swap ? from[at + 0] : from[at + 2];

				//Rounded rather than truncated: an edge pixel that loses a level on every channel is an outline half a shade dark all the way round.
				outPixels[at + 0] = (byte)( ( blue * alpha + 127 ) / 255 );
				outPixels[at + 1] = (byte)( ( green * alpha + 127 ) / 255 );
				outPixels[at + 2] = (byte)( ( red * alpha + 127 ) / 255 );
				outPixels[at + 3] = (byte)alpha;
			}

			return bytes;
		}
	}
}
