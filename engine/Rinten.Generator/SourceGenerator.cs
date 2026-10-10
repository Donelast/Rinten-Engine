using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rinten.Generator;

[Generator]
public class SourceGenerator : IIncrementalGenerator
{
	public void Initialize( IncrementalGeneratorInitializationContext context )
	{
		context.RegisterSourceOutput( context.CompilationProvider, static ( spc, compilation ) =>
		{
			//Razor files are now handled by the Razor SDK in IDE scenarios and by Compiler.Razor.cs during engine compilation

			var processor = new Processor();
			processor.Context = spc;

			processor.Run( (CSharpCompilation)compilation );
		} );
	}
}
