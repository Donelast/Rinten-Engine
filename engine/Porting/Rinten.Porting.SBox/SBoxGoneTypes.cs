using System.IO;
using System.Text.RegularExpressions;

namespace Rinten.Porting.SBox;

//Their types with no counterpart here; each use in ported code is reported with the reason
internal static class SBoxGoneTypes
{
	//What is gone, and why - written for somebody reading the report who has to decide what to write instead.
	private static readonly (string Name, string Why)[] Gone =
	{
		( "MapInstance",
			"a level is placed into the scene when the project is ported, so there is no component " +
			"that loads one by name at run time - code that set MapName or waited for OnMapLoaded " +
			"has to become whatever this game does to change level" ),

		( "MapComponent",
			"the older name for MapInstance, and gone with it" ),
	};

	private static readonly Regex Named = new(
		$@"\b(?<name>{string.Join( "|", Gone.Select( x => Regex.Escape( x.Name ) ) )})\b",
		RegexOptions.Compiled );

	/// <summary>Report every place a ported source names a type this engine does not have.</summary>
	public static int Report( IReadOnlyList<(string Path, string Named)> files, PortReport report )
	{
		var found = 0;

		foreach ( var (path, named) in files )
		{
			string[] lines;

			try
			{
				lines = File.ReadAllLines( path );
			}
			catch ( Exception )
			{
				//A file that cannot be read has already been reported by whatever tried to convert it.
				continue;
			}

			for ( var i = 0; i < lines.Length; i++ )
			{
				//Blanked the same way the units pass blanks a line, so a type named in a comment or a string is not reported as code.
				var code = PortCodeUnits.CodeOnly( lines[i] );

				foreach ( Match match in Named.Matches( code ) )
				{
					var why = Gone.First( x => x.Name == match.Groups["name"].Value ).Why;

					report.CouldNotConvert( $"{named}:{i + 1}", match.Groups["name"].Value,
						lines[i].Trim(), $"this engine has no {match.Groups["name"].Value} - {why}" );

					++found;
				}
			}
		}

		return found;
	}
}
