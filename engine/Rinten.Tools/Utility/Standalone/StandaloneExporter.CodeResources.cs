using NativeEngine;
using System.IO;

namespace Editor;

partial class StandaloneExporter
{

	record CodeResource( string RelativePath, string AbsolutePath );

	//Fetch all code-defined core resources
	private static List<CodeResource> GetCodeResources( string engineDir )
	{
		//There is no native resource manifest to read these from, so there is nothing here to copy. An empty list rather
		//than null: the caller walks whatever comes back, and null threw a NullReferenceException through the middle of every build.
		return new List<CodeResource>();
	}
}
