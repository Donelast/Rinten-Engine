namespace Rinten;

//No-op Skia handle lock: single-threaded wasm runs finalizers inline, so re-entry must not throw
internal sealed class SkiaLock : SkiaSharp.Internals.IPlatformLock
{
	public void EnterReadLock() { }
	public void ExitReadLock() { }
	public void EnterWriteLock() { }
	public void ExitWriteLock() { }
	public void EnterUpgradeableReadLock() { }
	public void ExitUpgradeableReadLock() { }

	//Install before Skia is touched; presize the handle table so it never grows mid-finalizer
	[System.Diagnostics.CodeAnalysis.DynamicDependency( System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields, "SkiaSharp.HandleDictionary", "SkiaSharp" )]
	internal static void Install()
	{
		SkiaSharp.Internals.PlatformLock.Factory = static () => new SkiaLock();

		var table = typeof( SkiaSharp.SKObject ).Assembly.GetType( "SkiaSharp.HandleDictionary" )?
			.GetField( "instances", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );

		if ( table?.GetValue( null ) is System.Collections.Generic.Dictionary<System.IntPtr, System.WeakReference> instances )
			instances.EnsureCapacity( 1 << 16 );
		else
			System.Console.WriteLine( "Skia's handle table was not found - it can grow under a finalizer" );
	}
}
