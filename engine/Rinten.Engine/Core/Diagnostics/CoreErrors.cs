using System.Threading;

namespace Rinten;

//Count of backend errors (Vulkan validation, WebGPU); tests assert it doesn't grow. Report all here
internal static class CoreErrors
{
	private static int count;

	//The browser's half is counted on the page, not here: WebGPU reports uncaptured errors through onuncapturederror , on its own schedule and not inside any call of ours, so the shim keeps the tally and this asks for it.
	/// <summary>How many error-severity messages a backend has reported, ever.</summary>
	public static int Count => OperatingSystem.IsBrowser()
		? Volatile.Read( ref count ) + BrowserNative.ErrorCount()
		: Volatile.Read( ref count );

	/// <summary>Counts one. Called from whichever thread the backend was on.</summary>
	public static void Raised() => Interlocked.Increment( ref count );

	private static int warnings;

	/// <summary>How many warning-severity messages the validation layers have reported, ever - what a run that is meant to be clean reads beside <see cref="Count"/>.</summary>
	public static int Warnings => Volatile.Read( ref warnings );

	/// <summary>Counts one warning.</summary>
	public static void Warned() => Interlocked.Increment( ref warnings );
}
