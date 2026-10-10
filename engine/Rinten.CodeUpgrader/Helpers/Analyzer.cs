namespace Rinten.CodeUpgrader;

public interface IAnalyzerTest
{
	public Task TestWithMarkup( string code );
}

/// <summary>Wraps DiagnosticAnalyzer to make it less of a pain in the ass</summary>
public abstract partial class Analyzer : DiagnosticAnalyzer
{
	public virtual DiagnosticDescriptor Rule { get; }

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create( Rule );

	public sealed override void Initialize( AnalysisContext context )
	{
		context.ConfigureGeneratedCodeAnalysis( GeneratedCodeAnalysisFlags.Analyze );
		context.EnableConcurrentExecution();

		Init( context );
	}

	public virtual void Init( AnalysisContext context ) { }

	public virtual Task RunTests( IAnalyzerTest tester ) => Task.CompletedTask;
}
