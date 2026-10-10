namespace Rinten;

//Delegates to webxr.js, set by RintenWeb; never [JSImport] here, it throws on desktop at startup
internal static class XrBind
{
	//A frame's worth of headset read into the engine's own memory in one call - see CoreWebXr for
	//what the words are. A Span cannot be a Func's argument, so this is a delegate of its own.
	internal delegate int PollWords( Span<double> into );

	//One for yes, nought for no, below nought for "the page has not settled yet".
	internal static Func<int> Supported;

	//Whether there is a session with a layer to draw into.
	internal static Func<int> Active;

	//Why there is no headset, in words worth showing a player.
	internal static Func<string> Message;

	//Whether this build asks for a headset at all - the manifest said so, or ?vr did.
	internal static Func<int> Asked;

	//Ask for a session. Only a gesture may open one, so a wish made outside one is remembered.
	internal static Action Enter;

	//End the session.
	internal static Action Leave;

	//Whether the page should draw the way in for a game that has none of its own.
	internal static Action<bool> Offer;

	//The head, the eyes and the hands, all of it, once a frame.
	internal static PollWords Poll;

	//A hand buzzed: which, for how long, how hard.
	internal static Action<int, double, double> Buzz;

	//Whether anything is reading fingers. A headset answers for a joint by walking its own tracking state, and twenty-six a hand a frame is
	//the most expensive thing a game that never looks at a finger can be made to pay for - see CoreWebXr.Hands and webxr.js's writeJoints.
	internal static Action<bool> Fingers;

	//The handle a blit into one eye's part of the headset's framebuffer names.
	internal static Func<int, ulong> EyeTarget;

	//The body the session's animation frame is to call. While there is a session the page drives the
	//engine, because the poses and the layer are good only inside that callback - see CoreWebXr.Frame.
	internal static Action<Action> SetFrame;

	//Whether the page filled these in at all - false in every desktop build.
	internal static bool Attached => Poll is not null;
}
