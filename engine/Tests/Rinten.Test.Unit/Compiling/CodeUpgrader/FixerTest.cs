using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Rinten.CodeUpgrader;

namespace CodeUpgraderTests;

public class FixerTest<T, TFix> : IFixerTest where T : DiagnosticAnalyzer, new() where TFix : CodeFixProvider, new()
{
	public async Task Test( string oldcode, string fixedcode )
	{
		var test = new CSharpCodeFixTest<T, TFix, DefaultVerifier>
		{
			ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
			TestCode = oldcode,
			FixedCode = fixedcode,
		};

		//Force LF so the formatter matches the LF raw string expectations on every OS
		test.TestState.AnalyzerConfigFiles.Add( ("/.editorconfig", "root = true\n\n[*.cs]\nend_of_line = lf\n") );

		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Rinten.Internal.GlobalGameNamespace ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Rinten.ConCmdAttribute ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( NetFlags ).Assembly.Location ) );

		await test.RunAsync();
	}
}
