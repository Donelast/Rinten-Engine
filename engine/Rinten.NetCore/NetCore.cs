using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

//Entry point for starting .NET from C++; references no dlls since it loads in an isolated context

internal static class NetCore
{
	[UnmanagedCallersOnly]
	public static int InitializeEngine( IntPtr gameFolderPtr )
	{
		//This should contain the minimal amount of logic needed to call InitializeInterop

		var gameFolder = Marshal.PtrToStringUTF8( gameFolderPtr );
		var managedFolder = $"{gameFolder}\\bin\\managed\\";

		var assembly = Assembly.LoadFrom( $"{managedFolder}Rinten.Engine.dll" );
		var type = assembly.GetTypes().Where( x => x.Name == "NetCore" ).FirstOrDefault();
		var method = type.GetMethod( "InitializeInterop", BindingFlags.Static | BindingFlags.NonPublic );

		method.Invoke( null, new object[] { gameFolder } );

		return 0;
	}
}
