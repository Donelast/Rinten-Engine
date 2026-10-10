using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Rinten;

//Compiled shader blob: SPIR-V and WGSL per stage, all variants of one source
internal static partial class CoreShaderFile
{
	/// <summary>'ESSS', little end first - a shader, as against 'ESSM' a model.</summary>
	public const uint Magic = 0x53535345;

	/// <summary>Blob layout version; older or ambiguous versions are refused and rebuilt</summary>
	public const uint Version = 20;


	/// <summary>Which stage a blob is.</summary>
	public enum Stage
	{
		Vertex = 0,
		Pixel = 1,
		SkinnedVertex = 2,
		SkinnedPixel = 3,

		/// <summary>The pixel stage again, writing the world's second attachment: the normal and how much of a mirror the surface is, for the passes that come after the world and never saw it drawn.</summary>
		ScenePixel = 4,
		SkinnedScenePixel = 5,

		//Compute stage, optional: a shader may declare MainCs, MainVs, or both
		/// <summary>A pass that runs rather than draws: <c>MainCs</c>, with its own <c>[numthreads]</c>.</summary>
		Compute = 6,

		//Depth pre-pass uses the surface's own vertex stage (e.g. terrain heights), built with DEPTH_ONLY
		/// <summary>The pixel stage with nothing to write: what the depth pre-pass runs.</summary>
		DepthPixel = 7,

		//Separate stage so models without blend shapes pay no extra binding or branch; rigid only
		/// <summary>The vertex stage with the model's blend shapes on top.</summary>
		MorphedVertex = 8,

		/// <summary>The same, on a model that is rigged as well as sculpted.</summary>
		MorphedSkinnedVertex = 9,

		/// <summary>Cluster pass stages for world surfaces, generated around the surface's Vertex and Shade</summary>
		ClusterVertex = 10,
		ClusterPixel = 11,

		//A separate stage rather than a branch: what a pixel stage writes is baked into it, and a shader compiled for two attachments cannot be handed one.
		/// <summary>The cluster pixel stage again for a frame that shades straight onto the screen: one attachment, eight bits a channel, and the curve applied here because there is no pass afterwards to apply it - see TARGET_SCREEN in frame.slang and CoreSceneDrawer's wantsOffscreen.</summary>
		ClusterScreenPixel = 12,

		//Not Pixel with a define, because Pixel is what every pass that is not the world's picture already runs - a mask, a preview, the editor's overlay over a finished frame - and none of those wants the curve applied a second time.
		/// <summary>And the classic pixel stages for the same frame: what the sky and everything drawn loose beside the clusters run when the world is being shaded straight onto the screen.</summary>
		ScreenPixel = 13,
		SkinnedScreenPixel = 14,

		//Separate stages because a stage bakes in where its draw numbers come from; portable frame only
		/// <summary>Portable frame instanced draw: the surface's Vertex and Shade over copies from the bone ring</summary>
		InstancedVertex = 15,
		InstancedScreenPixel = 16,
		InstancedDepthPixel = 17,

		//Only sky shaders have these, so the sky lighting and backdrop come from one function
		/// <summary>The pass that fills the sky's table: <c>MainSkyTableVs</c> and <c>MainSkyTablePs</c>, the engine's entries round a sky's own <c>SkyLight</c> - see sky_surface.slang.</summary>
		SkyTableVertex = 18,
		SkyTablePixel = 19,

		//Lets alpha-cut surfaces write depth first so the shading pass can use early depth testing
		/// <summary>The cluster pass's pixel stage with <c>DEPTH_ONLY</c> defined: the same MainClusterPs, run for a pre-pass that writes depth and nothing else.</summary>
		ClusterDepthPixel = 20,

	}

	public const int StageCount = 21;

	//How many languages a stage is kept in: the full frame's SPIR-V, WGSL, the portable
	//frame's SPIR-V, GLSL, and the GLSL that draws both of a headset's eyes at once.
	private const int RunsPerStage = 5;

	//Magic, version, then an offset and a size per run of each stage - see RunOf.
	private const int HeaderSize = 8 + ( StageCount * RunsPerStage * 8 );

	//Which of a stage's runs a format is kept in.
	private static int RunOf( CoreShaderCompiler.Format format ) => format switch
	{
		CoreShaderCompiler.Format.Wgsl => 1,
		CoreShaderCompiler.Format.SpirvPortable => 2,
		CoreShaderCompiler.Format.Glsl => 3,
		CoreShaderCompiler.Format.GlslStereo => 4,
		_ => 0,
	};

	//What has to be defined for each of them.
	private static string Defines( Stage stage )
	{
		var morphed = stage is Stage.MorphedVertex or Stage.MorphedSkinnedVertex;
		var skinned = stage is Stage.SkinnedVertex or Stage.SkinnedPixel
			or Stage.SkinnedScenePixel or Stage.MorphedSkinnedVertex or Stage.SkinnedScreenPixel;
		var scene = stage is Stage.ScenePixel or Stage.SkinnedScenePixel;
		var compute = stage is Stage.Compute;
		var depth = stage is Stage.DepthPixel or Stage.InstancedDepthPixel or Stage.ClusterDepthPixel;
		var cluster = IsCluster( stage );
		var instanced = IsInstanced( stage );

		//The stages that finish the picture themselves: one attachment, eight bits a channel, and the exposure and the curve applied here because
		//there is no compose behind them - see CoreSceneDrawer, which draws this way only when nothing is going to read the world's picture.
		var screen = stage is Stage.ClusterScreenPixel or Stage.ScreenPixel or Stage.SkinnedScreenPixel
			or Stage.InstancedScreenPixel;

		return ( skinned ? "#define SKINNED 1\n" : "" ) +
			( cluster ? "#define CLUSTER 1\n" : "" ) +
			( instanced ? "#define INSTANCED 1\n" : "" ) +
			( cluster && !screen && !depth ? "#define TARGET_SCENE 1\n" : "" ) +
			( screen ? "#define TARGET_SCREEN 1\n" : "" ) +
			( morphed ? "#define MORPHED 1\n" : "" ) +
			( scene ? "#define TARGET_SCENE 1\n" : "" ) +
			( compute ? "#define COMPUTE 1\n" : "" ) +
			( depth ? "#define DEPTH_ONLY 1\n" : "" );
	}

