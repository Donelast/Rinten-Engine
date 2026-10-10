using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Rinten;

//Probes managed assemblies before startup so policy blocks fail with a clear message.
static class ManagedAssemblyDiagnosis
{
	const int ErrorAccessDisabledByPolicy = unchecked( (int)0x800711C7 );

	public static void ProbeGameInstance()
	{
		if ( !OperatingSystem.IsWindows() ) return;

		var root = Environment.CurrentDirectory;
		var path = Path.Combine( root, "game", "bin", "managed", "Rinten.GameInstance.dll" );
		if ( !File.Exists( path ) )
			path = Path.Combine( root, "bin", "managed", "Rinten.GameInstance.dll" );

		if ( !File.Exists( path ) )
			return;

		try
		{
			Assembly.LoadFrom( path );
		}
		catch ( FileLoadException ex ) when ( ex.HResult == ErrorAccessDisabledByPolicy )
		{
			throw new InvalidOperationException(
				$"Windows Application Control blocked loading Rinten.GameInstance.dll.\n\n" +
				$"Path: {path}\n\n" +
				"Fix: unblock the DLL (Properties → Unblock), or run scripts/unblock-managed.ps1 from the repo root, " +
				"or disable Smart App Control / check WDAC policy for this folder.",
				ex );
		}
	}
}
