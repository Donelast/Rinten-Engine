using System.IO;

namespace Rinten;

//Parses a shader's STATE block (blend mode, tonemap curve); a draw's own blend attribute still wins
internal sealed class CoreShaderState
{
	//The block's name, as it heads its line.
	internal const string BlockName = "STATE";

	//Nothing said: every pass replaces what it draws over.
	internal static readonly CoreShaderState None = new();

	//How the file's passes land, where no entry is named - see PipelineKind.PostBlend.
	internal int Blend { get; private set; }

	//Whether this file's point effect is the frame's tonemap curve - see CorePointChain.
	internal bool Curve { get; private set; }

	//How each named entry lands, where one was named.
	private Dictionary<string, int> entries;

	//How one entry's pass lands: its own, or the file's.
	internal int BlendOf( string entry )
		=> entry is not null && entries is not null && entries.TryGetValue( entry, out var own ) ? own : Blend;

	//The block of the file on disk at path, or None.
	internal static CoreShaderState Of( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) ) return None;

		try
		{
			return Parse( CompiledFile.TextOf( path ) );
		}
		catch ( IOException )
		{
			return None;
		}
	}

	//The block out of a file's text.
	internal static CoreShaderState Parse( string text )
	{
		if ( !CoreShaderFeatures.Block( text ?? "", BlockName, out var body ) ) return None;

		var state = new CoreShaderState();

		foreach ( var raw in body.Split( ';' ) )
		{
			var line = WithoutComments( raw ).Trim();

			if ( line.Length == 0 ) continue;

			var open = line.IndexOf( '(' );
			var close = line.LastIndexOf( ')' );

			var name = ( open < 0 ? line : line[..open] ).Trim();
			var inside = open >= 0 && close > open ? line[( open + 1 )..close] : "";
			var parts = inside.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );

			switch ( name )
			{
				case "Blend" when parts.Length == 1:
					state.Blend = BlendNamed( parts[0], line );
					break;

				case "Blend" when parts.Length == 2:
					state.entries ??= new Dictionary<string, int>( StringComparer.Ordinal );
					state.entries[parts[0]] = BlendNamed( parts[1], line );
					break;

				case "Curve":
					state.Curve = true;
					break;

				default:
					Log.Warning( $"STATE: '{line}' is not something a shader file can say - Blend( mode ), Blend( entry, mode ) or Curve()" );
					break;
			}
		}

		return state;
	}

	//A blend by the name a file gives it, as PipelineKind.PostBlend numbers them.
	private static int BlendNamed( string name, string line ) => name.Trim().Trim( '"' ) switch
	{
		"None" or "Replace" => 0,
		"Additive" or "Add" => 2,
		"Screen" => 3,
		"Alpha" or "Mix" => 4,
		"AddByAlpha" => 5,
		"Multiply" => 6,
		_ => Unknown( line ),
	};

	private static int Unknown( string line )
	{
		Log.Warning( $"STATE: '{line}' names no blend - None, Additive, Screen, Alpha, AddByAlpha or Multiply" );
		return 0;
	}

	//Blanks comments so a statement is not lost along with the comments above it
	private static string WithoutComments( string text )
	{
		text = System.Text.RegularExpressions.Regex.Replace( text, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline );

		return System.Text.RegularExpressions.Regex.Replace( text, @"//[^\n]*", "" );
	}
}
