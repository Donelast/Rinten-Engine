using Rinten.UI;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Editor;

//Loads editor panel stylesheets embedded in the assembly, since no game filesystem is mounted
internal static class EditorStyles
{
	/// <summary>Applies the stylesheet that ships beside a panel's markup.</summary>
	public static bool Apply( Panel panel, string name )
	{
		if ( panel is null ) return false;

		//Editor panels receive pointer events by default; it cascades, so pass-through panels opt out
		panel.Style.PointerEvents = PointerEvents.All;

		var sheet = Sheet( panel.GetType().Assembly, name );

		if ( sheet is null ) return false;

		panel.StyleSheet.Add( sheet );

		return true;
	}

	//The parsed sheet, read the first time it is wanted and kept after that. Kept because a control is not one panel: every text field in a window asks
	//for the same sheet, and parsing it again for each of them is the design tokens read from the assembly, parsed and thrown away, once per field.
	private static StyleSheet Sheet( Assembly assembly, string name )
	{
		var key = $"{assembly?.GetName()?.Name}:{name}";

		//Under a lock because a sheet is asked for from whatever thread wanted a panel,
		//and two of them arriving at once is a dictionary being written while it is read.
		lock ( sheets )
		{
			if ( sheets.TryGetValue( key, out var found ) ) return found;

			//The panel's own assembly first, because the launcher is a different binary with its own panels and its
			//own sheets beside them - and this one after, for the shared controls those panels are built out of.
			var text = Read( assembly, name )
				?? Read( typeof( EditorStyles ).Assembly, name );

			StyleSheet sheet = null;

			if ( !string.IsNullOrEmpty( text ) )
			{
				sheet = StyleSheet.FromString(
					EditorTokens.InFrontOf( text, EditorSheetPreamble.ThemeText() ),
					name );
			}

			sheets[key] = sheet;

			return sheet;
		}
	}

	private static readonly Dictionary<string, StyleSheet> sheets = new();

	//Re-parses embedded editor sheets with the current theme.
	internal static void Reload()
	{
		lock ( sheets )
		{
			foreach ( var (key, sheet) in sheets.ToArray() )
			{
				if ( sheet is null )
					continue;

				var colon = key.IndexOf( ':' );
				if ( colon < 0 )
					continue;

				var assemblyName = key[..colon];
				var name = key[(colon + 1)..];

				var assembly = System.AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault( x => x.GetName().Name == assemblyName );

				var text = Read( assembly, name )
					?? Read( typeof( EditorStyles ).Assembly, name );

				if ( string.IsNullOrEmpty( text ) )
					continue;

				sheet.Variables = null;
				sheet.UpdateFromString(
					EditorTokens.InFrontOf( text, EditorSheetPreamble.ThemeText() ),
					name,
					failSilently: true );
			}
		}
	}

	//The text of an embedded stylesheet, or nothing where there is no such resource - a missing sheet is
	//a panel with no styling, which is visible enough on its own without stopping the editor to say so.
	private static string Read( Assembly assembly, string name )
	{
		if ( assembly is null ) return null;

		//Embedded resources are named by their path with dots for slashes, and which folder a file sits in is not the caller's business.
		var found = assembly.GetManifestResourceNames()
			.FirstOrDefault( x => x.EndsWith( name, System.StringComparison.OrdinalIgnoreCase ) );

		if ( found is null ) return null;

		using var stream = assembly.GetManifestResourceStream( found );

		if ( stream is null ) return null;

		using var reader = new StreamReader( stream );

		return reader.ReadToEnd();
	}
}
