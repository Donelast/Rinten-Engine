using Silk.NET.OpenXR;
using XrResult = Silk.NET.OpenXR.Result;

namespace Rinten;

unsafe partial class CoreXr
{
	//One eye's swapchain, the runtime's images in it, and the view and drawer the eye is drawn with.
	private sealed class Eye
	{
		public ulong Swapchain;
		public ulong[] Images = [];

		/// <summary>The view and the drawer, which are the same for any headset - see CoreEyes.</summary>
		public readonly CoreEyes.Eye Draws = new();
	}

	private static readonly Eye[] eyes = [new(), new()];

	//Both eyes in one view of two layers, drawn in one pass - where the device can; see
	//CoreRenderDevice.Multiview. A view and a drawer of its own; the swapchains are still each eye's.
	private static readonly Eye both = new();

	//The formats worth having, best first: eight bits a channel with the curve, which is what the drawer finishes in.
	private static readonly long[] WantedFormats =
	[
		43, //VK_FORMAT_R8G8B8A8_SRGB
		50, //VK_FORMAT_B8G8R8A8_SRGB
	];

	private static bool MakeSwapchains()
	{
		uint count = 0;
		xr.EnumerateSwapchainFormats( session, 0, &count, null );
		if ( count == 0 ) return false;

		var formats = new long[count];

		fixed ( long* into = formats )
			xr.EnumerateSwapchainFormats( session, count, &count, into );

		//The runtime lists its own preference first; ours wins where it is there.
		var format = formats[0];

		foreach ( var wanted in WantedFormats.Reverse() )
			if ( formats.Contains( wanted ) ) format = wanted;

		if ( !WantedFormats.Contains( format ) )
			log.Warning( $"{RuntimeName} offers no sRGB swapchain - the headset's picture will be off in its gamma (format {format})" );

		foreach ( var eye in eyes )
		{
			var info = new SwapchainCreateInfo
			{
				Type = StructureType.SwapchainCreateInfo,
				UsageFlags = SwapchainUsageFlags.ColorAttachmentBit | SwapchainUsageFlags.TransferDstBit,
				Format = format,
				SampleCount = 1,
				Width = (uint)EyeSize.Width,
				Height = (uint)EyeSize.Height,
				FaceCount = 1,
				ArraySize = 1,
				MipCount = 1,
			};

			ulong made;
			var result = xr.CreateSwapchain( session, &info, &made );

			if ( result != XrResult.Success )
			{
				log.Warning( $"no swapchain ({result})" );
				return false;
			}

			eye.Swapchain = made;

			uint images = 0;
			xr.EnumerateSwapchainImages( made, 0, &images, null );

			var list = new SwapchainImageVulkan2KHR[images];

			for ( var i = 0; i < images; i++ ) list[i].Type = VulkanImageType;

			fixed ( SwapchainImageVulkan2KHR* into = list )
				xr.EnumerateSwapchainImages( made, images, &images, into );

			eye.Images = list.Select( x => x.Image ).ToArray();
		}

		return true;
	}

	private static void DestroySwapchains()
	{
		foreach ( var eye in eyes )
		{
			if ( eye.Swapchain != 0 ) xr.DestroySwapchain( eye.Swapchain );

			eye.Swapchain = 0;
			eye.Images = [];

			CoreEyes.Let( eye.Draws );
		}

		CoreEyes.Let( both.Draws );
	}

	//Draws each eye from the scene camera into the runtime's image; both in one pass where possible
	private static bool DrawEyes( Scene scene, CompositionLayerProjectionView* layerViews )
	{
		if ( !scene.IsValid() ) return false;

		var camera = scene.Camera;
		if ( !camera.IsValid() ) return false;

		if ( !CoreSceneDrawer.EnsureResources() ) return false;

		framesShown++;

		//RINTEN_XR_SHOT=<folder> writes what each eye was given, once, a few seconds in: the only way to see what a headset is shown without one on.
		var shooting = shotFolder is not null && framesShown == ShotFrame;

		var anchor = Rinten.VR.VRSystem.AnchorFor( camera );
		var scale = 1.0f / MathF.Max( Rinten.VR.VRSystem.WorldScale, 0.0001f );
		var (width, height) = EyeSize;

		var fields = stackalloc XrEyeView[2];

		//Where the eyes look, for the map and the cull - see CoreFovea.
		var fovea = CoreFovea.ForHeadset( anchor );

		for ( var i = 0; i < 2; i++ )
		{
			var fov = located[i].Fov;

			fields[i] = CoreEyes.Field( ToTransform( located[i].Pose ), anchor, scale,
				fov.AngleLeft, fov.AngleRight, fov.AngleUp, fov.AngleDown );
		}

		var drawn = CoreRenderDevice.Multiview
			? DrawBoth( scene, camera, fields, width, height, shooting, fovea )
			: DrawEach( scene, camera, fields, width, height, shooting, fovea );

		if ( !drawn ) return false;

		for ( var i = 0; i < 2; i++ )
		{
			layerViews[i] = new CompositionLayerProjectionView
			{
				Type = StructureType.CompositionLayerProjectionView,
				Pose = located[i].Pose,
				Fov = located[i].Fov,
				SubImage = new SwapchainSubImage
				{
					Swapchain = new Swapchain( eyes[i].Swapchain ),
					ImageRect = new Rect2Di( new Offset2Di( 0, 0 ), new Extent2Di( width, height ) ),
					ImageArrayIndex = 0,
				},
			};
		}

		return true;
	}

