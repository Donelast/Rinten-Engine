using System;
using System.Collections.Generic;
using System.IO;

namespace Rinten.Porting.SBox;

//Materials from a model document: the surface table plus the use_global_default switch
internal static class SBoxMaterials
{
	private const string GroupNode = "DefaultMaterialGroup";

	//The table, and the one material everything it does not name wears.
	internal readonly record struct Assignment( Dictionary<string, string> Remaps, string Default );

	/// <summary>What their document says goes on the art.</summary>
	public static Assignment Read( string file )
	{
		var remaps = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var fallback = "";

		var document = SBoxModelDoc.Load( file );

		if ( document is null ) return new Assignment( remaps, fallback );

		foreach ( var node in SBoxModelDoc.Nodes( document, file ) )
		{
			if ( (node.String( "_class" ) ?? "") != GroupNode ) continue;

			foreach ( var entry in node["remaps"]?.Items ?? Array.Empty<CoreImportedKeyValues3>() )
			{
				var from = entry?.String( "from" );
				var to = entry?.String( "to" );

				if ( string.IsNullOrWhiteSpace( from ) || string.IsNullOrWhiteSpace( to ) ) continue;

				var ours = SBoxPorter.Rewrite( to );

				remaps[from] = ours;
				remaps[Path.GetFileNameWithoutExtension( from )] = ours;
			}

			//Only when switched on; the global default then overrides the whole model, even default.vmat
			if ( node.Bool( "use_global_default" ) )
				fallback = SBoxPorter.Rewrite( node.String( "global_default_material" ) ?? "" );

			break;
		}

		return new Assignment( remaps, fallback );
	}
}
