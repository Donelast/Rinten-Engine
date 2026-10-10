using System.Reflection;

namespace Rinten.UI;

//Editor design tokens, prepended to each sheet since variables can't be injected after parsing
internal static class EditorTokens
{
	/// <summary>The file, as it sits beside the sheets that use it.</summary>
	public const string Name = "_tokens.scss";

	/// <summary>Theme token text between design tokens and a sheet — set by the editor on project load.</summary>
	public static Func<string> ThemePreamble;

	/// <summary>Whether a filesystem path should get the editor theme preamble.</summary>
	public static Func<string, bool> ShouldPreparePath;

	/// <summary>Token text, read once thread-safely from the assembly; empty if unreadable so sheets fail loudly</summary>
	public static string Text => text.Value;

	private static readonly System.Lazy<string> text = new( Read, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication );

	/// <summary>A stylesheet with the tokens in front of it, ready to be parsed.</summary>
	public static string InFrontOf( string sheet, string extra = null )
	{
		if ( string.IsNullOrEmpty( sheet ) ) return sheet;

		var tokens = Text;

		if ( !string.IsNullOrEmpty( tokens ) && sheet.StartsWith( tokens, StringComparison.Ordinal ) )
		{
			if ( string.IsNullOrEmpty( extra ) ) return sheet;

			if ( sheet.Contains( extra, StringComparison.Ordinal ) )
				return sheet;

			return $"{tokens}\n{extra}\n{sheet[tokens.Length..].TrimStart( '\r', '\n' )}";
		}

		if ( string.IsNullOrEmpty( tokens ) && string.IsNullOrEmpty( extra ) )
			return sheet;

		return $"{tokens}\n{extra}\n{sheet}";
	}

	/// <summary>Design tokens plus theme preamble when <see cref="ShouldPreparePath"/> says so.</summary>
	public static string PrepareSheetText( string sheet, string path )
	{
		if ( string.IsNullOrEmpty( sheet ) ) return sheet;

		if ( ShouldPrepare( path ) && ThemePreamble is not null )
			return InFrontOf( sheet, ThemePreamble() );

		if ( ShouldPrepare( path ) )
			return InFrontOf( sheet );

		return sheet;
	}

	private static bool ShouldPrepare( string path ) =>
		ShouldPreparePath is not null && ShouldPreparePath( path );

	/// <summary>One colour token, by the name it has in the sheet without its dollar.</summary>
	public static Color Colour( string name, Color fallback = default )
	{
		var tokens = Text;
		if ( string.IsNullOrEmpty( tokens ) ) return fallback;

		var at = tokens.IndexOf( $"${name}:", System.StringComparison.Ordinal );
		if ( at < 0 ) return fallback;

		at += name.Length + 2;

		var end = tokens.IndexOf( ';', at );
		if ( end < 0 ) return fallback;

		var value = tokens[at..end].Trim();

		return Color.Parse( value ) ?? fallback;
	}

	private static string Read()
	{
		var assembly = Assembly.GetExecutingAssembly();

		//By what it is called rather than by its full name, which is made of the folder the file happens to sit in.
		var name = System.Array.Find( assembly.GetManifestResourceNames(),
			x => x.EndsWith( Name, System.StringComparison.OrdinalIgnoreCase ) );

		if ( name is null )
		{
			new Diagnostics.Logger( "Ui" ).Warning( "the editor's design tokens are not in the assembly" );
			return null;
		}

		using var stream = assembly.GetManifestResourceStream( name );

		if ( stream is null ) return null;

		using var reader = new System.IO.StreamReader( stream );

		return reader.ReadToEnd();
	}
}
