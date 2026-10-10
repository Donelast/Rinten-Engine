using Silk.NET.OpenXR;
using XrResult = Silk.NET.OpenXR.Result;

namespace Rinten;

unsafe partial class CoreXr
{
	//What the runtime said of the frame being made: when it will be shown, and whether to draw it at all.
	private static FrameState frameState;

	//Between xrBeginFrame and xrEndFrame.
	private static bool frameOpen;

	//Where each eye will be, and how wide it sees, when this frame is shown - in the play area's space.
	private static readonly View[] located = new View[2];
	private static bool eyesLocated;

	//Where the headset will be, in the play area's space.
	internal static Transform Head { get; private set; } = Transform.Zero;
	internal static bool HeadTracked { get; private set; }

	//How far apart the eyes are, in metres, as the headset last said.
	internal static float EyeDistance { get; private set; }

	//Whether this frame is one the headset wants drawn.
	internal static bool Drawing => frameOpen && frameState.ShouldRender != 0 && eyesLocated;

	//Frame start: polls events, waits on xrWaitFrame for headset pacing, locates poses before the tick
	internal static void FrameStart()
	{
		if ( instance == 0 ) return;

		Poll();

		if ( session == 0 || !running || frameOpen ) return;

		var wait = new FrameWaitInfo { Type = StructureType.FrameWaitInfo };
		var state = new FrameState { Type = StructureType.FrameState };

		if ( xr.WaitFrame( session, &wait, &state ) != XrResult.Success ) return;

		frameState = state;

		if ( state.PredictedDisplayPeriod > 0 )
			RefreshRate = 1.0e9f / state.PredictedDisplayPeriod;

		//The runtime may use the device's queue in here, and the OpenXR Vulkan binding wants that queue to have one user at a time - so not beside
		//a window's present still on its way. See VkSubmitter; the same before the eye images are taken and given back, and before the frame ends.
		CoreRenderDevice.FramesHandedOver();

		var begin = new FrameBeginInfo { Type = StructureType.FrameBeginInfo };
		var begun = xr.BeginFrame( session, &begin );

		//Discarded is what a runtime says when the last frame never ended - it is still a frame begun.
		if ( begun != XrResult.Success && begun != XrResult.FrameDiscarded ) return;

		frameOpen = true;

		LocateEyes();
		LocateHead();

		Input.Sync( state.PredictedDisplayTime );
		Gaze.Locate( state.PredictedDisplayTime, Input.Synced );
		Hands.Locate( state.PredictedDisplayTime );
	}

	private static void LocateEyes()
	{
		eyesLocated = false;

		var info = new ViewLocateInfo
		{
			Type = StructureType.ViewLocateInfo,
			ViewConfigurationType = ViewConfigurationType.PrimaryStereo,
			DisplayTime = frameState.PredictedDisplayTime,
			Space = new Space( stageSpace ),
		};

		var state = new ViewState { Type = StructureType.ViewState };
		var views = stackalloc View[2];
		views[0].Type = views[1].Type = StructureType.View;

		uint count = 0;
		if ( xr.LocateViews( session, &info, &state, 2, &count, views ) != XrResult.Success || count != 2 ) return;

		//A headset that has lost where it is still says which way it faces; a frame with neither is not drawn from anywhere.
		if ( ( state.ViewStateFlags & ViewStateFlags.OrientationValidBit ) == 0 ) return;

		located[0] = views[0];
		located[1] = views[1];
		eyesLocated = true;

		var between = ToTransform( views[0].Pose ).Position - ToTransform( views[1].Pose ).Position;
		EyeDistance = between.Length;
	}

	private static void LocateHead()
	{
		var location = new SpaceLocation { Type = StructureType.SpaceLocation };

		if ( xr.LocateSpace( viewSpace, stageSpace, frameState.PredictedDisplayTime, &location ) != XrResult.Success ) return;

		HeadTracked = ( location.LocationFlags & SpaceLocationFlags.PositionValidBit ) != 0;

		if ( ( location.LocationFlags & SpaceLocationFlags.OrientationValidBit ) != 0 )
			Head = ToTransform( location.Pose );
	}

	//The end of the frame: each eye drawn, where the headset wants them, and the frame handed back.
	//A frame the runtime did not want drawn is handed back empty - every begun frame has to end.
	internal static void FrameEnd( Scene scene )
	{
		if ( !frameOpen ) return;

		frameOpen = false;

		var views = stackalloc CompositionLayerProjectionView[2];
		var layer = new CompositionLayerProjection
		{
			Type = StructureType.CompositionLayerProjection,
			Space = new Space( stageSpace ),
			ViewCount = 2,
			Views = views,
		};

		//Whatever the drawing does, the frame is handed back - a begun frame that never ends stops the headset asking for the next.
		var drawn = false;

		try
		{
			drawn = frameState.ShouldRender != 0 && eyesLocated && DrawEyes( scene, views );
		}
		catch ( Exception e )
		{
			if ( !saidDraw )
			{
				saidDraw = true;
				log.Error( e, "an eye would not draw - the headset is shown nothing this frame" );
			}
		}

		var layers = stackalloc CompositionLayerBaseHeader*[1];
		layers[0] = (CompositionLayerBaseHeader*)&layer;

		var end = new FrameEndInfo
		{
			Type = StructureType.FrameEndInfo,
			DisplayTime = frameState.PredictedDisplayTime,
			EnvironmentBlendMode = blendMode,
			LayerCount = drawn ? 1u : 0u,
			Layers = layers,
		};

		CoreRenderDevice.FramesHandedOver();

		var ended = xr.EndFrame( session, &end );

		if ( ended != XrResult.Success && !saidEnd )
		{
			saidEnd = true;
			log.Warning( $"the headset refused a frame ({ended})" );
		}
	}

	private static bool saidEnd;
	private static bool saidDraw;
}
