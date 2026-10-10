using System.Collections.Generic;
using System.IO;

namespace Rinten;

//Anim stack names in an FBX, for the model editor clip list.
internal static class CoreFbxTakes
{
	public static List<string> List( string path )
	{
		if ( !CoreNative.Available || string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) )
			return new List<string>();

		return CoreFbxImport.ListTakes( path );
	}
}
