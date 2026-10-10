using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.OpenXR;
using XrResult = Silk.NET.OpenXR.Result;

namespace Rinten;

//OpenXR headset: found before Vulkan since the runtime makes the device; headset paces frames
internal static unsafe partial class CoreXr
{
	private static readonly Diagnostics.Logger log = new( "OpenXR" );

	//Whether this run is to look for a headset - at launch for a VR game or a Settings/VR-triggered editor restart - and not told -novr .
	internal static bool Wanted { get; set; }

	//The JavaVM and the activity, on Android, which the loader and the runtime need before anything else.
	internal static nint AndroidVm { get; set; }
	internal static nint AndroidActivity { get; set; }

	private static XrLoader loader;
	private static readonly XrFunctions xr = new();

	private static ulong instance;
	private static ulong systemId;
	private static ulong session;

	//The play area - the floor where the runtime has one - and the headset.
	private static ulong stageSpace;
	private static ulong viewSpace;

	//What the runtime and the headset call themselves.
	internal static string RuntimeName { get; private set; }
	internal static string SystemName { get; private set; }

	//What is known about the headset beyond yes and no. OpenXR says it through the log as it goes, so
	//there is nothing to hold here - see CoreWebXr.Message, where a page has no log a player can reach.
	internal static string Message => RuntimeName is null ? "" : $"{RuntimeName}";

	//Whether a headset was found, and the device is being made for it.
	internal static bool Hooked => instance != 0 && systemId != 0;

	//Whether there is a session - the headset is ours to draw to while the runtime says so.
	internal static bool HasSession => session != 0;

	//What the runtime last said of the session.
	internal static SessionState State { get; private set; } = SessionState.Unknown;

	//The same, in the engine's own words - see VRSystem.SessionState.
	internal static Rinten.XR.SessionState Session => State switch
	{
		SessionState.Idle => Rinten.XR.SessionState.Idle,
		SessionState.Ready => Rinten.XR.SessionState.Ready,
		SessionState.Synchronized => Rinten.XR.SessionState.Synchronized,
		SessionState.Visible => Rinten.XR.SessionState.Visible,
		SessionState.Focused => Rinten.XR.SessionState.Focused,
		SessionState.Stopping => Rinten.XR.SessionState.Stopping,
		SessionState.LossPending => Rinten.XR.SessionState.LossPending,
		SessionState.Exiting => Rinten.XR.SessionState.Exiting,
		_ => Rinten.XR.SessionState.Unknown,
	};

	//Between xrBeginSession and xrEndSession: frames are wanted.
	private static bool running;

	//The headset's refresh, as far as the frames it asks for say.
	internal static float RefreshRate { get; private set; }

	//The size the runtime suggests for each eye's picture.
	internal static (int Width, int Height) EyeSize { get; private set; }

	//The blend mode the headset shows in: opaque for a headset, additive or alpha for see-through.
	private static EnvironmentBlendMode blendMode = EnvironmentBlendMode.Opaque;

	private static readonly string[] AndroidExtensions = ["XR_KHR_android_create_instance"];

	//The three structures XR_KHR_vulkan_enable2 shares with the first Vulkan extension. The spec gives them the first extension's numbers; Silk's enum
	//gives the "2" names numbers of their own, which are other structures' - and a runtime handed one reads a chain with no graphics binding in it.
	private const StructureType VulkanBindingType = StructureType.GraphicsBindingVulkanKhr;
	private const StructureType VulkanImageType = StructureType.SwapchainImageVulkanKhr;
	private const StructureType VulkanRequirementsType = StructureType.GraphicsRequirementsVulkanKhr;

