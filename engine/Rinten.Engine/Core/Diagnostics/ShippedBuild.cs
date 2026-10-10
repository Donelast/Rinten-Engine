using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Rinten.Diagnostics;

//Warns when a shipped build loads editor-only assemblies (e.g. Roslyn) and names the code that asked
internal static class ShippedBuild
{
	//What a game has no use for. Matched on the start of the simple name, so Microsoft.CodeAnalysis.CSharp
	//is caught by the first of these. The export leaves these out of every build - see IsEditorOnly.
	private static readonly string[] EditorOnly =
	{
		"Microsoft.CodeAnalysis",

		//Not Mono.Cecil. It is the engine's as much as the editor's: a game's package assemblies are resolved through it (PackageAssemblyResolver),
		//and hotloading and networking read types with it - so every game loads it, rightly, and naming it here was a warning about nothing.

		"Rinten.Compiling",
		"Rinten.Tools",

		//The rest of the compiler: the engine's half of it (and the code fixer, which its name catches too), the
		//razor compiler, the solution writer and the source generator the compiler runs. See ProjectCompiling.
		"Rinten.Code",
		"Rinten.Razor",
		"Rinten.SolutionGenerator",
		"Rinten.Generator",

		//The fast hotload's method swapping - ILHotload, in Rinten.Code with the compiler - and the editor's cloud asset index. No game references either.
		"MonoMod",
		"LiteDB",
	};

	//How many of our own frames to name. The first few are the ones that matter.
	private const int FramesNamed = 8;

	private static readonly HashSet<string> said = new( StringComparer.OrdinalIgnoreCase );

	private static bool watching;

	//Starts watching, if this is a build that should not be loading these. Called as early as there is a log to write to.
	internal static void Watch()
	{
		if ( watching ) return;
		if ( !Application.IsStandalone ) return;

		//A release build step, which builds models with the editor's compiler on purpose
		if ( Rinten.Utility.CommandLine.HasSwitch( "-bakemodels" ) ) return;

		watching = true;

		//Listening before looking, so nothing can load between the two unseen.
		AppDomain.CurrentDomain.AssemblyLoad += ( _, e ) => Check( e.LoadedAssembly, new StackTrace( 1, false ) );

		foreach ( var loaded in AppDomain.CurrentDomain.GetAssemblies() )
			Check( loaded, null );
	}

	private static void Check( Assembly assembly, StackTrace loadedBy )
	{
		var name = assembly.GetName().Name;

		if ( name is null || !IsEditorOnly( name ) ) return;
		if ( !said.Add( name ) ) return;

		Log.Warning( $"shipped build loaded {name}, which is an editor assembly - " +
			$"a game compiles nothing and should never map it. {Who( loadedBy )}" );
	}

	//Whether a game has no use for this assembly: the export leaves it out of every build, and a build that loads it anyway says so.
	internal static bool IsEditorOnly( string assemblyName )
	{
		foreach ( var unwanted in EditorOnly )
		{
			if ( assemblyName.StartsWith( unwanted, StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	//The code of ours on the stack when the load happened: the runtime maps an assembly while compiling
	//the first method that names one of its types, so the innermost of these is the caller of that method.
	private static string Who( StackTrace loadedBy )
	{
		if ( loadedBy is null ) return "It was loaded before the watch began.";

		var ours = loadedBy.GetFrames()
			.Select( frame => frame.GetMethod() )
			.Where( method => method?.DeclaringType?.Assembly.GetName().Name is { } owner
				&& owner.StartsWith( "Rinten", StringComparison.Ordinal )
				&& !EditorOnly.Any( x => owner.StartsWith( x, StringComparison.Ordinal ) ) )
			.Take( FramesNamed )
			.Select( method => $"{method.DeclaringType.FullName}.{method.Name}" )
			.ToArray();

		return ours.Length == 0
			? "Nothing of ours was on the stack."
			: "Loaded from:\n  " + string.Join( "\n  ", ours );
	}
}
