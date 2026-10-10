using System.IO;
using System.Runtime.InteropServices;
using Silk.NET.OpenXR;

namespace Rinten;

//Loads the OpenXR loader (local first, then system); functions via xrGetInstanceProcAddr
internal sealed unsafe class XrLoader
{
	private readonly nint library;

	private readonly delegate* unmanaged[Cdecl]<ulong, byte*, nint*, Result> getProcAddr;

	private XrLoader( nint library )
	{
		this.library = library;
		getProcAddr = (delegate* unmanaged[Cdecl]<ulong, byte*, nint*, Result>)NativeLibrary.GetExport( library, "xrGetInstanceProcAddr" );
	}

	private static IEnumerable<string> LibraryNames()
	{
		if ( OperatingSystem.IsWindows() )
		{
			yield return "openxr_loader.dll";
			yield break;
		}

		if ( OperatingSystem.IsAndroid() )
		{
			yield return "libopenxr_loader.so";
			yield break;
		}

		yield return "libopenxr_loader.so.1";
		yield return "libopenxr_loader.so";
	}

	/// <summary>The loader, or null with a reason where there is none.</summary>
	public static XrLoader Open( out string failure )
	{
		failure = null;

		foreach ( var name in LibraryNames() )
		{
			var beside = Path.Combine( NetCore.NativeDllPath, name );

			if ( File.Exists( beside ) && NativeLibrary.TryLoad( beside, out var local ) && HasRoot( local ) )
				return new XrLoader( local );

			if ( NativeLibrary.TryLoad( name, out var system ) && HasRoot( system ) )
				return new XrLoader( system );
		}

		failure = "no OpenXR loader beside the engine or on the system";
		return null;
	}

	private static bool HasRoot( nint library )
		=> NativeLibrary.TryGetExport( library, "xrGetInstanceProcAddr", out _ );

	/// <summary>A function's address, through an instance where there is one - nought where it has none.</summary>
	public nint Proc( ulong instance, string name )
	{
		var bytes = System.Text.Encoding.ASCII.GetBytes( name + "\0" );
		nint address = 0;

		fixed ( byte* chars = bytes )
		{
			if ( getProcAddr( instance, chars, &address ) != Result.Success ) return 0;
		}

		return address;
	}

	/// <summary>The loader's own export by name, for the one call made before any instance on Android.</summary>
	public nint Export( string name )
		=> NativeLibrary.TryGetExport( library, name, out var address ) ? address : 0;
}

//Every OpenXR function this engine calls, fetched through the loader once an instance exists. Handles are passed
//as their numbers: Silk's handle types are a number each, and a number is what the calling convention wants.
internal sealed unsafe class XrFunctions
{
	public delegate* unmanaged[Cdecl]<ulong, Result> DestroyInstance;
	public delegate* unmanaged[Cdecl]<ulong, InstanceProperties*, Result> GetInstanceProperties;
	public delegate* unmanaged[Cdecl]<ulong, SystemGetInfo*, ulong*, Result> GetSystem;
	public delegate* unmanaged[Cdecl]<ulong, ulong, SystemProperties*, Result> GetSystemProperties;
	public delegate* unmanaged[Cdecl]<ulong, ulong, ViewConfigurationType, uint, uint*, ViewConfigurationView*, Result> EnumerateViewConfigurationViews;
	public delegate* unmanaged[Cdecl]<ulong, ulong, ViewConfigurationType, uint, uint*, EnvironmentBlendMode*, Result> EnumerateEnvironmentBlendModes;
	public delegate* unmanaged[Cdecl]<ulong, EventDataBuffer*, Result> PollEvent;

	public delegate* unmanaged[Cdecl]<ulong, ulong, GraphicsRequirementsVulkan2KHR*, Result> GetVulkanGraphicsRequirements2;
	public delegate* unmanaged[Cdecl]<ulong, VulkanInstanceCreateInfoKHR*, nint*, int*, Result> CreateVulkanInstance;
	public delegate* unmanaged[Cdecl]<ulong, VulkanDeviceCreateInfoKHR*, nint*, int*, Result> CreateVulkanDevice;
	public delegate* unmanaged[Cdecl]<ulong, VulkanGraphicsDeviceGetInfoKHR*, nint*, Result> GetVulkanGraphicsDevice2;