	/// <summary>Whether a stage is a vertex stage.</summary>
	public static bool IsVertex( Stage stage )
		=> stage is Stage.Vertex or Stage.SkinnedVertex
			or Stage.MorphedVertex or Stage.MorphedSkinnedVertex;

	/// <summary>Whether a stage runs rather than draws.</summary>
	public static bool IsCompute( Stage stage ) => stage is Stage.Compute;

	/// <summary>Whether a stage is one of the cluster pass's.</summary>
	public static bool IsCluster( Stage stage )
		=> stage is Stage.ClusterVertex or Stage.ClusterPixel or Stage.ClusterScreenPixel
			or Stage.ClusterDepthPixel;

	/// <summary>Whether a stage is one of the portable frame's instanced draw's.</summary>
	public static bool IsInstanced( Stage stage )
		=> stage is Stage.InstancedVertex or Stage.InstancedScreenPixel or Stage.InstancedDepthPixel;

	/// <summary>Whether a stage is one of the sky table's - see sky_surface.slang.</summary>
	public static bool IsSkyTable( Stage stage )
		=> stage is Stage.SkyTableVertex or Stage.SkyTablePixel;

	//Appended after the surface text so entries see its declarations and error line numbers still match
	private const string ClusterSurface = "\n#include \"cluster_surface.slang\"\n";

	//The same for the instanced stages - see instanced_surface.slang.
	private const string InstancedSurface = "\n#include \"instanced_surface.slang\"\n";

	//And for the sky's table, round the sky's own SkyLight - see sky_surface.slang.
	private const string SkySurface = "\n#include \"engine/sky_surface.slang\"\n";

	//Whether a shader file is a pass over the frame - written against postprocess.slang - which as a compute shader is bound as a blit is. What
	//a pass's outputs declared in "compact" are compiled as, where not the device's own - the bake compiles both, since it has no device to ask.
	internal static string CompactName { get; set; }

	internal static bool IsPass( string resourcePath )
		=> Source( Real( resourcePath ) ) is { } found && IncludesFile( found, "postprocess.slang" );

	//Whether a file includes another, by its name: an include directive, not the name anywhere in
	//the text - a surface whose comment says "see postprocess.slang" is not a pass over the frame.
	internal static bool IncludesFile( string path, string file )
	{
		foreach ( System.Text.RegularExpressions.Match m in IncludeDirective().Matches( Declared( path ) ) )
		{
			if ( string.Equals( Path.GetFileName( m.Groups[1].Value ), file, StringComparison.Ordinal ) ) return true;
		}

		return false;
	}

	//Whether a file declares - or calls - a function of this name, comments aside.
	internal static bool Declares( string path, string name )
		=> System.Text.RegularExpressions.Regex.IsMatch( Declared( path ),
			@"\b" + System.Text.RegularExpressions.Regex.Escape( name ) + @"\s*\(" );

	//A file's text with its comments blanked, read once while the file is unchanged. The path is the file on disk, the same one Build reads.
	private static string Declared( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return "";

		try
		{
			if ( !File.Exists( path ) ) return "";

			var written = File.GetLastWriteTimeUtc( path );

			lock ( declared )
			{
				if ( declared.TryGetValue( path, out var kept ) && kept.Written == written ) return kept.Text;
			}

			var text = WithoutComments( CompiledFile.TextOf( path ) );

			lock ( declared ) declared[path] = (written, text);

			return text;
		}
		catch ( IOException )
		{
			return "";
		}
	}

	private static readonly Dictionary<string, (DateTime Written, string Text)> declared = new();

	[System.Text.RegularExpressions.GeneratedRegex( @"^[ \t]*#[ \t]*include[ \t]*[""<]([^"">]+)["">]", System.Text.RegularExpressions.RegexOptions.Multiline )]
	private static partial System.Text.RegularExpressions.Regex IncludeDirective();

	//What a stage is called in the file, and what the core calls it.
	private static (string Entry, CoreShaderCompiler.Stage Kind) Which( Stage stage )
	{
		if ( IsCompute( stage ) ) return ( "MainCs", CoreShaderCompiler.Stage.Compute );

		if ( stage is Stage.ClusterVertex ) return ( "MainClusterVs", CoreShaderCompiler.Stage.Vertex );
		if ( stage is Stage.ClusterPixel or Stage.ClusterScreenPixel or Stage.ClusterDepthPixel )
			return ( "MainClusterPs", CoreShaderCompiler.Stage.Pixel );

		if ( stage is Stage.InstancedVertex ) return ( "MainInstancedVs", CoreShaderCompiler.Stage.Vertex );
		if ( stage is Stage.InstancedScreenPixel or Stage.InstancedDepthPixel )
			return ( "MainInstancedPs", CoreShaderCompiler.Stage.Pixel );

		if ( stage is Stage.SkyTableVertex ) return ( "MainSkyTableVs", CoreShaderCompiler.Stage.Vertex );
		if ( stage is Stage.SkyTablePixel ) return ( "MainSkyTablePs", CoreShaderCompiler.Stage.Pixel );

		return IsVertex( stage )
			? ( "MainVs", CoreShaderCompiler.Stage.Vertex )
			: ( "MainPs", CoreShaderCompiler.Stage.Pixel );
	}

	/// <summary>A written shader, compiled into every stage it can serve.</summary>
	public static byte[][] Compile( string path )
		=> Compile( path, CoreShaderCache.Target );

