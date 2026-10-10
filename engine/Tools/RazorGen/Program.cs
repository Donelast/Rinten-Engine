using System;
using System.IO;
using System.Linq;

namespace Rinten.RazorGen;

//Build step: compiles an engine project's .razor files to C# in obj/.razor before compile
internal static class Program
{
	private static int Main( string[] args )
	{
		var root = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
		var rootNamespace = args.Length > 1 ? args[1] : null;

		try
		{
			Generate( root, rootNamespace );
			return 0;
		}
		catch ( Exception e )
		{
			//One line the compiler's output can carry, since this runs inside a build and nobody is watching a console.
			Console.Error.WriteLine( $"RazorGen: {e.Message}" );
			return 1;
		}
	}

	//Usings every panel needs; engine projects lack a game's global usings, so they're added here
	private static readonly string[] Preamble =
	{
		"System",
		"System.Collections.Generic",
		"System.Linq",
		"System.Threading.Tasks",
		"Rinten",
		"Rinten.UI",
		"Microsoft.AspNetCore.Components",
	};

	//The markup with whatever of the preamble it did not already say. Saying it twice is an error rather
	//than a shrug, so a file that imports one of these itself keeps its own line and gets the rest.
	private static string WithPreamble( string text )
	{
		var lines = text.Split( '\n' ).Select( x => x.Trim() ).ToArray();

		var missing = Preamble
			.Where( x => !lines.Contains( $"@using {x}" ) )
			.Select( x => $"@using {x}" );

		var head = string.Join( "\n", missing );

		return string.IsNullOrEmpty( head ) ? text : head + "\n" + text;
	}

	private static void Generate( string root, string rootNamespace )
	{
		var into = Path.Combine( root, "obj", ".razor" );

		Directory.CreateDirectory( into );

		var sources = Directory
			.EnumerateFiles( root, "*.razor", SearchOption.AllDirectories )
			.Where( x => !x.Replace( '\\', '/' ).Contains( "/obj/" ) )
			.Where( x => !x.Replace( '\\', '/' ).Contains( "/bin/" ) )
			.ToArray();

		var written = new System.Collections.Generic.HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var source in sources )
		{
			var local = Path.GetRelativePath( root, source ).Replace( '\\', '/' );
			var text = WithPreamble( File.ReadAllText( source ) );

			//The full path, because the generated C# points back at the markup with #line directives, and the
			//compiler resolves those against wherever the generated file sits rather than against the project.
			var generated = Rinten.Razor.RazorProcessor.GenerateFromSource(
				text, Path.GetFullPath( source ), rootNamespace );

			//One flat file per razor, named after where it came from, so that two files of the same name in different folders do not collide.
			var name = local.Replace( '/', '.' ) + ".g.cs";
			var target = Path.Combine( into, name );

			written.Add( target );

			//Only when it actually changed: rewriting every file every build makes the compiler rebuild everything that reads them.
			if ( File.Exists( target ) && File.ReadAllText( target ) == generated ) continue;

			File.WriteAllText( target, generated );
		}

		//A razor file that has been deleted leaves its C# behind, and the project would go on compiling a panel nobody has the markup for.
		foreach ( var stale in Directory.EnumerateFiles( into, "*.g.cs" ) )
		{
			if ( written.Contains( stale ) ) continue;

			File.Delete( stale );
		}

		Console.WriteLine( $"RazorGen: {sources.Length} razor file(s)" );
	}
}