	public delegate* unmanaged[Cdecl]<ulong, SessionCreateInfo*, ulong*, Result> CreateSession;
	public delegate* unmanaged[Cdecl]<ulong, Result> DestroySession;
	public delegate* unmanaged[Cdecl]<ulong, SessionBeginInfo*, Result> BeginSession;
	public delegate* unmanaged[Cdecl]<ulong, Result> EndSession;
	public delegate* unmanaged[Cdecl]<ulong, Result> RequestExitSession;

	public delegate* unmanaged[Cdecl]<ulong, ReferenceSpaceCreateInfo*, ulong*, Result> CreateReferenceSpace;
	public delegate* unmanaged[Cdecl]<ulong, uint, uint*, ReferenceSpaceType*, Result> EnumerateReferenceSpaces;
	public delegate* unmanaged[Cdecl]<ulong, Result> DestroySpace;
	public delegate* unmanaged[Cdecl]<ulong, ulong, long, SpaceLocation*, Result> LocateSpace;

	public delegate* unmanaged[Cdecl]<ulong, uint, uint*, long*, Result> EnumerateSwapchainFormats;
	public delegate* unmanaged[Cdecl]<ulong, SwapchainCreateInfo*, ulong*, Result> CreateSwapchain;
	public delegate* unmanaged[Cdecl]<ulong, Result> DestroySwapchain;
	public delegate* unmanaged[Cdecl]<ulong, uint, uint*, SwapchainImageVulkan2KHR*, Result> EnumerateSwapchainImages;
	public delegate* unmanaged[Cdecl]<ulong, SwapchainImageAcquireInfo*, uint*, Result> AcquireSwapchainImage;
	public delegate* unmanaged[Cdecl]<ulong, SwapchainImageWaitInfo*, Result> WaitSwapchainImage;
	public delegate* unmanaged[Cdecl]<ulong, SwapchainImageReleaseInfo*, Result> ReleaseSwapchainImage;

	public delegate* unmanaged[Cdecl]<ulong, FrameWaitInfo*, FrameState*, Result> WaitFrame;
	public delegate* unmanaged[Cdecl]<ulong, FrameBeginInfo*, Result> BeginFrame;
	public delegate* unmanaged[Cdecl]<ulong, FrameEndInfo*, Result> EndFrame;
	public delegate* unmanaged[Cdecl]<ulong, ViewLocateInfo*, ViewState*, uint, uint*, View*, Result> LocateViews;

	public delegate* unmanaged[Cdecl]<ulong, byte*, ulong*, Result> StringToPath;
	public delegate* unmanaged[Cdecl]<ulong, ulong, uint, uint*, byte*, Result> PathToString;
	public delegate* unmanaged[Cdecl]<ulong, ActionSetCreateInfo*, ulong*, Result> CreateActionSet;
	public delegate* unmanaged[Cdecl]<ulong, ActionCreateInfo*, ulong*, Result> CreateAction;
	public delegate* unmanaged[Cdecl]<ulong, InteractionProfileSuggestedBinding*, Result> SuggestInteractionProfileBindings;
	public delegate* unmanaged[Cdecl]<ulong, SessionActionSetsAttachInfo*, Result> AttachSessionActionSets;
	public delegate* unmanaged[Cdecl]<ulong, ActionsSyncInfo*, Result> SyncActions;
	public delegate* unmanaged[Cdecl]<ulong, ActionStateGetInfo*, ActionStateBoolean*, Result> GetActionStateBoolean;
	public delegate* unmanaged[Cdecl]<ulong, ActionStateGetInfo*, ActionStateFloat*, Result> GetActionStateFloat;
	public delegate* unmanaged[Cdecl]<ulong, ActionStateGetInfo*, ActionStateVector2f*, Result> GetActionStateVector2f;
	public delegate* unmanaged[Cdecl]<ulong, ActionStateGetInfo*, ActionStatePose*, Result> GetActionStatePose;
	public delegate* unmanaged[Cdecl]<ulong, ActionSpaceCreateInfo*, ulong*, Result> CreateActionSpace;
	public delegate* unmanaged[Cdecl]<ulong, HapticActionInfo*, HapticVibration*, Result> ApplyHapticFeedback;
	public delegate* unmanaged[Cdecl]<ulong, ulong, InteractionProfileState*, Result> GetCurrentInteractionProfile;

