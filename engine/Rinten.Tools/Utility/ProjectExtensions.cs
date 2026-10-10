using Rinten;

namespace Editor;

/// <summary>Editor access to a project's compilers. They are Rinten.Code's, which game code never references - editor code reaches them through these extensions, as it did when they were internal members of <see cref="Project"/>.</summary>
public static class ProjectExtensions
{
	extension( Project project )
	{
		/// <summary>The compiler for this project's game code, null when it has none.</summary>
		public Compiler Compiler => ProjectCompilers.Of( project )?.Game;

		/// <summary>The compiler for this project's editor code, null when it has none.</summary>
		public Compiler EditorCompiler => ProjectCompilers.Of( project )?.Editor;
	}

	extension( Project )
	{
		/// <summary>The compile group every local project's code builds in. Its compilers carry the live build state - what's building, whether the last build succeeded, and its diagnostics.</summary>
		public static CompileGroup CompileGroup => CodeCompiling.Group;

		/// <summary>What the last compile said. Nothing where nothing has compiled - asking does not make a compile group to ask.</summary>
		public static IEnumerable<Microsoft.CodeAnalysis.Diagnostic> GetCompileDiagnostics() => CodeCompiling.Diagnostics();

		/// <summary>(Re)generate the active project's solution file.</summary>
		public static Task GenerateSolution() => ProjectSolution.GenerateSolution();
	}
}
