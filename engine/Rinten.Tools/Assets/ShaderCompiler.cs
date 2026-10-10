using Rinten.Resources;
using System.IO;

namespace Rinten;

/// <summary>Compiles a Slang shader and its skinned/scene variants to SPIR-V, full and portable</summary>
[Expose]
[ResourceIdentity( "shader" )]
internal class ShaderCompiler : ResourceCompiler
{
	protected override Task<bool> Compile()
	{
		var path = Path.GetFullPath( Context.AbsolutePath );

		if ( !CoreShaderCompiler.Available )
		{
			Log.Warning( $"{Context.AbsolutePath}: no shader compiler - slang.dll is missing" );
			return Task.FromResult( false );
		}

		//A legacy-format shader, not Slang: its own format, with modes, features and includes of its HLSL. Refused rather than attempted, and refusing hands it
		//back to the compiler that has always built it - so the two coexist while its half of the engine is still here, and this check goes when they do.
		if ( IsSourceFormat( path ) ) return Task.FromResult( false );

		//Portable-tier SPIR-V for weaker Vulkan devices, surfaces only, built in parallel with the full one
		var portable = CoreResources.IsSurface( path )
			? Task.Run( () => CoreShaderFile.Compile( path, CoreShaderCompiler.Format.SpirvPortable ) )
			: Task.FromResult<byte[][]>( null );

		//Every variant the renderer can ask for, compiled the one way there is to compile them -
		//the same call the renderer makes itself where a project's content has not been built yet.
		var spirv = CoreShaderFile.Compile( path );
		if ( spirv is null )
		{
			Log.Warning( $"{Context.AbsolutePath} did not compile: {CoreRenderDevice.ShaderError}" );
			return Task.FromResult( false );
		}

		//Not the browser's two languages: a web export compiles those itself, as it writes the build - see StandaloneExporter.BuildShaders.
		var file = CoreShaderFile.Write( spirv, null, portable.Result );
		if ( file is null ) return Task.FromResult( false );

		Context.Data.Write( file );
		return Task.FromResult( true );
	}

	//Whether this is a legacy-format shader rather than Slang, judged by content, not location
	private static bool IsSourceFormat( string path ) => CoreShaderFile.IsForeign( path );

}