	/// <summary>Every one of them through the instance; false naming the first the runtime did not hand over.</summary>
	public bool Load( XrLoader loader, ulong instance, out string missing )
	{
		string first = null;

		nint P( string name )
		{
			var address = loader.Proc( instance, name );
			if ( address == 0 ) first ??= name;
			return address;
		}

		DestroyInstance = (delegate* unmanaged[Cdecl]<ulong, Result>)P( "xrDestroyInstance" );
		GetInstanceProperties = (delegate* unmanaged[Cdecl]<ulong, InstanceProperties*, Result>)P( "xrGetInstanceProperties" );
		GetSystem = (delegate* unmanaged[Cdecl]<ulong, SystemGetInfo*, ulong*, Result>)P( "xrGetSystem" );
		GetSystemProperties = (delegate* unmanaged[Cdecl]<ulong, ulong, SystemProperties*, Result>)P( "xrGetSystemProperties" );
		EnumerateViewConfigurationViews = (delegate* unmanaged[Cdecl]<ulong, ulong, ViewConfigurationType, uint, uint*, ViewConfigurationView*, Result>)P( "xrEnumerateViewConfigurationViews" );
		EnumerateEnvironmentBlendModes = (delegate* unmanaged[Cdecl]<ulong, ulong, ViewConfigurationType, uint, uint*, EnvironmentBlendMode*, Result>)P( "xrEnumerateEnvironmentBlendModes" );
		PollEvent = (delegate* unmanaged[Cdecl]<ulong, EventDataBuffer*, Result>)P( "xrPollEvent" );
		GetVulkanGraphicsRequirements2 = (delegate* unmanaged[Cdecl]<ulong, ulong, GraphicsRequirementsVulkan2KHR*, Result>)P( "xrGetVulkanGraphicsRequirements2KHR" );
		CreateVulkanInstance = (delegate* unmanaged[Cdecl]<ulong, VulkanInstanceCreateInfoKHR*, nint*, int*, Result>)P( "xrCreateVulkanInstanceKHR" );
		CreateVulkanDevice = (delegate* unmanaged[Cdecl]<ulong, VulkanDeviceCreateInfoKHR*, nint*, int*, Result>)P( "xrCreateVulkanDeviceKHR" );
		GetVulkanGraphicsDevice2 = (delegate* unmanaged[Cdecl]<ulong, VulkanGraphicsDeviceGetInfoKHR*, nint*, Result>)P( "xrGetVulkanGraphicsDevice2KHR" );
		CreateSession = (delegate* unmanaged[Cdecl]<ulong, SessionCreateInfo*, ulong*, Result>)P( "xrCreateSession" );
		DestroySession = (delegate* unmanaged[Cdecl]<ulong, Result>)P( "xrDestroySession" );
		BeginSession = (delegate* unmanaged[Cdecl]<ulong, SessionBeginInfo*, Result>)P( "xrBeginSession" );
		EndSession = (delegate* unmanaged[Cdecl]<ulong, Result>)P( "xrEndSession" );
		RequestExitSession = (delegate* unmanaged[Cdecl]<ulong, Result>)P( "xrRequestExitSession" );
		CreateReferenceSpace = (delegate* unmanaged[Cdecl]<ulong, ReferenceSpaceCreateInfo*, ulong*, Result>)P( "xrCreateReferenceSpace" );
		EnumerateReferenceSpaces = (delegate* unmanaged[Cdecl]<ulong, uint, uint*, ReferenceSpaceType*, Result>)P( "xrEnumerateReferenceSpaces" );
		DestroySpace = (delegate* unmanaged[Cdecl]<ulong, Result>)P( "xrDestroySpace" );
		LocateSpace = (delegate* unmanaged[Cdecl]<ulong, ulong, long, SpaceLocation*, Result>)P( "xrLocateSpace" );
		EnumerateSwapchainFormats = (delegate* unmanaged[Cdecl]<ulong, uint, uint*, long*, Result>)P( "xrEnumerateSwapchainFormats" );
		CreateSwapchain = (delegate* unmanaged[Cdecl]<ulong, SwapchainCreateInfo*, ulong*, Result>)P( "xrCreateSwapchain" );
		DestroySwapchain = (delegate* unmanaged[Cdecl]<ulong, Result>)P( "xrDestroySwapchain" );
		EnumerateSwapchainImages = (delegate* unmanaged[Cdecl]<ulong, uint, uint*, SwapchainImageVulkan2KHR*, Result>)P( "xrEnumerateSwapchainImages" );
		AcquireSwapchainImage = (delegate* unmanaged[Cdecl]<ulong, SwapchainImageAcquireInfo*, uint*, Result>)P( "xrAcquireSwapchainImage" );
		WaitSwapchainImage = (delegate* unmanaged[Cdecl]<ulong, SwapchainImageWaitInfo*, Result>)P( "xrWaitSwapchainImage" );
		ReleaseSwapchainImage = (delegate* unmanaged[Cdecl]<ulong, SwapchainImageReleaseInfo*, Result>)P( "xrReleaseSwapchainImage" );
		WaitFrame = (delegate* unmanaged[Cdecl]<ulong, FrameWaitInfo*, FrameState*, Result>)P( "xrWaitFrame" );
		BeginFrame = (delegate* unmanaged[Cdecl]<ulong, FrameBeginInfo*, Result>)P( "xrBeginFrame" );
		EndFrame = (delegate* unmanaged[Cdecl]<ulong, FrameEndInfo*, Result>)P( "xrEndFrame" );
		LocateViews = (delegate* unmanaged[Cdecl]<ulong, ViewLocateInfo*, ViewState*, uint, uint*, View*, Result>)P( "xrLocateViews" );
		StringToPath = (delegate* unmanaged[Cdecl]<ulong, byte*, ulong*, Result>)P( "xrStringToPath" );
		PathToString = (delegate* unmanaged[Cdecl]<ulong, ulong, uint, uint*, byte*, Result>)P( "xrPathToString" );
		CreateActionSet = (delegate* unmanaged[Cdecl]<ulong, ActionSetCreateInfo*, ulong*, Result>)P( "xrCreateActionSet" );
		CreateAction = (delegate* unmanaged[Cdecl]<ulong, ActionCreateInfo*, ulong*, Result>)P( "xrCreateAction" );
		SuggestInteractionProfileBindings = (delegate* unmanaged[Cdecl]<ulong, InteractionProfileSuggestedBinding*, Result>)P( "xrSuggestInteractionProfileBindings" );
		AttachSessionActionSets = (delegate* unmanaged[Cdecl]<ulong, SessionActionSetsAttachInfo*, Result>)P( "xrAttachSessionActionSets" );
		SyncActions = (delegate* unmanaged[Cdecl]<ulong, ActionsSyncInfo*, Result>)P( "xrSyncActions" );
		GetActionStateBoolean = (delegate* unmanaged[Cdecl]<ulong, ActionStateGetInfo*, ActionStateBoolean*, Result>)P( "xrGetActionStateBoolean" );
		GetActionStateFloat = (delegate* unmanaged[Cdecl]<ulong, ActionStateGetInfo*, ActionStateFloat*, Result>)P( "xrGetActionStateFloat" );
		GetActionStateVector2f = (delegate* unmanaged[Cdecl]<ulong, ActionStateGetInfo*, ActionStateVector2f*, Result>)P( "xrGetActionStateVector2f" );
		GetActionStatePose = (delegate* unmanaged[Cdecl]<ulong, ActionStateGetInfo*, ActionStatePose*, Result>)P( "xrGetActionStatePose" );
		CreateActionSpace = (delegate* unmanaged[Cdecl]<ulong, ActionSpaceCreateInfo*, ulong*, Result>)P( "xrCreateActionSpace" );
		ApplyHapticFeedback = (delegate* unmanaged[Cdecl]<ulong, HapticActionInfo*, HapticVibration*, Result>)P( "xrApplyHapticFeedback" );
		GetCurrentInteractionProfile = (delegate* unmanaged[Cdecl]<ulong, ulong, InteractionProfileState*, Result>)P( "xrGetCurrentInteractionProfile" );

		missing = first;
		return missing is null;
	}
}