	//Finds the runtime and asks it for a headset. Before the graphics device - see the remarks. Quiet where
	//there is nothing: a machine with no headset plugged in is the ordinary case, even for a game that wants one.
	internal static void Begin()
	{
		if ( !Wanted || instance != 0 ) return;

		loader = XrLoader.Open( out var failure );

		if ( loader is null )
		{
			log.Warning( $"no headset: {failure}" );
			return;
		}

		if ( OperatingSystem.IsAndroid() && !InitialiseAndroidLoader() ) return;

		var available = AvailableExtensions();

		if ( !available.Contains( "XR_KHR_vulkan_enable2" ) )
		{
			log.Warning( "no headset: the OpenXR runtime cannot draw with Vulkan (no XR_KHR_vulkan_enable2)" );
			return;
		}

		var extensions = new List<string> { "XR_KHR_vulkan_enable2" };

		if ( OperatingSystem.IsAndroid() )
			extensions.AddRange( AndroidExtensions.Where( available.Contains ) );

		//The controllers a runtime may have that the core profiles do not name - see CoreXr.Input, which binds to them where they are here.
		foreach ( var optional in Input.OptionalExtensions )
			if ( available.Contains( optional ) ) extensions.Add( optional );

		//And the hands, joint by joint, where the runtime follows them - see CoreXr.Hands.
		foreach ( var optional in new[] { Hands.Extension, Hands.RangeExtension } )
			if ( available.Contains( optional ) ) extensions.Add( optional );

		//And the eyes, where the runtime follows them - see CoreXr.Gaze.
		if ( available.Contains( Gaze.Extension ) ) extensions.Add( Gaze.Extension );

		if ( !CreateInstance( extensions ) ) return;

		if ( !xr.Load( loader, instance, out var missing ) )
		{
			log.Warning( $"no headset: the runtime has no {missing}" );
			Forget();
			return;
		}

		enabledExtensions = extensions;

		var getInfo = new SystemGetInfo
		{
			Type = StructureType.SystemGetInfo,
			FormFactor = FormFactor.HeadMountedDisplay,
		};

		ulong system;
		var found = xr.GetSystem( instance, &getInfo, &system );

		if ( found != XrResult.Success )
		{
			//Not an error: the runtime is there and the headset is not plugged in, or is asleep. Said once, and the game runs on the screen.
			log.Info( $"{RuntimeName} is running and has no headset ({found}) - drawing to the screen" );
			Forget();
			return;
		}

		systemId = system;

		var properties = new SystemProperties { Type = StructureType.SystemProperties };
		xr.GetSystemProperties( instance, systemId, &properties );
		SystemName = Utf8( properties.SystemName, 256 );

		if ( !ReadViews() )
		{
			Forget();
			return;
		}

		//Asked because the runtime requires the question before a session, and because the answer is worth a line when a device is refused.
		var requirements = new GraphicsRequirementsVulkan2KHR { Type = VulkanRequirementsType };
		xr.GetVulkanGraphicsRequirements2( instance, systemId, &requirements );

		log.Info( $"{SystemName} through {RuntimeName}: {EyeSize.Width}x{EyeSize.Height} an eye, " +
			$"Vulkan {Version( requirements.MinApiVersionSupported )} to {Version( requirements.MaxApiVersionSupported )}" );
	}

	private static List<string> enabledExtensions = [];

	//Whether the instance was made with an extension.
	internal static bool Enabled( string extension ) => enabledExtensions.Contains( extension );

	private static bool InitialiseAndroidLoader()
	{
		if ( AndroidVm == 0 || AndroidActivity == 0 )
		{
			log.Warning( "no headset: the launcher did not say which JavaVM and activity the loader is for" );
			return false;
		}

		var initialise = (delegate* unmanaged[Cdecl]<LoaderInitInfoAndroidKHR*, XrResult>)loader.Proc( 0, "xrInitializeLoaderKHR" );

		if ( initialise == null ) return true;

		var info = new LoaderInitInfoAndroidKHR
		{
			Type = StructureType.LoaderInitInfoAndroidKhr,
			ApplicationVM = (void*)AndroidVm,
			ApplicationContext = (void*)AndroidActivity,
		};

		var result = initialise( &info );

		if ( result != XrResult.Success )
		{
			log.Warning( $"no headset: the Android loader would not start ({result})" );
			return false;
		}

		return true;
	}

	private static HashSet<string> AvailableExtensions()
	{
		var found = new HashSet<string>( StringComparer.Ordinal );

		var enumerate = (delegate* unmanaged[Cdecl]<byte*, uint, uint*, ExtensionProperties*, XrResult>)loader.Proc( 0, "xrEnumerateInstanceExtensionProperties" );
		if ( enumerate == null ) return found;

		uint count = 0;
		if ( enumerate( null, 0, &count, null ) != XrResult.Success || count == 0 ) return found;

		var properties = new ExtensionProperties[count];

		for ( var i = 0; i < count; i++ ) properties[i].Type = StructureType.ExtensionProperties;

		fixed ( ExtensionProperties* into = properties )
		{
			if ( enumerate( null, count, &count, into ) != XrResult.Success ) return found;

			for ( var i = 0; i < count; i++ )
				found.Add( Utf8( into[i].ExtensionName, 128 ) );
		}

		return found;
	}

