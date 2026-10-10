using Rinten.DataModel;

namespace Rinten;

//What a project's config says about compiling its code.
internal static class ProjectConfigCompiling
{
	//The compiler settings a project's config carries, cleaned - the defaults where it carries none.
	internal static Compiler.Configuration GetCompileSettings( this ProjectConfig config )
	{
		if ( !config.TryGetMeta<Compiler.Configuration>( "Compiler", out var compilerSettings ) )
		{
			compilerSettings = new Compiler.Configuration();
		}

		compilerSettings.Clean();

		return compilerSettings;
	}
}