	/// <summary>The six stages in one language, or null if the shader does not compile at all.</summary>
	public static byte[][] Compile( string path, CoreShaderCompiler.Format format, string combo = null )
	{
		//What every stage is written in front of: the features, and the block the shader reads its own numbers from - see Preludes.
		var (draws, runs) = Preludes( path, combo );

		//No compiler is not the end of it: every stage is looked for in the cache first, and only a miss needs one - see CoreShaderCache.Words.
		if ( !CoreNative.Available || string.IsNullOrWhiteSpace( path ) ) return null;

		//World surfaces always get the engine-written cluster stages; non-surfaces have no Vertex for them
		var surface = CoreResources.IsSurface( path );

		//What a machine with no compiler does with a combination that was never baked - see the tail of this function.
		var instead = string.IsNullOrEmpty( combo ) ? null : "drawn with the shader's own features instead";

		//The build, not just the file. A machine with no compiler says once per name what it has not got, and every combination of one shader shares its
		//path: "complex.shader is not in the cache" was said once however many of its builds were missing, and named none of them. See CoreShaderCache.
		var build = string.IsNullOrEmpty( combo ) ? path : $"{path} ({combo})";

		//Every stage at once, on the compile threads: none reads another's words, and one after another they were a quarter of a second each - see
		//CoreShaderThreads. The pixel stages of a pair that has a vertex stage of its own wait for it instead, and are not compiled where it failed.
		var compiling = new Task<(byte[] Words, string Error)>[StageCount];
		var none = Task.FromResult<(byte[] Words, string Error)>( (null, null) );

		for ( var i = 0; i < StageCount; i++ )
		{
			var stage = (Stage)i;

			var (entry, kind) = Which( stage );

			//The portable frame draws a part at a time and computes nothing - see RenderTier - so it has no cluster stages
			//and no compute stage to build, and a surface written for both frames is not broken for lacking them there.
			if ( CoreShaderCompiler.IsPortable( format ) && ( IsCluster( stage ) || IsCompute( stage ) ) )
			{
				compiling[i] = none;
				continue;
			}

			//And the other way round for the instanced pair: they are the portable frame's answer to a field of copies, and the full frame draws one
			//off its instance table as clusters. Built for the portable languages only, so a desktop build carries neither the stages nor the bake.
			if ( IsInstanced( stage ) )
			{
				compiling[i] = surface && CoreShaderCompiler.IsPortable( format )
					? After( stage is Stage.InstancedVertex ? null : compiling[(int)Stage.InstancedVertex],
						() => Build( path, entry, kind, draws + Defines( stage ), format, InstancedSurface, instead, build ) )
					: none;

				continue;
			}

			//And the sky's table, for a file that says it is a sky. The entries are the engine's, round the one function a sky
			//declares - see sky_surface.slang - so a sky shader carries no pass of its own and there is nothing for one to leave out.
			if ( IsSkyTable( stage ) )
			{
				compiling[i] = CoreResources.IsSky( path )
					? After( stage is Stage.SkyTableVertex ? null : compiling[(int)Stage.SkyTableVertex],
						() => Build( path, entry, kind, draws + Defines( stage ), format, SkySurface, instead, build ) )
					: none;

				continue;
			}

			if ( IsCluster( stage ) )
			{
				//Without the vertex stage the pass cannot be drawn whatever the pixel stages would do, so they are not built for nothing.
				compiling[i] = surface
					? After( stage is Stage.ClusterVertex ? null : compiling[(int)Stage.ClusterVertex],
						() => Build( path, entry, kind, draws + Defines( stage ), format, ClusterSurface, instead, build ) )
					: none;

				continue;
			}

			//A pixel or compute entry the file does not declare is not there to be found - the vertex one may come from an include, postprocess.slang's. Tried
			//anyway it is a compile per stage for nothing, every run, and on a machine with no compiler a warning that something is missing when nothing is.
			if ( !IsVertex( stage ) && !Declares( path, entry ) )
			{
				compiling[i] = none;
				continue;
			}

			compiling[i] = After( null,
				() => Build( path, entry, kind, ( IsCompute( stage ) ? runs : draws ) + Defines( stage ), format, "", instead, build ) );
		}

		Task.WaitAll( compiling );

		var stages = new byte[StageCount][];
		for ( var i = 0; i < StageCount; i++ ) stages[i] = compiling[i].Result.Words;

		//Why it failed, said on the thread that asked - the compile that failed said it on its own. The
		//first stage to fail, which is the one a shader that does not compile at all fails in first.
		if ( compiling.Select( x => x.Result.Error ).FirstOrDefault( x => !string.IsNullOrEmpty( x ) ) is { } why )
			CoreShaderCompiler.Failed( why );

		//A shader is either something that draws or something that runs. The rigid pair has to compile for the first; the compute stage on its
		//own is the second, which is what a mesher or a cull pass is - and asking it for a vertex stage it never had is not a broken shader.
		var drawn = stages[(int)Stage.Vertex] is not null && stages[(int)Stage.Pixel] is not null;

		//And a pass over the frame whose entries all have names of their own - a bloom's three passes in one
		//file - has neither of the plain ones, only the vertex stage every such pass shares. See EntryWords.
		var named = stages[(int)Stage.Vertex] is not null && IncludesFile( path, "postprocess.slang" );

		var built = drawn || named || stages[(int)Stage.Compute] is not null ? stages : null;

		if ( built is not null || instead is null || CoreShaderCompiler.Available ) return built;

		//No compiler and an unbaked combo: fall back to the default-feature build rather than not drawing
		return Compile( path, format );
	}

	//One stage's compile on the compile threads, with why it failed. Given a stage
	//to wait for, it waits, and is not compiled at all where that one made nothing.
	private static Task<(byte[] Words, string Error)> After( Task<(byte[] Words, string Error)> first, Func<byte[]> compile )
	{
		(byte[] Words, string Error) Run()
		{
			var words = compile();
			return (words, words is null ? CoreShaderCompiler.LastError : null);
		}

		if ( first is null ) return CoreShaderThreads.Run( Run );

		return first.ContinueWith( made => made.Result.Words is null
				? Task.FromResult<(byte[] Words, string Error)>( (null, null) )
				: CoreShaderThreads.Run( Run ),
			TaskContinuationOptions.ExecuteSynchronously ).Unwrap();
	}