	private static bool CreateInstance( List<string> extensions )
	{
		var create = (delegate* unmanaged[Cdecl]<InstanceCreateInfo*, ulong*, XrResult>)loader.Proc( 0, "xrCreateInstance" );

		if ( create == null )
		{
			log.Warning( "no headset: the loader has no xrCreateInstance" );
			return false;
		}

		var names = new nint[extensions.Count];

		for ( var i = 0; i < extensions.Count; i++ )
			names[i] = Marshal.StringToHGlobalAnsi( extensions[i] );

		try
		{
			var android = new InstanceCreateInfoAndroidKHR
			{
				Type = StructureType.InstanceCreateInfoAndroidKhr,
				ApplicationVM = (void*)AndroidVm,
				ApplicationActivity = (void*)AndroidActivity,
			};

			fixed ( nint* list = names )
			{
				var info = new InstanceCreateInfo
				{
					Type = StructureType.InstanceCreateInfo,
					Next = extensions.Contains( "XR_KHR_android_create_instance" ) ? &android : null,
					EnabledExtensionCount = (uint)extensions.Count,
					EnabledExtensionNames = (byte**)list,
				};

				Write( info.ApplicationInfo.ApplicationName, 128, Application.GameIdent ?? "Rinten" );
				Write( info.ApplicationInfo.EngineName, 128, "Rinten" );
				info.ApplicationInfo.ApplicationVersion = 1;
				info.ApplicationInfo.EngineVersion = 1;

				//1.0: every runtime answers to it, and nothing here needs what 1.1 added.
				info.ApplicationInfo.ApiVersion = MakeVersion( 1, 0, 0 );

				ulong made;
				var result = create( &info, &made );

				if ( result != XrResult.Success )
				{
					//No runtime installed or none running is the usual reason, and the loader says which in its own log.
					log.Info( $"no headset: no OpenXR runtime answered ({result})" );
					return false;
				}

				instance = made;
			}
		}
		finally
		{
			foreach ( var name in names ) Marshal.FreeHGlobal( name );
		}

		var getProperties = (delegate* unmanaged[Cdecl]<ulong, InstanceProperties*, XrResult>)loader.Proc( instance, "xrGetInstanceProperties" );
		var properties = new InstanceProperties { Type = StructureType.InstanceProperties };

		if ( getProperties != null && getProperties( instance, &properties ) == XrResult.Success )
			RuntimeName = Utf8( properties.RuntimeName, 128 );

		RuntimeName ??= "the OpenXR runtime";
		return true;
	}

	private static bool ReadViews()
	{
		uint count = 0;
		xr.EnumerateViewConfigurationViews( instance, systemId, ViewConfigurationType.PrimaryStereo, 0, &count, null );

		if ( count != 2 )
		{
			log.Warning( $"no headset: {SystemName} does not show two eyes ({count} views)" );
			return false;
		}

		var views = stackalloc ViewConfigurationView[2];
		views[0].Type = views[1].Type = StructureType.ViewConfigurationView;
		xr.EnumerateViewConfigurationViews( instance, systemId, ViewConfigurationType.PrimaryStereo, 2, &count, views );

		EyeSize = ((int)views[0].RecommendedImageRectWidth, (int)views[0].RecommendedImageRectHeight);

		//Opaque where the headset offers it, which a headset always does; a see- through one offers only what it can show.
		uint modes = 0;
		xr.EnumerateEnvironmentBlendModes( instance, systemId, ViewConfigurationType.PrimaryStereo, 0, &modes, null );

		if ( modes > 0 )
		{
			var offered = stackalloc EnvironmentBlendMode[(int)modes];
			xr.EnumerateEnvironmentBlendModes( instance, systemId, ViewConfigurationType.PrimaryStereo, modes, &modes, offered );
			blendMode = offered[0];

			for ( var i = 0; i < modes; i++ )
				if ( offered[i] == EnvironmentBlendMode.Opaque ) blendMode = EnvironmentBlendMode.Opaque;
		}

		return true;
	}

	//-- the graphics device, made the runtime's way -----------------------------------------

	//The Vulkan instance, made by the runtime out of ours - see VkDevice.CreateInstance. False where it could not; vulkanResult is Vulkan's own answer.
	internal static bool CreateVulkanInstance( void* createInfo, nint getInstanceProcAddr, out nint vulkanInstance, out int vulkanResult )
	{
		var info = new VulkanInstanceCreateInfoKHR
		{
			Type = StructureType.VulkanInstanceCreateInfoKhr,
			SystemId = systemId,
			VulkanCreateInfo = createInfo,
		};

		*(nint*)&info.PfnGetInstanceProcAddr = getInstanceProcAddr;

		nint made = 0;
		int vk = 0;
		var result = xr.CreateVulkanInstance( instance, &info, &made, &vk );

		vulkanInstance = made;
		vulkanResult = vk;

		if ( result != XrResult.Success ) log.Warning( $"the runtime would not make the Vulkan instance ({result}, {vk})" );

		return result == XrResult.Success && vk == 0;
	}

