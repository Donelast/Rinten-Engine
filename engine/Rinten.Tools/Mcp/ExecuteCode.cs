using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Editor.Mcp;

/// <summary>Runs a C# snippet an agent wrote against the live editor. The snippet is compiled with the engine's own Roslyn against everything the process has loaded - the engine, the editor, and the project's assemblies exactly as they are running - loaded beside the packages so it resolves the same types they do, run once on the main thread, and unloaded.</summary>
[McpToolset( "editor" )]
internal static class ExecuteCode
{
	[McpTool( "execute_code" ), McpListed]
	[Description( "Compile and run a C# snippet inside the editor, on its main thread, with the same access a tool in an addon has - without writing one. Use it for anything no tool covers: read any property, call any method, walk the scene, measure something, poke the physics. Give one expression and its value comes back; give statements and 'return' hands a value back (leave it out to get just what was logged). The snippet sees Rinten.Engine, Rinten.Tools and every project assembly as they are running, plus the engine's and editor's internals. Already in scope: ActiveScene - the scene the editor has open, which while playing is the running game's - plus Game, Project.Current, SceneEditorSession.Active, EditorScene, Log (goes to the console and comes back with the result), TypeLibrary, EditorTypeLibrary; usings System, System.Linq, System.Collections.Generic, System.Threading.Tasks, Rinten and Editor - add 'using X;' lines at the top for more. Local functions work; classes and records declared beside the code do too. 'await' works and resumes on the main thread. The value comes back like any tool result: a GameObject or Component as a compact reference, a Resource as its path, a Bitmap as an image, anything else as json. Compile errors come with snippet line numbers, exceptions with their stack. Prefer a dedicated tool where one exists - set_component pushes an undo step, this does not - and mind that a loop that never ends hangs the editor." )]
	public static async Task<object> Run(
		[Description( "The C# to run - an expression, or statements with an optional 'return'. Markdown fences are tolerated." )] string code )
	{
		code = Unfence( code );

		if ( string.IsNullOrWhiteSpace( code ) )
			throw new Exception( "Give some C# to run - an expression like 'Game.ActiveScene.Name', or statements." );

		var loader = GameInstanceDll.PackageLoader
			?? throw new Exception( "The editor is still starting - there is nothing to run code against yet. Poll editor_status." );

		//What's loaded is read here, on the main thread, and the compile - the slow part, and
		//none of the editor's business - happens off it. The run is the main thread's again.
		var references = Snippet.References.For( loader );
		var compiled = await Task.Run( () => Snippet.Compile( code, references ) );

		await MainThread.Wait();

		var since = LogBuffer.Last;
		var stopwatch = Stopwatch.StartNew();
		var assembly = loader.LoadTransient( compiled );

		object value = null;

		try
		{
			var run = assembly.GetType( Snippet.ClassName )?.GetMethod( Snippet.MethodName, BindingFlags.Public | BindingFlags.Static )
				?? throw new InvalidOperationException( $"The compiled snippet has no {Snippet.ClassName}.{Snippet.MethodName}" );

			try
			{
				value = await ToolRegistry.Unwrap( run.Invoke( null, null ) );
			}
			catch ( TargetInvocationException e ) when ( e.InnerException is not null )
			{
				ExceptionDispatchInfo.Capture( e.InnerException ).Throw();
			}
		}
		finally
		{
			//Whatever the snippet made stays alive for as long as something holds it - the
			//result is serialized after this - and the assembly goes once nothing does.
			loader.UnloadTransient( assembly );
		}

		stopwatch.Stop();

		var result = value is null
			? McpResult.Text( "Ran, returned nothing." )
			: ToolRegistry.Shape( value );

		var console = LogBuffer.TextSince( since );

		result.WithText( console is null
			? $"Ran in {stopwatch.Elapsed.TotalMilliseconds:0.#} ms, nothing logged."
			: $"Ran in {stopwatch.Elapsed.TotalMilliseconds:0.#} ms. Console during the run:\n{console}" );

		return result;
	}

	//Agents wrap code in ```csharp fences by habit. The code is what's inside.
	static string Unfence( string code )
	{
		if ( code is null )
			return null;

		var text = code.Trim();

		if ( !text.StartsWith( "```", StringComparison.Ordinal ) || !text.EndsWith( "```", StringComparison.Ordinal ) || text.Length < 6 )
			return text;

		text = text[3..^3];

		//The language tag, if any, sits on the opening fence's line
		var firstLine = text.IndexOf( '\n' );

		if ( firstLine >= 0 && !text[..firstLine].Trim().Contains( ' ' ) && text[..firstLine].Trim().Length <= 10 )
			text = text[(firstLine + 1)..];

		return text.Trim();
	}
}