	//An eye at a time, each into its own view.
	private static bool DrawEach( Scene scene, CameraComponent camera, XrEyeView* fields, int width, int height, bool shooting,
		CoreFovea.Setting fovea )
	{
		for ( var i = 0; i < 2; i++ )
		{
			var eye = eyes[i];

			if ( !CoreEyes.EnsureView( eye.Draws, width, height ) ) return false;
			if ( !Acquire( eye, out var index ) ) return false;

			var drawn = false;

			try
			{
				eye.Draws.Drawer.Eye = fields[i];
				eye.Draws.Drawer.Fovea = fovea;
				drawn = CoreEyes.Draw( eye.Draws, scene, camera, width, height, shooting, [eye.Images[index]] );
			}
			finally
			{
				Release( eye );
			}

			if ( !drawn ) return false;
			if ( shooting ) Shoot( eye.Draws, 0, i, width, height );
		}

		return true;
	}

	//Both eyes in one pass, into a view a layer each, each layer copied into its eye's image - the scene gathered,
	//culled and sorted once rather than twice, and every draw recorded once for two. See CoreSceneDrawer.SecondEye.
	private static bool DrawBoth( Scene scene, CameraComponent camera, XrEyeView* fields, int width, int height, bool shooting,
		CoreFovea.Setting fovea )
	{
		if ( !CoreEyes.EnsureView( both.Draws, width, height, 2 ) ) return false;

		if ( !Acquire( eyes[0], out var first ) ) return false;

		if ( !Acquire( eyes[1], out var second ) )
		{
			Release( eyes[0] );
			return false;
		}

		bool drawn;

		try
		{
			both.Draws.Drawer.Eye = fields[0];
			both.Draws.Drawer.SecondEye = fields[1];
			both.Draws.Drawer.Fovea = fovea;

			drawn = CoreEyes.Draw( both.Draws, scene, camera, width, height, shooting,
				[eyes[0].Images[first], eyes[1].Images[second]] );
		}
		finally
		{
			Release( eyes[0] );
			Release( eyes[1] );
		}

		if ( drawn && shooting )
		{
			Shoot( both.Draws, 0, 0, width, height );
			Shoot( both.Draws, 1, 1, width, height );
		}

		return drawn;
	}

	//The eye's next image, waited for until the runtime is done with it.
	private static bool Acquire( Eye eye, out uint index )
	{
		var acquire = new SwapchainImageAcquireInfo { Type = StructureType.SwapchainImageAcquireInfo };
		uint got;

		index = 0;

		//Not beside a window's present - see FrameStart.
		CoreRenderDevice.FramesHandedOver();

		if ( xr.AcquireSwapchainImage( eye.Swapchain, &acquire, &got ) != XrResult.Success ) return false;

		index = got;

		var wait = new SwapchainImageWaitInfo { Type = StructureType.SwapchainImageWaitInfo, Timeout = long.MaxValue };
		xr.WaitSwapchainImage( eye.Swapchain, &wait );

		return true;
	}

	//The image back to the runtime. After the submit: the runtime reads the image once the queue has run what was submitted before this - see
	//the OpenXR Vulkan binding. And always, drawn or not: an image acquired and never released is a swapchain that hands out nothing more.
	private static void Release( Eye eye )
	{
		var release = new SwapchainImageReleaseInfo { Type = StructureType.SwapchainImageReleaseInfo };

		//Not beside a window's present - see FrameStart.
		CoreRenderDevice.FramesHandedOver();

		xr.ReleaseSwapchainImage( eye.Swapchain, &release );
	}

	//How many frames have been handed to the headset - said when the session closes.
	private static long framesShown;

	private static readonly string shotFolder = Environment.GetEnvironmentVariable( "RINTEN_XR_SHOT" );
	private const int ShotFrame = 240;

	private static void Shoot( CoreEyes.Eye eye, int layer, int index, int width, int height )
	{
		var pixels = new byte[width * height * 4];
		if ( !CoreRenderDevice.ReadView( eye.View, pixels, layer ) ) return;

		var path = System.IO.Path.Combine( shotFolder, index == 0 ? "left.ppm" : "right.ppm" );
		var header = System.Text.Encoding.ASCII.GetBytes( $"P6\n{width} {height}\n255\n" );
		var rgb = new byte[width * height * 3];

		for ( var p = 0; p < width * height; p++ )
		{
			rgb[p * 3] = pixels[p * 4];
			rgb[p * 3 + 1] = pixels[p * 4 + 1];
			rgb[p * 3 + 2] = pixels[p * 4 + 2];
		}

		System.IO.Directory.CreateDirectory( shotFolder );
		System.IO.File.WriteAllBytes( path, [.. header, .. rgb] );
		log.Info( $"the {( index == 0 ? "left" : "right" )} eye written to {path}" );
	}
}