	//The card the headset is plugged into, which is the one to draw with.
	internal static nint PhysicalDevice( nint vulkanInstance )
	{
		var info = new VulkanGraphicsDeviceGetInfoKHR
		{
			Type = StructureType.VulkanGraphicsDeviceGetInfoKhr,
			SystemId = systemId,
			VulkanInstance = new Silk.NET.Core.Native.VkHandle( vulkanInstance ),
		};

		nint device = 0;
		return xr.GetVulkanGraphicsDevice2( instance, &info, &device ) == XrResult.Success ? device : 0;
	}

	//The Vulkan device, made by the runtime out of ours - see VkDevice.CreateDevice.
	internal static bool CreateVulkanDevice( nint physicalDevice, void* createInfo, nint getInstanceProcAddr, out nint vulkanDevice, out int vulkanResult )
	{
		var info = new VulkanDeviceCreateInfoKHR
		{
			Type = StructureType.VulkanDeviceCreateInfoKhr,
			SystemId = systemId,
			VulkanPhysicalDevice = new Silk.NET.Core.Native.VkHandle( physicalDevice ),
			VulkanCreateInfo = createInfo,
		};

		*(nint*)&info.PfnGetInstanceProcAddr = getInstanceProcAddr;

		nint made = 0;
		int vk = 0;
		var result = xr.CreateVulkanDevice( instance, &info, &made, &vk );

		vulkanDevice = made;
		vulkanResult = vk;

		if ( result != XrResult.Success ) log.Warning( $"the runtime would not make the Vulkan device ({result}, {vk})" );

		return result == XrResult.Success && vk == 0;
	}

	//-- the session -------------------------------------------------------------------------

	//The session, on the device just made: the spaces, the swapchains and the controllers. Called by VkDevice once the device is up.
	internal static void Bind( nint vulkanInstance, nint physicalDevice, nint device, uint queueFamily )
	{
		if ( !Hooked || session != 0 ) return;

		var binding = new GraphicsBindingVulkan2KHR
		{
			Type = VulkanBindingType,
			Instance = new Silk.NET.Core.Native.VkHandle( vulkanInstance ),
			PhysicalDevice = new Silk.NET.Core.Native.VkHandle( physicalDevice ),
			Device = new Silk.NET.Core.Native.VkHandle( device ),
			QueueFamilyIndex = queueFamily,
			QueueIndex = 0,
		};

		var info = new SessionCreateInfo
		{
			Type = StructureType.SessionCreateInfo,
			Next = &binding,
			SystemId = systemId,
		};

		ulong made;
		var result = xr.CreateSession( instance, &info, &made );

		if ( result != XrResult.Success )
		{
			log.Warning( $"no session on {SystemName} ({result}) - drawing to the screen" );
			return;
		}

		session = made;

		stageSpace = ReferenceSpace( HasStage() ? ReferenceSpaceType.Stage : ReferenceSpaceType.Local );
		viewSpace = ReferenceSpace( ReferenceSpaceType.View );

		Input.Create();
		Hands.Create();

		if ( !MakeSwapchains() )
		{
			log.Warning( $"no swapchain on {SystemName} - drawing to the screen" );
			Shutdown();
			return;
		}

		log.Info( $"session open on {SystemName}" );
	}

	private static bool HasStage()
	{
		uint count = 0;
		xr.EnumerateReferenceSpaces( session, 0, &count, null );

		var types = stackalloc ReferenceSpaceType[(int)Math.Max( count, 1u )];
		xr.EnumerateReferenceSpaces( session, count, &count, types );

		for ( var i = 0; i < count; i++ )
			if ( types[i] == ReferenceSpaceType.Stage ) return true;

		return false;
	}

	private static ulong ReferenceSpace( ReferenceSpaceType type )
	{
		var info = new ReferenceSpaceCreateInfo
		{
			Type = StructureType.ReferenceSpaceCreateInfo,
			ReferenceSpaceType = type,
			PoseInReferenceSpace = new Posef { Orientation = new Quaternionf { W = 1 } },
		};

		ulong made = 0;
		xr.CreateReferenceSpace( session, &info, &made );
		return made;
	}

