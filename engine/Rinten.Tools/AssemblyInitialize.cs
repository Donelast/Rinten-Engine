global using Editor;
global using Rinten;
global using Rinten.Diagnostics;
global using System.Collections;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using static Rinten.Internal.GlobalGameNamespace;
global using static Rinten.Internal.GlobalToolsNamespace;
using Rinten.Engine;
using Rinten.Internal;
using System.Reflection;



namespace Editor;

//Called before anything else. The only purpose of this is to load the native dlls
//and swap function pointers with them. We should not be doing anything else here.
internal static class AssemblyInitialize
{
	public static void Initialize()
	{
		RintenCode.Install();
		IToolsDll.Current = new ToolsDll();
	}

	public static void InitializeUnitTest( System.Reflection.Assembly callingAssembly )
	{
		Initialize();

		var callerName = callingAssembly.GetName().Name;

		//Set up TypeLibrary with data from our base assembly and game assemblies

		Game.TypeLibrary = new TypeLibrary();
		Game.TypeLibrary.AddIntrinsicTypes();
		Game.TypeLibrary.AddAssembly( Assembly.Load( "Rinten.System" ), false );
		Game.TypeLibrary.AddAssembly( Assembly.Load( "Rinten.Engine" ), false );

		try
		{
			var gameDll = callerName.Replace( ".unittest", "" );
			var gameAssembly = Assembly.Load( gameDll );
			if ( gameAssembly is null ) System.Console.Error.Write( $"Couldn't find [{gameAssembly}.dll]" );

			Game.TypeLibrary.AddAssembly( Assembly.Load( "Base Library" ), true );
			Game.TypeLibrary.AddAssembly( Assembly.Load( gameDll ), true );
		}
		catch ( System.Exception )
		{
			// ignore - we can only load these dlls in unit tests in addon sln
		}

		Json.Initialize();
	}
}
