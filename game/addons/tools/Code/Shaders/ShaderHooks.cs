using System.Diagnostics;
using System.Threading;

namespace Editor;

internal static partial class ShaderHooks
{
	[Event( "content.changed" )]
	public static void OnShaderWasEdited( string filename )
	{
		//only respond to shaders that changed todo if a hlsl changed we could look at what files include it and recompile those that would be genuinely useful

		//Idea: only recompile changed shaders, or only the combos in use by loaded materials
		//ConsoleSystem.Run( $"mat_reloadshaders {shaderName}" );

		//Compile the shaders - in fast mode
		CompileShader( filename.Trim( '/' ) );
	}

	private static ICodeEditor _editor;
	private static ICodeEditor Editor
	{
		get
		{
			if ( _editor != null )
				return _editor;

			var editor = new CodeEditors.VisualStudioCode();
			if ( editor.IsInstalled() )
			{
				_editor = editor;
				return editor;
			}

			return null;
		}
	}

	[Event( "open.shader" )]
	public static void OpenShader( string filename )
	{
		Editor?.OpenFile( filename );
	}

	static CancellationTokenSource cts;

	[Event( "compile.shader" )]
	public static void CompileShader( string shader )
	{
		if ( !FileSystem.Mounted.FileExists( shader ) ) return;
		if ( !shader.EndsWith( ".shader" ) ) return;

		cts?.Cancel();
		cts?.Dispose();
		cts = new CancellationTokenSource();

		_ = CompileShader( shader, cts.Token );
	}

	static async Task CompileShader( string file, CancellationToken token )
	{
		Log.Info( $"Compiling: {file}" );

		var sw = Stopwatch.StartNew();

		var asset = AssetSystem.FindByPath( file );

		if ( asset is null )
		{
			Log.Warning( $"{file} is not an asset" );
			return;
		}

		//Shaders rebuild from source when next drawn, so no builder is normal; warn only on refusal
		if ( !asset.CanRecompile )
		{
			Log.Info( $"{file} will be compiled again from source when next drawn" );
			return;
		}

		if ( !asset.Compile( full: true ) )
		{
			Log.Warning( $"Shader compile failed after {sw.Elapsed.TotalMilliseconds:0.00}ms" );
			return;
		}

		Log.Info( $"Done in {sw.Elapsed.TotalMilliseconds:0.00}ms" );

		await Task.CompletedTask;
	}
}
