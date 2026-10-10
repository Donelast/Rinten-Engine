using Rinten.XR;
using NativeEngine;
using Rinten.Utility;
using System.Runtime.InteropServices;
using System.Text;

namespace Rinten.VR;

internal static unsafe partial class VRSystem
{
	public enum States
	{
		Inactive,
		Standby,
		Active
	}

	public static States State { get; private set; }
	public static bool IsActive => State == States.Active;
	internal static bool InternalIsActive() => IsActive; //For native side

	private static float IPD { get; set; } = 0f;
	/// <summary>Distance between the eyes, in metres.</summary>
	public static float IPDMetres => IPD;
	public static float RefreshRate { get; private set; }
	public static List<TrackedDevice> TrackedDevices { get; private set; }

	internal static Vector2 FullRenderTargetSize { get; private set; }
	internal static Vector2 EyeRenderTargetSize { get; private set; }

	/// <summary>Also draw the scene to the window while in VR; off by default since it costs a whole extra scene</summary>
	public static bool MirrorToScreen { get; set; }

	/// <summary>Eye resolution multiplier (0.5 = quarter pixels); applied when the next session opens</summary>
	public static float RenderScale
	{
		get => renderScale;
		set => renderScale = Math.Clamp( value, 0.1f, 2.0f );
	}

	private static float renderScale = 1.0f;

	/// <summary>Edge shading coarseness, 0 (off) to 1 (coarsest); uses eye tracking where available. Applies at once</summary>
	public static float Foveation
	{
		get => foveation;
		set => foveation = Math.Clamp( value, 0.0f, 1.0f );
	}

	private static float foveation = 0.5f;

	public static float WorldScale { get; set; } = 1.0f;
	public static bool HasHeadset { get; set; } = false;

	internal static bool IsRendering = false;

	public static void Init()
	{
		//If we've already inited, don't do it again
		if ( IsActive )
			return;

		//Check if we actually want to init VR
		if ( CommandLine.HasSwitch( "-novr" ) )
			return;

		//The headset was found before the graphics device, and the session made on it as the device came up - see CoreHeadset. What is left is to say so.
		HasHeadset = CoreHeadset.Hooked;

		if ( !CoreHeadset.HasSession ) return;

		Begin();
	}

	//Starts VR on a session; called from Init (OpenXR) or FrameStart (web session opened later)
	private static void Begin()
	{
		HasHeadset = true;

		EyeRenderTargetSize = new Vector2( CoreHeadset.EyeSize.Width, CoreHeadset.EyeSize.Height );
		FullRenderTargetSize = new Vector2( CoreHeadset.EyeSize.Width * 2, CoreHeadset.EyeSize.Height );

		VRInput.Current ??= new VRInput( new TrackedDevice( InputSource.LeftHand ), new TrackedDevice( InputSource.RightHand ) );

		State = States.Active;
	}

	/// <summary>Whether there is a headset to be entered at all, session or no. A page answers this late - the browser is asked through a promise - and answers no until the player has allowed it, so a game showing its own way in reads this rather than <see cref="HasHeadset"/>.</summary>
	public static bool HeadsetAvailable => CoreHeadset.Hooked;

	/// <summary>What the headset, or the page it is reached through, has to say - why there is none, or how a session that opened came to close.</summary>
	public static string HeadsetMessage => CoreHeadset.Message;

	/// <summary>Ask for the headset, for a game that draws its own way in. A browser only opens a session during a gesture, so a wish made outside one is remembered and taken on the next click. Does nothing where a session is not the player's to ask for, which is every desktop runtime.</summary>
	public static void RequestHeadset() => CoreHeadset.Request();

	/// <summary>Leave the headset and go back to the screen.</summary>
	public static void DismissHeadset() => CoreHeadset.Dismiss();

	[ConCmd( "vr_info", ConVarFlags.Protected )]
	public static void VrInfoConCommand()
	{
		if ( !IsActive )
		{
			Log.Info( "VR not initialized." );
			return;
		}

		Log.Info( $"Refresh rate: {RefreshRate}Hz" );
		Log.Info( $"Distance between eyes (IPD): {IPD * 1000.0f:0.#}mm" );
		Log.Info( $"Render target size: {EyeRenderTargetSize}" );
		Log.Info( $"System name: '{GetSystemName()}'" );
		Log.Info( $"Session state: {SessionState}" );

		Log.Info( $"------------------------------------------------------------------------------------------------------------------------" );

		Log.Info( $"Tracked objects:" );

		Log.Info( $"Head: {CoreHeadset.Head.Position} (tracked: {CoreHeadset.HeadTracked})" );
		Log.Info( $"Left hand: {CoreHeadset.Input.Holding( 0 )}, right hand: {CoreHeadset.Input.Holding( 1 )}" );
	}

	//The start of the engine's frame: the headset's frame waited for and begun, and
	//the head and hands read for the tick that follows - see CoreHeadset.FrameStart.
	internal static void FrameStart()
	{
		CoreHeadset.FrameStart();

		//A session the runtime took away is VR over; on a desktop the game goes on, on the screen.
		if ( IsActive && !CoreHeadset.HasSession )
		{
			State = States.Inactive;
			HasHeadset = false;
			return;
		}

		//A session that arrived turns VR on wherever it came from - see Begin.
		if ( !IsActive && CoreHeadset.HasSession ) Begin();

		if ( !IsActive ) return;

		RefreshRate = CoreHeadset.RefreshRate;
		IPD = CoreHeadset.EyeDistance;

		Update();
	}

	//The end of the engine's frame, after the window has been drawn: each eye drawn from the active scene's camera and handed to the headset.
	internal static void FrameEnd()
	{
		if ( !CoreHeadset.HasSession ) return;

		IsRendering = true;

		try
		{
			CoreHeadset.FrameEnd( IsActive ? Game.ActiveScene : null );
		}
		finally
		{
			IsRendering = false;
		}
	}

