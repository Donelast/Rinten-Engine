using System;
using System.Runtime.InteropServices.JavaScript;

namespace Rinten;

//JSImport bindings for webxr.js; kept out of Rinten.Engine for the same reason as RenderBind
static partial class XrPage
{
	[JSImport( "globalThis.RintenXr.supported" )]
	internal static partial int Supported();

	[JSImport( "globalThis.RintenXr.active" )]
	internal static partial int Active();

	[JSImport( "globalThis.RintenXr.message" )]
	internal static partial string Message();

	//Whether the build or the address asked for a headset - index.html worked it out.
	[JSImport( "globalThis.RintenXr.wanted" )]
	internal static partial int Asked();

	[JSImport( "globalThis.RintenXr.enter" )]
	internal static partial void Enter();

	[JSImport( "globalThis.RintenXr.leave" )]
	internal static partial void Leave();

	[JSImport( "globalThis.RintenXr.offer" )]
	internal static partial void Offer( bool state );

	[JSImport( "globalThis.RintenXr.buzz" )]
	internal static partial void Buzz( int hand, double seconds, double amplitude );

	//Whether the page is to follow the fingers at all - see XrBind.Fingers.
	[JSImport( "globalThis.RintenXr.fingers" )]
	internal static partial void Fingers( bool on );

	[JSImport( "globalThis.RintenXr.eyeTarget" )]
	internal static partial int EyeTarget( int eye );

	//The whole headset in one call. The span is the engine's own array, and arrives in JavaScript as .NET's MemoryView
	//- webxr.js fills a Float64Array and copies it in with set(), which is the only way that writes wasm memory.
	[JSImport( "globalThis.RintenXr.poll" )]
	internal static partial int Poll( [JSMarshalAs<JSType.MemoryView>] Span<double> into );

	//The body the session's animation frame is to call. While there is a session the page drives the engine - see CoreHeadset.Driving.
	[JSImport( "globalThis.RintenXr.setFrame" )]
	internal static partial void SetFrame( [JSMarshalAs<JSType.Function>] Action body );

	internal static void Attach()
	{
		XrBind.Supported = Supported;
		XrBind.Active = Active;
		XrBind.Message = Message;
		XrBind.Asked = Asked;
		XrBind.Enter = Enter;
		XrBind.Leave = Leave;
		XrBind.Offer = Offer;
		XrBind.Buzz = ( hand, seconds, amplitude ) => Buzz( hand, seconds, amplitude );
		XrBind.Fingers = Fingers;
		XrBind.EyeTarget = eye => (ulong)EyeTarget( eye );
		XrBind.Poll = Poll;
		XrBind.SetFrame = SetFrame;
	}
}