//Turns a snippet into an assembly: the source around it, the references it is compiled against, and the diagnostics as the agent should read them.
static class Snippet
{
	/// <summary>The one assembly name every snippet is compiled as. Rinten.Engine and Rinten.Tools name it in their InternalsVisibleTo, which is what lets a snippet look at internals.</summary>
	public const string AssemblyName = "McpSnippet";

	public const string ClassName = "McpSnippet";
	public const string MethodName = "Run";

	//The file name diagnostics and stack frames report snippet lines under.
	const string SourceName = "snippet.cs";

	//The namespaces a snippet sees without asking - the ones editor code gets, less the framework's obscure corners.
	static readonly string[] Usings =
	[
		"System",
		"System.Collections.Generic",
		"System.Linq",
		"System.Text",
		"System.Threading.Tasks",
		"Rinten",
		"Rinten.Diagnostics",
		"Editor",
	];

	//What editor projects get as global static usings - Log, TypeLibrary, EditorWindow and the rest.
	static readonly string[] StaticUsings =
	[
		"Rinten.Internal.GlobalGameNamespace",
		"Rinten.Internal.GlobalToolsNamespace",
	];

	static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
		.WithLanguageVersion( LanguageVersion.Preview );

	//The wrapper's own warnings are not the snippet's business: the return the wrapper appends is
	//unreachable after the snippet's own, and a Run with no await in it is still async so that one can be.
	static readonly ImmutableDictionary<string, ReportDiagnostic> SuppressedDiagnostics = new Dictionary<string, ReportDiagnostic>
	{
		["CS0162"] = ReportDiagnostic.Suppress, //unreachable code
		["CS1998"] = ReportDiagnostic.Suppress, //async without await
		["CS8321"] = ReportDiagnostic.Suppress, //local function never used
	}.ToImmutableDictionary();

	static readonly CSharpCompilationOptions CompilationOptions = new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
		.WithOptimizationLevel( OptimizationLevel.Debug )
		.WithNullableContextOptions( NullableContextOptions.Disable )
		.WithAllowUnsafe( false )
		.WithSpecificDiagnosticOptions( SuppressedDiagnostics );

	//Line numbers land in the assembly, so an exception's stack says which snippet line threw.
	static readonly EmitOptions EmitOptions = new( debugInformationFormat: DebugInformationFormat.Embedded );

	/// <summary>Compile a snippet to an assembly's bytes. Throws with the diagnostics, written for the agent, when it doesn't compile.</summary>
	public static byte[] Compile( string code, IReadOnlyList<MetadataReference> references )
	{
		var parsed = Parse( code );

		//An expression first, if the snippet could be one: its value is the answer. Only the compiler can say whether it has one - a call
		//to a void method parses the same as a call that returns something - so ask it, and take the statement reading when it doesn't.
		if ( parsed.Expression is not null )
		{
			var asExpression = Compilation( parsed.Source( expression: true ), references );

			if ( ExpressionHasValue( asExpression ) )
				return Emit( asExpression );
		}

		return Emit( Compilation( parsed.Source( expression: false ), references ) );
	}

	static CSharpCompilation Compilation( string source, IEnumerable<MetadataReference> references )
	{
		var tree = CSharpSyntaxTree.ParseText( source, ParseOptions, path: SourceName, encoding: Encoding.UTF8 );

		return CSharpCompilation.Create( AssemblyName, [tree], references, CompilationOptions );
	}

	//Whether the expression the wrapper returns has a type at all - void, and it's a statement; unresolved, and the errors are the ones to show.
	static bool ExpressionHasValue( CSharpCompilation compilation )
	{
		var tree = compilation.SyntaxTrees[0];
		var model = compilation.GetSemanticModel( tree );

		var returned = tree.GetRoot().DescendantNodes().OfType<ReturnStatementSyntax>().FirstOrDefault()?.Expression;

		if ( returned is null )
			return false;

		//No type or an unresolved one is still the expression reading: the errors it reports name
		//what's missing, where the statement reading would only say the expression can't stand alone.
		var type = model.GetTypeInfo( returned ).Type;

		return type is null || type.TypeKind == TypeKind.Error || type.SpecialType != SpecialType.System_Void;
	}

