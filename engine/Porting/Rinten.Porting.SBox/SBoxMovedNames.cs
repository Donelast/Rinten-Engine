using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rinten.Porting.SBox;

//Renames engine types whose meaning changed after import (e.g. IconButton -> SidebarButton)
//Rewritten in place (a using alias loses to the Editor namespace), only where needed, and reported
internal static class SBoxMovedNames
{
	//A name the ported code uses, what it has to say instead for that code to build, what has to be in the file for that to be right, and why.
	private readonly record struct Moved( string Name, string Becomes, string Beside, string Why );

	private static readonly Moved[] Names =
	{
		new( "IconButton", "SidebarButton", @"\bCreateButton\s*\(",
			"ToolSidebarWidget.CreateButton returned an IconButton when the editor was imported and returns a SidebarButton now" ),
	};

	/// <summary>Puts the moved names right in the files that use them.</summary>
	public static int Apply( IReadOnlyList<(string Path, string Named)> files, PortReport report )
	{
		var changed = 0;

		foreach ( var (path, named) in files )
		{
			string[] lines;

			try { lines = File.ReadAllLines( path ); }
			catch ( Exception ) { continue; }

			//The file with its comments and strings blanked, so a name written in prose does not
			//ask to be rewritten - the same reading the units pass and the gone-types pass take.
			var code = lines.Select( PortCodeUnits.CodeOnly ).ToArray();
			var whole = string.Join( '\n', code );

			var wanted = Names
				.Where( x => Regex.IsMatch( whole, $@"\b{Regex.Escape( x.Name )}\b" ) )
				.Where( x => Regex.IsMatch( whole, x.Beside ) )
				.ToArray();

			if ( wanted.Length == 0 ) continue;

			var touched = false;

			for ( var i = 0; i < lines.Length; i++ )
			{
				foreach ( var moved in wanted )
				{
					//Code only: matches come from the comment-blanked line and are spliced in at the same offsets
					var places = Regex.Matches( code[i], $@"\b{Regex.Escape( moved.Name )}\b" );
					if ( places.Count == 0 ) continue;

					//Backwards, so the offsets ahead of each one still stand.
					for ( var m = places.Count - 1; m >= 0; m-- )
					{
						var place = places[m];

						lines[i] = lines[i][..place.Index] + moved.Becomes + lines[i][( place.Index + place.Length )..];
					}

					touched = true;

					report.Notes.Add( $"{named}:{i + 1}: {moved.Name} is now {moved.Becomes} - {moved.Why}" );
				}
			}

			if ( !touched ) continue;

			try
			{
				File.WriteAllLines( path, lines );
				++changed;
			}
			catch ( Exception e ) { report.Skipped.Add( $"{named}: {e.Message}" ); }
		}

		return changed;
	}
}
