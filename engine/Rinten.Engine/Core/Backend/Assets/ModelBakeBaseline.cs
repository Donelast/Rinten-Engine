using System.IO;

namespace Rinten;

//Pre-transform copy (name.mdl_c.baked) so recompiling a baked model applies its transform only once
internal static class ModelBakeBaseline
{
	/// <summary>The sidecar beside a compiled model's own path.</summary>
	public static string PathFor( string compiledPath ) => compiledPath + ".baked";

	/// <summary>Forget the baseline, so the next compile that needs one captures whatever is on disk right then instead of transforming stale geometry.</summary>
	public static void Reset( string compiledPath )
	{
		if ( string.IsNullOrWhiteSpace( compiledPath ) ) return;

		var baseline = PathFor( compiledPath );
		if ( File.Exists( baseline ) ) File.Delete( baseline );
	}
}