	//Text prepended to every stage: feature defines, then the parameter block (push or material)
	private static (string Draws, string Runs) Preludes( string path, string combo )
	{
		var features = CoreShaderFeatures.DefinesFor( CoreShaderFeatures.Of( path ), combo );
		var parameters = CoreShaderParams.Of( path );

		if ( !IncludesFile( path, "postprocess.slang" ) )
			return ( features + CoreShaderParams.Declare( parameters ), features + CoreShaderParams.DeclarePush( parameters ) );

		//What "compact" is on this device, for an output declared in it.
		var pass = features + CoreShaderParams.DeclarePost( parameters ) +
			$"#define ENGINE_COMPACT_FORMAT \"{CompactName ?? CoreRenderDevice.CompactStorageName}\"\n" +
			CoreShaderParams.DeclareOutputs( CoreShaderParams.OutputsOf( path ) );

		return ( pass, pass );
	}

	//Compiles one named entry of a multi-pass file, with the same preludes; null if unavailable
	internal static byte[] EntryWords( string path, string combo, string entry, CoreShaderCompiler.Stage kind,
		CoreShaderCompiler.Format format = CoreShaderCompiler.Format.Spirv )
	{
		if ( !CoreNative.Available || string.IsNullOrWhiteSpace( path ) || string.IsNullOrWhiteSpace( entry ) ) return null;

		var (draws, runs) = Preludes( path, combo );

		return kind == CoreShaderCompiler.Stage.Compute
			? Build( path, entry, kind, runs + Defines( Stage.Compute ), format )
			: Build( path, entry, kind, draws, format );
	}