	//Lets go of everything, the instance last. The device outlives it: the runtime made it, and it is ours now.
	internal static void Shutdown()
	{
		if ( session != 0 ) log.Info( $"session closed after {framesShown} frames shown on {SystemName}" );

		DestroySwapchains();
		Hands.Destroy();
		Input.Destroy();

		if ( stageSpace != 0 ) xr.DestroySpace( stageSpace );
		if ( viewSpace != 0 ) xr.DestroySpace( viewSpace );
		stageSpace = viewSpace = 0;

		if ( session != 0 )
		{
			if ( running ) Stop();
			xr.DestroySession( session );
		}

		session = 0;
		running = false;
		State = SessionState.Unknown;

		Forget();
	}

	//Requests session exit and ends it once the runtime reports stopping, waiting at most a second
	private static void Stop()
	{
		if ( xr.RequestExitSession( session ) != XrResult.Success ) return;

		var until = Environment.TickCount64 + 1000;
		var buffer = new EventDataBuffer();

		while ( running && Environment.TickCount64 < until )
		{
			buffer.Type = StructureType.EventDataBuffer;
			buffer.Next = null;

			if ( xr.PollEvent( instance, &buffer ) != XrResult.Success )
			{
				System.Threading.Thread.Sleep( 5 );
				continue;
			}

			if ( buffer.Type == StructureType.EventDataSessionStateChanged &&
				( (EventDataSessionStateChanged*)&buffer )->State == SessionState.Stopping )
			{
				xr.EndSession( session );
				running = false;
			}
		}
	}

	private static void Forget()
	{
		if ( instance != 0 && xr.DestroyInstance != null ) xr.DestroyInstance( instance );
		else if ( instance != 0 )
		{
			var destroy = (delegate* unmanaged[Cdecl]<ulong, XrResult>)loader.Proc( instance, "xrDestroyInstance" );
			if ( destroy != null ) destroy( instance );
		}

		instance = 0;
		systemId = 0;
	}

	//What the runtime has said since the last frame: the session starting and stopping, the headset taken off and put back on, the runtime going away.
	private static void Poll()
	{
		var buffer = new EventDataBuffer();

		while ( true )
		{
			buffer.Type = StructureType.EventDataBuffer;
			buffer.Next = null;

			if ( xr.PollEvent( instance, &buffer ) != XrResult.Success ) break;

			switch ( buffer.Type )
			{
				case StructureType.EventDataSessionStateChanged:
					SessionChanged( ( (EventDataSessionStateChanged*)&buffer )->State );
					break;

				case StructureType.EventDataInstanceLossPending:
					log.Warning( $"{RuntimeName} is going away - drawing to the screen" );
					Shutdown();
					return;

				case StructureType.EventDataInteractionProfileChanged:
					Input.ProfileChanged();
					break;
			}
		}
	}

	private static void SessionChanged( SessionState state )
	{
		State = state;

		switch ( state )
		{
			case SessionState.Ready:
			{
				var info = new SessionBeginInfo
				{
					Type = StructureType.SessionBeginInfo,
					PrimaryViewConfigurationType = ViewConfigurationType.PrimaryStereo,
				};

				var begun = xr.BeginSession( session, &info );
				running = begun == XrResult.Success;

				if ( !running ) log.Warning( $"the session would not begin ({begun})" );
				break;
			}

			case SessionState.Stopping:
				xr.EndSession( session );
				running = false;
				break;

			case SessionState.Exiting:
			case SessionState.LossPending:
				//The runtime is done with us - the headset was quit from its own menu. On a
				//headset that is the game quitting; on a desktop the game goes on, on the screen.
				running = false;

				if ( OperatingSystem.IsAndroid() ) Application.Exit();
				else Shutdown();

				break;
		}
	}

	//-- helpers --------------------------------------------------------------------------------

	private static ulong MakeVersion( ulong major, ulong minor, ulong patch ) => ( major << 48 ) | ( minor << 32 ) | patch;

	private static string Version( ulong version ) => $"{version >> 48}.{( version >> 32 ) & 0xFFFF}";

	private static void Write( byte* into, int capacity, string text )
	{
		var bytes = Encoding.UTF8.GetBytes( text );
		var length = Math.Min( bytes.Length, capacity - 1 );

		for ( var i = 0; i < length; i++ ) into[i] = bytes[i];
		into[length] = 0;
	}

	private static string Utf8( byte* from, int capacity )
	{
		var length = 0;
		while ( length < capacity && from[length] != 0 ) length++;
		return Encoding.UTF8.GetString( from, length );
	}

	//The engine's transform out of a pose: the same axes, the same metres.
	internal static Transform ToTransform( in Posef pose )
		=> new( new Vector3( pose.Position.X, pose.Position.Y, pose.Position.Z ),
			new Rotation( pose.Orientation.X, pose.Orientation.Y, pose.Orientation.Z, pose.Orientation.W ) );
}