	static byte[] Emit( CSharpCompilation compilation )
	{
		using var stream = new MemoryStream();

		var result = compilation.Emit( stream, options: EmitOptions );

		//Invalid params, as the registry's own binding errors are: the code is the argument, and what's wrong with it is the whole message - no stack wanted.
		if ( !result.Success )
			throw new McpException( JsonRpcError.InvalidParams, DescribeErrors( result.Diagnostics ) );

		return stream.ToArray();
	}

	//The errors as the agent should read them - snippet line numbers, one per line, and a hint for the usual first mistake.
	static string DescribeErrors( ImmutableArray<Diagnostic> diagnostics )
	{
		var errors = diagnostics
			.Where( x => x.Severity == DiagnosticSeverity.Error )
			.OrderBy( x => x.Location.GetMappedLineSpan().StartLinePosition.Line )
			.Take( 20 )
			.ToArray();

		var builder = new StringBuilder();
		builder.AppendLine( $"The snippet didn't compile ({errors.Length} error{(errors.Length == 1 ? "" : "s")}):" );

		foreach ( var error in errors )
		{
			var span = error.Location.GetMappedLineSpan();
			var where = span.Path == SourceName ? $"line {span.StartLinePosition.Line + 1}" : "wrapper";

			builder.AppendLine( $"  {where}: {error.Id} {error.GetMessage()}" );
		}

		if ( errors.Any( x => x.Id is "CS0246" or "CS0103" ) )
			builder.AppendLine( "A missing type or name usually wants a 'using' line at the top of the snippet - the game's namespaces aren't imported by default. get_component_type tells you a component's namespace." );

		return builder.ToString().TrimEnd();
	}

	//The snippet pulled apart: its using directives, type declarations to stand beside the wrapper class, and the
	//statements that go in the method - each remembering its line so the source it's written into can map errors back.
	sealed class Parsed
	{
		public List<string> Usings { get; } = new();
		public List<(int Line, string Text)> Types { get; } = new();
		public List<(int Line, string Text)> Statements { get; } = new();

		/// <summary>The whole snippet as one expression, when that's what it looks like: exactly one statement and it's an expression. Whether that expression has a value is for the compiler to say.</summary>
		public ExpressionSyntax Expression { get; set; }

		public string Source( bool expression )
		{
			var builder = new StringBuilder();

			foreach ( var u in Usings.Concat( Snippet.Usings.Select( x => $"using {x};" ) ).Concat( StaticUsings.Select( x => $"using static {x};" ) ) )
			{
				builder.AppendLine( u );
			}

			builder.AppendLine();
			builder.AppendLine( $"public static class {ClassName}" );
			builder.AppendLine( "{" );

			//The scene every scene tool means by "the scene" - the editor's session, which in play mode is the game's - so a snippet starts where they do.
			builder.AppendLine( "\tpublic static Scene ActiveScene => SceneEditorSession.Active?.Scene ?? Game.ActiveScene;" );
			builder.AppendLine();
			builder.AppendLine( $"\tpublic static async Task<object> {MethodName}()" );
			builder.AppendLine( "\t{" );

			if ( expression )
			{
				var line = Expression.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

				builder.AppendLine( $"#line {line} \"{SourceName}\"" );
				builder.AppendLine( $"return ({Expression.ToFullString().Trim()});" );
			}
			else
			{
				foreach ( var (line, text) in Statements )
				{
					builder.AppendLine( $"#line {line} \"{SourceName}\"" );
					builder.AppendLine( text );
				}

				builder.AppendLine( "#line hidden" );
				builder.AppendLine( "return null;" );
			}

			builder.AppendLine( "\t}" );
			builder.AppendLine( "}" );

			foreach ( var (line, text) in Types )
			{
				builder.AppendLine( $"#line {line} \"{SourceName}\"" );
				builder.AppendLine( text );
			}

			return builder.ToString();
		}
	}