	//The entries a file declares beyond the ones every file has, and what each is. Read off the text: a fragment entry is what follows [shader("fragment")]
	//, a compute one what follows [numthreads(...)] . MainVs, MainPs and MainCs are the file's ordinary stages and are left to Compile.
	internal static (string Entry, CoreShaderCompiler.Stage Kind)[] EntriesOf( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) ) return Array.Empty<(string, CoreShaderCompiler.Stage)>();

		string text;

		try
		{
			text = WithoutComments( CompiledFile.TextOf( path ) );
		}
		catch ( IOException )
		{
			return Array.Empty<(string, CoreShaderCompiler.Stage)>();
		}

		var found = new List<(string, CoreShaderCompiler.Stage)>();

		foreach ( System.Text.RegularExpressions.Match m in FragmentEntry().Matches( text ) )
			Add( m.Groups[1].Value, CoreShaderCompiler.Stage.Pixel );

		foreach ( System.Text.RegularExpressions.Match m in ComputeEntry().Matches( text ) )
			Add( m.Groups[1].Value, CoreShaderCompiler.Stage.Compute );

		return found.ToArray();

		void Add( string name, CoreShaderCompiler.Stage kind )
		{
			if ( name is "MainVs" or "MainPs" or "MainCs" ) return;
			if ( found.Exists( x => x.Item1 == name ) ) return;

			found.Add( ( name, kind ) );
		}
	}

	[System.Text.RegularExpressions.GeneratedRegex( @"\[shader\(\s*""(?:fragment|pixel)""\s*\)\](?:\s*\[[^\]]*\])*\s*\w+\s+(\w+)\s*\(" )]
	private static partial System.Text.RegularExpressions.Regex FragmentEntry();

	[System.Text.RegularExpressions.GeneratedRegex( @"\[numthreads\s*\([^)]*\)\](?:\s*\[[^\]]*\])*\s*void\s+(\w+)\s*\(" )]
	private static partial System.Text.RegularExpressions.Regex ComputeEntry();

	//How wide one group of a compute entry's threads is, out of the [numthreads] in front of it. One each where it
	//says nothing, which turns a thread count into the same number of groups - wrong, but not silently nothing.
	internal static (int X, int Y, int Z) ThreadGroupOf( string path, string entry )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) ) return (1, 1, 1);

		try
		{
			var text = WithoutComments( CompiledFile.TextOf( path ) );

			foreach ( System.Text.RegularExpressions.Match m in ComputeGroup().Matches( text ) )
			{
				if ( entry is not null && m.Groups[4].Value != entry ) continue;

				return (CountOf( m.Groups[1].Value ), CountOf( m.Groups[2].Value ), CountOf( m.Groups[3].Value ));
			}
		}
		catch ( IOException )
		{
		}

		return (1, 1, 1);

	}

	[System.Text.RegularExpressions.GeneratedRegex( @"\[numthreads\s*\(\s*([^,\)]+),\s*([^,\)]+),\s*([^,\)]+)\)\](?:\s*\[[^\]]*\])*\s*void\s+(\w+)\s*\(" )]
	private static partial System.Text.RegularExpressions.Regex ComputeGroup();

	//Compute thread group size read from the compiled code; null if the code does not state it
	internal static (int X, int Y, int Z)? ThreadGroupOf( byte[] code )
	{
		if ( code is null || code.Length < 20 ) return null;

		var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( code.AsSpan( 0, code.Length & ~3 ) );

		if ( words[0] == 0x07230203 )
		{
			//Past the five words of header, one instruction after another: its length in words in the top half of its first word, what it is in the bottom half.
			for ( var at = 5; at < words.Length; )
			{
				var count = (int)( words[at] >> 16 );
				var opcode = words[at] & 0xFFFF;

				if ( count == 0 ) break;

				//OpExecutionMode( entry, LocalSize, x, y, z ).
				if ( opcode == 16 && count >= 6 && at + 5 < words.Length && words[at + 2] == 17 )
					return ((int)Math.Max( words[at + 3], 1u ), (int)Math.Max( words[at + 4], 1u ), (int)Math.Max( words[at + 5], 1u ));

				at += count;
			}

			return null;
		}

		var size = WorkgroupSize().Match( System.Text.Encoding.UTF8.GetString( code ) );

		return size.Success
			? (CountOf( size.Groups[1].Value ), CountOf( size.Groups[2].Value ), CountOf( size.Groups[3].Value ))
			: null;
	}

	private static int CountOf( string written )
		=> int.TryParse( written.Trim(), out var value ) && value > 0 ? value : 1;

	[System.Text.RegularExpressions.GeneratedRegex( @"@workgroup_size\s*\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?(?:,\s*(\d+)\s*)?\)" )]
	private static partial System.Text.RegularExpressions.Regex WorkgroupSize();

	//What is wrong with a file that has a world surface's two functions and is not one - or null. Such a file draws nothing and nothing else says
	//why: the engine builds the cluster pass only around a file that says it is a surface. See CoreResources.IsSurface and cluster_surface.slang.
	internal static string SurfaceDoubt( string path )
	{
		var text = Declared( path );

		if ( !SurfaceVertex().IsMatch( text ) || !SurfaceShade().IsMatch( text ) ) return null;
		if ( CoreResources.IsSurface( path ) ) return null;

		return $"{path} declares a surface's Vertex( VIn ) and Shade( V2F, bool ) and not what makes it one - " +
			"#define ENGINE_WORLD_PUSH before frame.slang, and its interpolants declared with DEPTH_ID_FIELD - " +
			"so no pass is built around it and nothing wearing it is drawn. See cluster_surface.slang.";
	}

	[System.Text.RegularExpressions.GeneratedRegex( @"\b\w+\s+Vertex\s*\(\s*VIn\b" )]
	private static partial System.Text.RegularExpressions.Regex SurfaceVertex();

	[System.Text.RegularExpressions.GeneratedRegex( @"\b\w+\s+Shade\s*\(\s*V2F\b" )]
	private static partial System.Text.RegularExpressions.Regex SurfaceShade();

	//A file's text with its comments blanked, so a word in a comment is not a declaration.
	private static string WithoutComments( string text )
	{
		text = System.Text.RegularExpressions.Regex.Replace( text, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline );
		return System.Text.RegularExpressions.Regex.Replace( text, @"//[^\n]*", "" );
	}

	//How many projects the search paths were last told about.
	private static int lookedAt = -1;

	//Whether the engine's own shader folder has been found yet.
	private static bool ours;

	//Registers include paths: engine shader library and every open project's shader folder
	internal static void Looking()
	{
		var projects = Project.All?.Count ?? 0;

		//Re-add engine paths each compile until found (cheap; the core dedups); projects only on count change
		var again = projects != lookedAt || !ours;

		lookedAt = projects;

		//Engine folders first, plus their parents, so includes by resource path resolve too
		foreach ( var folder in new[] { "/core/shaders", "/addons/base/Assets/shaders", "/core", "/addons/base/Assets" } )
		{
			try
			{
				var full = EngineFileSystem.Root?.GetFullPath( folder );

				if ( string.IsNullOrEmpty( full ) ) continue;

				CoreShaderCompiler.AddSearchPath( full );
				ours = true;
			}
			catch ( System.Exception )
			{
				//A host with no engine filesystem - a test - has no library to point at, and a shader that includes nothing does not need one.
			}
		}

		//Engine shader folder from disk when nothing is mounted, so shaders compile in tests and tools
		var beside = Path.GetDirectoryName( Shipped( "shaders/postprocess.slang" ) ?? "" );

		if ( !string.IsNullOrEmpty( beside ) )
		{
			CoreShaderCompiler.AddSearchPath( beside );

			if ( Path.GetDirectoryName( beside ) is { Length: > 0 } assets )
				CoreShaderCompiler.AddSearchPath( assets );

			ours = true;
		}

		if ( !again || Project.All is null ) return;

		foreach ( var project in Project.All )
		{
			if ( project is null || !project.HasAssetsPath() ) continue;

			//Project folders go in front of the engine's so a project can override engine includes
			CoreShaderCompiler.AddSearchPath( project.GetAssetsPath(), first: true );
			CoreShaderCompiler.AddSearchPath(
				System.IO.Path.Combine( project.GetAssetsPath(), "shaders" ), first: true );
		}
	}

	//One stage, compiled. Anything with something defined goes through the source with the defines on the front - in memory, because a temporary file beside
	//the original would appear in an asset folder the editor watches, and one in a temporary directory would break every relative include in the file.
	private static byte[] Build( string path, string entry, CoreShaderCompiler.Stage stage, string defines,
		CoreShaderCompiler.Format format = CoreShaderCompiler.Format.Spirv, string after = "", string instead = null,
		string name = null )
	{
		try
		{
			//Neither block is Slang and neither reaches the compiler. Cut rather than removed,
			//so the line numbers in an error still point at the line somebody is looking at.
			var source = CoreShaderFeatures.Without( CoreShaderFeatures.Without(
				CoreShaderFeatures.Without( CompiledFile.TextOf( path ) ),
				CoreShaderParams.BlockName ), CoreShaderState.BlockName );

			//Whether the target has specialization constants at all - SPIR-V, and a page's WGSL and GLSL too; see HasSpecialization.
			//A pipeline baked from it may then be told what a branch is going to answer - see kProbesOn in frame.slang.
			var specialized = CoreShaderCompiler.HasSpecialization( format )
				? "#define SPECIALIZED 1\n"
				: "";

			return CoreShaderCache.Words( specialized + defines + source + after,
				Path.GetDirectoryName( path ), entry, stage, format, name ?? path, instead );
		}
		catch ( IOException )
		{
			return null;
		}
	}

	//The text one of a surface's screen stages is compiled from with the frame's point effects after it - see CorePointChain.ScreenText, and FinishToScreen
	//in frame.slang, which ENGINE_SCREEN_CHAIN hands to them. Made on the thread that asks; the compile may run on another - see CoreShaderJobs.
	internal static (string Text, string Directory, string Entry, CoreShaderCompiler.Stage Kind)? ScreenChainText(
		string resourcePath, string combo, Stage stage, string chain )
	{
		var path = Source( Real( resourcePath ) );

		if ( path is null || chain is null ) return null;

		try
		{
			var (draws, _) = Preludes( path, combo );
			var (entry, kind) = Which( stage );

			var source = CoreShaderFeatures.Without( CoreShaderFeatures.Without(
				CoreShaderFeatures.Without( CompiledFile.TextOf( path ) ),
				CoreShaderParams.BlockName ), CoreShaderState.BlockName );

			var after = IsCluster( stage ) ? ClusterSurface : "";

			//Specialized where the language has the constants to be, as the surface's own stages are - see Compile.
			var specialized = CoreShaderCompiler.HasSpecialization( CoreShaderCache.Target ) ? "#define SPECIALIZED 1\n" : "";

			return ( specialized + draws + Defines( stage ) + "#define ENGINE_SCREEN_CHAIN 1\n" + source + after + chain,
				Path.GetDirectoryName( path ), entry, kind );
		}
		catch ( IOException )
		{
			return null;
		}
	}

	/// <summary>Every language a shader is kept in, together. A stage with nothing in it is written as an empty run rather than left out, so reading never has to ask which.</summary>
	public static byte[] Write( byte[][] spirv, byte[][] wgsl = null, byte[][] portable = null,
		byte[][] glsl = null, byte[][] stereo = null )
	{
		if ( spirv is null || spirv.Length != StageCount ) return null;

		var runs = new byte[RunsPerStage][][];
		runs[RunOf( CoreShaderCompiler.Format.Spirv )] = spirv;
		runs[RunOf( CoreShaderCompiler.Format.Wgsl )] = wgsl;
		runs[RunOf( CoreShaderCompiler.Format.SpirvPortable )] = portable;
		runs[RunOf( CoreShaderCompiler.Format.Glsl )] = glsl;
		runs[RunOf( CoreShaderCompiler.Format.GlslStereo )] = stereo;

		static byte[] At( byte[][] of, int stage )
			=> ( of is not null && stage < of.Length ? of[stage] : null ) ?? Array.Empty<byte>();

		var total = HeaderSize;

		for ( var i = 0; i < StageCount; i++ )
			for ( var run = 0; run < RunsPerStage; run++ )
				total += At( runs[run], i ).Length;

		var file = new byte[total];

		BitConverter.GetBytes( Magic ).CopyTo( file, 0 );
		BitConverter.GetBytes( Version ).CopyTo( file, 4 );

		var at = HeaderSize;

		for ( var i = 0; i < StageCount; i++ )
		{
			for ( var run = 0; run < RunsPerStage; run++ )
			{
				var words = At( runs[run], i );
				var entry = 8 + ( ( ( i * RunsPerStage ) + run ) * 8 );

				BitConverter.GetBytes( at ).CopyTo( file, entry );
				BitConverter.GetBytes( words.Length ).CopyTo( file, entry + 4 );
				words.CopyTo( file, at );
				at += words.Length;
			}
		}

		return file;
	}

	/// <summary>Whether these bytes are one of ours.</summary>
	public static bool IsOurs( ReadOnlySpan<byte> bytes )
		=> bytes.Length >= 8 && BitConverter.ToUInt32( bytes[..4] ) == Magic;

	/// <summary>One stage of the full frame's SPIR-V, or null where there is none.</summary>
	public static byte[] Read( byte[] file, Stage stage )
		=> Read( file, stage, CoreShaderCompiler.Format.Spirv );

	/// <summary>One WGSL stage, or null where there is none.</summary>
	public static byte[] ReadWgsl( byte[] file, Stage stage )
		=> Read( file, stage, CoreShaderCompiler.Format.Wgsl );

	/// <summary>One stage in one of the languages it is kept in, or null where there is none.</summary>
	public static byte[] Read( byte[] file, Stage stage, CoreShaderCompiler.Format format )
	{
		if ( file is null || !IsOurs( file ) ) return null;
		if ( file.Length < 8 ) return null;

		var version = BitConverter.ToUInt32( file, 4 );

		//This version and no other. A shader file is a build artifact: one written by an older engine
		//is compiled again, which is a build step rather than a guess at which layout the bytes are in.
		if ( version != Version ) return null;
		if ( file.Length < HeaderSize ) return null;

		var at = 8 + ( ( ( (int)stage * RunsPerStage ) + RunOf( format ) ) * 8 );
		var offset = BitConverter.ToInt32( file, at );
		var size = BitConverter.ToInt32( file, at + 4 );
		if ( size <= 0 || offset < HeaderSize || offset + size > file.Length ) return null;

		var bytes = new byte[size];
		Array.Copy( file, offset, bytes, 0, size );
		return bytes;
	}

	/// <summary>Every stage of a shader at a resource path, compiled or compiling.</summary>
	public static byte[][] Stages( string resourcePath, string combo = null,
		CoreShaderCompiler.Format? language = null, bool optional = false )
	{
		if ( string.IsNullOrWhiteSpace( resourcePath ) ) return null;

		var format = language ?? CoreShaderCache.Target;

		resourcePath = Real( resourcePath );

		//A blob on disk was built with the shader's features at their defaults, so it is an answer only
		//for a material that asked for those. Anything else is compiled, which is the price of a combo.
		var file = string.IsNullOrEmpty( combo ) ? Load( resourcePath ) : null;

		if ( file is not null )
		{
			var stages = new byte[StageCount][];

			//In the language this device takes, and no other: words written for another frame are not a worse answer
			//here, they are no answer - a device that wants the portable frame's cannot run the full frame's.
			for ( var i = 0; i < StageCount; i++ )
				stages[i] = Read( file, (Stage)i, format );

			if ( stages[(int)Stage.Vertex] is not null ) return stages;
		}

		//No compiler: optional combos still come from the shipped cache instead of returning null
		if ( optional && ( string.IsNullOrEmpty( combo ) || CoreShaderCompiler.Available ) ) return null;

		var source = Source( resourcePath );

		return source is null ? null : Compile( source, format, combo );
	}

	//Legacy surface shader names mapped onto our single surface file; real files take precedence
	private static readonly Dictionary<string, string> Aliases =
		new( StringComparer.OrdinalIgnoreCase )
		{
			["standard"] = "complex",
			["simple"] = "complex",
			["generic"] = "complex",
			["vr_standard"] = "complex",
			["blendable"] = "complex",

			//Character shaders (skin, eyeball) drawn with the standard surface as an approximation
			["skin"] = "complex",
			["eyeball"] = "complex",

			["sprite"] = "engine/unlit",
			["spritecard"] = "engine/unlit",

			//And the engine's own, which used to sit beside the surfaces and now sit under them - see the folder note
			//above. A material naming one by the name it had still finds it, which is the whole of what this table is for.
			["unlit"] = "engine/unlit",
			["vertex_color"] = "engine/vertex_color",
			["sky"] = "engine/sky",
			["terrain"] = "engine/terrain",
		};

	//Folder for the engine's own shaders, kept out of the material shader list by location
	internal const string EngineFolder = "engine";

	//Post-process shader folder name; offered as materials but listed separately at the end
	internal const string PostFolder = "postprocess";

	//Whether a shader lives in one of those folders.
	private static bool Under( string resourcePath, string folder )
	{
		var path = ( resourcePath ?? "" ).Replace( '\\', '/' );

		foreach ( var part in path.Split( '/' ) )
		{
			if ( string.Equals( part, folder, StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	//Whether this is one of the engine's own rather than a surface.
	internal static bool IsEngines( string resourcePath ) => Under( resourcePath, EngineFolder );

	//Whether a material may pick this shader: only under shaders or postprocess, not loose ported files
	internal static bool IsOffered( string resourcePath )
	{
		var path = ( resourcePath ?? "" ).Replace( '\\', '/' ).TrimStart( '/' );

		return path.StartsWith( "shaders/", StringComparison.OrdinalIgnoreCase ) ||
			path.StartsWith( PostFolder + "/", StringComparison.OrdinalIgnoreCase );
	}

	//Whether this draws over a finished picture rather than a surface in it.
	internal static bool IsPost( string resourcePath ) => Under( resourcePath, PostFolder );

	//Whether a .shader file is in the legacy shader format rather than ours - with modes and HLSL includes - which
	//nothing here can build. The asset compiler passes such a file on to the external compiler, and the bake leaves it out; see CoreShaderCache.BakeAll.
	internal static bool IsForeign( string path )
	{
		try
		{
			var text = CompiledFile.TextOf( path );

			//Any Slang include means it is ours, even if it has FEATURES/PARAMETERS blocks like theirs
			if ( text.Contains( ".slang\"", System.StringComparison.Ordinal ) ) return false;

			//As block headers - each on a line of its own, which is how their format writes them and how nothing in Slang ever would.
			foreach ( var line in text.Split( '\n' ) )
			{
				var trimmed = line.Trim();

				if ( trimmed is "MODES" or "FEATURES" or "COMMON" ) return true;
			}

			return text.Contains( "#include \"common/", System.StringComparison.Ordinal );
		}
		catch ( IOException )
		{
			return false;
		}
	}

	//The shader a name actually means: itself where there is a file, and whatever it is an alias for where there is not.
	internal static string Real( string resourcePath )
	{
		if ( string.IsNullOrWhiteSpace( resourcePath ) ) return resourcePath;
		if ( Source( resourcePath ) is not null ) return resourcePath;

		var bare = Path.GetFileNameWithoutExtension( resourcePath );

		if ( !Aliases.TryGetValue( bare, out var instead ) ) return resourcePath;

		var folder = Path.GetDirectoryName( resourcePath.Replace( '\\', '/' ) )?.Replace( '\\', '/' ) ?? "";

		return ( folder.Length == 0 ? instead : folder + "/" + instead ) + ".shader";
	}

	//The path in the engine's shipped assets, for when no project is mounted
	internal static string Shipped( string resourcePath )
	{
		var relative = Path.Combine( "addons", "base", "Assets",
			resourcePath.Replace( '/', Path.DirectorySeparatorChar ) );

		foreach ( var root in new[] { EngineFileSystem.Root?.GetFullPath( "/" ), Directory.GetCurrentDirectory() } )
		{
			if ( string.IsNullOrEmpty( root ) ) continue;

			var full = Path.Combine( root, relative );
			if ( File.Exists( full ) ) return full;
			if ( File.Exists( full + "_c" ) ) return full + "_c";
		}

		return null;
	}

	//A file's resource path, so compiled-stage cache keys do not depend on this machine's paths
	internal static string Named( string full )
	{
		if ( string.IsNullOrEmpty( full ) ) return null;

		string slashed;

		try
		{
			slashed = Path.GetFullPath( full ).Replace( '\\', '/' );
		}
		catch ( Exception e ) when ( e is ArgumentException or IOException )
		{
			return null;
		}

		//Shortest first, so a file is called what the engine would call it: "shaders/frame.slang", not the folders above Assets as well.
		var at = slashed.Length;

		while ( ( at = slashed.LastIndexOf( '/', at - 1 ) ) > 0 )
		{
			var candidate = slashed[( at + 1 )..];

			if ( CoreModel.ResolveOnDisk( candidate ) is not { } found ) continue;

			string back;

			try
			{
				back = Path.GetFullPath( found ).Replace( '\\', '/' );
			}
			catch ( Exception e ) when ( e is ArgumentException or IOException )
			{
				continue;
			}

			//Resolved back to something else - a project's file of the same name in front of this one. Then this file has no portable name.
			if ( back.Equals( slashed, StringComparison.Ordinal ) ) return candidate;
		}

		return null;
	}

	/// <summary>The compiled shader at a resource path, however it was written - straight out, or inside the resource container the editor writes through.</summary>
	public static byte[] Load( string resourcePath )
	{
		if ( string.IsNullOrWhiteSpace( resourcePath ) ) return null;

		var compiled = Path.ChangeExtension( resourcePath, ".shader_c" ).Replace( '\\', '/' );

		var full = CoreModel.ResolveOnDisk( compiled );
		if ( full is null ) return null;

		//Built before something it was built out of, so it is not an answer.
		if ( Stale( full, resourcePath ) ) return null;

		byte[] bytes;

		try
		{
			bytes = SealedFile.ReadAllBytes( full );
		}
		catch ( IOException )
		{
			return null;
		}

		//A packed source wears the same name in a build - it is the shader's text, not its stages, and the stages are in the cache. See CompiledFile.
		if ( CompiledFile.IsCompiled( bytes ) ) return null;

		return IsOurs( bytes ) ? bytes : CoreResource.Unwrap( bytes, Magic );
	}

	//Whether a blob is older than its source or any include; only checked when a compiler is available
	private static bool Stale( string blob, string resourcePath )
		=> CoreShaderCompiler.Available && StaleAgainst( blob, Source( resourcePath ) );

	//Source file for a shader name, trying the spelling given first and then the other (.shader/.slang)
	internal static string Source( string resourcePath )
	{
		if ( string.IsNullOrWhiteSpace( resourcePath ) ) return null;

		foreach ( var extension in new[] { null, ".shader", ".slang" } )
		{
			var written = ( extension is null
				? resourcePath
				: Path.ChangeExtension( resourcePath, extension ) ).Replace( '\\', '/' );

			if ( CoreModel.ResolveOnDisk( written ) is { } found ) return found;
			if ( Shipped( written ) is { } shipped ) return shipped;

			//And the packed one a build carries in place of the text - see CompiledFile. Handed back under its own name,
			//because everything that reads a shader's text reads it through CompiledFile.TextOf, which knows both names.
			if ( CoreModel.ResolveOnDisk( written + "_c" ) is { } packed ) return packed;
			if ( Shipped( written + "_c" ) is { } shippedPacked ) return shippedPacked;
		}

		return null;
	}

	//The same question, against a source already found.
	internal static bool StaleAgainst( string blob, string source )
	{
		if ( source is null ) return false;

		DateTime written;

		try
		{
			written = File.GetLastWriteTimeUtc( blob );
		}
		catch ( IOException )
		{
			return false;
		}

		//Breadth first, and each file only once: the includes make a graph, not a tree - every surface pulls in frame.slang and so does everything it pulls in.
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var queue = new Queue<string>();

		queue.Enqueue( source );
		seen.Add( source );

		while ( queue.Count > 0 )
		{
			var at = queue.Dequeue();

			DateTime edited;
			string text;

			try
			{
				edited = File.GetLastWriteTimeUtc( at );
				text = CompiledFile.TextOf( at );
			}
			catch ( IOException )
			{
				continue;
			}

			if ( edited > written ) return true;

			foreach ( var include in Includes( text, at ) )
			{
				if ( !seen.Add( include ) ) continue;

				queue.Enqueue( include );
			}
		}

		return false;
	}

	//The files a shader source pulls in, as paths on disk. Beside the including file first, which is how the compiler
	//resolves them, and then as a resource path - the engine's own shaders include each other by their place under Assets.
	private static IEnumerable<string> Includes( string text, string at )
		=> IncludesBeside( text, Path.GetDirectoryName( at ) );

	//Hash of every included file's contents, cached per file, for compiled-stage cache keys
	internal static string IncludeDigest( string text, string directory )
	{
		var digest = new StringBuilder();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var queue = new Queue<string>();

		foreach ( var include in IncludesBeside( text ?? "", directory ) )
		{
			if ( seen.Add( include ) ) queue.Enqueue( include );
		}

		while ( queue.Count > 0 )
		{
			var at = queue.Dequeue();

			if ( Known( at ) is not { } known ) continue;

			digest.Append( known.Hash ).Append( '\n' );

			foreach ( var include in known.Includes )
			{
				if ( seen.Add( include ) ) queue.Enqueue( include );
			}
		}

		return digest.ToString();
	}

	private static readonly Dictionary<string, (DateTime Written, string Hash, string[] Includes)> known =
		new( StringComparer.OrdinalIgnoreCase );

	//One included file's hash and what it includes, read again only when it has been written.
	private static (string Hash, string[] Includes)? Known( string path )
	{
		DateTime written;

		try
		{
			written = File.GetLastWriteTimeUtc( path );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			return null;
		}

		lock ( known )
		{
			if ( known.TryGetValue( path, out var was ) && was.Written == written ) return ( was.Hash, was.Includes );
		}

		string text;

		try
		{
			//Through the same reader as everything else: in a build the file beside a shader is the
			//packed one, and a digest taken over its packed bytes would not be the key the bake wrote.
			text = ( CompiledFile.TextOf( path ) ?? "" ).Replace( "\r\n", "\n" );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			return null;
		}

		var hash = Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text ) ) );
		var includes = IncludesBeside( text, Path.GetDirectoryName( path ) ).ToArray();

		lock ( known ) known[path] = ( written, hash, includes );

		return ( hash, includes );
	}

	//Forgets where every include was found - a project mounted or let go may answer a name differently. See CoreShaderCache.Changed.
	internal static void ForgetIncludes()
	{
		lock ( known ) known.Clear();
	}

	private static IEnumerable<string> IncludesBeside( string text, string beside )
	{

		var from = 0;

		while ( true )
		{
			var hash = text.IndexOf( "#include", from, StringComparison.Ordinal );
			if ( hash < 0 ) yield break;

			from = hash + 8;

			var open = text.IndexOf( '"', from );
			if ( open < 0 ) yield break;

			//On a later line than the directive, so a commented-out include three lines down is not read as this one's argument.
			if ( text.AsSpan( from, open - from ).IndexOf( '\n' ) >= 0 ) continue;

			var close = text.IndexOf( '"', open + 1 );
			if ( close < 0 ) yield break;

			var named = text[( open + 1 )..close].Replace( '\\', '/' );

			from = close + 1;

			if ( string.IsNullOrWhiteSpace( named ) ) continue;

			if ( !string.IsNullOrEmpty( beside ) )
			{
				var near = Path.GetFullPath( Path.Combine( beside, named ) );
				if ( File.Exists( near ) ) { yield return near; continue; }
				if ( File.Exists( near + "_c" ) ) { yield return near + "_c"; continue; }
			}

			var found = CoreModel.ResolveOnDisk( named ) ?? CoreModel.ResolveOnDisk( "shaders/" + named )
				?? CoreModel.ResolveOnDisk( named + "_c" ) ?? CoreModel.ResolveOnDisk( "shaders/" + named + "_c" );

			if ( found is not null ) yield return found;
		}
	}
}
