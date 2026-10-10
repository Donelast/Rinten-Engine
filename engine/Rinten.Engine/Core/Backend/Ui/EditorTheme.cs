using Rinten.UI;
using System.IO;
using System.Reflection;

namespace Rinten;

//The stylesheet every editor window of ours is dressed in. It ships inside the engine rather than in a content folder,
//because the editor has to look like itself before any filesystem is mounted - the first window is open long before a game is.
internal static class EditorTheme
{
	private static readonly Diagnostics.Logger log = new( "Ui" );

	private static StyleSheet sheet;
	private static bool tried;

	/// <summary>Dresses a panel and everything below it. Once per window is enough - a stylesheet on a root applies to the whole tree under it.</summary>
	public static void Apply( Panel panel )
	{
		if ( panel is null ) return;

		var styles = Sheet();
		if ( styles is null ) return;

		panel.StyleSheet.Add( styles );
	}

	//The parsed stylesheet, read from the assembly the first time it is wanted. Null where it
	//cannot be read, which leaves the window looking like nothing rather than not opening at all.
	private static StyleSheet Sheet()
	{
		if ( tried ) return sheet;

		tried = true;

		var text = Read();
		if ( text is null ) return null;

		//The tokens go in front of it: this window is open before there is a theme to take an accent from, so it uses the fallback one in there.
		sheet = StyleSheet.FromString( EditorTokens.InFrontOf( text ), "editor.scss" );

		return sheet;
	}

	private static string Read()
	{
		var assembly = Assembly.GetExecutingAssembly();

		//By what it is called rather than by its full name, which is made of the folder the file happens to sit in.
		var name = System.Array.Find( assembly.GetManifestResourceNames(),
			x => x.EndsWith( "editor.scss", System.StringComparison.OrdinalIgnoreCase ) );

		if ( name is null )
		{
			log.Warning( "the editor's stylesheet is not in the assembly" );
			return null;
		}

		using var stream = assembly.GetManifestResourceStream( name );
		if ( stream is null ) return null;

		using var reader = new StreamReader( stream );

		return reader.ReadToEnd();
	}
}
