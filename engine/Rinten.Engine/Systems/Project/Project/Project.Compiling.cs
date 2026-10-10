using Rinten.Engine;
using System.IO;
using System.Text.Json.Serialization;

namespace Rinten;

public partial class Project
{
	/// <summary>Whether the project's code has a compiler assigned.</summary>
	[JsonIgnore]
	public bool HasCompiler => code?.HasCompiler ?? false;

	//This project's compilers, made the first time it has code to compile, where this process has a compiler at all - see
	//ProjectCompiling. The compilers themselves are Rinten.Code's; editor code reaches them through the Rinten.Tools Project extensions.
	internal ProjectCode code;

	private void UpdateCompiler()
	{
		//Menu can be precompiled, load that and don't bother making a compiler if so
		if ( LoadPrecompiled() )
			return;

		//No code on the disk to compile and no editor to compile any: a built game's projects, whose code ships compiled. There is no compiler to update.
		if ( !HasCodePath() && !Application.IsEditor )
			return;

		UpdateCompilers();
	}

	private void UpdateCompilers()
	{
		code ??= ProjectCompiling.Current?.For( this );

		if ( code is null )
		{
			//Code on disk and nothing here to compile it: a build without the compiler -
			//a web page - given a project's sources. What runs is what shipped compiled.
			if ( !saidNoCompiler )
			{
				saidNoCompiler = true;
				Log.Warning( $"{Config.FullIdent}: its code is here to compile, and this build has no compiler" );
			}

			return;
		}

		code.Update();
	}

	private bool saidNoCompiler;

	//Lets go of this project's compilers, and makes them again on the next update.
	private void ForgetCompilers()
	{
		code?.Forget();
	}

	internal IEnumerable<Package> PackageReferences()
	{
		if ( (Config.Type == "game" || Config.Type == "addon") && !IsBuiltIn )
		{
			foreach ( var library in Project.Libraries.Where( x => x.HasCodePath() ) )
			{
				yield return library.Package;
			}
		}
	}

	//Builds every local project's code. False where it did not build - or where this process has no compiler to build it with.
	internal static Task<bool> CompileAsync() => ProjectCompiling.Current?.CompileAsync() ?? Task.FromResult( false );

	//Last compile's errors as text, without referencing Roslyn types so startup doesn't load Roslyn
	internal static string CompileErrorsText() => ProjectCompiling.Current is { HasGroup: true } compiling ? compiling.ErrorsText() : "";

	//Loads precompiled assemblies that might exist (menu) as long as we're not in editor or headless or anything
	private bool LoadPrecompiled()
	{
		//Don't use precompiled in editor or any sort of headless
		if ( Application.IsEditor || Application.IsUnitTest || Application.IsHeadless )
			return false;

		var assemblyPath = Path.Combine( GetRootPath(), ".bin" );
		if ( !Directory.Exists( assemblyPath ) )
		{
			return false;
		}

		var assemblies = Directory.EnumerateFiles( assemblyPath, "*.dll" );
		if ( assemblies.Count() == 0 )
		{
			return false;
		}

		AssemblyFileSystem.CreateDirectory( "/.bin" );
		foreach ( var assembly in assemblies )
		{
			AssemblyFileSystem.WriteAllBytes( $"/.bin/{Path.GetFileName( assembly )}", File.ReadAllBytes( assembly ) );
		}

		return true;
	}
}