	static Parsed Parse( string code )
	{
		//Parsed as a file with top-level statements: usings first, then statements and declarations in whatever order they
		//were written. Errors here don't matter - the wrapped source is compiled properly and reports them with better lines.
		var unit = SyntaxFactory.ParseCompilationUnit( code, options: ParseOptions );
		var parsed = new Parsed();

		foreach ( var u in unit.Usings )
		{
			parsed.Usings.Add( u.ToString() );
		}

		foreach ( var member in unit.Members )
		{
			//The full span, trivia included: the text written out is the full text, so the line it's
			//said to start on has to be the line its leading comments and blank lines start on.
			var line = unit.SyntaxTree.GetLineSpan( member.FullSpan ).StartLinePosition.Line + 1;

			switch ( member )
			{
				case GlobalStatementSyntax global:
					var text = global.Statement.ToFullString().TrimEnd();

					//A lone expression written without its semicolon parses with one missing; put it back so the statement reading compiles too.
					if ( global.Statement is ExpressionStatementSyntax { SemicolonToken.IsMissing: true } )
						text += ";";

					parsed.Statements.Add( (line, text) );
					break;

				case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
					parsed.Types.Add( (line, member.ToFullString().TrimEnd()) );
					break;

				default:
					//A method or field at the top level - the parser reads a stray one as a member. Let the compiler say what's wrong with it, in place.
					parsed.Statements.Add( (line, member.ToFullString().TrimEnd()) );
					break;
			}
		}

		if ( unit.Members.Count == 1 && unit.Members[0] is GlobalStatementSyntax { Statement: ExpressionStatementSyntax single } )
		{
			parsed.Expression = single.Expression;
		}

		return parsed;
	}

	/// <summary>What a snippet is compiled against: the framework's reference assemblies, every engine and third party assembly the process has loaded from disk, and the project's assemblies from the bytes they are running as.</summary>
	public static class References
	{
		//A file's reference, made once. The engine's assemblies don't change under a running editor.
		static readonly Dictionary<string, MetadataReference> byPath = new( StringComparer.OrdinalIgnoreCase );

		//A package assembly's reference, kept for as long as its bytes are - a hotload brings new bytes and a new reference.
		static readonly ConditionalWeakTable<byte[], MetadataReference> byImage = new();

		public static IReadOnlyList<MetadataReference> For( PackageLoader loader )
		{
			var references = new List<MetadataReference>();
			var taken = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

			//The framework, as reference assemblies - the same set every project compiles against. Its implementation assemblies
			//are loaded too, and must not be referenced beside these: a type in both is an ambiguity in every snippet.
			foreach ( var (file, reference) in FrameworkReferences.All )
			{
				if ( taken.Add( Path.GetFileNameWithoutExtension( file ) ) )
					references.Add( reference );
			}

			foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
			{
				if ( assembly.IsDynamic || string.IsNullOrEmpty( assembly.Location ) )
					continue;

				var name = assembly.GetName().Name;

				if ( name is null || IsFramework( name ) || !taken.Add( name ) )
					continue;

				references.Add( FromFile( assembly.Location ) );
			}

			foreach ( var loaded in loader.LoadedAssemblies )
			{
				if ( loaded.CompiledAssemblyBytes is null || loaded.Name is null || !taken.Add( loaded.Name ) )
					continue;

				references.Add( FromImage( loaded ) );
			}

			return references;
		}

		//The runtime's own assemblies, which the reference set above already stands for.
		static bool IsFramework( string name )
		{
			return name.StartsWith( "System.", StringComparison.OrdinalIgnoreCase )
				|| name.StartsWith( "Microsoft.Win32.", StringComparison.OrdinalIgnoreCase )
				|| name is "System" or "mscorlib" or "netstandard" or "WindowsBase" or "Microsoft.CSharp" or "Microsoft.VisualBasic";
		}

		static MetadataReference FromFile( string path )
		{
			lock ( byPath )
			{
				if ( !byPath.TryGetValue( path, out var reference ) )
				{
					reference = MetadataReference.CreateFromFile( path );
					byPath[path] = reference;
				}

				return reference;
			}
		}

		static MetadataReference FromImage( LoadedAssembly loaded )
		{
			lock ( byImage )
			{
				if ( !byImage.TryGetValue( loaded.CompiledAssemblyBytes, out var reference ) )
				{
					//The bytes as they are - the loader never writes to them - rather than a copy per snippet.
					reference = MetadataReference.CreateFromImage( ImmutableCollectionsMarshal.AsImmutableArray( loaded.CompiledAssemblyBytes ), filePath: $"{loaded.Name}.dll" );
					byImage.Add( loaded.CompiledAssemblyBytes, reference );
				}

				return reference;
			}
		}
	}
}