	public static void Enable()
	{
		if ( IsActive )
			return;

		if ( !CoreHeadset.HasSession ) return;

		VRInput.Current ??= new VRInput( new TrackedDevice( InputSource.LeftHand ), new TrackedDevice( InputSource.RightHand ) );
		State = States.Active;
	}

	/// <summary>Checks for a desktop headset from Settings/VR. A successful answer is followed by an editor restart so its Vulkan device can be made by OpenXR before any window or UI resource exists.</summary>
	public static bool ProbeHeadset()
	{
		if ( CoreHeadset.HasSession || CoreHeadset.Hooked ) return true;
		if ( CommandLine.HasSwitch( "-novr" ) ) return false;

		CoreHeadset.Wanted = true;
		CoreHeadset.Begin();
		return CoreHeadset.Hooked;
	}

	public static void Disable()
	{
		if ( !IsActive )
			return;

		State = States.Standby;
	}

	internal static void OnPlay()
	{
		Reset();
		CreateCompositor();
	}

	internal static void OnStop()
	{
		DestroyCompositor();
		Reset();
	}

	internal static void BeginFrame()
	{
	}
	internal static void EndFrame()
	{
	}

	internal static bool Submit( IntPtr pColorTexture, IntPtr pDepthTexture )
	{
		return false;
	}

	internal static string GetVulkanInstanceExtensionsRequired()
	{
		return default;
	}

	internal static string GetVulkanDeviceExtensionsRequired()
	{
		return default;
	}

	public delegate void DebugUtilsMessengerCallback( string message, DebugCallbackType type );
	public delegate void DebugUtilsErrorCallback( string message );

	private static Logger Log = new( "OpenXR" );

	private static void XrErrorCallback( string message )
	{
		Log.Error( $"Unhandled OpenXR exception: {message}" );

		//The editor's developer is told; a player is not shown the runtime's own words - the log carries them.
		if ( Application.IsEditor ) EnginePlatform.MessageBox( "Unhandled OpenXR exception", $"{message}" );

		Application.Exit();
	}

	private static void XrDebugCallback( string message, DebugCallbackType type )
	{
		switch ( type )
		{
			case DebugCallbackType.Verbose:
				Log.Trace( $"{message}" );
				break;
			case DebugCallbackType.Warning:
				Log.Warning( $"{message}" );
				break;
			case DebugCallbackType.Error:
				Log.Error( $"{message}" );
				break;
			case DebugCallbackType.Info:
			default:
				Log.Info( $"{message}" );
				break;
		}
	}


	private static void FpxrCheck( XRResult result )
	{
		if ( result < XRResult.Success )
		{
			Log.Warning( $"Rinten.XR: {result}" );
		}
	}

	private static void CopyStringToBuffer( string input, byte* buffer, uint maxLength )
	{
		byte[] inputBytes = Encoding.ASCII.GetBytes( input );

		for ( int i = 0; i < maxLength; i++ )
		{
			if ( i < inputBytes.Length )
				buffer[i] = inputBytes[i];
			else
				buffer[i] = 0;
		}
	}

	private static string BufferToString( byte* buffer, uint maxLength )
	{
		var str = new byte[maxLength];
		for ( int i = 0; i < maxLength; i++ )
		{
			str[i] = buffer[i];
		}

		return Encoding.ASCII.GetString( str ).TrimEnd( '\0' );
	}

	internal static void CreateInstance()
	{
		if ( !HasHeadset )
			return;

		if ( CommandLine.HasSwitch( "-vrdebug" ) )
		{
			var pDebugCallback = Marshal.GetFunctionPointerForDelegate<DebugUtilsMessengerCallback>( XrDebugCallback );
		}
	}

	private static uint ReadUInt32( IntPtr ptr )
	{
		var signedValue = Marshal.ReadInt32( ptr );
		return BitConverter.ToUInt32( BitConverter.GetBytes( signedValue ) );
	}

	internal static unsafe void CreateCompositor()
	{
		//No headset: the engine does not bind OpenXR yet.
	}

	internal static unsafe void DestroyCompositor()
	{
		//No headset: the engine does not bind OpenXR yet.
	}

	public static SessionState SessionState { get; private set; } = SessionState.Unknown;

	internal static void Update()
	{
		var input = VRInput.Current;
		if ( input is null ) return;

		input._head = CoreHeadset.Head.WithPosition( CoreHeadset.Head.Position / MathF.Max( WorldScale, 0.0001f ) );
		input.Update();

		//Whichever headset is here says it in the engine's own words - see CoreHeadset.
		SessionState = CoreHeadset.Session;
	}

	//Play-area transform: the VRAnchor if any, else the camera with yaw only (head adds turn, not height)
	internal static Transform AnchorFor( CameraComponent camera )
	{
		var input = VRInput.Current;
		if ( input is null ) return Transform.Zero;

		var scene = camera.Scene;

		if ( scene.GetAllComponents<VRAnchor>().Any( x => x.Active ) ) return input.Anchor;
		if ( camera.GameObject.Components.Get<VRTrackedObject>() is { Active: true } ) return input.Anchor;

		var head = CoreHeadset.Head;
		var yaw = Rotation.FromYaw( camera.WorldRotation.Yaw() );
		var position = camera.WorldPosition - ( yaw * head.Position ) / MathF.Max( WorldScale, 0.0001f );

		input.Anchor = new Transform( position, yaw );
		return input.Anchor;
	}

	internal static void Reset()
	{
		WorldScale = 1.0f;
	}

	internal static string GetSystemName() => CoreHeadset.SystemName;
}
